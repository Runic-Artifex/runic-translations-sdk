using System;
using System.Linq;
using System.Text.Json.Nodes;
using JsonValue = System.Text.Json.Nodes.JsonValue;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tool;

// Protocol 2's standard LSP refactorings: F2 (prepareRename and rename) on resources, MF2 locals, caller inputs and slot
// references. They reuse the workspace's transaction plans, so they edit exactly what the runic.* commands edit.
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

    // textDocument/rename under protocol 2. A resource rename in a workspace that contains application sources asks
    // the user with window/showMessageRequest instead of refusing, because Runic does not rewrite call sites; a client
    // without that request keeps the refusal. Declining returns no edit.
    private JsonObject? StandardRename(string uri, Rmf2ResourceNode? entry, int atByte, string newName)
    {
        RenameTarget target = RenameTargetAt(entry, atByte)
            ?? throw new InvalidOperationException("Rename requires a resource name, an MF2 variable or a slot reference.");
        string path = LocalPath(uri);
        bool mixed = target.Kind == RenameKind.Resource && RequireResourceOnlyWorkspace(path, allowMixed: _showMessageRequestSupport);
        var workspace = Workspace(path);
        // Clients put the whole selected token in the rename box; `$name` and `name` rename the same variable.
        if (target.Kind is RenameKind.Local or RenameKind.Input && newName.StartsWith('$')) newName = newName[1..];
        TranslationWorkspaceTransactionPlan plan = target.Kind switch {
            RenameKind.Resource => workspace.Rename(workspace.LogicalPath(path, entry!), newName),
            RenameKind.Local => workspace.RenameLocal(path, entry!.Key, target.Name, newName),
            RenameKind.Input => workspace.RenameInput(path, entry!.Key, target.Name, newName),
            _ => workspace.RenameSlot(path, entry!.Key, target.Name, newName),
        };
        if (!_fileOperations && plan.Edits.Any(edit => edit.Kind != TranslationWorkspaceEditKind.Replace)) throw new InvalidOperationException("Rename requires client file-operation support.");
        JsonObject edit = WorkspaceEdit(plan);
        if (mixed && !ConfirmResourceSourceRename(string.Join('_', workspace.LogicalPath(path, entry!)), newName)) return null;
        return edit;
    }

    private bool ConfirmResourceSourceRename(string key, string newName)
    {
        JsonNode? choice = ClientRequest("window/showMessageRequest", new JsonObject {
            ["type"] = 2,
            ["message"] = $"This workspace contains application sources (C#, TypeScript, Svelte and others). Runic renames '{key}' to '{newName}' in resource sources only; it does not update call sites. Rename in resource sources?",
            ["actions"] = new JsonArray(new JsonObject { ["title"] = RenameInResourceSources }, new JsonObject { ["title"] = "Cancel" }),
        });
        return choice is JsonObject action && action["title"] is JsonValue title && title.TryGetValue(out string? text) && text == RenameInResourceSources;
    }
}
