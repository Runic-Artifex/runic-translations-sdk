using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using TUnit.Core;
using static Runic.Translations.Build.Tests.Rmf2ProtocolLspTests;
using Session = Runic.Translations.Build.Tests.Rmf2DiagnosticLspTests.Session;

namespace Runic.Translations.Build.Tests;

// Protocol 2's F2: prepareRename and rename on resources, MF2 locals, caller inputs and slot references, and the
// confirmation of a resource rename in a workspace with application sources.
internal sealed class Rmf2RenameLspTests
{
    private const string English = "title = Hello {$name}\nlinked = {#link ref=help}Help{/link}\ngreeting =\n    .local $short = {$name}\n    {{Hi {$short}}}\n";
    private const string German = "title = Hallo {$name}\nlinked = {#link ref=help}Hilfe{/link}\ngreeting =\n    .local $short = {$name}\n    {{Hallo {$short}}}\n";

    private static (TemporaryDirectory Directory, string EnglishUri, string GermanUri) CreateRenameProject(bool application)
    {
        TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("runic.json"), Project);
        File.WriteAllText(temporary.Resolve("en.rmf2"), English);
        File.WriteAllText(temporary.Resolve("de.rmf2"), German);
        if (application) File.WriteAllText(temporary.Resolve("app.ts"), "export const title = text.title({ name: \"Ada\" });\n");
        return (temporary, new Uri(temporary.Resolve("en.rmf2")).AbsoluteUri, new Uri(temporary.Resolve("de.rmf2")).AbsoluteUri);
    }

    // The UTF-16 position of a needle's first occurrence in the text, moved by delta characters.
    private static JsonObject At(string text, string needle, int delta = 0)
    {
        int offset = text.IndexOf(needle, StringComparison.Ordinal) + delta;
        Assert.True(offset >= delta, "Missing " + needle);
        int line = text[..offset].Count(character => character == '\n');
        return Position(line, offset - (text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1));
    }

    private static JsonObject RenameArgs(string uri, JsonObject position, string newName) =>
        new() { ["textDocument"] = Document(uri), ["position"] = position, ["newName"] = newName };

    // The text a WorkspaceEdit gives a file (every Runic edit replaces the whole document).
    private static string EditedText(JsonNode response, string uri) =>
        response["result"]!["documentChanges"]!.AsArray().Single(change => change!["textDocument"]?["uri"]?.GetValue<string>() == uri)!["edits"]![0]!["newText"]!.GetValue<string>();

    private static string[] EditedUris(JsonNode response) =>
        response["result"]!["documentChanges"]!.AsArray().Select(change => change!["textDocument"]!["uri"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();

    [Test, DisplayName("LSP protocol 2 prepares and renames MF2 inputs, locals, slots and resources with F2")]
    public void RenameSymbols()
    {
        var (temporary, englishUri, germanUri) = CreateRenameProject(application: false);
        using (temporary)
        {
            using Session session = new(_ => { }, _ => { });
            JsonNode initialize = session.Request("initialize", Initialize(temporary.Path, FullClientCapabilities(), protocol: 2));
            Assert.True(initialize["result"]!["capabilities"]!["renameProvider"]!["prepareProvider"]!.GetValue<bool>(), "Missing prepareProvider.");
            Open(session, englishUri, English);

            // Each case: where the name starts and what it is. The request position lies inside the name.
            foreach ((string needle, int start, string placeholder) in new[] { ("{$name}", 2, "name"), ("ref=help", 4, "help"), ("{$short}", 2, "short"), ("title", 0, "title") })
            {
                JsonNode prepared = session.Request("textDocument/prepareRename", new JsonObject { ["textDocument"] = Document(englishUri), ["position"] = At(English, needle, start + 1) });
                Assert.Equal(placeholder, prepared["result"]?["placeholder"]?.GetValue<string>(), prepared.ToJsonString());
                // The range is the name alone, without `$`, so the rename box replaces exactly the name.
                JsonNode range = prepared["result"]!["range"]!;
                Assert.Equal(At(English, needle, start).ToJsonString(), range["start"]!.ToJsonString());
                Assert.Equal(At(English, needle, start + placeholder.Length).ToJsonString(), range["end"]!.ToJsonString());
            }
            JsonNode text = session.Request("textDocument/prepareRename", new JsonObject { ["textDocument"] = Document(englishUri), ["position"] = At(English, "Hello", 1) });
            Assert.True(text["error"] is null && text["result"] is null, "Plain pattern text is renameable: " + text.ToJsonString());

            // A caller input is renamed in every locale's resource; `$person` and `person` mean the same name.
            JsonNode input = session.Request("textDocument/rename", RenameArgs(englishUri, At(English, "{$name}", 3), "$person"));
            Assert.Equal(string.Join(",", new[] { germanUri, englishUri }.Order(StringComparer.Ordinal)), string.Join(",", EditedUris(input)), input.ToJsonString());
            Assert.Contains("title = Hello {$person}\n", EditedText(input, englishUri));
            Assert.Contains("title = Hallo {$person}\n", EditedText(input, germanUri));
            Assert.Contains(".local $short = {$name}", EditedText(input, englishUri), "Another message's input was renamed.");

            JsonNode slot = session.Request("textDocument/rename", RenameArgs(englishUri, At(English, "ref=help", 5), "docs"));
            Assert.Contains("{#link ref=docs}Help{/link}", EditedText(slot, englishUri));
            Assert.Contains("{#link ref=docs}Hilfe{/link}", EditedText(slot, germanUri));

            JsonNode local = session.Request("textDocument/rename", RenameArgs(englishUri, At(English, "{$short}", 2), "brief"));
            Assert.Equal(englishUri, string.Join(",", EditedUris(local)));
            Assert.Contains(".local $brief = {$name}\n    {{Hi {$brief}}}", EditedText(local, englishUri));

            JsonNode resource = session.Request("textDocument/rename", RenameArgs(englishUri, At(English, "title"), "heading"));
            Assert.Contains("heading = Hello {$name}", EditedText(resource, englishUri));
            Assert.Contains("heading = Hallo {$name}", EditedText(resource, germanUri));

            JsonNode nothing = session.Request("textDocument/rename", RenameArgs(englishUri, At(English, "Hello", 1), "x"));
            Assert.Equal(-32803, nothing["error"]!["code"]!.GetValue<int>(), nothing.ToJsonString());
            // A resource-only workspace needs no confirmation, and nothing was written.
            Assert.Equal(0, session.ServerRequests.Count);
            Assert.Equal(English, File.ReadAllText(temporary.Resolve("en.rmf2")));
        }
    }

    [Test, DisplayName("LSP protocol 2 asks before a resource rename in a workspace with application sources")]
    public void MixedWorkspaceConfirmation()
    {
        var (temporary, englishUri, germanUri) = CreateRenameProject(application: true);
        using (temporary)
        {
            using Session session = new(_ => { }, _ => { });
            session.Request("initialize", Initialize(temporary.Path, FullClientCapabilities(), protocol: 2));
            Open(session, englishUri, English);
            foreach ((JsonNode? answer, bool renamed) in new[] { ((JsonNode?)new JsonObject { ["title"] = "Rename in resource sources" }, true),
                (new JsonObject { ["title"] = "Cancel" }, false), (null, false) })
            {
                session.OnServerRequest = _ => new JsonObject { ["result"] = answer?.DeepClone() };
                int before = session.ServerRequests.Count;
                JsonNode response = session.Request("textDocument/rename", RenameArgs(englishUri, At(English, "title"), "heading"));
                Assert.True(response["error"] is null, response.ToJsonString());
                JsonNode question = session.ServerRequests.Single(frame => session.ServerRequests.IndexOf(frame) >= before);
                Assert.Equal("window/showMessageRequest", question["method"]!.GetValue<string>());
                Assert.Contains("does not update call sites", question["params"]!["message"]!.GetValue<string>());
                Assert.Equal("Rename in resource sources,Cancel", string.Join(",", question["params"]!["actions"]!.AsArray().Select(action => action!["title"]!.GetValue<string>())));
                if (renamed) Assert.Contains("heading = Hallo {$name}", EditedText(response, germanUri));
                else Assert.True(response["result"] is null, "A declined rename returned an edit: " + response.ToJsonString());
            }
            // Inputs and slots are resource-only names: no confirmation.
            int asked = session.ServerRequests.Count;
            JsonNode input = session.Request("textDocument/rename", RenameArgs(englishUri, At(English, "{$name}", 2), "person"));
            Assert.Contains("{$person}", EditedText(input, englishUri));
            Assert.Equal(asked, session.ServerRequests.Count);
        }
    }

    [Test, DisplayName("LSP protocol 2 asks Visual Studio 2026 before a mixed-workspace resource rename, although it advertises no window capabilities")]
    public void VisualStudioConfirmation()
    {
        var (temporary, englishUri, germanUri) = CreateRenameProject(application: true);
        using (temporary)
        {
            JsonObject capabilities = VisualStudioCapabilities();
            Assert.True(capabilities["window"] is null, "The captured Visual Studio capabilities gained a window block.");
            using Session session = new(_ => { }, _ => { });
            // Visual Studio shows the request as an InfoBar and answers with the chosen action.
            session.OnServerRequest = _ => new JsonObject { ["result"] = new JsonObject { ["title"] = "Rename in resource sources" } };
            session.Request("initialize", Initialize(temporary.Path, capabilities, protocol: 2));
            Open(session, englishUri, English);
            JsonNode prepared = session.Request("textDocument/prepareRename", new JsonObject { ["textDocument"] = Document(englishUri), ["position"] = At(English, "title") });
            Assert.Equal("title", prepared["result"]!["placeholder"]!.GetValue<string>());
            JsonNode response = session.Request("textDocument/rename", RenameArgs(englishUri, At(English, "title"), "heading"));
            Assert.Equal("window/showMessageRequest", session.ServerRequests.Single()["method"]!.GetValue<string>());
            Assert.Contains("heading = Hallo {$name}", EditedText(response, germanUri));
        }
    }

    [Test, DisplayName("LSP keeps the mixed-workspace refusal for clients that fail showMessageRequest")]
    public void MixedWorkspaceRefusal()
    {
        var (temporary, englishUri, _) = CreateRenameProject(application: true);
        using (temporary)
        {
            // Protocol 2 asks whether or not the client advertises window.showMessage; a client that answers with an
            // error, as one without the request does (MethodNotFound), gets the refusal.
            JsonObject capabilities = FullClientCapabilities();
            capabilities.Remove("window");
            using (Session session = new(_ => { }, _ => { }))
            {
                session.OnServerRequest = _ => new JsonObject { ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Unhandled method window/showMessageRequest" } };
                session.Request("initialize", Initialize(temporary.Path, capabilities, protocol: 2));
                Open(session, englishUri, English);
                JsonNode refused = session.Request("textDocument/rename", RenameArgs(englishUri, At(English, "title"), "heading"));
                Assert.Equal(-32803, refused["error"]!["code"]!.GetValue<int>(), refused.ToJsonString());
                Assert.Contains("Rename refused: this workspace contains application", refused["error"]!["message"]!.GetValue<string>());
                Assert.Equal("window/showMessageRequest", session.ServerRequests.Single()["method"]!.GetValue<string>());
            }
        }
    }

    [Test, DisplayName("LSP protocol 2 rename answers ContentModified when the document changes during the confirmation")]
    public void StaleConfirmation()
    {
        var (temporary, englishUri, _) = CreateRenameProject(application: true);
        using (temporary)
        {
            using Session session = new(_ => { }, _ => { });
            session.Request("initialize", Initialize(temporary.Path, FullClientCapabilities(), protocol: 2));
            Open(session, englishUri, English);
            int rename = session.Start("textDocument/rename", RenameArgs(englishUri, At(English, "title"), "heading"));
            JsonNode question = session.WaitForServerRequest("window/showMessageRequest");
            session.Notify("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = englishUri, ["version"] = 2 },
                ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = English.Replace("Hello", "Hi", StringComparison.Ordinal) }) });
            session.Respond(question["id"]!, new JsonObject { ["result"] = new JsonObject { ["title"] = "Rename in resource sources" } });
            JsonNode response = session.Wait(rename);
            Assert.Equal(-32801, response["error"]?["code"]?.GetValue<int>(), response.ToJsonString());
        }
    }

    [Test, DisplayName("LSP protocol 2 rename cancellation also cancels its pending confirmation")]
    public void CancelledConfirmation()
    {
        var (temporary, englishUri, _) = CreateRenameProject(application: true);
        using (temporary)
        {
            using Session session = new(_ => { }, _ => { });
            session.Request("initialize", Initialize(temporary.Path, FullClientCapabilities(), protocol: 2));
            Open(session, englishUri, English);
            int rename = session.Start("textDocument/rename", RenameArgs(englishUri, At(English, "title"), "heading"));
            JsonNode question = session.WaitForServerRequest("window/showMessageRequest");
            session.Notify("$/cancelRequest", new JsonObject { ["id"] = rename });
            JsonNode response = session.Wait(rename);
            Assert.Equal(-32800, response["error"]?["code"]?.GetValue<int>(), response.ToJsonString());
            Assert.True(session.Publications.Any(frame => frame["method"]?.GetValue<string>() == "$/cancelRequest" &&
                frame["params"]!["id"]!.ToJsonString() == question["id"]!.ToJsonString()), "The server did not cancel its confirmation request.");
            // A late answer is dropped, and the session keeps working.
            session.Respond(question["id"]!, new JsonObject { ["result"] = new JsonObject { ["title"] = "Rename in resource sources" } });
            JsonNode prepared = session.Request("textDocument/prepareRename", new JsonObject { ["textDocument"] = Document(englishUri), ["position"] = At(English, "title") });
            Assert.Equal("title", prepared["result"]!["placeholder"]!.GetValue<string>());
            Assert.Equal(English, File.ReadAllText(temporary.Resolve("en.rmf2")));
        }
    }
}
