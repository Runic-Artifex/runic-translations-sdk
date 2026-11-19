using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using JsonValue = System.Text.Json.Nodes.JsonValue;

namespace Runic.Translations.Tool;

// Which state changes make a request's result stale (#250). The worker computes a request from the state of every
// message queued before it, so its result is stale only when a state notification queued after it changes something
// the result depends on. LSP reserves ContentModified for that case: a change to the request's own document, not to
// any file of the workspace.
//
// - A request that only reads its document's syntax (outline, folding, semantic tokens, formatting, prepareRename)
//   depends on its document.
// - Other read requests (hover, completion, the code-action listing, runic/message, runic/documentInfo) also depend on
//   the project: runic.json, review files, restore and install state and the configuration.
// - A request that returns locations or edits in other files (rename, definition, references, workspace/executeCommand,
//   codeAction/resolve, and a code-action listing whose refactorings carry their edit) depends on every file below the
//   roots of the workspaces it compiled. An edit is never computed from text the client has changed since.
//
// A watched-file event for a file with an open buffer changes nothing when the file's content on disk is the buffer's
// (the save of an open document): the server reads the buffer, not the disk.
internal sealed partial class Rmf2LanguageServer
{
    private static readonly HashSet<string> SyntaxRequests = new(StringComparer.Ordinal) {
        "textDocument/documentSymbol", "textDocument/foldingRange", "textDocument/semanticTokens/full", "textDocument/formatting",
        "textDocument/prepareRename",
    };
    private static readonly HashSet<string> WorkspaceRequests = new(StringComparer.Ordinal) {
        "textDocument/rename", "textDocument/definition", "textDocument/references", "workspace/executeCommand", "codeAction/resolve",
    };

    // Records what a state notification changes, under _revisionGate, on the reader (or the server's own file watcher).
    // A URI that names no local file changes the project, which refuses every request that depends on more than syntax.
    private void RecordChange(string method, JsonObject request, long revision)
    {
        JsonNode? parameters = request["params"];
        if (method == "workspace/didChangeConfiguration") { _projectChange = revision; return; }
        if (method == "workspace/didChangeWatchedFiles")
        {
            // No changes is a rescan (the server's own watcher after a refused registration).
            if (parameters?["changes"] is not JsonArray changes || changes.Count == 0) { _projectChange = revision; return; }
            foreach (JsonNode? change in changes)
                if (UriKey(change?["uri"]) is { } key && IsResourceFile(key)) _diskChanges[key] = revision;
                else _projectChange = revision;
            return;
        }
        if (UriKey(parameters?["textDocument"]?["uri"]) is not { } document) { _projectChange = revision; return; }
        _bufferChanges[document] = revision;
        if (IsConfiguration(document)) _projectChange = revision;
    }

    // Drops the changes the worker has processed, before it handles the message with this revision.
    private void ForgetProcessedChanges(long revision)
    {
        foreach (var changes in new[] { _bufferChanges, _diskChanges })
            if (changes.Count != 0)
                foreach (string key in changes.Where(change => change.Value <= revision).Select(change => change.Key).ToArray()) changes.Remove(key);
    }

    // Whether a state change queued after the current request (computed at this revision) makes its result stale.
    // Called under _revisionGate, so no change is queued between the check and the response.
    private bool IsStale(long revision)
    {
        // A document named by anything but a file URI cannot be told apart from other files: any change counts.
        if (RequestUris().Any(uri => UriKey(uri) is null)) return _latestRevision != revision;
        string[] documents = RequestUris().Select(UriKey).OfType<string>().ToArray();
        bool workspace = _requestEdits || WorkspaceRequests.Contains(_requestMethod);
        if (!workspace && SyntaxRequests.Contains(_requestMethod))
            return documents.Any(key => FileChanged(key, revision));
        if (_projectChange > revision) return true;
        if (documents.Any(key => FileChanged(key, revision))) return true;
        if (!workspace || _requestRoots.Count == 0) return false;
        bool Below(string key) => _requestRoots.Any(root => IsWithin(root, key));
        return _bufferChanges.Any(change => change.Value > revision && Below(change.Key)) ||
            _diskChanges.Any(change => change.Value > revision && Below(change.Key) && !DiskMatchesBuffer(change.Key));
    }

    private bool FileChanged(string key, long revision) =>
        (_bufferChanges.TryGetValue(key, out long buffer) && buffer > revision) ||
        (_diskChanges.TryGetValue(key, out long disk) && disk > revision && !DiskMatchesBuffer(key));

    // The URIs of the files the current request names: its document, a resolved action's document and Inline Resource
    // destination, and a command's source and destination.
    private IEnumerable<JsonNode> RequestUris()
    {
        JsonObject args = _requestParams;
        var uris = new List<JsonNode?> { args["textDocument"]?["uri"] };
        if (_requestMethod == "codeAction/resolve") { uris.Add(args["data"]?["uri"]); uris.Add(args["data"]?["destination"]); }
        if (_requestMethod == "workspace/executeCommand" && args["arguments"] is JsonArray arguments)
        {
            if (arguments.Count > 0) uris.Add(arguments[0]);
            if (args["command"] is JsonValue command && command.TryGetValue(out string? name) && name == "runic.inlineResource" && arguments.Count > 1) uris.Add(arguments[1]);
        }
        return uris.OfType<JsonNode>();
    }

    // Whether a file with an open buffer has the buffer's content on disk (a UTF-8 BOM aside), as after its save.
    private bool DiskMatchesBuffer(string key)
    {
        if (!TryGetBuffer(key, out _, out Buffer? buffer)) return false;
        try
        {
            byte[] saved = File.ReadAllBytes(key);
            ReadOnlySpan<byte> content = saved.AsSpan();
            if (content.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) content = content[3..];
            return content.SequenceEqual(Utf8.GetBytes(buffer.Text));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    // The file key of a file URI; null for anything else, so a malformed URI never stops the reader.
    private static string? UriKey(JsonNode? uri)
    {
        if (uri is not JsonValue value || !value.TryGetValue(out string? text)) return null;
        try { return Uri.TryCreate(text, UriKind.Absolute, out Uri? parsed) && parsed.IsFile ? FileKey(LocalPath(text)) : null; }
        catch (Exception error) when (error is FormatException or ArgumentException or IOException or NotSupportedException or InvalidOperationException) { return null; }
    }

    private static bool IsResourceFile(string key) =>
        Path.GetExtension(key).Equals(".rmf2", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(key).Equals(".mf2", StringComparison.OrdinalIgnoreCase);

    private static bool IsConfiguration(string key) => Path.GetFileName(key).Equals("runic.json", StringComparison.OrdinalIgnoreCase);
}
