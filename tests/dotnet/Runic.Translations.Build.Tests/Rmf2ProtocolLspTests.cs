using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Tool;
using TUnit.Core;
using Session = Runic.Translations.Build.Tests.Rmf2DiagnosticLspTests.Session;

namespace Runic.Translations.Build.Tests;

// The versioned Runic protocol (experimental.runic). Every session speaks protocol 2: revision 1, the behaviour of
// Translations 0.13 for a client without the runicProtocol opt-in, was removed in 0.15 (D069.3, D070.1, #263).
internal sealed class Rmf2ProtocolLspTests
{
    internal const string Project = """{"schemaVersion":1,"catalog":"app","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","locales":["en","de"]}""";

    // Every client capability protocol 2 can use.
    internal static JsonObject FullClientCapabilities() => new() {
        ["workspace"] = new JsonObject {
            ["applyEdit"] = true,
            ["workspaceEdit"] = new JsonObject { ["documentChanges"] = true, ["resourceOperations"] = new JsonArray("create", "rename", "delete") },
            ["didChangeWatchedFiles"] = new JsonObject { ["dynamicRegistration"] = true },
        },
        ["window"] = new JsonObject { ["showMessage"] = new JsonObject() },
        ["textDocument"] = new JsonObject { ["rename"] = new JsonObject { ["prepareSupport"] = true },
            ["codeAction"] = new JsonObject { ["dataSupport"] = true, ["resolveSupport"] = new JsonObject { ["properties"] = new JsonArray("edit") } } },
    };

    // The client capabilities Visual Studio 2026 18.12.0 sent in its initialize request, captured by SPIKE-VS-0
    // (Fixtures/vs-18.12-lsp-initialize-request.json): create and rename but no delete, no `window` block, no dynamic
    // watched-file registration and no `edit` in codeAction.resolveSupport.
    internal static JsonObject VisualStudioCapabilities() =>
        JsonNode.Parse(File.ReadAllText(RepositoryPaths.Resolve("tests", "dotnet", "Runic.Translations.Build.Tests", "Fixtures", "vs-18.12-lsp-initialize-request.json")))!
            ["params"]!["capabilities"]!.DeepClone().AsObject();

    internal static JsonObject Initialize(string root, JsonObject capabilities, int? protocol)
    {
        var args = new JsonObject { ["rootUri"] = new Uri(root).AbsoluteUri, ["capabilities"] = capabilities,
            ["initializationOptions"] = new JsonObject { ["runicConfigurationSync"] = true } };
        if (protocol is { } value) args["initializationOptions"]!["runicProtocol"] = value;
        return args;
    }

    internal static (TemporaryDirectory Directory, string EnglishUri, string GermanUri) CreateProject()
    {
        TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("runic.json"), Project);
        File.WriteAllText(temporary.Resolve("en.rmf2"), "title = Hello {$name}\n");
        File.WriteAllText(temporary.Resolve("de.rmf2"), "title = Hallo {$name}\n");
        return (temporary, new Uri(temporary.Resolve("en.rmf2")).AbsoluteUri, new Uri(temporary.Resolve("de.rmf2")).AbsoluteUri);
    }

    internal static JsonObject Document(string uri) => new() { ["uri"] = uri };
    internal static JsonObject Position(int line, int character) => new() { ["line"] = line, ["character"] = character };
    internal static void Open(Session session, string uri, string text, int version = 1) =>
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = version, ["text"] = text } });
    // Requests are answered in order, so every message sent before is processed once this returns.
    internal static void Flush(Session session, string uri) =>
        session.Request("textDocument/documentSymbol", new JsonObject { ["textDocument"] = Document(uri) });

    [Test, DisplayName("LSP speaks protocol 2 to a client that sends no runicProtocol or asks for revision 1 (#263)")]
    public void Protocol1Removed()
    {
        var (temporary, englishUri, _) = CreateProject();
        using (temporary)
        {
            string expected;
            using (Session session = new(_ => { }, _ => { }))
                expected = session.Request("initialize", Initialize(temporary.Path, FullClientCapabilities(), protocol: 2))["result"]!.ToJsonString();
            foreach (int? requested in new int?[] { null, 1 })
            {
                using Session session = new(_ => { }, _ => { });
                session.OnServerRequest = request => request["method"]!.GetValue<string>() == "workspace/applyEdit"
                    ? new JsonObject { ["result"] = new JsonObject { ["applied"] = true } } : new JsonObject { ["result"] = null };
                JsonNode initialize = session.Request("initialize", Initialize(temporary.Path, FullClientCapabilities(), protocol: requested));
                // The same capabilities, experimental.runic.protocol 2 and the tool's version as for a protocol 2 client.
                Assert.Equal(expected, initialize["result"]!.ToJsonString(), "runicProtocol " + (requested?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "absent"));
                Open(session, englishUri, "title = Hello {$name}\n");
                Flush(session, englishUri);

                // Standard error codes, F2 with prepareRename.
                JsonNode unknown = session.Request("textDocument/inlayHint", new JsonObject { ["textDocument"] = Document(englishUri) });
                Assert.Equal(-32601, unknown["error"]!["code"]!.GetValue<int>(), unknown.ToJsonString());
                JsonNode prepare = session.Request("textDocument/prepareRename", new JsonObject { ["textDocument"] = Document(englishUri), ["position"] = Position(0, 16) });
                Assert.Equal("name", prepare["result"]!["placeholder"]!.GetValue<string>(), prepare.ToJsonString());

                // A refactoring command applies its edit through workspace/applyEdit and answers null; it no longer
                // returns the WorkspaceEdit to a client that can apply edits.
                JsonNode rename = session.Request("workspace/executeCommand", new JsonObject {
                    ["command"] = "runic.renameInput", ["arguments"] = new JsonArray(englishUri, "title", "name", "person") });
                Assert.True(rename["error"] is null && rename["result"] is null, rename.ToJsonString());
                JsonNode applied = session.ServerRequests.Single(request => request["method"]!.GetValue<string>() == "workspace/applyEdit");
                Assert.Contains("{$person}", applied["params"]!["edit"]!.ToJsonString());
            }
        }
    }

    [Test, DisplayName("LSP gives a generic client without initialization options the standard refactorings (#263)")]
    public void GenericClient()
    {
        var (temporary, englishUri, germanUri) = CreateProject();
        using (temporary)
        {
            using Session session = new(_ => { }, _ => { });
            // As Neovim or Helix with a plain configuration: no initializationOptions, no applyEdit, no resource operations.
            JsonNode initialize = session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
            Assert.Equal(2, initialize["result"]!["capabilities"]!["experimental"]!["runic"]!["protocol"]!.GetValue<int>());
            Open(session, englishUri, "title = Hello {$name}\n");
            Flush(session, englishUri);
            JsonNode rename = session.Request("textDocument/rename", new JsonObject { ["textDocument"] = Document(englishUri), ["position"] = Position(0, 16), ["newName"] = "person" });
            Assert.Equal(germanUri + "|" + englishUri, string.Join("|", rename["result"]!["documentChanges"]!.AsArray()
                .Select(change => change!["textDocument"]!["uri"]!.GetValue<string>()).Order(StringComparer.Ordinal)), rename.ToJsonString());
            // Without workspace.applyEdit a command returns its WorkspaceEdit for the client to apply.
            JsonNode command = session.Request("workspace/executeCommand", new JsonObject {
                ["command"] = "runic.renameInput", ["arguments"] = new JsonArray(englishUri, "title", "name", "person") });
            Assert.Contains("{$person}", command["result"]!["documentChanges"]!.ToJsonString());
            // Extract Group and Inline Resource need the create operation; without it the client gets no refactor actions.
            JsonNode actions = session.Request("textDocument/codeAction", new JsonObject { ["textDocument"] = Document(englishUri),
                ["range"] = new JsonObject { ["start"] = Position(0, 0), ["end"] = Position(0, 5) }, ["context"] = new JsonObject { ["diagnostics"] = new JsonArray(), ["only"] = new JsonArray("refactor") } });
            Assert.Equal(0, actions["result"]!.AsArray().Count, actions.ToJsonString());
            Assert.Equal(0, session.ServerRequests.Count, string.Join("\n", session.ServerRequests.Select(frame => frame.ToJsonString())));
        }
    }

    // The initialize result a generic client gets, with serverInfo.version replaced by a placeholder: the advertised
    // standard LSP surface and experimental.runic that the compatibility promise covers (language-server.md#compatibility).
    private static readonly string InitializeSnapshotPath = RepositoryPaths.Resolve("tests", "dotnet", "Runic.Translations.Build.Tests", "Fixtures", "lsp-initialize-result.json");
    private const string ToolVersionPlaceholder = "(tool version)";
    private static readonly JsonSerializerOptions SnapshotFormat = new() { WriteIndented = true };

    private static string InitializeSnapshot(string root, JsonObject capabilities, JsonObject? initializationOptions)
    {
        using Session session = new(_ => { }, _ => { });
        var args = new JsonObject { ["rootUri"] = new Uri(root).AbsoluteUri, ["capabilities"] = capabilities };
        if (initializationOptions is not null) args["initializationOptions"] = initializationOptions.DeepClone();
        JsonObject result = session.Request("initialize", args)["result"]!.AsObject();
        Assert.Equal(Program.Version, result["serverInfo"]!["version"]!.GetValue<string>());
        result["serverInfo"]!["version"] = ToolVersionPlaceholder;
        return result.ToJsonString(SnapshotFormat).ReplaceLineEndings("\n") + "\n";
    }

    [Test, DisplayName("LSP initialize result, experimental.runic included, matches its golden snapshot")]
    public void InitializeGolden()
    {
        var (temporary, _, _) = CreateProject();
        using (temporary)
        {
            // A generic client: no initialization options and no capabilities.
            string actual = InitializeSnapshot(temporary.Path, new JsonObject(), null);
            if (Environment.GetEnvironmentVariable("RUNIC_UPDATE_LSP_SNAPSHOT") == "1") File.WriteAllText(InitializeSnapshotPath, actual);
            Assert.Equal(File.ReadAllText(InitializeSnapshotPath).ReplaceLineEndings("\n"), actual,
                "The initialize result changed. The compatibility promise allows additions only; review the change, then update " +
                "Fixtures/lsp-initialize-result.json with RUNIC_UPDATE_LSP_SNAPSHOT=1 (docs/guides/translations/language-server.md#compatibility).");
            // Every client gets the same surface; only positionEncoding follows the client's general.positionEncodings.
            var options = new JsonObject { ["runicConfigurationSync"] = true, ["runicProtocol"] = 2 };
            Assert.Equal(actual, InitializeSnapshot(temporary.Path, FullClientCapabilities(), options), "VS Code's capabilities changed the result.");
            Assert.Equal(actual, InitializeSnapshot(temporary.Path, VisualStudioCapabilities(), options), "Visual Studio's capabilities changed the result.");
            var utf8 = new JsonObject { ["general"] = new JsonObject { ["positionEncodings"] = new JsonArray("utf-8", "utf-16") } };
            Assert.Equal(actual.Replace("\"positionEncoding\": \"utf-16\"", "\"positionEncoding\": \"utf-8\"", StringComparison.Ordinal),
                InitializeSnapshot(temporary.Path, utf8, null), "A UTF-8 client changed more than positionEncoding.");
        }
    }

    [Test, DisplayName("LSP protocol 2 advertises experimental.runic and the tool version and uses standard error codes")]
    public void Protocol2Negotiation()
    {
        var (temporary, englishUri, _) = CreateProject();
        using (temporary)
        {
            using Session session = new(_ => { }, _ => { });
            JsonNode initialize = session.Request("initialize", Initialize(temporary.Path, FullClientCapabilities(), protocol: 2));
            JsonNode runic = initialize["result"]!["capabilities"]!["experimental"]!["runic"]!;
            Assert.Equal(2, runic["protocol"]!.GetValue<int>());
            Assert.Equal(string.Join(",", Rmf2LanguageServer.WorkspaceCommands), string.Join(",", runic["commands"]!.AsArray().Select(value => value!.GetValue<string>())));
            Assert.Equal(string.Join(",", Rmf2LanguageServer.RunicRequests), string.Join(",", runic["requests"]!.AsArray().Select(value => value!.GetValue<string>())));
            Assert.Equal(Program.Version, initialize["result"]!["serverInfo"]!["version"]!.GetValue<string>());
            Assert.False(Program.Version == "1", "serverInfo.version still names the protocol revision.");

            // The server registers its file watchers with a client that supports dynamic registration.
            session.Notify("initialized", new JsonObject());
            JsonNode registration = session.WaitForServerRequest("client/registerCapability");
            JsonNode registered = registration["params"]!["registrations"]![0]!;
            Assert.Equal("workspace/didChangeWatchedFiles", registered["method"]!.GetValue<string>());
            Assert.Equal(string.Join(",", Rmf2LanguageServer.WatchedFilePatterns),
                string.Join(",", registered["registerOptions"]!["watchers"]!.AsArray().Select(watcher => watcher!["globPattern"]!.GetValue<string>())));
            session.Respond(registration["id"]!, new JsonObject { ["result"] = null });

            Open(session, englishUri, "title = Hello {$name}\n");
            JsonNode unknown = session.Request("textDocument/inlayHint", new JsonObject { ["textDocument"] = Document(englishUri) });
            Assert.Equal(-32601, unknown["error"]!["code"]!.GetValue<int>(), unknown.ToJsonString());
            JsonNode unknownRunic = session.Request("runic/unknown", new JsonObject());
            Assert.Equal(-32601, unknownRunic["error"]!["code"]!.GetValue<int>(), unknownRunic.ToJsonString());
            // A request the server cannot carry out is RequestFailed; malformed parameters stay InvalidParams.
            JsonNode refused = session.Request("textDocument/rename", new JsonObject { ["textDocument"] = Document(englishUri), ["position"] = Position(1, 0), ["newName"] = "x" });
            Assert.Equal(-32803, refused["error"]!["code"]!.GetValue<int>(), refused.ToJsonString());
            JsonNode malformed = session.Request("textDocument/hover", new JsonObject { ["textDocument"] = Document(englishUri), ["position"] = Position(7, 0) });
            Assert.Equal(-32602, malformed["error"]!["code"]!.GetValue<int>(), malformed.ToJsonString());

            // A response the server is not waiting for is dropped, never answered as if it were a request.
            session.SendFrame(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 4242, ["result"] = null });
            session.SendFrame(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = "stray", ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "no" } });
            Flush(session, englishUri);
            Assert.False(session.Publications.Any(frame => frame["id"] is not null), string.Join("\n", session.Publications.Select(frame => frame.ToJsonString())));
            Assert.Equal(1, session.ServerRequests.Count);
        }
    }

    [Test, DisplayName("LSP protocol 2 answers a newer requested revision with its own and registers watchers only when supported")]
    public void Protocol2Degradation()
    {
        var (temporary, englishUri, _) = CreateProject();
        using (temporary)
        {
            using Session session = new(_ => { }, _ => { });
            JsonNode initialize = session.Request("initialize", Initialize(temporary.Path, new JsonObject(), protocol: 3));
            Assert.Equal(Rmf2LanguageServer.LatestProtocol, initialize["result"]!["capabilities"]!["experimental"]!["runic"]!["protocol"]!.GetValue<int>());
            session.Notify("initialized", new JsonObject());
            Open(session, englishUri, "title = Hello {$name}\n");
            Flush(session, englishUri);
            Assert.Equal(0, session.ServerRequests.Count, "A client without dynamic registration received a registration.");
        }
    }

    private static JsonArray? LatestDiagnostics(Session session, string uri) =>
        session.Publications.LastOrDefault(frame => frame["method"]?.GetValue<string>() == "textDocument/publishDiagnostics" &&
            frame["params"]!["uri"]!.GetValue<string>() == uri)?["params"]!["diagnostics"]!.AsArray();

    // Waits until the server has published diagnostics for a file that an external change broke.
    private static void WaitForDiagnostics(Session session, string openUri, string uri)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (LatestDiagnostics(session, uri) is not { Count: > 0 })
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The server did not pick up the external change of " + uri);
            Thread.Sleep(50);
            Flush(session, openUri);
        }
    }

    private const string BrokenGerman = "title = Hallo {$name\n";

    [Test, DisplayName("LSP protocol 2 watches the workspace itself for Visual Studio 2026, which cannot register watchers")]
    public void ServerFileWatching()
    {
        var (temporary, englishUri, germanUri) = CreateProject();
        using (temporary)
        {
            using Session session = new(_ => { }, _ => { });
            JsonNode initialize = session.Request("initialize", Initialize(temporary.Path, VisualStudioCapabilities(), protocol: 2));
            Assert.Equal(2, initialize["result"]!["capabilities"]!["experimental"]!["runic"]!["protocol"]!.GetValue<int>());
            session.Notify("initialized", new JsonObject());
            Open(session, englishUri, "title = Hello {$name}\n");
            Flush(session, englishUri);
            Assert.True(LatestDiagnostics(session, germanUri) is not { Count: > 0 }, "The saved German resource already had diagnostics.");

            // An external edit of a closed resource reaches the diagnostics without a client notification.
            File.WriteAllText(temporary.Resolve("de.rmf2"), BrokenGerman);
            WaitForDiagnostics(session, englishUri, germanUri);
            Assert.Equal(0, session.ServerRequests.Count, "A client without dynamic registration received a registration.");
        }
    }

    [Test, DisplayName("LSP protocol 2 watches the workspace itself when the client refuses the watcher registration")]
    public void RefusedRegistration()
    {
        var (temporary, englishUri, germanUri) = CreateProject();
        using (temporary)
        {
            using Session session = new(_ => { }, _ => { });
            session.OnServerRequest = _ => new JsonObject { ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "no" } };
            session.Request("initialize", Initialize(temporary.Path, FullClientCapabilities(), protocol: 2));
            session.Notify("initialized", new JsonObject());
            Open(session, englishUri, "title = Hello {$name}\n");
            Flush(session, englishUri);
            Assert.Equal("client/registerCapability", session.ServerRequests.Single()["method"]!.GetValue<string>());
            File.WriteAllText(temporary.Resolve("de.rmf2"), BrokenGerman);
            WaitForDiagnostics(session, englishUri, germanUri);
        }
    }
}
