using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using TUnit.Core;
using static Runic.Translations.Build.Tests.Rmf2ProtocolLspTests;
using Session = Runic.Translations.Build.Tests.Rmf2DiagnosticLspTests.Session;

namespace Runic.Translations.Build.Tests;

// Protocol 2's refactor code actions (Extract Group, Inline Resource) and the runic.* commands applied through
// workspace/applyEdit.
internal sealed class Rmf2RefactorLspTests
{
    private const string Root = "title = Home\nshop {\n  cart = Cart\n}\n";
    private const string Sub = "item = Item\n";

    private sealed record Layout(TemporaryDirectory Directory, string RootUri, string FeatureUri, string SubUri) : IDisposable
    {
        public void Dispose() => Directory.Dispose();
    }

    private static Layout CreateLayout()
    {
        TemporaryDirectory temporary = new();
        // English only: the transaction plans validate the edited catalog.
        File.WriteAllText(temporary.Resolve("runic.json"), Project.Replace(",\"de\"", "", StringComparison.Ordinal));
        File.WriteAllText(temporary.Resolve("en.rmf2"), Root);
        Directory.CreateDirectory(temporary.Resolve("feature", "sub"));
        File.WriteAllText(temporary.Resolve("feature", "en.rmf2"), "name = Feature\n");
        File.WriteAllText(temporary.Resolve("feature", "sub", "en.rmf2"), Sub);
        return new Layout(temporary, new Uri(temporary.Resolve("en.rmf2")).AbsoluteUri,
            new Uri(temporary.Resolve("feature", "en.rmf2")).AbsoluteUri, new Uri(temporary.Resolve("feature", "sub", "en.rmf2")).AbsoluteUri);
    }

    private static JsonArray CodeActions(Session session, string uri, int line, int character, params string[] only)
    {
        var args = new JsonObject { ["textDocument"] = Document(uri), ["range"] = new JsonObject { ["start"] = Position(line, character), ["end"] = Position(line, character) },
            ["context"] = new JsonObject { ["diagnostics"] = new JsonArray() } };
        if (only.Length != 0) args["context"]!["only"] = new JsonArray(only.Select(kind => (JsonNode)JsonValue.Create(kind)).ToArray());
        JsonNode response = session.Request("textDocument/codeAction", args);
        Assert.True(response["error"] is null, response.ToJsonString());
        return response["result"]!.AsArray();
    }

    private static JsonNode[] Refactors(JsonArray actions) =>
        actions.Where(action => action!["kind"]!.GetValue<string>().StartsWith("refactor", StringComparison.Ordinal)).Select(action => action!).ToArray();

    [Test, DisplayName("LSP protocol 2 offers Extract Group and Inline Resource as refactor actions resolved with revision evidence")]
    public void RefactorActions()
    {
        using Layout layout = CreateLayout();
        using Session session = new(_ => { }, _ => { });
        JsonNode initialize = session.Request("initialize", Initialize(layout.Directory.Path, FullClientCapabilities(), protocol: 2));
        Assert.Equal("quickfix,refactor.extract,refactor.inline", string.Join(",",
            initialize["result"]!["capabilities"]!["codeActionProvider"]!["codeActionKinds"]!.AsArray().Select(kind => kind!.GetValue<string>())));
        Open(session, layout.RootUri, Root);
        Open(session, layout.SubUri, Sub);

        // Extract, on the group's name; the action is resolved lazily.
        JsonNode extract = Refactors(CodeActions(session, layout.RootUri, 1, 2)).Single();
        Assert.Equal("refactor.extract", extract["kind"]!.GetValue<string>());
        Assert.Equal("Extract group 'shop' to shop/en.rmf2", extract["title"]!.GetValue<string>());
        Assert.True(extract["edit"] is null, "The refactor action was built eagerly.");
        Assert.Equal(0, Refactors(CodeActions(session, layout.RootUri, 0, 2)).Length, "Extract was offered outside a group name.");
        JsonNode resolved = session.Request("codeAction/resolve", extract.DeepClone().AsObject());
        JsonArray changes = resolved["result"]!["edit"]!["documentChanges"]!.AsArray();
        string created = new Uri(layout.Directory.Resolve("shop", "en.rmf2")).AbsoluteUri;
        Assert.True(changes.Any(change => change!["kind"]?.GetValue<string>() == "create" && change["uri"]!.GetValue<string>() == created), resolved.ToJsonString());
        Assert.Contains("cart = Cart", changes.Single(change => change!["textDocument"]?["uri"]?.GetValue<string>() == created)!["edits"]![0]!["newText"]!.GetValue<string>());
        JsonNode rootEdit = changes.Single(change => change!["textDocument"]?["uri"]?.GetValue<string>() == layout.RootUri)!;
        Assert.Equal(1, rootEdit["textDocument"]!["version"]!.GetValue<int>());
        Assert.Equal("title = Home\n", rootEdit["edits"]![0]!["newText"]!.GetValue<string>());

        // Inline: one action per ancestor resource of the same locale, nearest first; `only: refactor` selects both kinds.
        JsonNode[] inline = Refactors(CodeActions(session, layout.SubUri, 0, 0, "refactor"));
        Assert.Equal("Inline into ../en.rmf2|Inline into ../../en.rmf2", string.Join("|", inline.Select(action => action["title"]!.GetValue<string>())));
        Assert.True(inline.All(action => action["kind"]!.GetValue<string>() == "refactor.inline"), "Wrong inline kind.");
        JsonArray inlined = session.Request("codeAction/resolve", inline[0].DeepClone().AsObject())["result"]!["edit"]!["documentChanges"]!.AsArray();
        Assert.True(inlined.Any(change => change!["kind"]?.GetValue<string>() == "delete" && change["uri"]!.GetValue<string>() == layout.SubUri), inlined.ToJsonString());
        Assert.Equal("name = Feature\nsub {\n  item = Item\n}\n",
            inlined.Single(change => change!["textDocument"]?["uri"]?.GetValue<string>() == layout.FeatureUri)!["edits"]![0]!["newText"]!.GetValue<string>());

        // `only` filters hierarchically: quick fixes alone exclude refactors, refactor.inline excludes extract.
        Assert.Equal(0, Refactors(CodeActions(session, layout.RootUri, 1, 2, "quickfix")).Length);
        Assert.Equal(0, CodeActions(session, layout.RootUri, 1, 2, "refactor.inline").Count);
        Assert.Equal(1, CodeActions(session, layout.RootUri, 1, 2, "refactor.extract").Count);

        // An action resolved after its document changed is stale.
        session.Notify("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = layout.RootUri, ["version"] = 2 },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = Root.Replace("Home", "Start", StringComparison.Ordinal) }) });
        JsonNode stale = session.Request("codeAction/resolve", extract.DeepClone().AsObject());
        Assert.Equal(-32801, stale["error"]?["code"]?.GetValue<int>(), stale.ToJsonString());
        Assert.Equal(Root, File.ReadAllText(layout.Directory.Resolve("en.rmf2")));
        Assert.False(File.Exists(layout.Directory.Resolve("shop", "en.rmf2")), "Resolving wrote the extracted file.");
    }

    [Test, DisplayName("LSP offers no refactor actions to protocol 1 or to clients without file operations")]
    public void RefactorDegradation()
    {
        using Layout layout = CreateLayout();
        JsonObject withoutFiles = FullClientCapabilities();
        withoutFiles["workspace"]!["workspaceEdit"]!.AsObject().Remove("resourceOperations");
        foreach ((JsonObject capabilities, int? protocol) in new[] { (FullClientCapabilities(), (int?)null), (withoutFiles, 2) })
        {
            using Session session = new(_ => { }, _ => { });
            session.Request("initialize", Initialize(layout.Directory.Path, capabilities, protocol));
            Open(session, layout.RootUri, Root);
            Open(session, layout.SubUri, Sub);
            Assert.Equal(0, Refactors(CodeActions(session, layout.RootUri, 1, 2)).Length, $"protocol {protocol}");
            Assert.Equal(0, Refactors(CodeActions(session, layout.SubUri, 0, 0)).Length, $"protocol {protocol}");
            // Protocol 1 answers a refactor-only request with nothing, as 0.13 did.
            if (protocol is null) Assert.Equal(0, CodeActions(session, layout.RootUri, 1, 2, "refactor").Count);
        }
    }

    [Test, DisplayName("LSP protocol 2 applies runic.* command edits through workspace/applyEdit")]
    public void CommandsApplyEdits()
    {
        using Layout layout = CreateLayout();
        using Session session = new(_ => { }, _ => { });
        session.Request("initialize", Initialize(layout.Directory.Path, FullClientCapabilities(), protocol: 2));
        Open(session, layout.RootUri, Root);
        int version = 1;
        string? failure = null;
        // As VS Code does, the client changes the open document before it answers applyEdit.
        session.OnServerRequest = request => {
            if (failure is not null) return new JsonObject { ["result"] = new JsonObject { ["applied"] = false, ["failureReason"] = failure } };
            session.Notify("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = layout.RootUri, ["version"] = ++version },
                ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = Root }) });
            return new JsonObject { ["result"] = new JsonObject { ["applied"] = true } };
        };
        JsonNode rename = session.Request("workspace/executeCommand", new JsonObject {
            ["command"] = "runic.renameResource", ["arguments"] = new JsonArray(layout.RootUri, new JsonArray("title"), "heading") });
        Assert.True(rename["error"] is null && rename["result"] is null, rename.ToJsonString());
        JsonNode applied = session.ServerRequests.Single();
        Assert.Equal("workspace/applyEdit", applied["method"]!.GetValue<string>());
        Assert.Equal("Rename resource", applied["params"]!["label"]!.GetValue<string>());
        Assert.Contains("heading = Home", applied["params"]!["edit"]!["documentChanges"]!.ToJsonString());

        JsonNode extract = session.Request("workspace/executeCommand", new JsonObject {
            ["command"] = "runic.extractGroup", ["arguments"] = new JsonArray(layout.RootUri, new JsonArray("shop")) });
        Assert.True(extract["error"] is null && extract["result"] is null, extract.ToJsonString());
        Assert.Contains("\"kind\":\"create\"", session.ServerRequests[^1]["params"]!["edit"]!.ToJsonString());

        failure = "The file is read-only.";
        JsonNode refused = session.Request("workspace/executeCommand", new JsonObject {
            ["command"] = "runic.renameResource", ["arguments"] = new JsonArray(layout.RootUri, new JsonArray("title"), "caption") });
        Assert.Equal(-32803, refused["error"]?["code"]?.GetValue<int>(), refused.ToJsonString());
        Assert.Contains("The file is read-only.", refused["error"]!["message"]!.GetValue<string>());
    }

    [Test, DisplayName("LSP protocol 2 returns command edits to a client without workspace/applyEdit")]
    public void CommandsWithoutApplyEdit()
    {
        using Layout layout = CreateLayout();
        JsonObject capabilities = FullClientCapabilities();
        capabilities["workspace"]!.AsObject().Remove("applyEdit");
        using Session session = new(_ => { }, _ => { });
        session.Request("initialize", Initialize(layout.Directory.Path, capabilities, protocol: 2));
        Open(session, layout.RootUri, Root);
        JsonNode rename = session.Request("workspace/executeCommand", new JsonObject {
            ["command"] = "runic.renameResource", ["arguments"] = new JsonArray(layout.RootUri, new JsonArray("title"), "heading") });
        Assert.Contains("heading = Home", rename["result"]!["documentChanges"]!.ToJsonString());
        Assert.Equal(0, session.ServerRequests.Count);
    }
}
