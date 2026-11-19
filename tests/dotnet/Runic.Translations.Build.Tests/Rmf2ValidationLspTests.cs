using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json.Nodes;
using TUnit.Core;
using static Runic.Translations.Build.Tests.Rmf2ProtocolLspTests;
using Session = Runic.Translations.Build.Tests.Rmf2DiagnosticLspTests.Session;

namespace Runic.Translations.Build.Tests;

// Queued changes are validated once, at the state of the last one (#271): the last edit's diagnostics are always
// published, and no publication of an older state follows a newer one.
internal sealed class Rmf2ValidationLspTests
{
    private const string English = "title = Hello {$name}\nother = Text\n";

    // Holds the worker before the first didChange after Arm, so a test can queue more changes behind it.
    private sealed class Hold : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new(false), _released = new(false), _cancelled = new(false);
        private int _armed;
        internal void Before(string method)
        {
            if (method != "textDocument/didChange" || Interlocked.Exchange(ref _armed, 0) == 0) return;
            _entered.Set();
            if (!_released.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The change was not released.");
        }
        internal void Cancelled(string _) => _cancelled.Set();

        // Sends `first`, holds the worker on it, runs `queue`, and releases the worker once the reader has queued it all.
        internal void Queue(Session session, Action first, Action queue)
        {
            Interlocked.Exchange(ref _armed, 1);
            first();
            Assert.True(_entered.Wait(TimeSpan.FromSeconds(10)), "The change was not held.");
            queue();
            int marker = session.Start("runic/libraryCatalogs", new JsonObject());
            session.Notify("$/cancelRequest", new JsonObject { ["id"] = marker });
            Assert.True(_cancelled.Wait(TimeSpan.FromSeconds(10)), "The reader did not reach the queued changes.");
            _released.Set();
            session.Wait(marker);
        }

        public void Dispose() { _released.Set(); _entered.Dispose(); _released.Dispose(); _cancelled.Dispose(); }
    }

    private static void Change(Session session, string uri, int version, string text) =>
        session.Notify("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = version },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text }) });

    private static JsonNode[] PublicationsOf(Session session, string uri, int from) =>
        session.Publications.Skip(from).Where(frame => frame["method"]?.GetValue<string>() == "textDocument/publishDiagnostics" &&
            frame["params"]!["uri"]!.GetValue<string>() == uri).ToArray();

    private static string Codes(JsonNode publication) =>
        string.Join(",", publication["params"]!["diagnostics"]!.AsArray().Select(diagnostic => diagnostic!["code"]!.GetValue<string>()));

    // A request answered after the queued changes: every notification before it is processed.
    private static void Settle(Session session) => session.Request("runic/libraryCatalogs", new JsonObject());

    [Test, DisplayName("LSP validates a burst of queued edits once and publishes only the last edit's diagnostics")]
    public void BurstValidatedOnce()
    {
        var (temporary, englishUri, _) = CreateProject();
        using (temporary)
        {
            File.WriteAllText(temporary.Resolve("en.rmf2"), English);
            using var hold = new Hold();
            using Session session = new(hold.Before, hold.Cancelled);
            session.Request("initialize", Initialize(temporary.Path, new JsonObject(), protocol: null));
            Open(session, englishUri, English);
            Settle(session);
            int validations = session.Server!.Validations, published = session.Publications.Count;

            // Four keystrokes, each with other diagnostics; only the last one's may reach the client.
            hold.Queue(session, () => Change(session, englishUri, 2, "title = Hello {$name\nother = Text\n"), () => {
                Change(session, englishUri, 3, "title = Hello {$name}\nother =\n");
                Change(session, englishUri, 4, "title = Hello {$name\nother =\n");
                Change(session, englishUri, 5, "title = Hello {$name}\nother = Text\nempty =\n");
            });
            Settle(session);
            Assert.Equal(1, session.Server.Validations - validations, "The queued changes were not validated once.");
            JsonNode[] publications = PublicationsOf(session, englishUri, published);
            Assert.True(publications.Length > 0, "The last edit's diagnostics were not published.");
            Assert.True(publications.All(frame => frame["params"]!["version"]!.GetValue<int>() == 5),
                "A publication of an older state was sent: " + string.Join("\n", publications.Select(frame => frame.ToJsonString())));
            Assert.Equal("RTR0050", Codes(publications[^1]));

            // A single change afterwards is validated at once.
            Change(session, englishUri, 6, English);
            Settle(session);
            Assert.Equal(2, session.Server.Validations - validations, "A single change was not validated.");
            Assert.Equal("", Codes(PublicationsOf(session, englishUri, published)[^1]));
        }
    }

    [Test, DisplayName("LSP validates every project a queued burst changed, once each")]
    public void BurstAcrossProjects()
    {
        using TemporaryDirectory temporary = new();
        foreach (string name in new[] { "a", "b" })
        {
            Directory.CreateDirectory(temporary.Resolve(name));
            File.WriteAllText(temporary.Resolve(name, "runic.json"), Project);
            File.WriteAllText(temporary.Resolve(name, "en.rmf2"), English);
        }
        string aUri = new Uri(temporary.Resolve("a", "en.rmf2")).AbsoluteUri, bUri = new Uri(temporary.Resolve("b", "en.rmf2")).AbsoluteUri;
        using var hold = new Hold();
        using Session session = new(hold.Before, hold.Cancelled);
        session.Request("initialize", Initialize(temporary.Path, new JsonObject(), protocol: null));
        Open(session, aUri, English);
        Open(session, bUri, English);
        Settle(session);
        int validations = session.Server!.Validations, published = session.Publications.Count;

        hold.Queue(session, () => Change(session, aUri, 2, English + "empty =\n"), () => {
            Change(session, bUri, 2, English + "empty =\n");
            Change(session, aUri, 3, English + "broken = {$x\n");
        });
        Settle(session);
        Assert.Equal(2, session.Server.Validations - validations, "Each changed project was not validated once.");
        JsonNode[] a = PublicationsOf(session, aUri, published), b = PublicationsOf(session, bUri, published);
        Assert.True(a.All(frame => frame["params"]!["version"]!.GetValue<int>() == 3), "A stale publication of a/en.rmf2 was sent.");
        Assert.True(Codes(a[^1]).Length > 0 && !Codes(a[^1]).Contains("RTR0050", StringComparison.Ordinal), "a/en.rmf2 does not show its last edit: " + a[^1].ToJsonString());
        Assert.Equal("RTR0050", Codes(b[^1]));
    }

    [Test, DisplayName("LSP clears the diagnostics of a document closed while its validation was pending")]
    public void ClosedWhilePending()
    {
        var (temporary, englishUri, _) = CreateProject();
        using (temporary)
        {
            File.WriteAllText(temporary.Resolve("en.rmf2"), English);
            using var hold = new Hold();
            using Session session = new(hold.Before, hold.Cancelled);
            session.Request("initialize", Initialize(temporary.Path, new JsonObject(), protocol: null));
            Open(session, englishUri, English);
            Settle(session);
            int validations = session.Server!.Validations, published = session.Publications.Count;

            hold.Queue(session, () => Change(session, englishUri, 2, English + "empty =\n"),
                () => session.Notify("textDocument/didClose", new JsonObject { ["textDocument"] = Document(englishUri) }));
            Settle(session);
            // The project has no open buffer left: nothing to validate, and the client keeps no diagnostics of the file.
            Assert.Equal(0, session.Server.Validations - validations, "A project without open buffers was validated.");
            Assert.Equal("", Codes(PublicationsOf(session, englishUri, published)[^1]));
        }
    }
}
