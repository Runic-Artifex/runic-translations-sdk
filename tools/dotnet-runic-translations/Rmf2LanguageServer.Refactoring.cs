using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using JsonValue = System.Text.Json.Nodes.JsonValue;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tool;

// The standard LSP refactorings: F2 (prepareRename and rename) on resources, MF2 locals, caller inputs and slot
// references, Extract Group and Inline Resource as refactor code actions, and runic.* commands applied through
// workspace/applyEdit. They reuse the workspace's transaction plans, so they edit exactly what the runic.* commands edit.
internal sealed partial class Rmf2LanguageServer
{
    private enum RenameKind { Resource, Local, Input, Slot }

    // A renameable symbol: its kind, its current name and the physical byte range of the name (without the `$` of a
    // variable or the `|` quotes of a slot), which is what the client's rename box replaces.
    private readonly record struct RenameTarget(RenameKind Kind, string Name, int StartByte, int EndByte);

    private const string RenameInResourceSources = "Rename in resource sources";

    private static RenameTarget? RenameTargetAt(Rmf2ResourceNode? entry, int atByte)
    {
        if (entry is null) return null;
        int nameEnd = entry.NameLocation.StartByte + entry.NameLocation.LengthBytes;
        if (atByte >= entry.NameLocation.StartByte && atByte <= nameEnd)
            return new RenameTarget(RenameKind.Resource, entry.Path[^1], entry.NameLocation.StartByte, nameEnd);
        if (entry.IsGroup || entry.MessageSyntax is not { } syntax || entry.MessageByteMap.Count == 0) return null;
        var map = entry.MessageByteMap;
        // A cursor directly after a name still names it, as editors place it after a double click.
        bool Covers(TextSourceLocation location) => map[location.StartByte] <= atByte && atByte <= map[location.StartByte + location.LengthBytes];
        var names = syntax.Declarations.Select(declaration => declaration.Name)
            .Concat(syntax.Tokens.Where(token => token.Kind == Mf2SyntaxTokenKind.Variable).Select(token => token.Value))
            .Distinct(StringComparer.Ordinal);
        foreach (string name in names)
            foreach (TextSourceLocation location in syntax.VariableReferences(name))
                if (Covers(location))
                {
                    bool local = syntax.Declarations.Any(declaration => declaration.Kind == "local" && declaration.Name == name);
                    return new RenameTarget(local ? RenameKind.Local : RenameKind.Input, name, map[location.StartByte] + 1, map[location.StartByte + location.LengthBytes]);
                }
        foreach (Mf2ExpressionSyntax expression in syntax.Expressions)
            foreach (Mf2PropertySyntax option in expression.Options.Where(option => option.Name == "ref" && option.Value is { Kind: not Mf2OperandKind.Variable }))
            {
                Mf2OperandSyntax value = option.Value!;
                if (!Covers(value.Location)) continue;
                int quote = value.Quoted ? 1 : 0;
                return new RenameTarget(RenameKind.Slot, value.Value, map[value.Location.StartByte] + quote, map[value.Location.StartByte + value.Location.LengthBytes] - quote);
            }
        return null;
    }

    // textDocument/prepareRename: the range and current name of the symbol, or null where nothing can be renamed.
    private JsonObject? PrepareRename(Buffer buffer, Rmf2ResourceNode? entry, int atByte)
    {
        if (RenameTargetAt(entry, atByte) is not { } target) return null;
        var location = new TextSourceLocation(buffer.Syntax.Source.Path, target.StartByte, target.EndByte - target.StartByte, 0, 0, 0, 0);
        return new JsonObject { ["range"] = Range(buffer, location), ["placeholder"] = target.Name };
    }

    // textDocument/rename. A resource rename in a workspace that contains application sources asks
    // the user with window/showMessageRequest instead of refusing, because Runic does not rewrite call sites. The
    // server asks whether or not the client advertises window.showMessage: Visual Studio 2026 advertises no `window`
    // capabilities yet shows the request as an InfoBar (SPIKE-VS-0). A client that answers with an error (such as
    // MethodNotFound) gets the refusal; declining, or answering null, returns no edit.
    private JsonObject? StandardRename(string uri, Rmf2ResourceNode? entry, int atByte, string newName)
    {
        RenameTarget target = RenameTargetAt(entry, atByte)
            ?? throw new InvalidOperationException("Rename requires a resource name, an MF2 variable or a slot reference.");
        string path = LocalPath(uri);
        bool mixed = target.Kind == RenameKind.Resource && RequireResourceOnlyWorkspace(path, allowMixed: true);
        var workspace = Workspace(path);
        // Clients put the whole selected token in the rename box; `$name` and `name` rename the same variable.
        if (target.Kind is RenameKind.Local or RenameKind.Input && newName.StartsWith('$')) newName = newName[1..];
        TranslationWorkspaceTransactionPlan plan = target.Kind switch {
            RenameKind.Resource => workspace.Rename(workspace.LogicalPath(path, entry!), newName),
            RenameKind.Local => workspace.RenameLocal(path, entry!.Key, target.Name, newName),
            RenameKind.Input => workspace.RenameInput(path, entry!.Key, target.Name, newName),
            _ => workspace.RenameSlot(path, entry!.Key, target.Name, newName),
        };
        if (!SupportsResourceOperations(plan)) throw new InvalidOperationException("Rename requires client file-operation support.");
        JsonObject edit = WorkspaceEdit(plan);
        if (mixed && !ConfirmResourceSourceRename(string.Join('_', workspace.LogicalPath(path, entry!)), newName)) return null;
        return edit;
    }

    private bool ConfirmResourceSourceRename(string key, string newName)
    {
        JsonNode? choice;
        try
        {
            choice = ClientRequest("window/showMessageRequest", new JsonObject {
                ["type"] = 2,
                ["message"] = $"This workspace contains application sources (C#, TypeScript, Svelte and others). Runic renames '{key}' to '{newName}' in resource sources only; it does not update call sites. Rename in resource sources?",
                ["actions"] = new JsonArray(new JsonObject { ["title"] = RenameInResourceSources }, new JsonObject { ["title"] = "Cancel" }),
            });
        }
        catch (ClientRequestFailedException) { throw new TranslationAuthoringException(MixedWorkspaceRefusal); }
        return choice is JsonObject action && action["title"] is JsonValue title && title.TryGetValue(out string? text) && text == RenameInResourceSources;
    }

    // Whether a code-action request's `only` selects a kind: LSP kinds are hierarchical, so `refactor` selects
    // `refactor.extract`, and an empty kind selects everything.
    private static bool RequestsKind(JsonArray? only, string kind) =>
        only is null || only.Any(value => value is JsonValue text && text.TryGetValue(out string? requested) &&
            (requested.Length == 0 || kind == requested || kind.StartsWith(requested + ".", StringComparison.Ordinal)));

    // Refactor actions. A client that resolves code actions gets them unresolved: codeAction/resolve builds the edit
    // against the revision they were offered for. Visual Studio 2026 lists no `edit` in resolveSupport.properties yet
    // resolves it (SPIKE-VS-0), so any resolveSupport counts. A client without resolveSupport gets the edit inline.
    // Extract Group creates a file, so it needs the `create` resource operation. Inline Resource deletes the inlined
    // file, or, for a client without `delete` (Visual Studio 2026), empties it and says so in its title.
    private IEnumerable<JsonObject> RefactorActions(string uri, Buffer buffer, int from, int to, JsonArray? only)
    {
        foreach (JsonObject action in UnresolvedRefactorActions(uri, buffer, from, to, only))
        {
            if (_codeActionResolve) { yield return action; continue; }
            // Building one action's edit can fail (an invalid catalog); that action is left out, not the request.
            JsonObject? resolved = null;
            try { resolved = ResolveRefactor(action, uri, action["data"]!); }
            catch (Exception error) when (error is TranslationAuthoringException or InvalidOperationException or IOException or ArgumentException) { }
            if (resolved is not null) yield return resolved;
        }
    }

    private IEnumerable<JsonObject> UnresolvedRefactorActions(string uri, Buffer buffer, int from, int to, JsonArray? only)
    {
        string path = LocalPath(uri);
        if (!_createFiles || !Path.GetExtension(path).Equals(".rmf2", StringComparison.OrdinalIgnoreCase)) yield break;
        string revision = Rmf2DiagnosticActions.Revision(buffer.Encoded);
        JsonObject Data(string refactor) => new() { ["uri"] = uri, ["version"] = buffer.Version, ["revision"] = revision, ["refactor"] = refactor };
        string fileName = Path.GetFileName(path);
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        // Extract Group, on a group's name: the group moves to the same locale's file in its directory.
        if (RequestsKind(only, "refactor.extract"))
            foreach (Rmf2ResourceNode group in buffer.Syntax.Nodes.Where(node => node.IsGroup && node.NameLocation.StartByte <= to && from <= node.NameLocation.StartByte + node.NameLocation.LengthBytes))
            {
                string destination = Path.Combine(directory, Path.Combine(group.Path.ToArray()), fileName);
                if (PathEntryExists(destination) || TryGetBuffer(destination, out _, out _)) continue;
                JsonObject data = Data("extractGroup");
                data["group"] = new JsonArray(group.Path.Select(segment => (JsonNode)JsonValue.Create(segment)).ToArray());
                yield return new JsonObject { ["title"] = $"Extract group '{string.Join('.', group.Path)}' to {string.Join('/', group.Path)}/{fileName}", ["kind"] = "refactor.extract", ["data"] = data };
            }
        // Inline Resource: one action per existing resource of the same locale in an ancestor directory within the
        // file's source root, nearest first. This replaces the destination picker of the runic.inlineResource command.
        if (RequestsKind(only, "refactor.inline") && InlineRoot(uri, path) is { } root)
            for (string? ancestor = Path.GetDirectoryName(directory); ancestor is not null && IsWithin(root, ancestor); ancestor = Path.GetDirectoryName(ancestor))
            {
                string destination = Path.Combine(ancestor, fileName);
                if (!File.Exists(destination) && !TryGetBuffer(destination, out _, out _)) continue;
                JsonObject data = Data("inlineResource");
                data["destination"] = ClientUri(destination);
                string title = $"Inline into {Path.GetRelativePath(directory, destination).Replace('\\', '/')}";
                if (!_deleteFiles) title += $" (leaves {fileName} empty)";
                yield return new JsonObject { ["title"] = title, ["kind"] = "refactor.inline", ["data"] = data };
            }
    }

    // The source root that contains a resource: the most specific configured root, or the project directory.
    private string? InlineRoot(string uri, string path)
    {
        if (BufferProject(uri, out _) is not { } project) return null;
        string[] roots = ProjectSourceRoots(project);
        string full = Path.GetFullPath(path);
        return (roots.Length == 0 ? [project] : roots).Select(Path.GetFullPath).Where(root => IsWithin(root, full))
            .OrderByDescending(root => root.Length).FirstOrDefault();
    }

    // codeAction/resolve of a refactor action whose revision evidence the caller has checked.
    private JsonObject ResolveRefactor(JsonObject action, string uri, JsonNode data)
    {
        if (!_createFiles) throw new InvalidOperationException("The client does not support the required file operations.");
        string path = LocalPath(uri);
        var workspace = Workspace(path);
        TranslationWorkspaceTransactionPlan plan = data["refactor"]!.GetValue<string>() switch {
            "extractGroup" => workspace.Extract(path, data["group"]!.AsArray().Select(segment => segment!.GetValue<string>()).ToArray()),
            "inlineResource" => workspace.Inline(path, LocalPath(data["destination"]!.GetValue<string>())),
            _ => throw new ArgumentException("Unknown Runic refactoring."),
        };
        JsonObject resolved = action.DeepClone().AsObject();
        resolved["edit"] = WorkspaceEdit(plan, emptyInsteadOfDelete: !_deleteFiles);
        return resolved;
    }

    // Whether the client can apply a plan's file creations and deletions. A deletion it cannot apply is
    // sent as an edit that empties the file (WorkspaceEdit's emptyInsteadOfDelete), which only Inline Resource does.
    private bool SupportsResourceOperations(TranslationWorkspaceTransactionPlan plan) =>
        (_createFiles || plan.Edits.All(edit => edit.Kind != TranslationWorkspaceEditKind.Create)) &&
        (_deleteFiles || plan.Edits.All(edit => edit.Kind != TranslationWorkspaceEditKind.Delete));

    // The server applies a runic.* command's edit with workspace/applyEdit and answers null, as LSP expects of
    // commands. A client without applyEdit receives the WorkspaceEdit as the result.
    private JsonObject? ApplyCommandEdit(string label, JsonObject edit)
    {
        if (!_applyEditSupport) return edit;
        // Never apply an edit computed from files the client has changed since (IsStale).
        lock (_revisionGate) if (IsStale(_processedRevision)) throw new ContentModifiedException();
        JsonNode? result = ClientRequest("workspace/applyEdit", new JsonObject { ["label"] = label, ["edit"] = edit });
        if (result is not JsonObject response || response["applied"] is not JsonValue applied || !applied.TryGetValue(out bool done) || !done)
        {
            string? reason = result is JsonObject failed && failed["failureReason"] is JsonValue text && text.TryGetValue(out string? value) ? value : null;
            throw new InvalidOperationException("The client did not apply the edit" + (reason is null ? "." : ": " + reason));
        }
        _editApplied = true;
        return null;
    }
}
