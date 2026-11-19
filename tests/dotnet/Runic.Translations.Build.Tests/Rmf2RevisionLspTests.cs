using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json.Nodes;
using TUnit.Core;
using static Runic.Translations.Build.Tests.Rmf2ProtocolLspTests;
using Session = Runic.Translations.Build.Tests.Rmf2DiagnosticLspTests.Session;

namespace Runic.Translations.Build.Tests;

// ContentModified only for a change that makes the result stale (#250): a request is refused when its own document or
// the project changed while it was pending, and an edit or a cross-file result when a file below its workspace changed.
// A watched-file event for another file, or the save of the request's own document, leaves it answered.
internal sealed class Rmf2RevisionLspTests
{
    private const string English = "title = Hello {$name}\nfarewell = Bye {$name}\n";
    private const string German = "title = Hallo {$name}\nfarewell = Tschüss {$name}\n";

    // Holds the worker before one request, so a test can queue notifications while that request is pending.
    private sealed class Hold : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new(false);
        private readonly ManualResetEventSlim _released = new(false);
        private readonly ManualResetEventSlim _cancelled = new(false);
        private string? _method;
        private int _armed;
        internal void Before(string method)
        {
            if (method != Volatile.Read(ref _method) || Interlocked.Exchange(ref _armed, 0) == 0) return;
            _entered.Set();
            if (!_released.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The request was not released.");
        }
        internal void Cancelled(string _) => _cancelled.Set();

        // Sends the request, runs `during` while the worker holds it, and returns its response.
        internal JsonNode WhilePending(Session session, string method, JsonObject args, Action during)
        {
            _entered.Reset(); _released.Reset(); _cancelled.Reset();
            Volatile.Write(ref _method, method);
            Interlocked.Exchange(ref _armed, 1);
            int pending = session.Start(method, args);
            Assert.True(_entered.Wait(TimeSpan.FromSeconds(10)), "The request was not held.");
            during();
            // The reader reads in order: once it has seen this cancellation, every notification sent before is queued.
            int marker = session.Start("runic/libraryCatalogs", new JsonObject());
            session.Notify("$/cancelRequest", new JsonObject { ["id"] = marker });
            Assert.True(_cancelled.Wait(TimeSpan.FromSeconds(10)), "The reader did not reach the queued notifications.");
            _released.Set();
            JsonNode response = session.Wait(pending);
            session.Wait(marker);
            return response;
        }

        public void Dispose() { _released.Set(); _entered.Dispose(); _released.Dispose(); _cancelled.Dispose(); }
    }

    // app/ with en.rmf2 and de.rmf2, and other/ with a project of its own.
    private static (TemporaryDirectory Directory, string EnglishUri, string GermanUri, string OtherUri) CreateWorkspace()
    {
        TemporaryDirectory temporary = new();
        foreach (string name in new[] { "app", "other" })
        {
            Directory.CreateDirectory(temporary.Resolve(name));
            File.WriteAllText(temporary.Resolve(name, "runic.json"), Project);
            File.WriteAllText(temporary.Resolve(name, "en.rmf2"), English);
            File.WriteAllText(temporary.Resolve(name, "de.rmf2"), German);
        }
        return (temporary, Uri(temporary, "app", "en.rmf2"), Uri(temporary, "app", "de.rmf2"), Uri(temporary, "other", "en.rmf2"));
    }

    private static string Uri(TemporaryDirectory directory, params string[] segments) => new Uri(directory.Resolve(segments)).AbsoluteUri;

    private static Action Watched(Session session, string uri, int type = 2) => () =>
        session.Notify("workspace/didChangeWatchedFiles", new JsonObject { ["changes"] = new JsonArray(new JsonObject { ["uri"] = uri, ["type"] = type }) });

    private static Action Changed(Session session, string uri, string text, int version) => () =>
        session.Notify("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = version },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text }) });

    // The save of an open document: its buffer is written to disk and the watcher reports the file.
    private static Action Saved(Session session, string uri, string text) => () => {
        File.WriteAllText(new Uri(uri).LocalPath, text);
        Watched(session, uri)();
    };

    private static JsonObject Symbols(string uri) => new() { ["textDocument"] = Document(uri) };
    private static JsonObject AtTitle(string uri) => new() { ["textDocument"] = Document(uri), ["position"] = Position(0, 2) };
    private static JsonObject CodeActions(string uri) => new() { ["textDocument"] = Document(uri),
        ["range"] = new JsonObject { ["start"] = Position(0, 0), ["end"] = Position(0, 5) }, ["context"] = new JsonObject { ["diagnostics"] = new JsonArray() } };

    private static void AssertAnswered(JsonNode response, string request)
    {
        Assert.True(response["error"] is null, request + " was refused: " + response.ToJsonString());
    }

    private static void AssertContentModified(JsonNode response, string request) =>
        Assert.Equal(-32801, response["error"]?["code"]?.GetValue<int>(), request + ": " + response.ToJsonString());

    [Test, DisplayName("LSP answers a pending documentSymbol after the save of another file (#250)")]
    public void OtherFileSaved()
    {
        var (temporary, englishUri, germanUri, _) = CreateWorkspace();
        using (temporary)
        {
            using var hold = new Hold();
            using Session session = new(hold.Before, hold.Cancelled);
            session.Request("initialize", Initialize(temporary.Path, new JsonObject(), protocol: null));
            Open(session, englishUri, English);
            string edited = German.Replace("Hallo", "Guten Tag", StringComparison.Ordinal);
            Open(session, germanUri, edited);
            Flush(session, englishUri);

            JsonNode symbols = hold.WhilePending(session, "textDocument/documentSymbol", Symbols(englishUri), Saved(session, germanUri, edited));
            AssertAnswered(symbols, "documentSymbol");
            Assert.Equal("title|farewell", string.Join('|', symbols["result"]!.AsArray().Select(symbol => symbol!["name"]!.GetValue<string>())));
        }
    }

    [Test, DisplayName("LSP answers pending read requests after watched-file events for other files and the save of their own document")]
    public void ReadsStayAnswered()
    {
        var (temporary, englishUri, germanUri, otherUri) = CreateWorkspace();
        using (temporary)
        {
            using var hold = new Hold();
            using Session session = new(hold.Before, hold.Cancelled);
            session.Request("initialize", Initialize(temporary.Path, new JsonObject(), protocol: null));
            string english = English.Replace("Bye", "Goodbye", StringComparison.Ordinal);
            Open(session, englishUri, english);
            Open(session, germanUri, German);
            Flush(session, englishUri);
            (string Method, JsonObject Args)[] reads = [
                ("textDocument/documentSymbol", Symbols(englishUri)), ("textDocument/foldingRange", Symbols(englishUri)),
                ("textDocument/semanticTokens/full", Symbols(englishUri)), ("textDocument/hover", AtTitle(englishUri)),
                ("textDocument/completion", AtTitle(englishUri)), ("textDocument/codeAction", CodeActions(englishUri)),
                ("runic/message", AtTitle(englishUri)), ("runic/documentInfo", Symbols(englishUri)),
            ];
            int germanVersion = 1;
            foreach (var (method, args) in reads)
            {
                // A closed file of the same project, a file of another project, a keystroke in another document.
                AssertAnswered(hold.WhilePending(session, method, args, () => {
                    Watched(session, Uri(temporary, "app", "fr.rmf2"), type: 1)();
                    Watched(session, otherUri)();
                    Changed(session, germanUri, German.Replace("Hallo", "Servus", StringComparison.Ordinal), ++germanVersion)();
                    Changed(session, germanUri, German, ++germanVersion)();
                }), method + " after other files changed");
                // The save of the request's own document: the disk now holds its buffer.
                AssertAnswered(hold.WhilePending(session, method, args, Saved(session, englishUri, english)), method + " after its document was saved");
                File.WriteAllText(temporary.Resolve("app", "en.rmf2"), English);
                session.Request("runic/libraryCatalogs", new JsonObject());
            }
            JsonNode message = hold.WhilePending(session, "runic/message", AtTitle(englishUri), Saved(session, germanUri, German));
            AssertAnswered(message, "runic/message");
            Assert.Equal("title", message["result"]!["key"]!.GetValue<string>());
        }
    }

    [Test, DisplayName("LSP answers ContentModified when the request's document or its project changes while it is pending")]
    public void OwnChangesRefuse()
    {
        var (temporary, englishUri, _, _) = CreateWorkspace();
        using (temporary)
        {
            using var hold = new Hold();
            using Session session = new(hold.Before, hold.Cancelled);
            session.Request("initialize", Initialize(temporary.Path, new JsonObject(), protocol: null));
            Open(session, englishUri, English);
            Flush(session, englishUri);
            AssertContentModified(hold.WhilePending(session, "textDocument/documentSymbol", Symbols(englishUri),
                Changed(session, englishUri, English.Replace("Hello", "Hi", StringComparison.Ordinal), 2)), "documentSymbol after a keystroke in its document");
            AssertContentModified(hold.WhilePending(session, "textDocument/hover", AtTitle(englishUri),
                Changed(session, englishUri, English, 3)), "hover after a keystroke in its document");
            // The file on disk now differs from the open buffer: an external change to the document.
            File.WriteAllText(temporary.Resolve("app", "en.rmf2"), "title = Elsewhere\n");
            AssertContentModified(hold.WhilePending(session, "textDocument/documentSymbol", Symbols(englishUri),
                Watched(session, englishUri)), "documentSymbol after an external change to its document");
            // runic.json changes the catalog every semantic request reads, but not a document's syntax.
            string manifest = Uri(temporary, "app", "runic.json");
            AssertContentModified(hold.WhilePending(session, "runic/message", AtTitle(englishUri), Watched(session, manifest)), "runic/message after runic.json changed");
            AssertContentModified(hold.WhilePending(session, "textDocument/codeAction", CodeActions(englishUri),
                () => session.Notify("workspace/didChangeConfiguration", new JsonObject { ["settings"] = new JsonObject() })), "codeAction after a configuration change");
            AssertAnswered(hold.WhilePending(session, "textDocument/documentSymbol", Symbols(englishUri), Watched(session, manifest)), "documentSymbol after runic.json changed");
        }
    }

    [Test, DisplayName("LSP refuses an edit computed before a file of its workspace changed, and answers one whose files did not change")]
    public void EditsStayStrict()
    {
        var (temporary, englishUri, germanUri, otherUri) = CreateWorkspace();
        using (temporary)
        {
            using var hold = new Hold();
            using Session session = new(hold.Before, hold.Cancelled);
            session.Request("initialize", Initialize(temporary.Path, FullClientCapabilities(), protocol: 2));
            Open(session, englishUri, English);
            Open(session, otherUri, English);
            Flush(session, englishUri);
            JsonObject Rename() => new() { ["textDocument"] = Document(englishUri), ["position"] = Position(0, 16), ["newName"] = "person" };

            // Another project's document and a saved open document are not files the rename read differently.
            JsonNode renamed = hold.WhilePending(session, "textDocument/rename", Rename(), () => {
                Changed(session, otherUri, English.Replace("Hello", "Hi", StringComparison.Ordinal), 2)();
                Saved(session, englishUri, English)();
            });
            AssertAnswered(renamed, "rename after other files changed");
            Assert.True(renamed["result"]!["documentChanges"]!.AsArray().Any(change => change!["textDocument"]!["uri"]!.GetValue<string>() == germanUri),
                "The input rename did not edit the German resource: " + renamed.ToJsonString());

            // A closed resource of the same catalog changed on disk: the rename's German edit would be stale.
            File.WriteAllText(temporary.Resolve("app", "de.rmf2"), German.Replace("Hallo", "Servus", StringComparison.Ordinal));
            AssertContentModified(hold.WhilePending(session, "textDocument/rename", Rename(), Watched(session, germanUri)), "rename after a catalog file changed");
            // A new resource of the catalog appeared.
            AssertContentModified(hold.WhilePending(session, "textDocument/rename", Rename(), Watched(session, Uri(temporary, "app", "fr.rmf2"), type: 1)), "rename after a catalog file was created");
            // A keystroke in another open document of the catalog.
            Open(session, germanUri, German);
            AssertContentModified(hold.WhilePending(session, "textDocument/rename", Rename(), Changed(session, germanUri, German.Replace("Hallo", "Moin", StringComparison.Ordinal), 2)), "rename after a keystroke in a catalog document");
            // A cross-file read (references) is refused by the same rule; an outline is not.
            AssertContentModified(hold.WhilePending(session, "textDocument/references", new JsonObject { ["textDocument"] = Document(englishUri), ["position"] = Position(0, 16),
                ["context"] = new JsonObject { ["includeDeclaration"] = true } }, Changed(session, germanUri, German, 3)), "references after a keystroke in a catalog document");
            AssertAnswered(hold.WhilePending(session, "textDocument/documentSymbol", Symbols(englishUri), Changed(session, germanUri, German.Replace("Hallo", "Moin", StringComparison.Ordinal), 4)), "documentSymbol after a keystroke in a catalog document");
        }
    }
}
