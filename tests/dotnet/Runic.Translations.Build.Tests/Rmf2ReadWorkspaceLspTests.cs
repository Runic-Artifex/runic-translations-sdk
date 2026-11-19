using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using TUnit.Core;
using static Runic.Translations.Build.Tests.Rmf2ProtocolLspTests;
using Session = Runic.Translations.Build.Tests.Rmf2DiagnosticLspTests.Session;

namespace Runic.Translations.Build.Tests;

// Read requests reuse one workspace per project (#271): it must follow every edit of an open document, every saved file a
// watched-file event reports, and every document opened in the project.
internal sealed class Rmf2ReadWorkspaceLspTests
{
    private const string English = "title = Hello {$name}\n";
    private const string German = "title = Hallo {$name}\n";

    private static JsonObject At(string uri, int line, int character) => new() { ["textDocument"] = Document(uri), ["position"] = Position(line, character) };

    // The files a definition of the message at the position names.
    private static string Definitions(Session session, string uri, int line) =>
        string.Join("|", session.Request("textDocument/definition", At(uri, line, 1))["result"]!.AsArray()
            .Select(location => Path.GetFileName(new Uri(location!["uri"]!.GetValue<string>()).LocalPath)).Order(StringComparer.Ordinal));

    private static void Change(Session session, string uri, int version, string text) =>
        session.Notify("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = version },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text }) });

    [Test, DisplayName("LSP read requests follow edits, saved-file changes and newly opened documents")]
    public void FollowsChanges()
    {
        var (temporary, englishUri, germanUri) = CreateProject();
        using (temporary)
        {
            File.WriteAllText(temporary.Resolve("en.rmf2"), English);
            File.WriteAllText(temporary.Resolve("de.rmf2"), German);
            using Session session = new(_ => { }, _ => { });
            session.Request("initialize", Initialize(temporary.Path, FullClientCapabilities(), protocol: 2));
            Open(session, englishUri, English);
            Assert.Equal("de.rmf2|en.rmf2", Definitions(session, englishUri, 0));

            // An edit of the open document: the next request sees the new message, which only English has.
            Change(session, englishUri, 2, English + "farewell = Bye {$name}\n");
            Assert.Equal("en.rmf2", Definitions(session, englishUri, 1));
            JsonNode completion = session.Request("textDocument/completion", At(englishUri, 1, 17));
            Assert.True(completion["result"]!.AsArray().Any(item => item!["label"]!.GetValue<string>() == "$name"), completion.ToJsonString());

            // The saved German file gains the message: the watched-file event makes the next request read it.
            File.WriteAllText(temporary.Resolve("de.rmf2"), German + "farewell = Tschüss {$name}\n");
            session.Notify("workspace/didChangeWatchedFiles", new JsonObject { ["changes"] = new JsonArray(new JsonObject { ["uri"] = germanUri, ["type"] = 2 }) });
            Assert.Equal("de.rmf2|en.rmf2", Definitions(session, englishUri, 1));

            // A German buffer that differs from its file: opening it changes the project's documents.
            Open(session, germanUri, German);
            Assert.Equal("en.rmf2", Definitions(session, englishUri, 1));
            // And an edit of that second buffer.
            Change(session, germanUri, 2, German + "farewell = Servus {$name}\n");
            Assert.Equal("de.rmf2|en.rmf2", Definitions(session, englishUri, 1));
        }
    }
}
