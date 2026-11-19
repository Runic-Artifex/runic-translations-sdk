using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json.Nodes;
using Runic.Translations.Tool;
using TUnit.Core;
using static Runic.Translations.Build.Tests.Rmf2ProtocolLspTests;
using Session = Runic.Translations.Build.Tests.Rmf2DiagnosticLspTests.Session;

namespace Runic.Translations.Build.Tests;

// Background validation (#271 B): requests are answered while a validation runs, a newer change cancels it, and only
// the latest state's diagnostics are published. These sessions do not settle diagnostics before responses.
internal sealed class Rmf2BackgroundValidationLspTests
{
    private const string English = "title = Hello {$name}\nother = Text\n";

    // Holds the validator thread as its next validation starts, until released.
    private sealed class ValidatorHold : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new(false), _released = new(false);
        private int _armed;
        internal void Before(string method)
        {
            if (method != Rmf2LanguageServer.ValidationHook || Interlocked.Exchange(ref _armed, 0) == 0) return;
            _entered.Set();
            if (!_released.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("The validation was not released.");
        }
        internal void Arm() { _entered.Reset(); _released.Reset(); Interlocked.Exchange(ref _armed, 1); }
        internal void WaitHeld() => Assert.True(_entered.Wait(TimeSpan.FromSeconds(10)), "No validation started.");
        internal void Release() => _released.Set();
        public void Dispose() { _released.Set(); _entered.Dispose(); _released.Dispose(); }
    }

    private static (TemporaryDirectory Directory, string Uri) CreateCatalog()
    {
        var (temporary, englishUri, _) = CreateProject();
        File.WriteAllText(temporary.Resolve("en.rmf2"), English);
        // Every key in both locales, so the catalog compiles and hover shows its contract.
        File.WriteAllText(temporary.Resolve("de.rmf2"), "title = Hallo {$name}\nother = Text\n");
        return (temporary, englishUri);
    }

    private static Session Start(TemporaryDirectory temporary, ValidatorHold hold)
    {
        var session = new Session(hold.Before, _ => { }, settleDiagnostics: false);
        session.Request("initialize", Initialize(temporary.Path, FullClientCapabilities(), protocol: 2));
        return session;
    }

    private static void Change(Session session, string uri, int version, string text) =>
        session.Notify("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = version },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text }) });

    private static bool IsPublicationOf(JsonNode frame, string uri, int version) =>
        frame["params"]!["uri"]!.GetValue<string>() == uri && frame["params"]!["version"]?.GetValue<int>() == version;

    private static JsonObject At(string uri, int line, int character) => new() { ["textDocument"] = Document(uri), ["position"] = Position(line, character) };

    [Test, DisplayName("LSP answers requests while the catalog is validated in the background")]
    public void RequestsDuringValidation()
    {
        var (temporary, uri) = CreateCatalog();
        using (temporary)
        {
            using var hold = new ValidatorHold();
            using Session session = Start(temporary, hold);
            hold.Arm();
            Open(session, uri, English);
            hold.WaitHeld();
            // The validation of the opened catalog is held: none of these waits for it.
            JsonNode hover = session.Request("textDocument/hover", At(uri, 0, 2));
            Assert.Contains("title", hover["result"]!["contents"]!["value"]!.GetValue<string>());
            Assert.True(session.Request("textDocument/completion", At(uri, 0, 16))["result"]!.AsArray().Any(item => item!["label"]!.GetValue<string>() == "$name"), "Completion during validation.");
            Assert.Equal(2, session.Request("textDocument/documentSymbol", new JsonObject { ["textDocument"] = Document(uri) })["result"]!.AsArray().Count);
            JsonNode actions = session.Request("textDocument/codeAction", new JsonObject { ["textDocument"] = Document(uri),
                ["range"] = new JsonObject { ["start"] = Position(0, 0), ["end"] = Position(0, 5) }, ["context"] = new JsonObject { ["diagnostics"] = new JsonArray() } });
            Assert.True(actions["error"] is null, actions.ToJsonString());
            Assert.False(session.Publications.Any(frame => frame["method"]?.GetValue<string>() == "textDocument/publishDiagnostics"),
                "Diagnostics were published before the validation finished.");
            hold.Release();
            JsonNode published = session.WaitForPublication(frame => IsPublicationOf(frame, uri, 1));
            Assert.Equal(0, published["params"]!["diagnostics"]!.AsArray().Count);
        }
    }

    [Test, DisplayName("LSP cancels a running validation for a newer change and publishes only the latest state")]
    public void NewerChangeCancels()
    {
        var (temporary, uri) = CreateCatalog();
        using (temporary)
        {
            using var hold = new ValidatorHold();
            using Session session = Start(temporary, hold);
            hold.Arm();
            Open(session, uri, English);
            hold.WaitHeld();
            int started = session.Server!.Validations;
            // Two edits while version 1 is validated: an error, then its repair with other text.
            Change(session, uri, 2, English + "empty =\n");
            Change(session, uri, 3, English.Replace("Hello", "Hi", StringComparison.Ordinal));
            session.Request("textDocument/documentSymbol", new JsonObject { ["textDocument"] = Document(uri) });
            hold.Release();
            JsonNode published = session.WaitForPublication(frame => IsPublicationOf(frame, uri, 3));
            Assert.Equal(0, published["params"]!["diagnostics"]!.AsArray().Count);
            JsonNode[] all = session.Publications.Where(frame => frame["method"]?.GetValue<string>() == "textDocument/publishDiagnostics" &&
                frame["params"]!["uri"]!.GetValue<string>() == uri).ToArray();
            Assert.True(all.All(frame => frame["params"]!["version"]!.GetValue<int>() == 3),
                "A cancelled or superseded validation was published: " + string.Join("\n", all.Select(frame => frame.ToJsonString())));
            // The held validation of version 1 was cancelled, and version 3 was validated once.
            Assert.Equal(1, session.Server.Validations - started, "Version 3 was not validated exactly once.");
            // A review view was applied: hover shows the compiled contract.
            JsonNode hover = session.Request("textDocument/hover", At(uri, 0, 2));
            Assert.Contains("Inputs: $name", hover["result"]!["contents"]!["value"]!.GetValue<string>());
        }
    }

    [Test, DisplayName("LSP hover reads the last applied validation while a newer one runs")]
    public void HoverUsesLastValidation()
    {
        var (temporary, uri) = CreateCatalog();
        using (temporary)
        {
            using var hold = new ValidatorHold();
            using Session session = Start(temporary, hold);
            Open(session, uri, English);
            session.WaitForPublication(frame => IsPublicationOf(frame, uri, 1));
            string contract = session.Request("textDocument/hover", At(uri, 0, 2))["result"]!["contents"]!["value"]!.GetValue<string>();
            Assert.Contains("Inputs: $name", contract);

            hold.Arm();
            Change(session, uri, 2, English.Replace("Hello", "Hi", StringComparison.Ordinal));
            hold.WaitHeld();
            // Answered at once, with the contract of the last applied validation.
            string during = session.Request("textDocument/hover", At(uri, 0, 2))["result"]!["contents"]!["value"]!.GetValue<string>();
            Assert.Contains("Inputs: $name", during);
            hold.Release();
            session.WaitForPublication(frame => IsPublicationOf(frame, uri, 2));
        }
    }

    [Test, DisplayName("LSP edit-producing requests read the current buffers while an older validation runs")]
    public void EditsReadCurrentState()
    {
        var (temporary, uri) = CreateCatalog();
        using (temporary)
        {
            using var hold = new ValidatorHold();
            using Session session = Start(temporary, hold);
            Open(session, uri, English);
            session.WaitForPublication(frame => IsPublicationOf(frame, uri, 1));
            hold.Arm();
            Change(session, uri, 2, English.Replace("Hello", "Hi", StringComparison.Ordinal));
            hold.WaitHeld();
            // F2 on the input while version 2 is validated: the edit is computed from version 2's text.
            JsonNode rename = session.Request("textDocument/rename", new JsonObject { ["textDocument"] = Document(uri), ["position"] = Position(0, 13), ["newName"] = "who" });
            JsonNode edit = rename["result"]!["documentChanges"]!.AsArray().Single(change => change!["textDocument"]!["uri"]!.GetValue<string>() == uri)!;
            Assert.Equal(2, edit["textDocument"]!["version"]!.GetValue<int>());
            Assert.Contains("title = Hi {$who}", edit["edits"]![0]!["newText"]!.GetValue<string>());
            hold.Release();
            session.WaitForPublication(frame => IsPublicationOf(frame, uri, 2));
        }
    }
}
