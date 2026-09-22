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
    private readonly List<string> _workspaceRoots = new();
    private readonly SortedSet<string> _diskProjectDirectories = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _projectDirectories = new(StringComparer.Ordinal);
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
                        if (id is not null && (request["params"]?["textDocument"] is not null || method == "workspace/executeCommand") && item.Revision != _latestRevision) throw new ContentModifiedException();
                        if (id is not null) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
                    }
                }
                catch (Exception error) when (error is ContentModifiedException or OperationCanceledException or ToolUsageException or ToolDiagnosticException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or TranslationAuthoringException or KeyNotFoundException or IOException or FormatException or OverflowException or TranslationFormatException or TranslationPackException or TranslationContractException or System.Text.Json.JsonException)
                {
                    if (id is not null) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = error is ContentModifiedException ? -32801 : error is OperationCanceledException ? -32800 : -32602, ["message"] = error is ContentModifiedException ? "Document content changed while the request was pending." : error is OperationCanceledException ? "Request cancelled." : error.Message } });
                }
                finally
                {
                    lock (_revisionGate) { if (id is not null) pending.Remove(id.ToJsonString()); item.Cancellation.Dispose(); }
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
        if (method == "initialize")
        {
            _configurationSync = args["initializationOptions"]?["runicConfigurationSync"]?.GetValue<bool>() == true;
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
                ["executeCommandProvider"] = new JsonObject { ["commands"] = new JsonArray("runic.extractGroup", "runic.inlineResource", "runic.preview", "runic.renameInput", "runic.renameSlot", "runic.renameResource", "runic.renderPreview") },
                ["completionProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray("$", ":", "#", "/", "=") },
            }, ["serverInfo"] = new JsonObject { ["name"] = "Runic RMF2", ["version"] = "1" } };
        }
        if (method == "shutdown") { _shutdown = true; return null; }
        if (method is "initialized" or "$/cancelRequest") return null;
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
            if (command is "runic.preview" or "runic.renderPreview")
            {
                string key = arguments[1]!.GetValue<string>(), locale = arguments[2]!.GetValue<string>();
                Rmf2ProjectCompilationV5 compilation = workspace.Validate();
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
                _buffers.Remove(uri); Publish(uri, new JsonArray());
                if (configuration) QueueGlobalDiagnosticsRefresh();
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
        if (Path.GetFileName(LocalPath(uri)) == "runic.json") return null;
        if (method == "textDocument/semanticTokens/full") return SemanticTokens(buffer);
        if (method == "textDocument/documentSymbol")
            return new JsonArray(buffer.Syntax.Nodes.Select(node => (JsonNode)new JsonObject {
                ["name"] = node.Path[^1], ["detail"] = string.Join('.', node.Path), ["kind"] = node.IsGroup ? 3 : 13,
                ["range"] = Range(buffer, node.Location), ["selectionRange"] = Range(buffer, node.NameLocation),
            }).ToArray());
        if (method == "textDocument/foldingRange")
            return new JsonArray(buffer.Syntax.Nodes.Where(n => n.Location.EndLine > n.NameLocation.Line).Select(node => (JsonNode)new JsonObject {
                ["startLine"] = node.NameLocation.Line - 1, ["endLine"] = node.Location.EndLine - (node.Location.EndColumn == 1 ? 2 : 1), ["kind"] = "region",
            }).ToArray());
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
                ["locale"] = Path.GetFileNameWithoutExtension(sourcePath),
                ["locales"] = new JsonArray(workspace.Documents.Select(document => Path.GetFileNameWithoutExtension(document.Source.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
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
            string content = entry.MessageSyntax is { } messageSyntax ? workspace.LanguageService.Hover(messageSyntax, MessageOffset(entry, atByte)) ?? key : key;
            Rmf2ProjectCompilationV5 compiled = workspace.Validate();
            if (compiled.Success && !entry.IsGroup)
            {
                string locale = Path.GetFileNameWithoutExtension(path);
                Rmf2ProjectV5 project = compiled.Project!;
                Rmf2TranslationV5? value = project.Locales.FirstOrDefault(item => item.Tag == locale)?.ResolvedResources.FirstOrDefault(item => item.Key == key);
                Rmf2MessageContractV5? contract = project.CanonicalMessages.Concat(project.ExtraMessages).FirstOrDefault(item => item.Key == key);
                if (value is not null) content += "\nContent locale: " + value.ContentLocale;
                if (contract is { Inputs.Count: > 0 }) content += "\nInputs: " + string.Join(", ", contract.Inputs.Select(input => "$" + input.Name + ": " + input.Type));
                string? fallback = project.Locales.FirstOrDefault(item => item.Tag == locale)?.FallbackTag;
                if (fallback is not null) content += "\nFallback: " + fallback;
            }
            content += "\n" + string.Join("\n", entry.Comments) + "\n" + string.Join("\n", entry.Properties);
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
                    return (JsonNode)new JsonObject { ["uri"] = new Uri(reference.Document.Source.Path).AbsoluteUri,
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
                .Select(node => (JsonNode)new JsonObject { ["uri"] = new Uri(document.Source.Path).AbsoluteUri, ["range"] = Range(new Buffer(Utf8.GetString(document.Source.GetUtf8Bytes()), 0, document), node.NameLocation) })).ToArray());
        }
        if (method == "textDocument/completion")
        {
            var labels = new HashSet<string>(StringComparer.Ordinal) { "one", "two", "few", "many", "zero", "*" };
            string sourcePath = LocalPath(uri); var workspace = Workspace(sourcePath);
            var completions = entry is { IsGroup: false } ? workspace.Complete(sourcePath, entry.Key, MessageOffset(entry, atByte)) : workspace.LanguageService.Complete(null, 0);
            var items = completions.ToDictionary(item => item.Label, item => item.Detail, StringComparer.Ordinal);
            foreach (string label in labels) items.TryAdd(label, "RMF2 execution profile");
            return new JsonArray(items.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => (JsonNode)new JsonObject { ["label"] = item.Key, ["detail"] = item.Value, ["kind"] = 14 }).ToArray());
        }
        return null;
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

    private void RefreshProjectIndex(bool rescanWorkspace)
    {
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
        var discovered = new SortedSet<string>(StringComparer.Ordinal);
        int visited = 0;
        foreach (string root in workspaceRoots.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!Directory.Exists(root)) continue;
            var pending = new SortedSet<string>(StringComparer.Ordinal) { root };
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
        return !PathEntryExists(path) && _buffers.ContainsKey(new Uri(path).AbsoluteUri);
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
            bool buffered = _buffers.ContainsKey(new Uri(config).AbsoluteUri) && (!PathEntryExists(config) || IsRegularManifestFile(config));
            if (IsRegularManifestFile(config) || buffered) return directory;
            if (string.Equals(directory, root, StringComparison.Ordinal)) break;
            directory = Path.GetDirectoryName(directory);
        }
        return ProjectDirectory(path);
    }

    private string[] ProjectSourceRoots(string directory)
    {
        string projectPath = Path.Combine(directory, "runic.json");
        if (PathEntryExists(projectPath) && !IsRegularManifestFile(projectPath)) return [];
        string text;
        if (_buffers.TryGetValue(new Uri(projectPath).AbsoluteUri, out Buffer? projectBuffer)) text = projectBuffer.Text;
        else
        {
            try { text = File.ReadAllText(projectPath, Utf8); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return [directory]; }
        }
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("sourceRoots", out var mounts) || mounts.ValueKind != System.Text.Json.JsonValueKind.Array)
                return [directory];
            return mounts.EnumerateArray()
                .Where(mount => mount.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    mount.TryGetProperty("path", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                .Select(mount => Path.GetFullPath(mount.GetProperty("path").GetString()!, directory))
                .ToArray();
        }
        catch (System.Text.Json.JsonException) { return [directory]; }
    }

    private Rmf2Workspace Workspace(string path)
    {
        // Explicit language operations may target a complete project outside
        // the opened workspace (for example an inert preview fixture). Such a
        // project is loaded directly but never added to the watched index.
        string directory = ProjectDirectory(path, allowExternalAncestor: true);
        string[] group = BufferUris(directory).ToArray();
        return Workspace(directory, group);
    }

    private Rmf2Workspace Workspace(string directory, IReadOnlyCollection<string> group)
    {
        _requestCancellation.ThrowIfCancellationRequested();
        string projectPath = Path.Combine(directory, "runic.json");
        if (!IsRegularManifestFile(projectPath))
            throw new InvalidOperationException("Translation project manifests must be regular non-linked runic.json files.");
        _buffers.TryGetValue(new Uri(projectPath).AbsoluteUri, out var projectBuffer);
        CompilerInputs inputs = InputFiles.ReadProject(directory, projectBuffer is null ? null : new TranslationSource(projectPath, Utf8.GetBytes(projectBuffer.Text)));
        var sources = inputs.Messages.Select(s => new TranslationSource(Path.GetFullPath(s.Path), s.GetUtf8Bytes())).ToDictionary(s => s.Path, StringComparer.Ordinal);
        foreach (string uri in group)
        {
            Buffer buffer = _buffers[uri];
            string file = LocalPath(uri);
            if (sources.ContainsKey(file) || (Path.GetExtension(file).Equals(".rmf2", StringComparison.OrdinalIgnoreCase) && inputs.SourceRoots?.Any(sourceRoot => IsWithin(sourceRoot, file)) == true))
                sources[file] = new TranslationSource(file, Utf8.GetBytes(buffer.Text));
        }
        string root = directory;
        foreach (string file in sources.Keys)
            while (!IsWithin(root, file)) root = Path.GetDirectoryName(root)!;
        return _syntaxCache.Create(root, new TranslationSource(Path.GetFullPath(inputs.Project.Path), inputs.Project.GetUtf8Bytes()), sources.Values, _requestCancellation);
    }

    private IEnumerable<string> BufferUris(string projectDirectory)
    {
        foreach (string uri in _buffers.Keys.Order(StringComparer.Ordinal))
        {
            string? candidate = null;
            try { candidate = BufferProjectDirectory(LocalPath(uri)); }
            catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            if (candidate is not null && string.Equals(candidate, projectDirectory, StringComparison.Ordinal)) yield return uri;
        }
    }
    private JsonObject WorkspaceEdit(TranslationWorkspaceTransactionPlan plan)
    {
        if (!_configurationSync && plan.Edits.Any(edit => Path.GetFileName(edit.RelativePath) == "runic.json"))
            throw new TranslationAuthoringException("Refactor refused: this client does not synchronize unsaved runic.json buffers. Use the Translations Editor or VS Code for configuration-changing transactions.");
        var edits = new JsonArray();
        foreach (var edit in plan.Edits)
        {
            string path = Path.GetFullPath(edit.RelativePath, plan.Root); string uri = new Uri(path).AbsoluteUri;
            if (edit.Kind == TranslationWorkspaceEditKind.Create) edits.Add((JsonNode)new JsonObject { ["kind"] = "create", ["uri"] = uri });
            if (edit.Kind == TranslationWorkspaceEditKind.Delete) { edits.Add((JsonNode)new JsonObject { ["kind"] = "delete", ["uri"] = uri }); continue; }
            _buffers.TryGetValue(uri, out Buffer? buffer);
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
        if (!configuration && !Path.GetExtension(LocalPath(uri)).Equals(".rmf2", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The RMF2 language server supports .rmf2 resources and runic.json synchronization; use the native service for application and legacy sources.");
        var syntax = Rmf2ResourceReader.Analyze(new TranslationSource(LocalPath(uri), configuration ? Array.Empty<byte>() : Utf8.GetBytes(text)));
        var buffer = new Buffer(text, version, syntax); _buffers[uri] = buffer;
        if (configuration)
        {
            // A configuration overlay can add, remove, or remount source roots.
            // Rebuild ownership before validating every resulting project group;
            // otherwise buffers from the old mapping retain stale diagnostics.
            QueueGlobalDiagnosticsRefresh();
            return;
        }
        IReadOnlyList<TranslationDiagnostic> catalogDiagnostics = Array.Empty<TranslationDiagnostic>();
        string[] group = [uri];
        try
        {
            string project = BufferProjectDirectory(LocalPath(uri));
            group = BufferUris(project).ToArray();
            catalogDiagnostics = Workspace(project, group).Validate().Diagnostics;
        }
        catch (Exception error) when (error is ToolUsageException or ToolDiagnosticException or UnauthorizedAccessException or InvalidOperationException or IOException or FormatException or OverflowException or TranslationFormatException or TranslationPackException or TranslationContractException or System.Text.Json.JsonException or TranslationAuthoringException) {
            if (configuration) catalogDiagnostics = ConfigDiagnostics(new TranslationSource(LocalPath(uri), Utf8.GetBytes(text)));
        }
        PublishDiagnostics(catalogDiagnostics, group);
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
        // The reader can queue a newer state revision while the scan or project
        // validations are running. Publications were suppressed in that case;
        // retain the dirty bit so the newest state notification retries once.
        lock (_revisionGate)
            if (_processedRevision == _latestRevision) _globalDiagnosticsRefreshPending = false;
    }

    private void RefreshDiagnostics(bool rescanWorkspace)
    {
        RefreshProjectIndex(rescanWorkspace);
        if (_buffers.Count == 0) return;
        var groups = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        foreach (string uri in _buffers.Keys.Order(StringComparer.Ordinal))
        {
            try
            {
                string project = BufferProjectDirectory(LocalPath(uri));
                if (!groups.TryGetValue(project, out List<string>? group)) groups.Add(project, group = []);
                group.Add(uri);
            }
            catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { unresolved.Add(uri); }
        }
        foreach (var group in groups)
        {
            IReadOnlyList<TranslationDiagnostic> diagnostics = Array.Empty<TranslationDiagnostic>();
            try { diagnostics = Workspace(group.Key, group.Value).Validate().Diagnostics; }
            catch (Exception error) when (error is ToolUsageException or ToolDiagnosticException or UnauthorizedAccessException or InvalidOperationException or IOException or FormatException or OverflowException or TranslationFormatException or TranslationPackException or TranslationContractException or System.Text.Json.JsonException or TranslationAuthoringException)
            {
                string configUri = new Uri(Path.Combine(group.Key, "runic.json")).AbsoluteUri;
                if (_buffers.TryGetValue(configUri, out Buffer? config))
                    diagnostics = ConfigDiagnostics(new TranslationSource(LocalPath(configUri), Utf8.GetBytes(config.Text)));
            }
            PublishDiagnostics(diagnostics, group.Value);
        }
        // An unresolved buffer has no project whose catalog diagnostics can be
        // attributed safely. Publish only its own syntax diagnostics.
        foreach (string uri in unresolved) PublishDiagnostics(Array.Empty<TranslationDiagnostic>(), [uri]);
    }

    private void PublishDiagnostics(IReadOnlyList<TranslationDiagnostic> catalogDiagnostics, IEnumerable<string> uris)
    {
        foreach (string uri in uris)
        {
            if (!_buffers.TryGetValue(uri, out Buffer? current)) continue;
            string path = LocalPath(uri);
            var diagnostics = current.Syntax.Diagnostics.Concat(catalogDiagnostics.Where(d => Path.GetFullPath(d.Location.Path).Replace('\\', '/') == path))
                .DistinctBy(d => (d.Id, d.Location.StartByte, d.Location.LengthBytes, d.Message));
            Publish(uri, new JsonArray(diagnostics.Select(d => (JsonNode)new JsonObject {
                ["range"] = Range(current, d.Location), ["severity"] = d.Severity == TranslationDiagnosticSeverity.Error ? 1 : 2,
                ["code"] = d.Id, ["source"] = "runic-rmf2", ["message"] = d.Message,
            }).ToArray()));
        }
    }

    private IReadOnlyList<TranslationDiagnostic> ConfigDiagnostics(TranslationSource project)
        => TranslationCompiler.CompileRmf2ProjectV5(project, Array.Empty<TranslationSource>(), null, _requestCancellation).Diagnostics;
    private void Publish(string uri, JsonArray diagnostics)
    {
        lock (_revisionGate)
        {
            if (_processedRevision != _latestRevision) return;
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "textDocument/publishDiagnostics", ["params"] = new JsonObject { ["uri"] = uri, ["version"] = _buffers.TryGetValue(uri, out var buffer) ? JsonValue.Create(buffer.Version) : null, ["diagnostics"] = diagnostics } });
        }
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
    private JsonObject Range(Buffer buffer, TextSourceLocation location)
    {
        byte[] bytes = Utf8.GetBytes(buffer.Text);
        int from = Utf8.GetCharCount(bytes.AsSpan(0, location.StartByte)), to = Utf8.GetCharCount(bytes.AsSpan(0, location.StartByte + location.LengthBytes));
        return new JsonObject { ["start"] = Position(buffer.Text, from), ["end"] = Position(buffer.Text, to) };
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
    private sealed record Buffer(string Text, int Version, Rmf2ResourceDocument Syntax);
}
