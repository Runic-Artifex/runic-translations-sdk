using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json.Nodes;
using Runic.Translations.Tool;
using TUnit.Core;
using Session = Runic.Translations.Build.Tests.Rmf2DiagnosticLspTests.Session;

namespace Runic.Translations.Build.Tests;

// The versioned Runic protocol (experimental.runic): protocol 2 after the runicProtocol opt-in, and the protocol 1
// behaviour of Translations 0.13 without it, which runic-editors 0.21 relies on for one minor (D069.3, D070.1).
internal sealed class Rmf2ProtocolLspTests
{
    internal const string Project = """{"schemaVersion":1,"catalog":"app","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","locales":["en","de"]}""";

    // The capabilities Translations 0.13 advertised, byte for byte.
    internal const string Protocol1Capabilities = """{"positionEncoding":"utf-16","textDocumentSync":2,"documentSymbolProvider":true,"semanticTokensProvider":{"legend":{"tokenTypes":["namespace","property","variable","function","keyword","string","comment","type"],"tokenModifiers":[]},"full":true},"referencesProvider":true,"foldingRangeProvider":true,"hoverProvider":true,"definitionProvider":true,"documentFormattingProvider":true,"renameProvider":true,"codeActionProvider":{"codeActionKinds":["quickfix"],"resolveProvider":true},"executeCommandProvider":{"commands":["runic.extractGroup","runic.inlineResource","runic.markReviewed","runic.preview","runic.renameInput","runic.renameSlot","runic.renameResource","runic.renderPreview"]},"completionProvider":{"triggerCharacters":["$",":","#","/","="]}}""";

    // Every client capability protocol 2 can use, so a protocol 1 session shows that it ignores them.
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

    [Test, DisplayName("LSP without the runicProtocol opt-in keeps the protocol 1 behaviour of Translations 0.13")]
    public void Protocol1Transition()
    {
        var (temporary, englishUri, _) = CreateProject();
        using (temporary)
        {
            using Session session = new(_ => { }, _ => { });
            session.OnServerRequest = _ => new JsonObject { ["result"] = null };
            JsonNode initialize = session.Request("initialize", Initialize(temporary.Path, FullClientCapabilities(), protocol: null));
            Assert.Equal(Protocol1Capabilities, initialize["result"]!["capabilities"]!.ToJsonString());
            Assert.Equal("""{"name":"Runic RMF2","version":"1"}""", initialize["result"]!["serverInfo"]!.ToJsonString());
            session.Notify("initialized", new JsonObject());
            Open(session, englishUri, "title = Hello {$name}\n");
            Flush(session, englishUri);

            // Unknown requests answer null and every failure is InvalidParams, as in 0.13.
            JsonNode unknown = session.Request("textDocument/inlayHint", new JsonObject { ["textDocument"] = Document(englishUri) });
            Assert.True(unknown["error"] is null && unknown["result"] is null, unknown.ToJsonString());
            JsonNode refused = session.Request("textDocument/rename", new JsonObject { ["textDocument"] = Document(englishUri), ["position"] = Position(1, 0), ["newName"] = "x" });
            Assert.Equal(-32602, refused["error"]!["code"]!.GetValue<int>(), refused.ToJsonString());

            // The refactoring commands return their WorkspaceEdit; nothing is applied by the server.
            JsonNode rename = session.Request("workspace/executeCommand", new JsonObject {
                ["command"] = "runic.renameResource", ["arguments"] = new JsonArray(englishUri, new JsonArray("title"), "heading") });
            JsonArray changes = rename["result"]!["documentChanges"]!.AsArray();
            Assert.Equal(2, changes.Count, rename.ToJsonString());
            Assert.True(changes.All(change => change!["textDocument"] is not null && change["edits"]![0]!["newText"]!.GetValue<string>().StartsWith("heading = ", StringComparison.Ordinal)), rename.ToJsonString());
            Assert.Equal(1, changes.Single(change => change!["textDocument"]!["uri"]!.GetValue<string>() == englishUri)!["textDocument"]!["version"]!.GetValue<int>());
            JsonNode input = session.Request("workspace/executeCommand", new JsonObject {
                ["command"] = "runic.renameInput", ["arguments"] = new JsonArray(englishUri, "title", "name", "person") });
            Assert.Contains("{$person}", input["result"]!["documentChanges"]!.ToJsonString());

            // No watcher registration and no other server-to-client request.
            Assert.Equal(0, session.ServerRequests.Count, string.Join("\n", session.ServerRequests.Select(frame => frame.ToJsonString())));
            Assert.Equal("Hello {$name}", File.ReadAllText(temporary.Resolve("en.rmf2"))[8..^1]);
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

    [Test, DisplayName("LSP without the runicProtocol opt-in gives Visual Studio 2026 the protocol 1 behaviour, without server file watching")]
    public void VisualStudioProtocol1()
    {
        var (temporary, englishUri, germanUri) = CreateProject();
        using (temporary)
        {
            using Session session = new(_ => { }, _ => { });
            JsonNode initialize = session.Request("initialize", Initialize(temporary.Path, VisualStudioCapabilities(), protocol: null));
            Assert.Equal(Protocol1Capabilities, initialize["result"]!["capabilities"]!.ToJsonString());
            session.Notify("initialized", new JsonObject());
            Open(session, englishUri, "title = Hello {$name}\n");
            Flush(session, englishUri);
            // Revision 1 clients forward file events themselves (FilesToWatch in Visual Studio).
            File.WriteAllText(temporary.Resolve("de.rmf2"), BrokenGerman);
            Thread.Sleep(Rmf2LanguageServer.WatchDelay * 5);
            Flush(session, englishUri);
            Assert.True(LatestDiagnostics(session, germanUri) is not { Count: > 0 }, "Protocol 1 watched files itself.");
            session.Notify("workspace/didChangeWatchedFiles", new JsonObject { ["changes"] = new JsonArray(new JsonObject { ["uri"] = germanUri, ["type"] = 2 }) });
            Flush(session, englishUri);
            Assert.True(LatestDiagnostics(session, germanUri) is { Count: > 0 }, "A forwarded change was not picked up.");
            Assert.Equal(0, session.ServerRequests.Count);
        }
    }
}
