using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tool;

/// <summary>Bounded stdio LSP transport over the shared recoverable resource model.</summary>
internal sealed class Rmf2LanguageServer
{
    private readonly Dictionary<string, Buffer> _buffers = new(StringComparer.Ordinal);
    private readonly Stream _input;
    private readonly Stream _output;
    private string _encoding = "utf-16";
    private bool _shutdown;
    private bool _fileOperations;
    private readonly List<string> _workspaceRoots = new();
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal Rmf2LanguageServer(Stream input, Stream output) { _input = input; _output = output; }
    internal int Run()
    {
        while (Read() is { } request)
        {
            string method = request["method"]?.GetValue<string>() ?? "";
            JsonNode? id = request["id"]?.DeepClone();
            try
            {
                if (method == "exit") return _shutdown ? 0 : 1;
                JsonNode? result = Handle(method, request["params"] as JsonObject ?? new JsonObject());
                if (id is not null) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or TranslationAuthoringException or KeyNotFoundException or IOException)
            {
                if (id is not null) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = -32602, ["message"] = error.Message } });
            }
        }
        return 0;
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
                ["foldingRangeProvider"] = true, ["hoverProvider"] = true, ["definitionProvider"] = true,
                ["documentFormattingProvider"] = true, ["renameProvider"] = true,
                ["executeCommandProvider"] = new JsonObject { ["commands"] = new JsonArray("runic.extractGroup", "runic.inlineResource") },
                ["completionProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray("$", ":", "#", "/", "=") },
            }, ["serverInfo"] = new JsonObject { ["name"] = "Runic RMF2", ["version"] = "1" } };
        }
        if (method == "shutdown") { _shutdown = true; return null; }
        if (method is "initialized" or "$/cancelRequest" or "workspace/didChangeConfiguration") return null;
        if (method == "workspace/executeCommand")
        {
            if (!_fileOperations) throw new InvalidOperationException("The client does not support the required file operations.");
            string command = args["command"]!.GetValue<string>();
            JsonArray arguments = args["arguments"]!.AsArray();
            string sourceUri = arguments[0]!.GetValue<string>(); string sourcePath = new Uri(sourceUri).LocalPath;
            var workspace = Workspace(sourcePath);
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
            // Resource renames never text-replace application inputs or MF2 locals.
            if (atByte < entry.NameLocation.StartByte || atByte > entry.NameLocation.StartByte + entry.NameLocation.LengthBytes)
                throw new InvalidOperationException("Resource rename requires a resource key or group name.");
            string path = new Uri(uri).LocalPath; var workspace = Workspace(path);
            var plan = workspace.Rename(workspace.LogicalPath(path, entry), args["newName"]!.GetValue<string>());
            if (!_fileOperations && plan.Edits.Any(e => e.Kind != TranslationWorkspaceEditKind.Replace)) throw new InvalidOperationException("Rename requires client file-operation support.");
            return WorkspaceEdit(plan);
        }
        if (method == "textDocument/hover")
        {
            if (entry is null) return null;
            string content = string.Join('.', entry.Path) + "\n\n" + string.Join("\n", entry.Comments) + "\n" + string.Join("\n", entry.Properties);
            return new JsonObject { ["contents"] = new JsonObject { ["kind"] = "plaintext", ["value"] = content }, ["range"] = Range(buffer, entry.NameLocation) };
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
            var labels = new HashSet<string>(StringComparer.Ordinal) { ":string", ":integer", ":number", ":date", ":time", ":datetime", ":runic:uuid", ":runic:boolean", ":runic:relative-time", "one", "two", "few", "many", "zero", "*" };
            foreach (string tag in new[] { "strong", "em", "bold", "italic", "code", "br", "link", "action", "icon" }) { labels.Add("#" + tag); labels.Add("/" + tag); }
            if (entry?.Message is string message)
            {
                foreach (Match match in Regex.Matches(message, "\\$[A-Za-z_][A-Za-z0-9_]*")) labels.Add(match.Value);
                foreach (Match match in Regex.Matches(message, "ref=([A-Za-z_][A-Za-z0-9_-]*)")) labels.Add("ref=" + match.Groups[1].Value);
            }
            return new JsonArray(labels.Order(StringComparer.Ordinal).Select(label => (JsonNode)new JsonObject { ["label"] = label, ["kind"] = 14 }).ToArray());
        }
        return null;
    }
    private Rmf2Workspace Workspace(string path)
    {
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
        return new Rmf2Workspace(root, new TranslationSource(Path.GetFullPath(inputs.Project.Path), inputs.Project.GetUtf8Bytes()), sources.Values);
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
        var diagnostics = new JsonArray(syntax.Diagnostics.Select(d => (JsonNode)new JsonObject {
            ["range"] = Range(buffer, d.Location), ["severity"] = d.Severity == TranslationDiagnosticSeverity.Error ? 1 : 2,
            ["code"] = d.Id, ["source"] = "runic-rmf2", ["message"] = d.Message,
        }).ToArray());
        Publish(uri, diagnostics);
    }
    private void Publish(string uri, JsonArray diagnostics) => Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "textDocument/publishDiagnostics", ["params"] = new JsonObject { ["uri"] = uri, ["diagnostics"] = diagnostics } });
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
