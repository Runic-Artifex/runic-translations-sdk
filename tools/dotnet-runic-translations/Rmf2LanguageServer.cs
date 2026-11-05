using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using JsonValue = System.Text.Json.Nodes.JsonValue;
using System.Text.RegularExpressions;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

/// <summary>Bounded stdio LSP transport over the shared recoverable resource model.</summary>
internal sealed class Rmf2LanguageServer
{
    private readonly Rmf2WorkspaceCache _syntaxCache = new();
    private readonly Dictionary<string, Buffer> _buffers = new(StringComparer.Ordinal);
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Action<string>? _beforeRequest;
    private readonly Action<string>? _requestCancelled;
    private readonly object _revisionGate = new();
    private string _encoding = "utf-16";
    private bool _shutdown;
    private bool _fileOperations;
    private bool _configurationSync;
    private bool _codeDescriptionSupport;
    // Review data per project and buffer group, for code actions and hover: the compilation with the open buffers, its
    // review compilation (TranslationReviewFile.ForReview), the fingerprints and the review file. Dropped when a buffer,
    // a watched file or the configuration changes, and when the review file's size or write time differs.
    private readonly Dictionary<string, ReviewView> _reviewViews = new(StringComparer.Ordinal);
    // Open buffers by file (FileKey). Clients spell one file's URI differently from .NET
    // (`file:///c%3A/...` and percent-encoded `+ @ ( ) !` in VS Code), so whether a file is
    // open, and which URI its publications use, is decided by its path, never by URI text.
    private readonly Dictionary<string, string> _bufferPaths = new(PathComparer);
    // Catalog diagnostics of project files that are not open buffers, per file. Several projects
    // can compile one file (shared source roots); each contributes its own diagnostics, and the
    // client receives their union, so one project's update or clear never erases another's.
    private readonly Dictionary<string, ProjectFile> _projectFiles = new(PathComparer);
    // The files each project contributes diagnostics to.
    private readonly Dictionary<string, HashSet<string>> _projectFileDiagnostics = new(PathComparer);
    // Diagnostics publications not yet sent, the latest per URI in the order they were made.
    // A publication computed while a newer state revision is queued is held back, not dropped:
    // the newest revision sends whatever it did not supersede, so no clear is ever lost.
    private readonly Dictionary<string, (long Sequence, JsonObject Params)> _outbox = new(StringComparer.Ordinal);
    private long _outboxSequence;
    // Whether each project uses the direct MF2 layout, which decides a file's locale. Recorded
    // whenever the project is loaded and dropped on project, configuration and watched-file
    // changes, so runic/documentInfo does not read the catalog from disk on every editor switch.
    private readonly Dictionary<string, bool> _projectLayouts = new(PathComparer);
    private const int MaximumPublishedProjectFiles = 2_000;
    private static readonly Regex DocumentedDiagnosticId = new("^RTR[0-9]{4}$", RegexOptions.CultureInvariant);
    private readonly List<string> _workspaceRoots = new();
    // Windows file systems are case-insensitive and clients disagree on drive
    // letter case (`file:///c%3A/...` versus `C:\...`). Project identity follows
    // the platform so one directory is never treated as two projects.
    internal static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly SortedSet<string> _diskProjectDirectories = new(PathComparer);
    private readonly SortedSet<string> _projectDirectories = new(PathComparer);
    // Disk source keys come from Path.GetFullPath (native separators) while
    // buffer keys come from URIs (portable separators). On Windows both the
    // separator and the case must be ignored or one file appears twice.
    internal static readonly IEqualityComparer<string> SourcePathComparer = new PlatformSourcePathComparer();
    // Failure categories already reported per open buffer (bounded by the
    // number of exception types, not by changing positions in messages).
    private readonly Dictionary<string, HashSet<string>> _reportedBufferFailures = new(StringComparer.Ordinal);
    // Project ownership per open buffer. Resolution depends on the buffer path,
    // open runic.json overlays, and the project index, so it is invalidated by
    // the buffer's own revision, by any configuration buffer change, and by
    // every project index refresh. Unrelated keystrokes reuse it.
    private readonly Dictionary<string, (string? Project, Exception? Error)> _bufferProjects = new(StringComparer.Ordinal);
    private const int MaximumProjectIndexEntries = 100_000;
    private bool _globalDiagnosticsRefreshPending;
    private bool _projectIndexRescanPending;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal Rmf2LanguageServer(Stream input, Stream output, Action<string>? beforeRequest = null,
        Action<string>? requestCancelled = null)
    {
        _input = input;
        _output = output;
        _beforeRequest = beforeRequest;
        _requestCancelled = requestCancelled;
    }
    private CancellationToken _requestCancellation;
    private long _latestRevision;
    private long _processedRevision;
    private sealed class ContentModifiedException : Exception { }
    internal int Run()
    {
        // The reader remains responsive to cancellation while one worker owns document state.
        using var queue = new BlockingCollection<(JsonObject Request, CancellationTokenSource Cancellation, long Revision)>(256);
        var pending = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        Task<int> worker = Task.Run(() => {
            foreach (var item in queue.GetConsumingEnumerable())
            {
                var request = item.Request; string method = request["method"]?.GetValue<string>() ?? "";
                JsonNode? id = request["id"]?.DeepClone();
                _requestCancellation = item.Cancellation.Token; _processedRevision = item.Revision;
                try
                {
                    _requestCancellation.ThrowIfCancellationRequested();
                    // Tests can coordinate the in-process worker through the
                    // internal constructor. The production stdio entry point
                    // never supplies a hook or exposes a barrier method.
                    _beforeRequest?.Invoke(method);
                    if (method == "exit") return _shutdown ? 0 : 1;
                    JsonNode? result = Handle(method, request["params"] as JsonObject ?? new JsonObject());
                    lock (_revisionGate)
                    {
                        // Project discovery commits its replacement index under
                        // this same gate. Cancellation after that commit point
                        // is completion, not a cancelled partial transaction.
                        if (method != "initialize") _requestCancellation.ThrowIfCancellationRequested();
                        if (id is not null && (request["params"]?["textDocument"] is not null || method is "workspace/executeCommand" or "codeAction/resolve") && item.Revision != _latestRevision) throw new ContentModifiedException();
                        if (id is not null) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
                    }
                }
                catch (Exception error) when (error is ContentModifiedException or OperationCanceledException or ToolUsageException or ToolDiagnosticException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or TranslationAuthoringException or KeyNotFoundException or IOException or FormatException or OverflowException or TranslationFormatException or TranslationPackException or TranslationContractException or System.Text.Json.JsonException)
                {
                    if (id is not null) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = error is ContentModifiedException ? -32801 : error is OperationCanceledException ? -32800 : -32602, ["message"] = error is ContentModifiedException ? "Document content changed while the request was pending." : error is OperationCanceledException ? "Request cancelled." : error.Message } });
                }
                finally
                {
                    lock (_revisionGate)
                    {
                        if (id is not null) pending.Remove(id.ToJsonString());
                        item.Cancellation.Dispose();
                        // A newer state revision is queued: it sends these publications, or
                        // supersedes them, once it has been processed.
                        if (item.Revision == _latestRevision) SendPublications();
                    }
                }
            }
            return 0;
        });
        try
        {
            while (Read() is { } request)
            {
                string method = request["method"]?.GetValue<string>() ?? "";
                if (method == "$/cancelRequest")
                {
                    string? target = request["params"]?["id"]?.ToJsonString();
                    bool observed = false;
                    lock (_revisionGate)
                    {
                        if (target is not null && pending.TryGetValue(target, out var cancellation))
                        {
                            cancellation.Cancel();
                            observed = true;
                        }
                    }
                    if (observed) _requestCancelled?.Invoke(target!);
                    continue;
                }
                var source = new CancellationTokenSource();
                long revision;
                lock (_revisionGate) {
                    if (method is "textDocument/didOpen" or "textDocument/didChange" or "textDocument/didClose" or "workspace/didChangeWatchedFiles" or "workspace/didChangeConfiguration") _latestRevision++;
                    revision = _latestRevision;
                    if (request["id"] is { } id) pending[id.ToJsonString()] = source;
                }
                while (!queue.TryAdd((request, source, revision), 100))
                    if (worker.IsCompleted) { source.Dispose(); worker.GetAwaiter().GetResult(); throw new IOException("Language server worker stopped."); }
                if (method == "exit") break;
            }
        }
        finally { queue.CompleteAdding(); worker.GetAwaiter().GetResult(); }
        return worker.GetAwaiter().GetResult();
    }
    private JsonNode? Handle(string method, JsonObject args)
    {
        if (method is "textDocument/didOpen" or "textDocument/didChange" or "textDocument/didClose" or "textDocument/didSave" or
            "workspace/didChangeWatchedFiles" or "workspace/didChangeConfiguration") _reviewViews.Clear();
        if (method == "initialize")
        {
            _configurationSync = args["initializationOptions"]?["runicConfigurationSync"]?.GetValue<bool>() == true;
            _codeDescriptionSupport = args["capabilities"]?["textDocument"]?["publishDiagnostics"]?["codeDescriptionSupport"]?.GetValue<bool>() == true;
            if (args["workspaceFolders"] is JsonArray folders)
                foreach (var folder in folders) _workspaceRoots.Add(LocalPath(folder!["uri"]!.GetValue<string>()));
            else if (args["rootUri"] is JsonValue rootUri) _workspaceRoots.Add(LocalPath(rootUri.GetValue<string>()));
            RefreshProjectIndex(rescanWorkspace: true);
            _fileOperations = args["capabilities"]?["workspace"]?["workspaceEdit"]?["resourceOperations"] is JsonArray operations && operations.Any(v => v?.GetValue<string>() == "create") && operations.Any(v => v?.GetValue<string>() == "delete");
            var encodings = args["capabilities"]?["general"]?["positionEncodings"] as JsonArray;
            _encoding = encodings?.Select(v => v!.GetValue<string>()).FirstOrDefault(v => v is "utf-8" or "utf-16" or "utf-32") ?? "utf-16";
            return new JsonObject { ["capabilities"] = new JsonObject {
                ["positionEncoding"] = _encoding, ["textDocumentSync"] = 2, ["documentSymbolProvider"] = true,
                ["semanticTokensProvider"] = new JsonObject { ["legend"] = new JsonObject { ["tokenTypes"] = new JsonArray("namespace", "property", "variable", "function", "keyword", "string", "comment", "type"), ["tokenModifiers"] = new JsonArray() }, ["full"] = true },
                ["referencesProvider"] = true, ["foldingRangeProvider"] = true, ["hoverProvider"] = true, ["definitionProvider"] = true,
                ["documentFormattingProvider"] = true, ["renameProvider"] = true,
                ["codeActionProvider"] = new JsonObject { ["codeActionKinds"] = new JsonArray("quickfix"), ["resolveProvider"] = true },
                ["executeCommandProvider"] = new JsonObject { ["commands"] = new JsonArray("runic.extractGroup", "runic.inlineResource", "runic.markReviewed", "runic.preview", "runic.renameInput", "runic.renameSlot", "runic.renameResource", "runic.renderPreview") },
                ["completionProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray("$", ":", "#", "/", "=") },
            }, ["serverInfo"] = new JsonObject { ["name"] = "Runic RMF2", ["version"] = "1" } };
        }
        if (method == "shutdown") { _shutdown = true; return null; }
        if (method is "initialized" or "$/cancelRequest") return null;
        if (method == "codeAction/resolve")
        {
            // A "mark reviewed" action is a command, complete as offered.
            if (args["data"] is null && args["command"] is JsonObject) return args.DeepClone();
            JsonNode data = args["data"] ?? throw new ArgumentException("Missing quick-fix revision evidence.");
            string actionUri = data["uri"]?.GetValue<string>() ?? throw new ArgumentException("Missing quick-fix document URI.");
            int actionVersion = data["version"]?.GetValue<int>() ?? throw new ArgumentException("Missing quick-fix document version.");
            string actionRevision = data["revision"]?.GetValue<string>() ?? throw new ArgumentException("Missing quick-fix source revision.");
            string fixId = data["fixId"]?.GetValue<string>() ?? throw new ArgumentException("Missing quick-fix ID.");
            if (!_buffers.TryGetValue(actionUri, out Buffer? actionBuffer)) throw new ContentModifiedException();
            var source = new TranslationSource(LocalPath(actionUri), Utf8.GetBytes(actionBuffer.Text));
            if (actionBuffer.Version != actionVersion || Rmf2DiagnosticActions.Revision(source) != actionRevision)
                throw new ContentModifiedException();
            Rmf2DiagnosticQuickFix? fix = Rmf2DiagnosticActions.GetQuickFixes(actionBuffer.Syntax, _requestCancellation)
                .Concat(ProjectQuickFixes(actionUri, actionBuffer).Fixes).FirstOrDefault(candidate => candidate.Id == fixId);
            if (fix is null) throw new ContentModifiedException();
            return QuickFixAction(actionUri, actionBuffer, fix, args["isPreferred"]?.GetValue<bool>() == true);
        }
        if (method is "workspace/didChangeWatchedFiles" or "workspace/didChangeConfiguration")
        {
            // File watchers and configuration synchronization are notifications,
            // but they still invalidate diagnostics for every open RMF2 buffer.
            // Do this before looking for textDocument: workspace notifications
            // intentionally have no document field.
            QueueGlobalDiagnosticsRefresh(rescanWorkspace: method == "workspace/didChangeWatchedFiles");
            FlushGlobalDiagnosticsRefresh();
            return null;
        }
        if (method == "workspace/executeCommand")
        {
            string command = args["command"]!.GetValue<string>();
            JsonArray arguments = args["arguments"]!.AsArray();
            string sourceUri = arguments[0]!.GetValue<string>(); string sourcePath = LocalPath(sourceUri);
            var workspace = Workspace(sourcePath);
            if (command == "runic.markReviewed")
                return MarkReviewed(sourcePath, arguments[1]!.GetValue<string>(), arguments[2]!.GetValue<string>());
            if (command is "runic.preview" or "runic.renderPreview")
            {
                string key = arguments[1]!.GetValue<string>(), locale = arguments[2]!.GetValue<string>();
                Rmf2ProjectCompilation compilation = workspace.Validate();
                if (!compilation.Success) throw new TranslationAuthoringException(string.Join("; ", compilation.Diagnostics.Select(d => d.Message)));
                Rmf2ProjectV5 project = compilation.Project!;
                JsonNode message;
                JsonNode markup;
                JsonArray inputContract;
                if (command == "runic.renderPreview") return Rmf2Preview.Render(project, key, locale, arguments[3]!.AsObject());
                JsonNode artifact = JsonNode.Parse(Rmf2LocaleArtifactV5.Render(project, locale).Text)!;
                message = artifact["messages"]?[key]?["ast"] ?? throw new TranslationAuthoringException("Unknown preview message.");
                markup = JsonNode.Parse(project.MarkupContract)!;
                Rmf2MessageContractV5 contract = project.CanonicalMessages.Concat(project.ExtraMessages).Single(item => item.Key == key);
                inputContract = new JsonArray(contract.Inputs.Select(input => (JsonNode)new JsonObject {
                    ["name"] = input.Name,
                    ["type"] = input.Type,
                }).ToArray());
                var examples = workspace.Documents.SelectMany(doc => doc.Nodes.Where(node => !node.IsGroup && string.Join('_', workspace.LogicalPath(doc.Source.Path, node)) == key))
                    .SelectMany(node => node.Properties.Where(value => value.StartsWith("example ", StringComparison.Ordinal)).Select(value => JsonNode.Parse(value.Substring(8))));
                return new JsonObject { ["key"] = key, ["locale"] = locale, ["ast"] = message.DeepClone(), ["inputs"] = inputContract, ["markup"] = markup, ["examples"] = new JsonArray(examples.ToArray()) };
            }
            if (command == "runic.renameResource")
                return WorkspaceEdit(workspace.Rename(arguments[1]!.AsArray().Select(value => value!.GetValue<string>()).ToArray(), arguments[2]!.GetValue<string>()));
            if (command == "runic.renameSlot")
                return WorkspaceEdit(workspace.RenameSlot(sourcePath, arguments[1]!.GetValue<string>(), arguments[2]!.GetValue<string>(), arguments[3]!.GetValue<string>()));
            if (command == "runic.renameInput")
                return WorkspaceEdit(workspace.RenameInput(sourcePath, arguments[1]!.GetValue<string>(), arguments[2]!.GetValue<string>(), arguments[3]!.GetValue<string>()));
            if (!_fileOperations) throw new InvalidOperationException("The client does not support the required file operations.");
            var plan = command switch {
                "runic.extractGroup" => workspace.Extract(sourcePath, arguments[1]!.AsArray().Select(v => v!.GetValue<string>()).ToArray()),
                "runic.inlineResource" => workspace.Inline(sourcePath, LocalPath(arguments[1]!.GetValue<string>())),
                _ => throw new ArgumentException("Unknown RMF2 workspace command."),
            };
            return WorkspaceEdit(plan);
        }
        var document = args["textDocument"] as JsonObject;
        if (document is null) return null;
        string uri = document["uri"]!.GetValue<string>();
        if (method == "textDocument/didClose")
        {
            bool configuration = Path.GetFileName(LocalPath(uri)).Equals("runic.json", StringComparison.OrdinalIgnoreCase);
            try
            {
                string? closedProject = !configuration && _buffers.ContainsKey(uri) ? BufferProject(uri, out _) : null;
                _buffers.Remove(uri); _reportedBufferFailures.Remove(uri);
                string key = FileKey(LocalPath(uri));
                if (_bufferPaths.TryGetValue(key, out string? mapped) && mapped == uri)
                {
                    _bufferPaths.Remove(key);
                    // The client may still have the file open under another spelling of its URI.
                    string? other = _buffers.Keys.Order(StringComparer.Ordinal).FirstOrDefault(candidate => PathComparer.Equals(FileKey(LocalPath(candidate)), key));
                    if (other is not null) _bufferPaths[key] = other;
                }
                if (configuration) _bufferProjects.Clear(); else _bufferProjects.Remove(uri);
                if (configuration) _projectLayouts.Clear(); else if (closedProject is not null) _projectLayouts.Remove(closedProject);
                Publish(uri, new JsonArray());
                // Diagnostics other projects report for the saved file return to the client.
                PublishProjectFile(key);
                if (configuration) QueueGlobalDiagnosticsRefresh();
                else if (closedProject is not null)
                {
                    // The closed file is still a project file: while its project has open buffers,
                    // its saved content is validated again and its diagnostics stay in the Problems view.
                    string[] remaining = BufferUris(closedProject).ToArray();
                    if (remaining.Length > 0) ValidateAndPublish(closedProject, remaining);
                    else ClearProjectFileDiagnostics(closedProject);
                }
            }
            finally { FlushGlobalDiagnosticsRefresh(); }
            return null;
        }
        if (method == "textDocument/didOpen")
        {
            try { Update(uri, document["text"]!.GetValue<string>(), document["version"]!.GetValue<int>()); }
            finally { FlushGlobalDiagnosticsRefresh(); }
            return null;
        }
        if (!_buffers.TryGetValue(uri, out Buffer? buffer)) throw new InvalidOperationException("Document is not open.");
        if (method == "textDocument/didChange")
        {
            try
            {
                int version = document["version"]!.GetValue<int>();
                if (version <= buffer.Version) throw new InvalidOperationException("Stale document revision.");
                string text = buffer.Text;
                foreach (JsonNode? change in (JsonArray)args["contentChanges"]!)
                {
                    string replacement = change!["text"]!.GetValue<string>();
                    if (change["range"] is JsonObject range)
                    { int from = Offset(text, range["start"]!), to = Offset(text, range["end"]!); if (to < from) throw new ArgumentException("Invalid edit range."); text = text[..from] + replacement + text[to..]; }
                    else text = replacement;
                }
                Update(uri, text, version);
            }
            finally { FlushGlobalDiagnosticsRefresh(); }
            return null;
        }
        if (method == "runic/documentInfo") return DocumentInfo(uri);
        if (Path.GetFileName(LocalPath(uri)) == "runic.json") return null;
        if (method == "textDocument/codeAction")
        {
            if (args["context"]?["only"] is JsonArray only && !only.Any(kind => kind?.GetValue<string>() is "" or "quickfix")) return new JsonArray();
            JsonObject requestedRange = args["range"]?.AsObject() ?? throw new ArgumentException("Missing code-action range.");
            int from = Utf8.GetByteCount(buffer.Text.AsSpan(0, Offset(buffer.Text, requestedRange["start"]!)));
            int to = Utf8.GetByteCount(buffer.Text.AsSpan(0, Offset(buffer.Text, requestedRange["end"]!)));
            if (to < from) throw new ArgumentException("Invalid code-action range.");
            // A fix applies at its diagnostic and at the source it edits (RTR0078 is reported at the
            // message name, but the line break is further down).
            bool AtEdit(Rmf2DiagnosticQuickFix fix) => fix.StartByte <= to && from <= fix.StartByte + fix.LengthBytes;
            // Document repairs, then project repairs (RTR0095: add the missing plural variants) and, for a stale
            // review entry (RTR0090), "mark reviewed", which records the current source in the review file.
            (IReadOnlyList<Rmf2DiagnosticQuickFix> projectFixes, IReadOnlyList<TranslationReviewStaleEntry> stale) = ProjectQuickFixes(uri, buffer);
            Rmf2DiagnosticQuickFix[] fixes = Rmf2DiagnosticActions.GetQuickFixes(buffer.Syntax, _requestCancellation).Concat(projectFixes).Where(fix => fix.Location.StartByte <= to &&
                from <= fix.Location.StartByte + fix.Location.LengthBytes || AtEdit(fix)).ToArray();
            // Several fixes can share one diagnostic (one RTR0078 join per line break, all reported at
            // the message name). Only a fix at the requested source, or the only fix for its
            // diagnostic, is preferred, so a client never auto-applies an arbitrary one of them.
            var actions = new JsonArray(fixes.Select(fix => (JsonNode)QuickFixAction(uri, buffer, fix, AtEdit(fix) ||
                fixes.Count(other => other.DiagnosticId == fix.DiagnosticId && other.Location.StartByte == fix.Location.StartByte && other.Location.LengthBytes == fix.Location.LengthBytes) == 1)).ToArray());
            foreach (TranslationReviewStaleEntry item in stale.Where(item => item.Location.StartByte <= to && from <= item.Location.StartByte + item.Location.LengthBytes))
            {
                string title = $"Mark the '{item.Entry.Locale}' translation of '{item.Entry.Key}' reviewed ({TranslationReviewFile.StateText(item.Entry.State)})";
                actions.Add((JsonNode)new JsonObject { ["title"] = title, ["kind"] = "quickfix",
                    ["command"] = new JsonObject { ["title"] = title, ["command"] = "runic.markReviewed",
                        ["arguments"] = new JsonArray((JsonNode)JsonValue.Create(uri), JsonValue.Create(item.Entry.Key), JsonValue.Create(item.Entry.Locale)) } });
            }
            return actions;
        }
        if (method == "textDocument/semanticTokens/full") return SemanticTokens(buffer);
        if (method == "textDocument/documentSymbol")
            return new JsonArray(buffer.Syntax.Nodes.Select(node => {
                var symbol = new JsonObject {
                    ["name"] = node.Path[^1], ["detail"] = string.Join('.', node.Path), ["kind"] = node.IsGroup ? 3 : 13,
                    ["range"] = Range(buffer, node.Location), ["selectionRange"] = Range(buffer, node.NameLocation),
                };
                // Document messages list their headings as an outline (kind String, as in Markdown).
                if (node.MessageSyntax is { } messageSyntax)
                {
                    var headings = Rmf2DocumentSyntax.Blocks(messageSyntax).Where(block => block.Name == "runic:h").Select(block => {
                        string text = Rmf2DocumentSyntax.HeadingText(messageSyntax, block);
                        return (JsonNode)new JsonObject {
                            ["name"] = text.Length == 0 ? Rmf2DocumentSyntax.Describe(block) : text, ["detail"] = Rmf2DocumentSyntax.Describe(block), ["kind"] = 15,
                            ["range"] = Range(buffer, MessageLocation(buffer, node, block.Open.Location.StartByte, block.End)),
                            ["selectionRange"] = Range(buffer, MessageLocation(buffer, node, block.Open.Location.StartByte, block.ContentStart)),
                        };
                    }).ToArray();
                    if (headings.Length > 0) symbol["children"] = new JsonArray(headings);
                }
                return (JsonNode)symbol;
            }).ToArray());
        if (method == "textDocument/foldingRange")
        {
            var ranges = buffer.Syntax.Nodes.Where(n => n.Location.EndLine > n.NameLocation.Line).Select(node => (JsonNode)new JsonObject {
                ["startLine"] = node.NameLocation.Line - 1, ["endLine"] = node.Location.EndLine - (node.Location.EndColumn == 1 ? 2 : 1), ["kind"] = "region",
            }).ToList();
            // Blocks of document messages that span lines fold from their open tag to their close tag.
            foreach (Rmf2ResourceNode node in buffer.Syntax.Nodes.Where(n => n.MessageSyntax is not null))
                foreach (var block in Rmf2DocumentSyntax.Blocks(node.MessageSyntax!))
                {
                    int startLine = Range(buffer, MessageLocation(buffer, node, block.Open.Location.StartByte, block.ContentStart))["start"]!["line"]!.GetValue<int>();
                    int endLine = Range(buffer, MessageLocation(buffer, node, block.ContentEnd, block.End))["start"]!["line"]!.GetValue<int>();
                    if (endLine > startLine) ranges.Add(new JsonObject { ["startLine"] = startLine, ["endLine"] = endLine, ["kind"] = "region" });
                }
            return new JsonArray(ranges.ToArray());
        }
        if (method == "textDocument/formatting")
        {
            string formatted = Utf8.GetString(Rmf2ResourceWriter.Format(buffer.Syntax.Source));
            if (formatted == buffer.Text) return new JsonArray();
            return new JsonArray(new JsonObject { ["range"] = new JsonObject { ["start"] = Position(buffer.Text, 0), ["end"] = Position(buffer.Text, buffer.Text.Length) }, ["newText"] = formatted });
        }
        int offset = args["position"] is JsonNode position ? Offset(buffer.Text, position) : 0;
        int atByte = Utf8.GetByteCount(buffer.Text.AsSpan(0, offset));
        Rmf2ResourceNode? entry = buffer.Syntax.Nodes.LastOrDefault(n => n.Location.StartByte <= atByte && atByte <= n.Location.StartByte + n.Location.LengthBytes);
        if (method == "runic/message")
        {
            if (entry is null) throw new TranslationAuthoringException("Place the cursor inside a resource or group.");
            string sourcePath = LocalPath(uri); var workspace = Workspace(sourcePath);
            var locals = entry.MessageSyntax?.Declarations.Where(declaration => declaration.Kind == "local").Select(declaration => declaration.Name).ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>();
            var inputs = entry.MessageSyntax?.Tokens.Where(token => token.Kind == Mf2SyntaxTokenKind.Variable && !locals.Contains(token.Value)).Select(token => token.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal) ?? Enumerable.Empty<string>();
            var slots = entry.MessageSyntax?.Expressions.SelectMany(expression => expression.Options.Where(option => option.Name == "ref" && option.Value is { Kind: not Mf2OperandKind.Variable })).Select(option => option.Value!.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal) ?? Enumerable.Empty<string>();
            return new JsonObject {
                ["key"] = string.Join('_', workspace.LogicalPath(sourcePath, entry)), ["localKey"] = entry.Key, ["isGroup"] = entry.IsGroup,
                ["logicalPath"] = new JsonArray(workspace.LogicalPath(sourcePath, entry).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["path"] = new JsonArray(entry.Path.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["locale"] = workspace.Locale(sourcePath),
                ["locales"] = new JsonArray(workspace.Documents.Select(document => workspace.Locale(document.Source.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["inputs"] = new JsonArray(inputs.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["slots"] = new JsonArray(slots.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            };
        }
        if (method == "textDocument/rename")
        {
            if (entry is null) throw new InvalidOperationException("The position is not on a resource symbol.");
            string path = LocalPath(uri); var workspace = Workspace(path);
            TranslationWorkspaceTransactionPlan plan;
            if (atByte >= entry.NameLocation.StartByte && atByte <= entry.NameLocation.StartByte + entry.NameLocation.LengthBytes)
            {
                RequireResourceOnlyWorkspace(path);
                plan = workspace.Rename(workspace.LogicalPath(path, entry), args["newName"]!.GetValue<string>());
            }
            else
            {
                var syntax = entry.MessageSyntax;
                var local = syntax?.Declarations.FirstOrDefault(d => d.Kind == "local" && syntax.VariableReferences(d.Name).Any(location => entry.MessageByteMap[location.StartByte] <= atByte && atByte < entry.MessageByteMap[location.StartByte + location.LengthBytes]));
                if (local is null) throw new InvalidOperationException("Rename requires a resource symbol or an MF2 local; caller-input changes require a catalog-wide plan.");
                plan = workspace.RenameLocal(path, entry.Key, local.Name, args["newName"]!.GetValue<string>());
            }
            if (!_fileOperations && plan.Edits.Any(e => e.Kind != TranslationWorkspaceEditKind.Replace)) throw new InvalidOperationException("Rename requires client file-operation support.");
            return WorkspaceEdit(plan);
        }
        if (method == "textDocument/hover")
        {
            if (entry is null) return null;
            string path = LocalPath(uri); var workspace = Workspace(path);
            string key = string.Join('_', workspace.LogicalPath(path, entry));
            string summary = entry.MessageSyntax is { } messageSyntax ? workspace.LanguageService.Hover(messageSyntax, MessageOffset(entry, atByte)) ?? key : key;
            var contractLines = new List<string>();
            Rmf2ResourceNode? baseEntry = null;
            string? reviewState = null;
            string locale = workspace.Locale(path);
            if (!entry.IsGroup)
            {
                // Translator context: the base-locale source of the same logical message, read from
                // the workspace (open buffers included), so it is shown even when the catalog has errors.
                IReadOnlyList<string> logical = workspace.LogicalPath(path, entry);
                if (!string.Equals(locale, workspace.BaseLocale, StringComparison.OrdinalIgnoreCase))
                    baseEntry = workspace.Documents.Where(document => string.Equals(workspace.Locale(document.Source.Path), workspace.BaseLocale, StringComparison.OrdinalIgnoreCase))
                        .SelectMany(document => document.Nodes.Where(node => !node.IsGroup && workspace.LogicalPath(document.Source.Path, node).SequenceEqual(logical, StringComparer.Ordinal)))
                        .FirstOrDefault();
            }
            string hoverProject = ProjectDirectory(path, allowExternalAncestor: true);
            ReviewView view = ReviewViewOf(hoverProject, BufferUris(hoverProject).ToArray(), workspace);
            Rmf2ProjectCompilation compiled = view.Compilation;
            if (!entry.IsGroup)
                reviewState = Rmf2ReviewFileState.Describe(view.Review.Project?.DefaultLocale, view.Fingerprints, view.File, view.Error, key, locale);
            if (compiled.Success && !entry.IsGroup)
            {
                Rmf2ProjectV5 project = compiled.Project!;
                Rmf2TranslationV5? value = project.Locales.FirstOrDefault(item => item.Tag == locale)?.ResolvedResources.FirstOrDefault(item => item.Key == key);
                Rmf2MessageContractV5? contract = project.CanonicalMessages.Concat(project.ExtraMessages).FirstOrDefault(item => item.Key == key);
                if (value is not null) contractLines.Add("Content locale: " + value.ContentLocale);
                if (contract is { Inputs.Count: > 0 }) contractLines.Add("Inputs: " + string.Join(", ", contract.Inputs.Select(input => "$" + input.Name + ": " + input.Type)));
                if (contract is not null) contractLines.Add("Content: " + contract.Content);
                // The locked block structure translations must keep, one skeleton per distinct source variant.
                if (contract is { Content: Rmf2DocumentProfileV5.Document })
                    contractLines.Add("Structure: " + string.Join(" | ", contract.Skeletons.Select(skeleton => skeleton.Length == 0 ? "(empty)" : skeleton)));
                string? fallback = project.Locales.FirstOrDefault(item => item.Tag == locale)?.FallbackTag;
                if (fallback is not null) contractLines.Add("Fallback: " + fallback);
            }
            string content = new Rmf2HoverContent {
                Summary = summary, BaseLocale = workspace.BaseLocale, BaseText = baseEntry?.Message,
                BaseComments = baseEntry?.Comments ?? [],
                BaseParameters = baseEntry?.Properties.Where(property => property.StartsWith("param ", StringComparison.Ordinal)).ToArray() ?? [],
                ReviewState = reviewState, Contract = contractLines, Comments = entry.Comments, Properties = entry.Properties,
            }.Render();
            return new JsonObject { ["contents"] = new JsonObject { ["kind"] = "plaintext", ["value"] = content } };
        }
        if (method is "textDocument/definition" or "textDocument/references" && entry?.MessageSyntax is { } symbolSyntax)
        {
            int messageByte = MessageOffset(entry, atByte);
            string? symbol = symbolSyntax.Declarations.Select(d => d.Name)
                .Concat(symbolSyntax.Tokens.Where(t => t.Kind == Mf2SyntaxTokenKind.Variable).Select(t => t.Value))
                .Distinct(StringComparer.Ordinal).FirstOrDefault(name => symbolSyntax.VariableReferences(name).Any(location => location.StartByte <= messageByte && messageByte < location.StartByte + location.LengthBytes));
            if (symbol is not null)
            {
                bool includeDeclaration = args["context"]?["includeDeclaration"]?.GetValue<bool>() ?? true;
                var references = Workspace(LocalPath(uri)).VariableReferences(LocalPath(uri), entry.Key, symbol);
                var selected = references.Where(reference => method == "textDocument/definition" ? reference.IsDeclaration : includeDeclaration || !reference.IsDeclaration);
                return new JsonArray(selected.Select(reference => {
                    var bytes = reference.Document.Source.GetUtf8Bytes();
                    string text = Utf8.GetString(bytes);
                    return (JsonNode)new JsonObject { ["uri"] = ClientUri(reference.Document.Source.Path),
                        ["range"] = new JsonObject {
                            ["start"] = Position(text, Utf8.GetCharCount(bytes.AsSpan(0, reference.Resource.MessageByteMap[reference.Location.StartByte]))),
                            ["end"] = Position(text, Utf8.GetCharCount(bytes.AsSpan(0, reference.Resource.MessageByteMap[reference.Location.StartByte + reference.Location.LengthBytes]))),
                        } };
                }).ToArray());
            }
            if (method == "textDocument/references") return new JsonArray();
        }
        if (method == "textDocument/definition")
        {
            if (entry is null) return new JsonArray();
            string path = LocalPath(uri); var workspace = Workspace(path); var logical = workspace.LogicalPath(path, entry);
            return new JsonArray(workspace.Documents.SelectMany(document => document.Nodes.Where(node => workspace.LogicalPath(document.Source.Path, node).SequenceEqual(logical, StringComparer.Ordinal))
                .Select(node => (JsonNode)new JsonObject { ["uri"] = ClientUri(document.Source.Path), ["range"] = Range(new Buffer(Utf8.GetString(document.Source.GetUtf8Bytes()), 0, document), node.NameLocation) })).ToArray());
        }
        if (method == "textDocument/completion")
        {
            var labels = new HashSet<string>(StringComparer.Ordinal) { "one", "two", "few", "many", "zero", "*" };
            string sourcePath = LocalPath(uri); var workspace = Workspace(sourcePath);
            var completions = entry is { IsGroup: false } ? workspace.Complete(sourcePath, entry.Key, MessageOffset(entry, atByte)) : workspace.LanguageService.Complete(null, 0);
            var items = completions.ToDictionary(item => item.Label, item => item.Detail, StringComparer.Ordinal);
            foreach (string label in labels) items.TryAdd(label, "Runic semantic translation contract");
            return new JsonArray(items.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => (JsonNode)new JsonObject { ["label"] = item.Key, ["detail"] = item.Value, ["kind"] = 14 }).ToArray());
        }
        return null;
    }
    // The owning catalog and the file's locale, for a client's status display. A file that no
    // project owns, or a project whose manifest cannot be read yet, answers with null members
    // rather than an error: the client shows the state, not a failure.
    private JsonObject DocumentInfo(string uri)
    {
        string path = LocalPath(uri);
        bool configuration = Path.GetFileName(path).Equals("runic.json", StringComparison.OrdinalIgnoreCase);
        string? project = configuration ? Path.GetDirectoryName(Path.GetFullPath(path)) : BufferProject(uri, out _);
        var result = new JsonObject { ["project"] = null, ["catalog"] = null, ["baseLocale"] = null, ["locale"] = null };
        if (project is null) return result;
        string manifestPath = Path.Combine(project, "runic.json");
        result["project"] = ClientUri(manifestPath);
        try
        {
            byte[] bytes = TryGetBuffer(manifestPath, out _, out Buffer? manifestBuffer) ? Utf8.GetBytes(manifestBuffer.Text) : File.ReadAllBytes(manifestPath);
            if (JsonNode.Parse(bytes) is JsonObject manifest)
            {
                if (manifest["catalog"] is JsonValue catalog && catalog.TryGetValue(out string? catalogId)) result["catalog"] = catalogId;
                if (manifest["baseLocale"] is JsonValue baseLocale && baseLocale.TryGetValue(out string? baseTag)) result["baseLocale"] = baseTag;
            }
            if (!configuration)
            {
                // The locale depends only on the path and the project's layout. The layout is
                // recorded whenever the project is validated, which every edit already does.
                if (!_projectLayouts.TryGetValue(project, out bool direct)) direct = Workspace(project, BufferUris(project).ToArray()).IsDirect;
                result["locale"] = Rmf2Workspace.Locale(path, direct);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or TranslationAuthoringException or ToolUsageException or ToolDiagnosticException) { }
        return result;
    }

    // Physical source location of a message byte range.
    private static TextSourceLocation MessageLocation(Buffer buffer, Rmf2ResourceNode node, int from, int to)
    {
        int last = node.MessageByteMap.Count - 1;
        int start = node.MessageByteMap[Math.Clamp(from, 0, last)], end = node.MessageByteMap[Math.Clamp(to, 0, last)];
        return new TextSourceLocation(buffer.Syntax.Source.Path, start, Math.Max(0, end - start), 0, 0, 0, 0);
    }
    private static int MessageOffset(Rmf2ResourceNode entry, int physicalByte)
    {
        if (entry.MessageByteMap.Count == 0 || physicalByte < entry.MessageByteMap[0]) return -1;
        int index = 0;
        while (index + 1 < entry.MessageByteMap.Count && entry.MessageByteMap[index + 1] <= physicalByte) index++;
        return index;
    }
    private void RequireResourceOnlyWorkspace(string path)
    {
        // Native application-language services own call sites. Never return a partial F2 edit.
        string root = _workspaceRoots.Where(value => IsWithin(value, path)).OrderByDescending(value => value.Length).FirstOrDefault()
            ?? throw new TranslationAuthoringException("Rename requires an explicit workspace root. Use Rename Resource in Resource Sources for an intentionally bounded edit.");
        var directories = new Stack<string>(); directories.Push(root);
        int visited = 0;
        while (directories.Count > 0)
        {
            _requestCancellation.ThrowIfCancellationRequested();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directories.Pop()))
            {
                if (++visited > 100_000) throw new TranslationAuthoringException("Workspace is too large to establish safe rename scope. Use Rename Resource in Resource Sources explicitly.");
                if (Path.GetFileName(entry) is ".git" or "node_modules" or "bin" or "obj" or "dist" or "artifacts" or ".cache" or ".direnv" or ".vs") continue;
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new TranslationAuthoringException("Rename refused: linked workspace sources cannot be safely indexed. Use Rename Resource in Resource Sources explicitly.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (Path.GetFileName(entry) is not (".git" or "node_modules" or "bin" or "obj" or "dist" or "artifacts" or ".cache" or ".direnv" or ".vs")) directories.Push(entry);
                }
                else if (Path.GetExtension(entry).ToLowerInvariant() is ".cs" or ".ts" or ".tsx" or ".js" or ".jsx" or ".svelte" or ".resx" or ".razor" or ".xaml" or ".cpp" or ".h" or ".mf2")
                    throw new TranslationAuthoringException("Rename refused: this workspace contains application or legacy sources whose references Runic cannot safely rewrite. Use the native language refactor, or explicitly choose Rename Resource in Resource Sources and update call sites separately.");
            }
        }
    }
    private static bool IsWithin(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
    // TranslationSource uses portable separators, including on Windows. Buffer overlays
    // must use the same key as disk sources or the workspace receives duplicates.
    private static string LocalPath(string uri) => new Uri(uri).LocalPath.Replace('\\', '/');
    // One key per file, whatever URI spelling or relative form names it; compared with PathComparer.
    internal static string FileKey(string path) => Path.GetFullPath(path).Replace('\\', '/');

    // The open buffer of a file, found by path rather than by the URI text the client chose.
    private bool TryGetBuffer(string path, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? uri,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Buffer? buffer)
    {
        buffer = null;
        return _bufferPaths.TryGetValue(FileKey(path), out uri) && _buffers.TryGetValue(uri, out buffer);
    }

    // The URI the client uses for an open file; .NET's form for a closed one.
    private string ClientUri(string path) => TryGetBuffer(path, out string? uri, out _) ? uri : new Uri(path).AbsoluteUri;

    private void RefreshProjectIndex(bool rescanWorkspace)
    {
        _bufferProjects.Clear();
        _projectLayouts.Clear();
        if (rescanWorkspace)
            ReplaceProjectIndex(_workspaceRoots, _diskProjectDirectories, MaximumProjectIndexEntries, _revisionGate, null, _requestCancellation);
        _projectDirectories.Clear();
        _projectDirectories.UnionWith(_diskProjectDirectories);
        // Unsaved new configurations participate only when they are contained
        // by an opened workspace and do not cross a linked directory boundary.
        foreach (string path in _buffers.Keys.Select(LocalPath)
            .Where(path => Path.GetFileName(path).Equals("runic.json", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal))
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            if (IsSafeWorkspacePath(path) && (!PathEntryExists(path) || IsRegularManifestFile(path))) _projectDirectories.Add(directory);
        }
    }

    internal static void ReplaceProjectIndex(
        IEnumerable<string> workspaceRoots,
        SortedSet<string> destination,
        int entryLimit,
        object commitGate,
        Action<string>? entryObserved,
        CancellationToken cancellationToken)
    {
        var discovered = new SortedSet<string>(PathComparer);
        int visited = 0;
        foreach (string root in workspaceRoots.Select(Path.GetFullPath).Distinct(PathComparer).Order(PathComparer))
        {
            if (!Directory.Exists(root)) continue;
            var pending = new SortedSet<string>(PathComparer) { root };
            if (++visited > entryLimit)
                throw new InvalidOperationException($"Workspace project discovery exceeds the bounded entry limit of {entryLimit}.");
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pending.Min!;
                pending.Remove(directory);
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++visited > entryLimit)
                        throw new InvalidOperationException($"Workspace project discovery exceeds the bounded entry limit of {entryLimit}.");
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        if ((attributes & FileAttributes.Directory) == 0)
                        {
                            if (Path.GetFileName(entry).Equals("runic.json", StringComparison.Ordinal))
                                discovered.Add(directory);
                        }
                        else
                        {
                            string name = Path.GetFileName(entry);
                            if (name is not (".git" or ".worktrees" or "node_modules" or "bin" or "obj" or "dist" or "artifacts" or ".cache" or ".direnv" or ".vs" or ".runic" or ".runic-translations"))
                                pending.Add(entry);
                        }
                    }
                    entryObserved?.Invoke(entry);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
        // Publish a new index only after a complete scan; cancellation or a
        // bounded-scan failure must not leave a partially replaced mapping.
        lock (commitGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination.Clear();
            destination.UnionWith(discovered);
        }
    }

    private static bool PathEntryExists(string path) => TryGetPathAttributes(path, out _);

    private static bool TryGetPathAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException) { attributes = default; return false; }
        catch (DirectoryNotFoundException) { attributes = default; return false; }
    }

    private static bool IsRegularManifestFile(string path)
    {
        if (!TryGetPathAttributes(path, out FileAttributes attributes)) return false;
        return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    }

    private bool HasUsableProjectManifest(string directory)
    {
        string path = Path.Combine(directory, "runic.json");
        if (IsRegularManifestFile(path)) return true;
        return !PathEntryExists(path) && TryGetBuffer(path, out _, out _);
    }

    private bool IsSafeWorkspacePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        foreach (string root in _workspaceRoots.Select(Path.GetFullPath).OrderByDescending(value => value.Length))
        {
            if (!IsWithin(root, fullPath)) continue;
            string? current = fullPath;
            while (current is not null && IsWithin(root, current))
            {
                if (TryGetPathAttributes(current, out FileAttributes attributes) &&
                    (attributes & FileAttributes.ReparsePoint) != 0) return false;
                if (string.Equals(current, root, StringComparison.Ordinal)) break;
                current = Path.GetDirectoryName(current);
            }
            return true;
        }
        return false;
    }

    private string ProjectDirectory(string path, bool allowExternalAncestor = false)
    {
        _requestCancellation.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        while (directory is not null)
        {
            if (_projectDirectories.Contains(directory) && HasUsableProjectManifest(directory)) return directory;
            if (allowExternalAncestor && IsRegularManifestFile(Path.Combine(directory, "runic.json"))) return directory;
            directory = Path.GetDirectoryName(directory);
        }

        // A mounted resource need not have its project in its ancestor chain.
        // Resolve it against the bounded set of conventional workspace projects
        // and open configuration buffers. Prefer the most-specific matching
        // source root, then the canonical project path for deterministic ties.
        string[] candidates = _projectDirectories.Where(HasUsableProjectManifest).ToArray();
        var matches = new List<(string Project, int Specificity)>();
        foreach (string candidate in candidates)
            foreach (string sourceRoot in ProjectSourceRoots(candidate).Where(IsSafeWorkspacePath))
                if (IsWithin(sourceRoot, fullPath)) matches.Add((candidate, Path.GetFullPath(sourceRoot).Length));
        if (matches.Count != 0)
            return matches.OrderByDescending(match => match.Specificity).ThenBy(match => match.Project, StringComparer.Ordinal).First().Project;
        throw new InvalidOperationException("No runic.json project contains this resource in its project directory or configured source roots.");
    }

    private string BufferProjectDirectory(string path)
    {
        if (!IsSafeWorkspacePath(path))
            throw new InvalidOperationException("Open translation buffers must remain inside an explicit workspace boundary.");
        // Direct ancestors are authoritative for nested projects. The index is
        // still required to associate a sibling mounted resource with a closed
        // manifest elsewhere in the workspace.
        string fullPath = Path.GetFullPath(path);
        string root = _workspaceRoots.Select(Path.GetFullPath).Where(candidate => IsWithin(candidate, fullPath))
            .OrderByDescending(candidate => candidate.Length).First();
        string? directory = Path.GetDirectoryName(fullPath);
        while (directory is not null && IsWithin(root, directory))
        {
            string config = Path.Combine(directory, "runic.json");
            bool buffered = TryGetBuffer(config, out _, out _) && (!PathEntryExists(config) || IsRegularManifestFile(config));
            if (IsRegularManifestFile(config) || buffered) return directory;
            if (string.Equals(directory, root, StringComparison.Ordinal)) break;
            directory = Path.GetDirectoryName(directory);
        }
        return ProjectDirectory(path);
    }

    private bool IsInsideWorkspaceRoot(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return _workspaceRoots.Any(root => IsWithin(Path.GetFullPath(root), fullPath));
    }

    // Resolves the project that owns an open buffer for overlays and diagnostics.
    // Buffers inside an explicit workspace root use the strict boundary rules
    // first, including the refusal of linked paths. When no project owns a safe
    // in-root buffer (a root opened on a feature folder below its runic.json),
    // or the buffer lies outside every root (Visual Studio loose-file mode, or
    // a root that does not contain the file), the buffer uses the same nearest
    // regular runic.json ancestor rule as explicit commands, so it joins exactly
    // the project a preview loads from disk. The workspace overlays buffers of
    // such an external project only onto sources it already reads from disk.
    private string ResolveBufferProjectDirectory(string path)
    {
        if (!IsInsideWorkspaceRoot(path)) return ProjectDirectory(path, allowExternalAncestor: true);
        try { return BufferProjectDirectory(path); }
        catch (InvalidOperationException) when (IsSafeWorkspacePath(path))
        {
            return ProjectDirectory(path, allowExternalAncestor: true);
        }
    }

    private string? BufferProject(string uri, out Exception? error)
    {
        if (!_bufferProjects.TryGetValue(uri, out var entry))
        {
            try { entry = (ResolveBufferProjectDirectory(LocalPath(uri)), null); }
            catch (Exception failure) when (failure is InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                entry = (null, failure);
            }
            _bufferProjects[uri] = entry;
        }
        error = entry.Error;
        return entry.Project;
    }

    private void ReportBufferFailure(string uri, Exception error)
    {
        // stdout is the LSP channel. Report each failure category once per open
        // buffer on stderr instead of once per keystroke; messages can carry
        // changing source positions, so they are not part of the key.
        if (!_reportedBufferFailures.TryGetValue(uri, out HashSet<string>? reported))
            _reportedBufferFailures[uri] = reported = new HashSet<string>(StringComparer.Ordinal);
        if (reported.Add(error.GetType().FullName ?? error.GetType().Name))
            Console.Error.WriteLine($"runic-rmf2: open buffer {uri} has no project overlay or catalog diagnostics: {error.Message}");
    }

    // Mounted-resource ownership reads the manifest through the same reader as
    // the command-line tool and the Translations Editor, so a buffer belongs to
    // exactly the project whose compilation discovers it. A manifest whose
    // source layout is unusable (including one bad entry while a mount is being
    // typed) contributes no mounts: its mounted buffers publish only their own
    // syntax diagnostics until it is fixed, while the open runic.json, which
    // still resolves through its directory, carries the located configuration
    // diagnostic. Buffers beneath the project directory resolve through the
    // ancestor rule, so no implicit-directory fallback is needed here.
    private string[] ProjectSourceRoots(string directory)
    {
        string projectPath = Path.Combine(directory, "runic.json");
        if (PathEntryExists(projectPath) && !IsRegularManifestFile(projectPath)) return [];
        byte[] bytes;
        if (TryGetBuffer(projectPath, out _, out Buffer? projectBuffer)) bytes = Utf8.GetBytes(projectBuffer.Text);
        else
        {
            try { bytes = File.ReadAllBytes(projectPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
        }
        TranslationManifestLayout layout = TranslationManifestReader.Read(bytes, directory);
        return layout.IsValid ? layout.SourceRoots.ToArray() : [];
    }

    private Rmf2Workspace Workspace(string path)
    {
        // Explicit language operations may target a complete project outside
        // the opened workspace (for example an inert preview fixture). Such a
        // project is loaded directly but never added to the watched index.
        string directory = ProjectDirectory(path, allowExternalAncestor: true);
        // The command resolved its project from disk just now. A runic.json may
        // have appeared without a watched-file notification (common in loose-file
        // mode), so cached buffer ownership is re-resolved against the same disk
        // state. Commands are rare; keystrokes keep using the cache.
        _bufferProjects.Clear();
        string[] group = BufferUris(directory).ToArray();
        return Workspace(directory, group);
    }

    private Rmf2Workspace Workspace(string directory, IReadOnlyCollection<string> group)
    {
        _requestCancellation.ThrowIfCancellationRequested();
        string projectPath = Path.Combine(directory, "runic.json");
        if (!IsRegularManifestFile(projectPath))
            throw new InvalidOperationException("Translation project manifests must be regular non-linked runic.json files.");
        TryGetBuffer(projectPath, out _, out Buffer? projectBuffer);
        CompilerInputs inputs = InputFiles.ReadProject(directory, projectBuffer is null ? null : new TranslationSource(projectPath, Utf8.GetBytes(projectBuffer.Text)));
        var sources = inputs.Messages.Select(s => new TranslationSource(Path.GetFullPath(s.Path), s.GetUtf8Bytes())).ToDictionary(s => s.Path, SourcePathComparer);
        // A project outside every workspace root was reached through the
        // explicit-command ancestor rule, not through the bounded index.
        bool externalProject = !IsInsideWorkspaceRoot(directory);
        foreach (string uri in group)
        {
            Buffer buffer = _buffers[uri];
            string file = LocalPath(uri);
            if (externalProject || !IsInsideWorkspaceRoot(file))
            {
                // Buffers of an external project, and loose buffers outside
                // every root, may only replace a source that this project
                // already reads from disk. They never add a file that the
                // preview would not otherwise compile.
                if (sources.ContainsKey(file)) sources[file] = new TranslationSource(file, Utf8.GetBytes(buffer.Text));
                continue;
            }
            // A new unsaved resource joins the project only where discovery
            // would find it on disk: beneath a configured source root, or the
            // project directory when no roots are configured. ReadProject
            // reports the project directory as the implicit root and returns
            // no roots only for a malformed configuration.
            if (sources.ContainsKey(file) || ((Path.GetExtension(file).Equals(".rmf2", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(file).Equals(".mf2", StringComparison.OrdinalIgnoreCase)) &&
                (inputs.SourceRoots is { } sourceRoots ? sourceRoots.Any(sourceRoot => IsWithin(sourceRoot, file)) : IsWithin(directory, file))))
                sources[file] = new TranslationSource(file, Utf8.GetBytes(buffer.Text));
        }
        string root = directory;
        foreach (string file in sources.Keys)
            while (!IsWithin(root, file)) root = Path.GetDirectoryName(root)!;
        Rmf2Workspace workspace = _syntaxCache.Create(root, new TranslationSource(Path.GetFullPath(inputs.Project.Path), inputs.Project.GetUtf8Bytes()), sources.Values, _requestCancellation);
        _projectLayouts[directory] = workspace.IsDirect;
        return workspace;
    }

    private IEnumerable<string> BufferUris(string projectDirectory)
    {
        foreach (string uri in _buffers.Keys.Order(StringComparer.Ordinal))
        {
            string? candidate = BufferProject(uri, out Exception? error);
            if (error is not null) ReportBufferFailure(uri, error);
            if (candidate is not null && PathComparer.Equals(candidate, projectDirectory)) yield return uri;
        }
    }
    private JsonObject WorkspaceEdit(TranslationWorkspaceTransactionPlan plan)
    {
        if (!_configurationSync && plan.Edits.Any(edit => Path.GetFileName(edit.RelativePath) == "runic.json"))
            throw new TranslationAuthoringException("Refactor refused: this client does not synchronize unsaved runic.json buffers. Use the Translations Editor or VS Code for configuration-changing transactions.");
        var edits = new JsonArray();
        foreach (var edit in plan.Edits)
        {
            string path = Path.GetFullPath(edit.RelativePath, plan.Root);
            // An open document is edited under the URI the client opened it with.
            TryGetBuffer(path, out string? openUri, out Buffer? buffer);
            string uri = openUri ?? new Uri(path).AbsoluteUri;
            if (edit.Kind == TranslationWorkspaceEditKind.Create) edits.Add((JsonNode)new JsonObject { ["kind"] = "create", ["uri"] = uri });
            if (edit.Kind == TranslationWorkspaceEditKind.Delete) { edits.Add((JsonNode)new JsonObject { ["kind"] = "delete", ["uri"] = uri }); continue; }
            string before = edit.Kind == TranslationWorkspaceEditKind.Create ? "" : buffer?.Text ?? File.ReadAllText(path, Utf8);
            edits.Add((JsonNode)new JsonObject {
                ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = buffer is null ? null : JsonValue.Create(buffer.Version) },
                ["edits"] = new JsonArray(new JsonObject { ["range"] = new JsonObject { ["start"] = Position(before, 0), ["end"] = Position(before, before.Length) }, ["newText"] = Utf8.GetString(edit.GetUtf8Bytes()!) }),
            });
        }
        return new JsonObject { ["documentChanges"] = edits };
    }

    private void Update(string uri, string text, int version)
    {
        if (Utf8.GetByteCount(text) > 8 * 1024 * 1024) throw new ArgumentException("Document exceeds RMF2 byte limit.");
        if (!_buffers.ContainsKey(uri) && _buffers.Count >= 256) throw new ArgumentException("Too many open resource buffers.");
        bool configuration = Path.GetFileName(LocalPath(uri)) == "runic.json";
        string extension = Path.GetExtension(LocalPath(uri));
        if (!configuration && !extension.Equals(".rmf2", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".mf2", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Runic language server supports .mf2, .rmf2, and runic.json synchronization; use the native service for application sources.");
        var source = new TranslationSource(LocalPath(uri), configuration ? Array.Empty<byte>() : Utf8.GetBytes(text));
        var syntax = extension.Equals(".mf2", StringComparison.OrdinalIgnoreCase)
            ? Rmf2ResourceReader.AnalyzeDirect(source)
            : Rmf2ResourceReader.Analyze(source);
        var buffer = new Buffer(text, version, syntax); _buffers[uri] = buffer;
        _bufferPaths[FileKey(LocalPath(uri))] = uri;
        if (configuration)
        {
            // A configuration overlay can add, remove, or remount source roots.
            // Rebuild ownership before validating every resulting project group;
            // otherwise buffers from the old mapping retain stale diagnostics.
            _bufferProjects.Clear();
            _projectLayouts.Clear();
            QueueGlobalDiagnosticsRefresh();
            return;
        }
        _bufferProjects.Remove(uri);
        string? project = BufferProject(uri, out Exception? unresolved);
        if (project is null)
        {
            // Without a resolvable project only the buffer's own syntax
            // diagnostics are published; the reason is logged, not swallowed.
            ReportBufferFailure(uri, unresolved!);
            PublishDiagnostics(Array.Empty<TranslationDiagnostic>(), [uri], null);
            return;
        }
        ValidateAndPublish(project, BufferUris(project).ToArray());
    }

    // Validates one project with its open buffers overlaid and publishes the result for every
    // project file: the open buffers and, from disk, the closed files that carry diagnostics.
    private void ValidateAndPublish(string project, IReadOnlyCollection<string> group)
    {
        IReadOnlyList<TranslationDiagnostic> diagnostics = Array.Empty<TranslationDiagnostic>();
        try
        {
            diagnostics = WithReviewDiagnostics(ReviewViewOf(project, group));
            // A later failure after recovery is worth reporting again.
            foreach (string uri in group) _reportedBufferFailures.Remove(uri);
        }
        catch (Exception error) when (error is ToolUsageException or ToolDiagnosticException or UnauthorizedAccessException or InvalidOperationException or IOException or FormatException or OverflowException or TranslationFormatException or TranslationPackException or TranslationContractException or System.Text.Json.JsonException or TranslationAuthoringException)
        {
            foreach (string uri in group) ReportBufferFailure(uri, error);
            if (TryGetBuffer(Path.Combine(project, "runic.json"), out string? configUri, out Buffer? config))
                diagnostics = ConfigDiagnostics(new TranslationSource(LocalPath(configUri), Utf8.GetBytes(config.Text)));
        }
        PublishDiagnostics(diagnostics, group, project);
    }

    private void QueueGlobalDiagnosticsRefresh(bool rescanWorkspace = false)
    {
        _globalDiagnosticsRefreshPending = true;
        _projectIndexRescanPending |= rescanWorkspace;
    }

    private void FlushGlobalDiagnosticsRefresh()
    {
        if (!_globalDiagnosticsRefreshPending || _processedRevision != Interlocked.Read(ref _latestRevision)) return;
        bool rescanWorkspace = _projectIndexRescanPending;
        RefreshDiagnostics(rescanWorkspace);
        if (rescanWorkspace) _projectIndexRescanPending = false;
        // A state revision queued meanwhile does not lose these results: its
        // publications wait in the outbox until the newest revision sends them.
        _globalDiagnosticsRefreshPending = false;
    }

    private void RefreshDiagnostics(bool rescanWorkspace)
    {
        RefreshProjectIndex(rescanWorkspace);
        var groups = new SortedDictionary<string, List<string>>(PathComparer);
        var unresolved = new List<string>();
        foreach (string uri in _buffers.Keys.Order(StringComparer.Ordinal))
        {
            string? project = BufferProject(uri, out Exception? error);
            if (project is null) { ReportBufferFailure(uri, error!); unresolved.Add(uri); continue; }
            if (!groups.TryGetValue(project, out List<string>? group)) groups.Add(project, group = []);
            group.Add(uri);
        }
        // A project without open buffers no longer owns diagnostics in the client.
        foreach (string project in _projectFileDiagnostics.Keys.Where(project => !groups.ContainsKey(project)).ToArray())
            ClearProjectFileDiagnostics(project);
        foreach (var group in groups) ValidateAndPublish(group.Key, group.Value);
        // An unresolved buffer has no project whose catalog diagnostics can be
        // attributed safely. Publish only its own syntax diagnostics.
        foreach (string uri in unresolved) PublishDiagnostics(Array.Empty<TranslationDiagnostic>(), [uri], null);
    }

    private void PublishDiagnostics(IReadOnlyList<TranslationDiagnostic> catalogDiagnostics, IEnumerable<string> uris, string? project)
    {
        // Closed files first: a file that has just been opened withdraws its closed-file
        // publication here, before its buffer publication below, so a client that maps both
        // URI spellings to one resource never applies the withdrawal after the buffer's diagnostics.
        if (project is not null) PublishProjectFileDiagnostics(project, catalogDiagnostics);
        foreach (string uri in uris)
        {
            if (!_buffers.TryGetValue(uri, out Buffer? current)) continue;
            string path = FileKey(LocalPath(uri));
            PublishProjectFile(path);
            var diagnostics = current.Syntax.Diagnostics.Concat(catalogDiagnostics.Where(d => PathComparer.Equals(DiagnosticPath(d), path)))
                .DistinctBy(d => (d.Id, d.Location.StartByte, d.Location.LengthBytes, d.Message));
            Publish(uri, new JsonArray(diagnostics.Select(d => (JsonNode)Diagnostic(d, Range(current, d.Location))).ToArray()));
        }
    }

    // Catalog diagnostics located in project files that are not open (a translation that lacks a
    // message, a closed runic.json) are published from the saved file, so the Problems view shows
    // the whole catalog rather than only the open buffers.
    private void PublishProjectFileDiagnostics(string project, IReadOnlyList<TranslationDiagnostic> catalogDiagnostics)
    {
        var files = catalogDiagnostics.Where(d => !string.IsNullOrEmpty(d.Location.Path))
            .GroupBy(DiagnosticPath, PathComparer)
            .Where(file => !_bufferPaths.ContainsKey(file.Key))
            .OrderBy(file => file.Key, StringComparer.Ordinal)
            .Take(MaximumPublishedProjectFiles);
        if (!_projectFileDiagnostics.TryGetValue(project, out HashSet<string>? contributed))
            _projectFileDiagnostics[project] = contributed = new HashSet<string>(PathComparer);
        var current = new HashSet<string>(PathComparer);
        foreach (var file in files)
        {
            _requestCancellation.ThrowIfCancellationRequested();
            var located = file.DistinctBy(d => (d.Id, d.Location.StartByte, d.Location.LengthBytes, d.Message)).ToArray();
            // Keystrokes in another file revalidate the project; a closed file is read again only
            // when its diagnostics or its saved content changed since the last publication.
            string? stamp = FileStamp(file.Key);
            if (stamp is null) continue;
            string fingerprint = stamp + "\n" + string.Join("\n", located.Select(d => string.Join('\u001f', d.Id, d.Severity, d.Location.StartByte, d.Location.LengthBytes, d.Message)));
            if (_projectFiles.TryGetValue(file.Key, out ProjectFile? entry) && entry.Contributions.TryGetValue(project, out var previous) && previous.Fingerprint == fingerprint)
            {
                current.Add(file.Key);
                continue;
            }
            if (!TryReadProjectFile(file.Key, out string text, out TranslationSource? encoded)) continue;
            var payload = new JsonArray(located.Select(d => (JsonNode)Diagnostic(d, encoded is null ? EmptyRange() : Range(text, encoded, d.Location))).ToArray());
            if (entry is null) _projectFiles[file.Key] = entry = new ProjectFile();
            entry.Contributions[project] = (fingerprint, payload);
            current.Add(file.Key); contributed.Add(file.Key);
            PublishProjectFile(file.Key);
        }
        foreach (string file in contributed.Where(file => !current.Contains(file)).ToArray())
            RemoveProjectFileContribution(project, contributed, file);
        if (contributed.Count == 0) _projectFileDiagnostics.Remove(project);
    }

    private void ClearProjectFileDiagnostics(string project)
    {
        if (!_projectFileDiagnostics.Remove(project, out HashSet<string>? contributed)) return;
        foreach (string file in contributed.ToArray()) RemoveProjectFileContribution(project, contributed, file);
    }

    private void RemoveProjectFileContribution(string project, HashSet<string> contributed, string file)
    {
        contributed.Remove(file);
        if (_projectFiles.TryGetValue(file, out ProjectFile? entry) && entry.Contributions.Remove(project)) PublishProjectFile(file);
    }

    // Publishes the union of every project's diagnostics for a closed file, or withdraws the
    // closed-file publication when the file is open or no project reports it any more.
    private void PublishProjectFile(string file)
    {
        if (!_projectFiles.TryGetValue(file, out ProjectFile? entry)) return;
        bool open = _bufferPaths.TryGetValue(file, out string? bufferUri);
        if (open || entry.Contributions.Count == 0)
        {
            // An open file's diagnostics come from its buffer under the client's URI. Clearing
            // the same URI would erase them; another spelling of it is cleared before the
            // buffer is published (PublishDiagnostics publishes closed files first).
            if (entry.SentUri is { } sent && sent != bufferUri) Publish(sent, new JsonArray());
            entry.SentUri = null;
            if (entry.Contributions.Count == 0) _projectFiles.Remove(file);
            return;
        }
        entry.SentUri ??= new Uri(file).AbsoluteUri;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var union = new JsonArray();
        foreach (var contribution in entry.Contributions.OrderBy(item => item.Key, StringComparer.Ordinal))
            foreach (JsonNode? diagnostic in contribution.Value.Payload)
                if (seen.Add(diagnostic!.ToJsonString())) union.Add(diagnostic.DeepClone());
        Publish(entry.SentUri, union);
    }

    private static string? FileStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return null;
            return info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    // Reads a closed project file for diagnostic ranges. A linked or unreadable file is skipped;
    // a file that is not valid UTF-8 (RTR0001) is published at its start.
    private static bool TryReadProjectFile(string path, out string text, out TranslationSource? encoded)
    {
        text = ""; encoded = null;
        if (!TryGetPathAttributes(path, out FileAttributes attributes) || (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return false;
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        try { text = Utf8.GetString(bytes); encoded = new TranslationSource(path, bytes); }
        catch (DecoderFallbackException) { text = ""; encoded = null; }
        return true;
    }

    private static string DiagnosticPath(TranslationDiagnostic diagnostic) => FileKey(diagnostic.Location.Path);

    private JsonObject Diagnostic(TranslationDiagnostic diagnostic, JsonObject range)
    {
        var result = new JsonObject {
            ["range"] = range, ["severity"] = diagnostic.Severity switch { TranslationDiagnosticSeverity.Error => 1, TranslationDiagnosticSeverity.Warning => 2, _ => 3 },
            ["code"] = diagnostic.Id, ["source"] = "runic-rmf2", ["message"] = diagnostic.Message,
        };
        // Every RTR ID has an entry in docs/guides/translations/diagnostics.md; the link names
        // the release tag of this tool, as the C# generator's help links do.
        if (_codeDescriptionSupport && DocumentedDiagnosticId.IsMatch(diagnostic.Id))
            result["codeDescription"] = new JsonObject { ["href"] = RunicReleaseLinks.Diagnostic(diagnostic.Id) };
        return result;
    }

    private IReadOnlyList<TranslationDiagnostic> ConfigDiagnostics(TranslationSource project)
        => TranslationCompiler.CompileRmf2ProjectV5(project, Array.Empty<TranslationSource>(), null, _requestCancellation).Diagnostics;
    // Queues a publication; the latest per URI replaces an earlier unsent one and moves to the end.
    private void Publish(string uri, JsonArray diagnostics) =>
        _outbox[uri] = (++_outboxSequence, new JsonObject { ["uri"] = uri, ["version"] = _buffers.TryGetValue(uri, out var buffer) ? JsonValue.Create(buffer.Version) : null, ["diagnostics"] = diagnostics });

    // Caller holds _revisionGate and has processed the latest state revision.
    private void SendPublications()
    {
        foreach (var publication in _outbox.Values.OrderBy(item => item.Sequence))
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "textDocument/publishDiagnostics", ["params"] = publication.Params });
        _outbox.Clear();
    }
    private JsonObject SemanticTokens(Buffer buffer)
    {
        var spans = new List<(int Start, int End, int Kind)>();
        foreach (var node in buffer.Syntax.Nodes)
        {
            spans.Add((node.NameLocation.StartByte, node.NameLocation.StartByte + node.NameLocation.LengthBytes, node.IsGroup ? 0 : 1));
            if (node.MessageSyntax is not { } syntax) continue;
            foreach (var token in syntax.Tokens)
            {
                int kind = token.Kind switch { Mf2SyntaxTokenKind.Variable => 2, Mf2SyntaxTokenKind.Function => 3, Mf2SyntaxTokenKind.Literal => 5, Mf2SyntaxTokenKind.Name => 1, Mf2SyntaxTokenKind.Attribute => 1, Mf2SyntaxTokenKind.MarkupOpen or Mf2SyntaxTokenKind.MarkupClose => 7, _ => -1 };
                if (kind >= 0 && token.Location.LengthBytes > 0)
                    spans.Add((node.MessageByteMap[token.Location.StartByte], node.MessageByteMap[token.Location.StartByte + token.Location.LengthBytes], kind));
            }
        }
        byte[] bytes = buffer.Syntax.Source.GetUtf8Bytes(); var data = new JsonArray(); int previousLine = 0, previousCharacter = 0;
        foreach (var span in spans.OrderBy(span => span.Start))
        {
            _requestCancellation.ThrowIfCancellationRequested();
            int start = Utf8.GetCharCount(bytes.AsSpan(0, span.Start)), end = Utf8.GetCharCount(bytes.AsSpan(0, span.End));
            while (start < end)
            {
                int newline = buffer.Text.IndexOf('\n', start); int finish = newline < 0 ? end : Math.Min(end, newline);
                if (finish > start && buffer.Text[finish - 1] == '\r') finish--;
                if (finish > start)
                {
                    var from = Position(buffer.Text, start); var to = Position(buffer.Text, finish);
                    int line = from["line"]!.GetValue<int>(), character = from["character"]!.GetValue<int>();
                    data.Add((JsonNode)JsonValue.Create(line - previousLine)); data.Add((JsonNode)JsonValue.Create(line == previousLine ? character - previousCharacter : character));
                    data.Add((JsonNode)JsonValue.Create(to["character"]!.GetValue<int>() - character)); data.Add((JsonNode)JsonValue.Create(span.Kind)); data.Add((JsonNode)JsonValue.Create(0));
                    previousLine = line; previousCharacter = character;
                }
                if (newline < 0 || newline >= end) break;
                start = newline + 1;
            }
        }
        return new JsonObject { ["data"] = data, ["resultId"] = buffer.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) };
    }
    private JsonObject Range(Buffer buffer, TextSourceLocation location) => Range(buffer.Text, buffer.Encoded, location);
    private JsonObject Range(string text, TranslationSource encoded, TextSourceLocation location)
    {
        Rmf2DiagnosticSpan span = Rmf2DiagnosticActions.GetSpan(encoded, location);
        return new JsonObject { ["start"] = Position(text, span.StartUtf16), ["end"] = Position(text, span.StartUtf16 + span.LengthUtf16) };
    }
    private static JsonObject EmptyRange() => new() {
        ["start"] = new JsonObject { ["line"] = 0, ["character"] = 0 }, ["end"] = new JsonObject { ["line"] = 0, ["character"] = 0 } };
    private sealed record ReviewView(string Stamp, Rmf2ProjectCompilation Compilation, Rmf2ProjectCompilation Review,
        IReadOnlyDictionary<string, string>? Fingerprints, string? ReviewPath, TranslationInterchangeReview? File, string? Error,
        TranslationInterchangeException? ReadFailure);

    // The review data of a project compiled with the buffers of group, cached (see _reviewViews). The review compilation
    // is the compilation itself when it succeeded, else the same sources without validation policies, else the base
    // locale alone, so RTR0090, hover's review state and "Mark reviewed" work while translations are incomplete.
    private ReviewView ReviewViewOf(string project, IReadOnlyCollection<string> group, Rmf2Workspace? workspace = null)
    {
        string cacheKey = project + "\n" + string.Join("\n", group.Order(StringComparer.Ordinal));
        if (_reviewViews.TryGetValue(cacheKey, out ReviewView? cached) && cached.Stamp == ReviewStamp(cached.ReviewPath)) return cached;
        workspace ??= Workspace(project, group);
        Rmf2ProjectCompilation compilation = workspace.Validate();
        Rmf2ProjectCompilation review = TranslationReviewFile.ForReview(compilation, workspace.ProjectSource, workspace.Sources, null, _requestCancellation);
        IReadOnlyDictionary<string, string>? fingerprints = review is { Success: true, Project: not null } ? TranslationInterchange.ComputeSourceFingerprints(review) : null;
        string? catalog = review.Project?.Id ?? compilation.Project?.Id;
        string? path = catalog is not null && TranslationReviewFile.IsFileName(catalog) ? TranslationReviewFile.PathFor(Path.Combine(project, "runic.json"), catalog) : null;
        string stamp = ReviewStamp(path);
        TranslationInterchangeReview? file = null;
        TranslationInterchangeException? failure = null;
        string? error = null;
        if (path is not null)
        {
            try { file = TranslationReviewFile.Read(path, catalog!); }
            catch (TranslationInterchangeException exception) { failure = exception; error = exception.Message; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { error = exception.Message; }
        }
        var view = new ReviewView(stamp, compilation, review, fingerprints, path, file, error, failure);
        _reviewViews[cacheKey] = view;
        return view;
    }

    private static string ReviewStamp(string? path)
    {
        if (path is null) return "-";
        var info = new FileInfo(path);
        return info.Exists ? info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
    }

    // The review file's warnings beside the compiler's: RTR0090 for each stale entry, RTR0091 when the file cannot be read
    // (the same rules and file as runic-translations validate, through Tooling).
    private static IReadOnlyList<TranslationDiagnostic> WithReviewDiagnostics(ReviewView view)
    {
        IReadOnlyList<TranslationDiagnostic> diagnostics = view.Compilation.Diagnostics;
        if (view.ReadFailure is not null && view.ReviewPath is not null) return [.. diagnostics, TranslationReviewFile.InvalidDiagnostic(view.ReviewPath, view.ReadFailure)];
        if (view.File is null || view.Fingerprints is null) return diagnostics;
        return [.. diagnostics, .. TranslationReviewFile.StaleDiagnostics(view.Review, view.File)];
    }

    // The project repairs for one open buffer and its stale review entries whose "Mark reviewed" can run now (the files
    // with the key's base message and translation are saved), from its project compiled with the open buffers.
    private (IReadOnlyList<Rmf2DiagnosticQuickFix> Fixes, IReadOnlyList<TranslationReviewStaleEntry> Stale) ProjectQuickFixes(string uri, Buffer buffer)
    {
        string? project = BufferProject(uri, out _);
        if (project is null) return ([], []);
        try
        {
            ReviewView view = ReviewViewOf(project, BufferUris(project).ToArray());
            string path = LocalPath(uri);
            var source = new TranslationSource(path, Utf8.GetBytes(buffer.Text));
            IReadOnlyList<Rmf2DiagnosticQuickFix> fixes = Rmf2DiagnosticActions.GetProjectQuickFixes(view.Compilation, source, _requestCancellation);
            IReadOnlyList<TranslationReviewStaleEntry> stale = [];
            if (view.File is { } review && view.Fingerprints is not null)
                stale = TranslationReviewFile.Stale(view.Review, review).Where(entry => PathComparer.Equals(FileKey(entry.Location.Path), FileKey(path)) &&
                    UnsavedReviewFiles(project, view.Review.Project, null, entry.Entry.Key, entry.Entry.Locale).Count == 0).ToArray();
            return (fixes, stale);
        }
        catch (Exception error) when (error is ToolUsageException or ToolDiagnosticException or UnauthorizedAccessException or InvalidOperationException or IOException or FormatException or TranslationInterchangeException or TranslationAuthoringException or System.Text.Json.JsonException)
        {
            return ([], []);
        }
    }

    // The open buffers, among the files with the key's base message and its translation, whose text differs from the
    // file on disk. Where no compilation locates the translation (it is missing, or only the base locale compiled), every
    // buffer of the project in that locale counts.
    private List<string> UnsavedReviewFiles(string project, Rmf2ProjectV5? buffers, Rmf2ProjectV5? disk, string key, string locale)
    {
        var files = new HashSet<string>(PathComparer);
        bool translationFound = false;
        foreach (Rmf2ProjectV5 compiled in new[] { buffers, disk }.OfType<Rmf2ProjectV5>())
            foreach (Rmf2LocaleV5 item in compiled.Locales.Where(item => item.Tag == compiled.DefaultLocale || item.Tag == locale))
                foreach (Rmf2TranslationV5 resource in item.DirectResources.Where(resource => resource.Key == key))
                {
                    files.Add(Path.GetFullPath(resource.SourceLocation.Path));
                    translationFound |= item.Tag == locale;
                }
        string[] group = BufferUris(project).ToArray();
        if (!translationFound && group.Length != 0)
        {
            Rmf2Workspace workspace = Workspace(project, group);
            foreach (string uri in group)
                if (string.Equals(workspace.Locale(LocalPath(uri)), locale, StringComparison.OrdinalIgnoreCase)) files.Add(LocalPath(uri));
        }
        var unsaved = new List<string>();
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            if (!TryGetBuffer(file, out _, out Buffer? buffer)) continue;
            byte[]? saved = File.Exists(file) ? File.ReadAllBytes(file) : null;
            if (saved is null || !saved.AsSpan().SequenceEqual(Utf8.GetBytes(buffer.Text))) unsaved.Add(file);
        }
        return unsaved;
    }

    // runic.markReviewed: records the entry's state again for the source on disk, as runic-translations review mark
    // does, and republishes the diagnostics. It refuses while the base message's or the translation's file has unsaved
    // changes: the review file is on disk, so it must describe the files on disk. Works while translations are
    // incomplete (the review compilation); the review file is written with compare-and-swap.
    private JsonNode? MarkReviewed(string sourcePath, string key, string locale)
    {
        string projectDirectory = ProjectDirectory(sourcePath, allowExternalAncestor: true);
        CompilerInputs inputs = InputFiles.ReadProject(projectDirectory, null);
        var projectSource = new TranslationSource(Path.GetFullPath(inputs.Project.Path), inputs.Project.GetUtf8Bytes());
        TranslationSource[] sources = inputs.Messages.Select(source => new TranslationSource(Path.GetFullPath(source.Path), source.GetUtf8Bytes())).ToArray();
        Rmf2ProjectCompilation review = TranslationReviewFile.ForReview(TranslationCompiler.CompileRmf2ProjectV5(projectSource, sources, null, _requestCancellation),
            projectSource, sources, null, _requestCancellation);
        if (!review.Success || review.Project is not { } project)
            throw new TranslationAuthoringException("Fix the errors in runic.json or the base locale before recording a review.");
        if (locale == project.DefaultLocale || !project.Locales.Any(item => item.Tag == locale))
            throw new ArgumentException($"'{locale}' is not a translated locale of catalog '{project.Id}'.");
        IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(review);
        if (!fingerprints.ContainsKey(key)) throw new ArgumentException($"'{key}' is not a message of catalog '{project.Id}'.");
        Rmf2ProjectV5? buffers = BufferUris(projectDirectory).Any() ? ReviewViewOf(projectDirectory, BufferUris(projectDirectory).ToArray()).Review.Project : null;
        List<string> unsaved = UnsavedReviewFiles(projectDirectory, buffers, project, key, locale);
        if (unsaved.Count != 0)
            throw new TranslationAuthoringException("Save " + string.Join(", ", unsaved.Select(Path.GetFileName)) +
                " first to record a review: the review file describes the files on disk.");
        string path = TranslationReviewFile.PathFor(Path.Combine(projectDirectory, "runic.json"), project.Id);
        try
        {
            TranslationReviewFile.Update(path, project.Id, existing => TranslationReviewFile.Mark(existing, project.Id, [key], locale,
                existing?.Entries.FirstOrDefault(entry => entry.Key == key && entry.Locale == locale)?.State ?? TranslationReviewState.Translated, null, fingerprints));
        }
        catch (TranslationInterchangeException exception) { throw new TranslationAuthoringException("The review was not recorded: " + exception.Message); }
        _reviewViews.Clear();
        QueueGlobalDiagnosticsRefresh();
        FlushGlobalDiagnosticsRefresh();
        return null;
    }

    private JsonObject QuickFixAction(string uri, Buffer buffer, Rmf2DiagnosticQuickFix fix, bool preferred)
    {
        // Repairs were authorized by the shared compiler against this immutable buffer syntax.
        var editLocation = new TextSourceLocation(buffer.Syntax.Source.Path, fix.StartByte, fix.LengthBytes, 0, 0, 0, 0);
        return new JsonObject {
            ["title"] = fix.Title, ["kind"] = "quickfix", ["isPreferred"] = preferred,
            ["data"] = new JsonObject { ["uri"] = uri, ["version"] = buffer.Version, ["revision"] = fix.ExpectedRevision, ["fixId"] = fix.Id },
            ["edit"] = new JsonObject { ["documentChanges"] = new JsonArray(new JsonObject {
                ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = buffer.Version },
                ["edits"] = new JsonArray(new JsonObject { ["range"] = Range(buffer, editLocation), ["newText"] = fix.NewText }),
            }) },
        };
    }
    private JsonObject Position(string text, int offset)
    {
        int line = 0, start = 0;
        for (int i = 0; i < offset; i++) if (text[i] == '\n') { line++; start = i + 1; }
        ReadOnlySpan<char> value = text.AsSpan(start, offset - start);
        int character = _encoding == "utf-8" ? Utf8.GetByteCount(value) : _encoding == "utf-32" ? ScalarCount(value) : value.Length;
        return new JsonObject { ["line"] = line, ["character"] = character };
    }
    private static int ScalarCount(ReadOnlySpan<char> value) { int count = 0; foreach (Rune rune in value.EnumerateRunes()) { _ = rune; count++; } return count; }
    private int Offset(string text, JsonNode position)
    {
        int line = position["line"]!.GetValue<int>(), character = position["character"]!.GetValue<int>(), at = 0;
        if (line < 0 || character < 0) throw new ArgumentException("Negative LSP position.");
        for (int i = 0; i < line; i++) { int next = text.IndexOf('\n', at); if (next < 0) throw new ArgumentException("Position is outside the document."); at = next + 1; }
        int used = 0;
        while (used < character && at < text.Length && text[at] is not ('\n' or '\r'))
        {
            Rune rune = Rune.GetRuneAt(text, at);
            used += _encoding == "utf-8" ? rune.Utf8SequenceLength : _encoding == "utf-32" ? 1 : rune.Utf16SequenceLength;
            at += rune.Utf16SequenceLength;
        }
        if (used != character) throw new ArgumentException("Position splits a Unicode scalar or exceeds the line.");
        return at;
    }
    private JsonObject? Read()
    {
        var header = new List<byte>();
        while (header.Count < 8192)
        {
            int value = _input.ReadByte(); if (value < 0) return null; header.Add((byte)value);
            if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
        }
        string headers = Encoding.ASCII.GetString(header.ToArray());
        Match length = Regex.Match(headers, "(?im)^Content-Length: *([0-9]+)\\r$");
        if (!length.Success || !int.TryParse(length.Groups[1].Value, out int count) || count > 16 * 1024 * 1024) throw new IOException("Invalid LSP frame length.");
        byte[] payload = new byte[count]; _input.ReadExactly(payload);
        return JsonNode.Parse(payload)?.AsObject();
    }
    private void Send(JsonObject message)
    {
        byte[] bytes = Utf8.GetBytes(message.ToJsonString());
        _output.Write(Encoding.ASCII.GetBytes("Content-Length: " + bytes.Length + "\r\n\r\n")); _output.Write(bytes); _output.Flush();
    }
    private sealed record Buffer(string Text, int Version, Rmf2ResourceDocument Syntax)
    {
        private TranslationSource? _encoded;
        // The UTF-8 source of Text, encoded once per buffer: folding ranges and the outline map
        // many block locations of one immutable buffer.
        internal TranslationSource Encoded => _encoded ??= new TranslationSource(Syntax.Source.Path, Utf8.GetBytes(Text));
    }
    private sealed class ProjectFile
    {
        // Per project directory: a fingerprint of the diagnostics and the saved file, and their payload.
        internal Dictionary<string, (string Fingerprint, JsonArray Payload)> Contributions { get; } = new(PathComparer);
        // The URI of the closed-file publication the client currently shows, if any.
        internal string? SentUri { get; set; }
    }
    private sealed class PlatformSourcePathComparer : IEqualityComparer<string>
    {
        // Backslash is a legal file-name character on Unix, so only Windows
        // folds separators; case follows PathComparer.
        private static string Normalize(string path) => OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;
        public bool Equals(string? x, string? y) => x is null || y is null ? x == y : PathComparer.Equals(Normalize(x), Normalize(y));
        public int GetHashCode(string path) => PathComparer.GetHashCode(Normalize(path));
    }
}
