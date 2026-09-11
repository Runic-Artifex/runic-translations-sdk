using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Tool;

/// <summary>Bounded stdio LSP transport over the shared recoverable resource model.</summary>
internal sealed class Rmf2LanguageServer
{
    private readonly Rmf2WorkspaceCache _syntaxCache = new();
    private readonly Dictionary<string, Buffer> _buffers = new(StringComparer.Ordinal);
    private readonly Stream _input;
    private readonly Stream _output;
    private string _encoding = "utf-16";
    private bool _shutdown;
    private bool _fileOperations;
    private readonly List<string> _workspaceRoots = new();
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal Rmf2LanguageServer(Stream input, Stream output) { _input = input; _output = output; }
    private CancellationToken _requestCancellation;
    private long _latestRevision;
    private long _processedRevision;
    private sealed class ContentModifiedException : Exception { }
    internal int Run()
    {
        // The reader remains responsive to cancellation while one worker owns document state.
        using var queue = new BlockingCollection<(JsonObject Request, CancellationTokenSource Cancellation, long Revision)>(256);
        var pending = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        var gate = new object();
        Task<int> worker = Task.Run(() => {
            foreach (var item in queue.GetConsumingEnumerable())
            {
                var request = item.Request; string method = request["method"]?.GetValue<string>() ?? "";
                JsonNode? id = request["id"]?.DeepClone();
                _requestCancellation = item.Cancellation.Token; _processedRevision = item.Revision;
                try
                {
                    _requestCancellation.ThrowIfCancellationRequested();
                    if (method == "exit") return _shutdown ? 0 : 1;
                    JsonNode? result = Handle(method, request["params"] as JsonObject ?? new JsonObject());
                    lock (gate)
                    {
                        _requestCancellation.ThrowIfCancellationRequested();
                        if (id is not null && (request["params"]?["textDocument"] is not null || method == "workspace/executeCommand") && item.Revision != _latestRevision) throw new ContentModifiedException();
                        if (id is not null) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
                    }
                }
                catch (Exception error) when (error is ContentModifiedException or OperationCanceledException or ArgumentException or InvalidOperationException or TranslationAuthoringException or KeyNotFoundException or IOException or System.Text.Json.JsonException)
                {
                    if (id is not null) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = error is ContentModifiedException ? -32801 : error is OperationCanceledException ? -32800 : -32602, ["message"] = error is ContentModifiedException ? "Document content changed while the request was pending." : error is OperationCanceledException ? "Request cancelled." : error.Message } });
                }
                finally
                {
                    lock (gate) { if (id is not null) pending.Remove(id.ToJsonString()); item.Cancellation.Dispose(); }
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
                    lock (gate) { if (target is not null && pending.TryGetValue(target, out var cancellation)) cancellation.Cancel(); }
                    continue;
                }
                var source = new CancellationTokenSource();
                long revision;
                lock (gate) {
                    if (method is "textDocument/didOpen" or "textDocument/didChange" or "textDocument/didClose") _latestRevision++;
                    revision = _latestRevision;
                    if (request["id"] is { } id) pending[id.ToJsonString()] = source;
                }
                queue.Add((request, source, revision));
                if (method == "exit") break;
            }
        }
        finally { queue.CompleteAdding(); }
        return worker.GetAwaiter().GetResult();
    }
    private JsonNode? Handle(string method, JsonObject args)
    {
        if (method == "initialize")
        {
            if (args["workspaceFolders"] is JsonArray folders)
                foreach (var folder in folders) _workspaceRoots.Add(new Uri(folder!["uri"]!.GetValue<string>()).LocalPath);
            else if (args["rootUri"] is JsonValue rootUri) _workspaceRoots.Add(new Uri(rootUri.GetValue<string>()).LocalPath);
            _fileOperations = args["capabilities"]?["workspace"]?["workspaceEdit"]?["resourceOperations"] is JsonArray operations && operations.Any(v => v?.GetValue<string>() == "create") && operations.Any(v => v?.GetValue<string>() == "delete");
            var encodings = args["capabilities"]?["general"]?["positionEncodings"] as JsonArray;
            _encoding = encodings?.Select(v => v!.GetValue<string>()).FirstOrDefault(v => v is "utf-8" or "utf-16" or "utf-32") ?? "utf-16";
            return new JsonObject { ["capabilities"] = new JsonObject {
                ["positionEncoding"] = _encoding, ["textDocumentSync"] = 2, ["documentSymbolProvider"] = true,
                ["semanticTokensProvider"] = new JsonObject { ["legend"] = new JsonObject { ["tokenTypes"] = new JsonArray("namespace", "property", "variable", "function", "keyword", "string", "comment", "type"), ["tokenModifiers"] = new JsonArray() }, ["full"] = true },
                ["referencesProvider"] = true, ["foldingRangeProvider"] = true, ["hoverProvider"] = true, ["definitionProvider"] = true,
                ["documentFormattingProvider"] = true, ["renameProvider"] = true,
                ["executeCommandProvider"] = new JsonObject { ["commands"] = new JsonArray("runic.extractGroup", "runic.inlineResource", "runic.preview", "runic.renameInput", "runic.renameSlot") },
                ["completionProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray("$", ":", "#", "/", "=") },
            }, ["serverInfo"] = new JsonObject { ["name"] = "Runic RMF2", ["version"] = "1" } };
        }
        if (method == "shutdown") { _shutdown = true; return null; }
        if (method is "initialized" or "$/cancelRequest" or "workspace/didChangeConfiguration") return null;
        if (method == "workspace/executeCommand")
        {
            string command = args["command"]!.GetValue<string>();
            JsonArray arguments = args["arguments"]!.AsArray();
            string sourceUri = arguments[0]!.GetValue<string>(); string sourcePath = new Uri(sourceUri).LocalPath;
            var workspace = Workspace(sourcePath);
            if (command == "runic.preview")
            {
                string key = arguments[1]!.GetValue<string>(), locale = arguments[2]!.GetValue<string>();
                var compilation = workspace.Validate();
                if (!compilation.Success) throw new TranslationAuthoringException(string.Join("; ", compilation.Diagnostics.Select(d => d.Message)));
                var catalog = compilation.Catalogs.Single();
                var artifact = JsonNode.Parse(TranslationOutputRenderer.RenderLocaleJson(catalog, locale).Text)!;
                var message = artifact["messages"]?[key] ?? throw new TranslationAuthoringException("Unknown preview message.");
                var examples = workspace.Documents.SelectMany(doc => doc.Nodes.Where(node => !node.IsGroup && string.Join('_', workspace.LogicalPath(doc.Source.Path, node)) == key))
                    .SelectMany(node => node.Properties.Where(value => value.StartsWith("example ", StringComparison.Ordinal)).Select(value => JsonNode.Parse(value.Substring(8))));
                return new JsonObject { ["key"] = key, ["locale"] = locale, ["ast"] = message.DeepClone(), ["markup"] = JsonNode.Parse(catalog.Rmf2MarkupContract!), ["examples"] = new JsonArray(examples.ToArray()) };
            }
            if (command == "runic.renameSlot")
                return WorkspaceEdit(workspace.RenameSlot(sourcePath, arguments[1]!.GetValue<string>(), arguments[2]!.GetValue<string>(), arguments[3]!.GetValue<string>()));
            if (command == "runic.renameInput")
                return WorkspaceEdit(workspace.RenameInput(sourcePath, arguments[1]!.GetValue<string>(), arguments[2]!.GetValue<string>(), arguments[3]!.GetValue<string>()));
            if (!_fileOperations) throw new InvalidOperationException("The client does not support the required file operations.");
            var plan = command switch {
                "runic.extractGroup" => workspace.Extract(sourcePath, arguments[1]!.AsArray().Select(v => v!.GetValue<string>()).ToArray()),
                "runic.inlineResource" => workspace.Inline(sourcePath, new Uri(arguments[1]!.GetValue<string>()).LocalPath),
                _ => throw new ArgumentException("Unknown RMF2 workspace command."),
            };
            return WorkspaceEdit(plan);
        }
        var document = args["textDocument"] as JsonObject;
        if (document is null) return null;
        string uri = document["uri"]!.GetValue<string>();
        if (method == "textDocument/didClose") { _buffers.Remove(uri); Publish(uri, new JsonArray()); return null; }
        if (method == "textDocument/didOpen")
        { Update(uri, document["text"]!.GetValue<string>(), document["version"]!.GetValue<int>()); return null; }
        if (!_buffers.TryGetValue(uri, out Buffer? buffer)) throw new InvalidOperationException("Document is not open.");
        if (method == "textDocument/didChange")
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
            Update(uri, text, version); return null;
        }
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
        if (method == "textDocument/rename")
        {
            if (entry is null) throw new InvalidOperationException("The position is not on a resource symbol.");
            string path = new Uri(uri).LocalPath; var workspace = Workspace(path);
            TranslationWorkspaceTransactionPlan plan;
            if (atByte >= entry.NameLocation.StartByte && atByte <= entry.NameLocation.StartByte + entry.NameLocation.LengthBytes)
                plan = workspace.Rename(workspace.LogicalPath(path, entry), args["newName"]!.GetValue<string>());
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
            if (entry.MessageSyntax is { } messageSyntax)
            {
                string? description = Workspace(new Uri(uri).LocalPath).LanguageService.Hover(messageSyntax, MessageOffset(entry, atByte));
                if (description is not null) return new JsonObject { ["contents"] = new JsonObject { ["kind"] = "plaintext", ["value"] = description } };
            }
            string content = string.Join('.', entry.Path) + "\n\n" + string.Join("\n", entry.Comments) + "\n" + string.Join("\n", entry.Properties);
            return new JsonObject { ["contents"] = new JsonObject { ["kind"] = "plaintext", ["value"] = content }, ["range"] = Range(buffer, entry.NameLocation) };
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
                var references = Workspace(new Uri(uri).LocalPath).VariableReferences(new Uri(uri).LocalPath, entry.Key, symbol);
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
            string path = new Uri(uri).LocalPath; var workspace = Workspace(path); var logical = workspace.LogicalPath(path, entry);
            return new JsonArray(workspace.Documents.SelectMany(document => document.Nodes.Where(node => workspace.LogicalPath(document.Source.Path, node).SequenceEqual(logical, StringComparer.Ordinal))
                .Select(node => (JsonNode)new JsonObject { ["uri"] = new Uri(document.Source.Path).AbsoluteUri, ["range"] = Range(new Buffer(Utf8.GetString(document.Source.GetUtf8Bytes()), 0, document), node.NameLocation) })).ToArray());
        }
        if (method == "textDocument/completion")
        {
            var labels = new HashSet<string>(StringComparer.Ordinal) { "one", "two", "few", "many", "zero", "*" };
            string sourcePath = new Uri(uri).LocalPath; var workspace = Workspace(sourcePath);
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
    private Rmf2Workspace Workspace(string path)
    {
        _requestCancellation.ThrowIfCancellationRequested();
        string? directory = Path.GetDirectoryName(path);
        while (directory is not null && !File.Exists(Path.Combine(directory, "runic.json"))) directory = Path.GetDirectoryName(directory);
        if (directory is null)
            directory = _workspaceRoots.SelectMany(root => new[] { root, Path.Combine(root, "translations") }).FirstOrDefault(root => File.Exists(Path.Combine(root, "runic.json")));
        if (directory is null) throw new InvalidOperationException("No runic.json project was found in the resource ancestors or workspace translations directory.");
        CompilerInputs inputs = InputFiles.ReadProject(directory);
        var sources = inputs.Messages.Select(s => new TranslationSource(Path.GetFullPath(s.Path), s.GetUtf8Bytes())).ToDictionary(s => s.Path, StringComparer.Ordinal);
        foreach (var pair in _buffers)
        {
            string file = new Uri(pair.Key).LocalPath;
            if (sources.ContainsKey(file)) sources[file] = new TranslationSource(file, Utf8.GetBytes(pair.Value.Text));
        }
        string root = directory;
        foreach (string file in sources.Keys)
            while (Path.GetRelativePath(root, file).StartsWith("../", StringComparison.Ordinal)) root = Path.GetDirectoryName(root)!;
        return _syntaxCache.Create(root, new TranslationSource(Path.GetFullPath(inputs.Project.Path), inputs.Project.GetUtf8Bytes()), sources.Values, _requestCancellation);
    }
    private JsonObject WorkspaceEdit(TranslationWorkspaceTransactionPlan plan)
    {
        var edits = new JsonArray();
        foreach (var edit in plan.Edits)
        {
            string path = Path.GetFullPath(edit.RelativePath, plan.Root); string uri = new Uri(path).AbsoluteUri;
            if (edit.Kind == TranslationWorkspaceEditKind.Create) edits.Add(new JsonObject { ["kind"] = "create", ["uri"] = uri });
            if (edit.Kind == TranslationWorkspaceEditKind.Delete) { edits.Add(new JsonObject { ["kind"] = "delete", ["uri"] = uri }); continue; }
            _buffers.TryGetValue(uri, out Buffer? buffer);
            string before = edit.Kind == TranslationWorkspaceEditKind.Create ? "" : buffer?.Text ?? File.ReadAllText(path, Utf8);
            edits.Add(new JsonObject {
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
        var syntax = Rmf2ResourceReader.Analyze(new TranslationSource(new Uri(uri).LocalPath, Utf8.GetBytes(text)));
        var buffer = new Buffer(text, version, syntax); _buffers[uri] = buffer;
        IReadOnlyList<TranslationDiagnostic> catalogDiagnostics = Array.Empty<TranslationDiagnostic>();
        try { catalogDiagnostics = Workspace(new Uri(uri).LocalPath).Validate().Diagnostics; }
        catch (Exception error) when (error is InvalidOperationException or IOException or System.Text.Json.JsonException or TranslationAuthoringException) { /* Recoverable syntax remains available while the project cannot compile. */ }
        foreach (var pair in _buffers)
        {
            var current = pair.Value;
            string path = new Uri(pair.Key).LocalPath;
            var diagnostics = current.Syntax.Diagnostics.Concat(catalogDiagnostics.Where(d => Path.GetFullPath(d.Location.Path) == path))
                .DistinctBy(d => (d.Id, d.Location.StartByte, d.Location.LengthBytes, d.Message));
            Publish(pair.Key, new JsonArray(diagnostics.Select(d => (JsonNode)new JsonObject {
                ["range"] = Range(current, d.Location), ["severity"] = d.Severity == TranslationDiagnosticSeverity.Error ? 1 : 2,
                ["code"] = d.Id, ["source"] = "runic-rmf2", ["message"] = d.Message,
            }).ToArray()));
        }
    }
    private void Publish(string uri, JsonArray diagnostics)
    {
        if (_processedRevision != Interlocked.Read(ref _latestRevision)) return;
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "textDocument/publishDiagnostics", ["params"] = new JsonObject { ["uri"] = uri, ["version"] = _buffers.TryGetValue(uri, out var buffer) ? JsonValue.Create(buffer.Version) : null, ["diagnostics"] = diagnostics } });
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
                    data.Add(line - previousLine); data.Add(line == previousLine ? character - previousCharacter : character);
                    data.Add(to["character"]!.GetValue<int>() - character); data.Add(span.Kind); data.Add(0);
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
        byte[] bytes = buffer.Syntax.Source.GetUtf8Bytes();
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
