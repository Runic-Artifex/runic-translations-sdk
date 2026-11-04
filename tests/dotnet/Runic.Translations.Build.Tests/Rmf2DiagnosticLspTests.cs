using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Runic.Translations.Compiler;
using Runic.Translations.Tool;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

internal sealed class Rmf2DiagnosticLspTests
{
    [Test, DisplayName("LSP mounted Unicode diagnostic quick fixes share compiler edits and refuse stale resolution")]
    public void CodeActions()
    {
        foreach (string encoding in new[] { "utf-8", "utf-16", "utf-32" })
        {
            using TemporaryDirectory temporary = new();
            Directory.CreateDirectory(temporary.Resolve("project"));
            Directory.CreateDirectory(temporary.Resolve("feature"));
            File.WriteAllText(temporary.Resolve("project/runic.json"), """{"schemaVersion":1,"catalog":"app","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","sourceRoots":[{"path":"../feature","namespace":["shop"]}]}""");
            const string text = "# 😀 context\r\nblock =\r\n    😀 }\r\nempty =   \r\n";
            string path = temporary.Resolve("feature/en.rmf2"), uri = new Uri(path).AbsoluteUri;
            File.WriteAllText(path, text);
            using Session session = new(temporary.Path);
            JsonNode initialize = session.Request("initialize", new JsonObject {
                ["rootUri"] = new Uri(temporary.Path).AbsoluteUri,
                ["capabilities"] = new JsonObject { ["general"] = new JsonObject { ["positionEncodings"] = new JsonArray(encoding) } },
            });
            Assert.True(initialize["result"]?["capabilities"]?["codeActionProvider"]?["resolveProvider"]?.GetValue<bool>() == true, "Missing code-action capability.");
            session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["text"] = text, ["version"] = 7 } });
            var args = new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri },
                ["range"] = new JsonObject { ["start"] = Position(3, 0), ["end"] = Position(3, 10) },
                ["context"] = new JsonObject { ["diagnostics"] = new JsonArray() },
            };
            JsonArray actions = session.Request("textDocument/codeAction", args)["result"]!.AsArray();
            Assert.Equal(1, actions.Count);
            JsonNode action = actions[0]!;
            JsonNode diagnostic = session.Publications.Last(frame => frame["params"]?["uri"]?.GetValue<string>() == uri)["params"]!["diagnostics"]!.AsArray()
                .Single(item => item?["message"]?.GetValue<string>() == "This pattern character must be escaped or removed.")!;
            Assert.Equal(2, diagnostic["range"]!["start"]!["line"]!.GetValue<int>());
            Assert.Equal(encoding == "utf-8" ? 9 : encoding == "utf-16" ? 7 : 6, diagnostic["range"]!["start"]!["character"]!.GetValue<int>());
            JsonNode change = action["edit"]!["documentChanges"]![0]!;
            Assert.Equal(uri, change["textDocument"]!["uri"]!.GetValue<string>());
            Assert.Equal(7, change["textDocument"]!["version"]!.GetValue<int>());
            Assert.Equal(3, change["edits"]![0]!["range"]!["start"]!["line"]!.GetValue<int>());
            Assert.Equal(7, change["edits"]![0]!["range"]!["start"]!["character"]!.GetValue<int>());
            Assert.Equal(" {{}}", change["edits"]![0]!["newText"]!.GetValue<string>());
            var source = new TranslationSource(path, Encoding.UTF8.GetBytes(text));
            var shared = Rmf2ResourceReader.Analyze(source).Diagnostics.SelectMany(item => Rmf2DiagnosticActions.GetQuickFixes(source, item)).Single();
            Assert.Equal(shared.ExpectedRevision, action["data"]!["revision"]!.GetValue<string>());
            Assert.Equal(shared.Id, action["data"]!["fixId"]!.GetValue<string>());
            Assert.Equal(" {{}}", shared.NewText);
            Assert.True(session.Request("codeAction/resolve", action.DeepClone().AsObject())["error"] is null, "Current quick fix failed resolution.");
            var malformed = action.DeepClone().AsObject(); malformed["data"]!.AsObject().Remove("version");
            Assert.Equal(-32602, session.Request("codeAction/resolve", malformed)["error"]!["code"]!.GetValue<int>());
            // A reopened buffer may reuse a version; the content hash must still refuse old evidence.
            session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["text"] = text.Replace("context", "changed", StringComparison.Ordinal), ["version"] = 7 } });
            Assert.Equal(-32801, session.Request("codeAction/resolve", action.DeepClone().AsObject())["error"]!["code"]!.GetValue<int>());
            // Identical bytes at a newer document version must also refuse the old action.
            session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["text"] = text, ["version"] = 8 } });
            Assert.Equal(-32801, session.Request("codeAction/resolve", action.DeepClone().AsObject())["error"]!["code"]!.GetValue<int>());
            string after = Encoding.UTF8.GetString(Rmf2DiagnosticActions.ApplyQuickFix(source, shared).GetUtf8Bytes());
            session.Notify("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = 9 },
                ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = after }) });
            Assert.Equal(-32801, session.Request("codeAction/resolve", action.DeepClone().AsObject())["error"]!["code"]!.GetValue<int>());
            Assert.Equal(0, session.Request("textDocument/codeAction", args)["result"]!.AsArray().Count);
            Assert.Equal(text, File.ReadAllText(path), "LSP code-action discovery wrote the source file.");
        }
    }
    private const string MountedProject = """{"schemaVersion":1,"catalog":"app","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","sourceRoots":[{"path":"../feature","namespace":["shop"]}]}""";

    [Test, DisplayName("LSP adds unsaved project-directory buffers only where source discovery would")]
    public void SourceRootOverlays()
    {
        // With sourceRoots configured, discovery ignores resources in the
        // project directory itself. An open buffer there must not join the
        // compilation either, or every preview fails for the whole project.
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("project"));
        Directory.CreateDirectory(temporary.Resolve("feature"));
        File.WriteAllText(temporary.Resolve("project/runic.json"), MountedProject);
        File.WriteAllText(temporary.Resolve("project/en.rmf2"), "stray = Outside every source root\n");
        File.WriteAllText(temporary.Resolve("feature/en.rmf2"), "title = Saved\n");
        string strayUri = new Uri(temporary.Resolve("project/en.rmf2")).AbsoluteUri;
        string featureUri = new Uri(temporary.Resolve("feature/en.rmf2")).AbsoluteUri;
        string newFeatureUri = new Uri(temporary.Resolve("feature/de.rmf2")).AbsoluteUri;
        using Session session = new(temporary.Path);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = strayUri, ["version"] = 1, ["text"] = "stray = Unsaved stray\n" } });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = featureUri, ["version"] = 1, ["text"] = "title = UNSAVED mounted marker\n" } });
        JsonNode preview = session.Request("workspace/executeCommand", new JsonObject {
            ["command"] = "runic.renderPreview", ["arguments"] = new JsonArray(featureUri, "shop_title", "en", new JsonObject()) });
        Assert.True(preview["error"] is null, "An open project-directory buffer outside the source roots broke the preview: " + preview.ToJsonString());
        Assert.Contains("UNSAVED mounted marker", preview["result"]!["runs"]!.ToJsonString());
        // A new unsaved resource beneath a configured source root still joins.
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = newFeatureUri, ["version"] = 1, ["text"] = "title = Ungespeichert\n" } });
        JsonNode german = session.Request("workspace/executeCommand", new JsonObject {
            ["command"] = "runic.renderPreview", ["arguments"] = new JsonArray(newFeatureUri, "shop_title", "de", new JsonObject()) });
        Assert.Contains("Ungespeichert", german.ToJsonString());
    }

    [Test, DisplayName("LSP reports malformed unsaved source roots as configuration diagnostics")]
    public void MalformedSourceRoots()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("project"));
        Directory.CreateDirectory(temporary.Resolve("feature"));
        File.WriteAllText(temporary.Resolve("project/runic.json"), MountedProject);
        File.WriteAllText(temporary.Resolve("feature/en.rmf2"), "title = Saved\n");
        string configUri = new Uri(temporary.Resolve("project/runic.json")).AbsoluteUri;
        string featureUri = new Uri(temporary.Resolve("feature/en.rmf2")).AbsoluteUri;
        using Session session = new(temporary.Path);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = featureUri, ["version"] = 1, ["text"] = "title = Unsaved\n" } });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = configUri, ["version"] = 1,
            ["text"] = MountedProject.Replace(",\"namespace\":[\"shop\"]", "", StringComparison.Ordinal) } });
        JsonNode preview = session.Request("workspace/executeCommand", new JsonObject {
            ["command"] = "runic.renderPreview", ["arguments"] = new JsonArray(featureUri, "title", "en", new JsonObject()) });
        string message = preview["error"]?["message"]?.GetValue<string>() ?? "";
        Assert.Contains("Missing required member 'namespace'.", message);
        Assert.False(message.Contains("given key", StringComparison.Ordinal), "A malformed mount leaked a raw dictionary error: " + message);
        JsonArray config = session.Publications.Last(frame => frame["method"]?.GetValue<string>() == "textDocument/publishDiagnostics" &&
            frame["params"]?["uri"]?.GetValue<string>() == configUri)["params"]!["diagnostics"]!.AsArray();
        Assert.True(config.Any(diagnostic => diagnostic?["message"]?.GetValue<string>() == "Missing required member 'namespace'."),
            "The open runic.json did not receive the located mount diagnostic: " + config.ToJsonString());
    }

    [Test, DisplayName("LSP mounted-buffer ownership follows the shared manifest reader")]
    public void PartialSourceRoots()
    {
        // Ownership of mounted buffers uses the same manifest reader as the CLI
        // and the Editor: one unusable entry (here a path still being typed)
        // makes the whole source layout unusable, rather than keeping the valid
        // mounts as the language server once did. The open runic.json carries
        // the located diagnostic, and fixing it restores the mount.
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("project"));
        Directory.CreateDirectory(temporary.Resolve("feature"));
        File.WriteAllText(temporary.Resolve("project/runic.json"), MountedProject);
        File.WriteAllText(temporary.Resolve("feature/en.rmf2"), "title = Saved\n");
        string configUri = new Uri(temporary.Resolve("project/runic.json")).AbsoluteUri;
        string featureUri = new Uri(temporary.Resolve("feature/en.rmf2")).AbsoluteUri;
        using Session session = new(temporary.Path);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = featureUri, ["version"] = 1, ["text"] = "title = Unsaved\n" } });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = configUri, ["version"] = 1,
            ["text"] = MountedProject.Replace("}]}", "},{\"path\":\"   \",\"namespace\":[\"draft\"]}]}", StringComparison.Ordinal) } });
        JsonNode preview = session.Request("workspace/executeCommand", new JsonObject {
            ["command"] = "runic.renderPreview", ["arguments"] = new JsonArray(featureUri, "shop_title", "en", new JsonObject()) });
        Assert.Equal("No runic.json project contains this resource in its project directory or configured source roots.",
            preview["error"]?["message"]?.GetValue<string>(), preview.ToJsonString());
        JsonArray config = session.Publications.Last(frame => frame["method"]?.GetValue<string>() == "textDocument/publishDiagnostics" &&
            frame["params"]?["uri"]?.GetValue<string>() == configUri)["params"]!["diagnostics"]!.AsArray();
        Assert.True(config.Any(diagnostic => diagnostic?["message"]?.GetValue<string>() == "Each source root must declare a non-empty path."),
            "The open runic.json did not receive the located mount diagnostic: " + config.ToJsonString());
        session.Notify("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = configUri, ["version"] = 2 },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = MountedProject }) });
        JsonNode fixedPreview = session.Request("workspace/executeCommand", new JsonObject {
            ["command"] = "runic.renderPreview", ["arguments"] = new JsonArray(featureUri, "shop_title", "en", new JsonObject()) });
        Assert.True(fixedPreview["error"] is null, "Fixing runic.json did not restore the mount: " + fixedPreview.ToJsonString());
        Assert.Contains("Unsaved", fixedPreview["result"]!["runs"]!.ToJsonString());
    }

    [Test, DisplayName("LSP reports a non-object runic.json as a configuration diagnostic")]
    public void NonObjectConfiguration()
    {
        // An unsaved runic.json whose root is an array must surface the
        // compiler's configuration diagnostic, not a raw JSON access error.
        using TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("runic.json"), """{"schemaVersion":1,"catalog":"app","code":{"namespace":"Example","className":"Text"},"baseLocale":"en"}""");
        File.WriteAllText(temporary.Resolve("en.rmf2"), "x = Saved\n");
        string configUri = new Uri(temporary.Resolve("runic.json")).AbsoluteUri;
        string sourceUri = new Uri(temporary.Resolve("en.rmf2")).AbsoluteUri;
        using Session session = new(temporary.Path);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = sourceUri, ["version"] = 1, ["text"] = "x = Unsaved\n" } });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = configUri, ["version"] = 1, ["text"] = """["not","an","object"]""" } });
        JsonNode preview = session.Request("workspace/executeCommand", new JsonObject {
            ["command"] = "runic.renderPreview", ["arguments"] = new JsonArray(sourceUri, "x", "en", new JsonObject()) });
        JsonNode rename = session.Request("workspace/executeCommand", new JsonObject {
            ["command"] = "runic.renameResource", ["arguments"] = new JsonArray(sourceUri, new JsonArray("x"), "renamed") });
        JsonNode symbolRename = session.Request("textDocument/rename", new JsonObject {
            ["textDocument"] = new JsonObject { ["uri"] = sourceUri }, ["position"] = Position(0, 0), ["newName"] = "renamed" });
        foreach ((string operation, JsonNode response) in new[] { ("runic.renderPreview", preview), ("runic.renameResource", rename), ("textDocument/rename", symbolRename) })
            Assert.Equal("Runic project root must be an object.", response["error"]?["message"]?.GetValue<string>() ?? response.ToJsonString(), operation);
        JsonArray config = session.Publications.Last(frame => frame["method"]?.GetValue<string>() == "textDocument/publishDiagnostics" &&
            frame["params"]?["uri"]?.GetValue<string>() == configUri)["params"]!["diagnostics"]!.AsArray();
        Assert.True(config.Any(diagnostic => diagnostic?["message"]?.GetValue<string>() == "Runic project root must be an object."),
            "The open runic.json did not receive the root-kind diagnostic: " + config.ToJsonString());
    }

    [Test, DisplayName("LSP refuses renames with a clear error when runic.json markup is mistyped")]
    public void MistypedMarkupRename()
    {
        // "markup": [] (or a non-object slots member) must not surface a raw
        // JsonNode type error through either rename route; the edited catalog
        // is refused with the compiler's configuration message instead.
        foreach ((string markup, string expected) in new[] {
            ("[]", "The complete edited catalog is invalid: markup must be an object."),
            ("{\"slots\":[]}", "The complete edited catalog is invalid: markup.slots must be an object.") })
        {
            using TemporaryDirectory temporary = new();
            string config = """{"schemaVersion":1,"catalog":"app","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","markup":""" + markup + "}";
            File.WriteAllText(temporary.Resolve("runic.json"), """{"schemaVersion":1,"catalog":"app","code":{"namespace":"Example","className":"Text"},"baseLocale":"en"}""");
            File.WriteAllText(temporary.Resolve("en.rmf2"), "x = Saved\n");
            string configUri = new Uri(temporary.Resolve("runic.json")).AbsoluteUri;
            string sourceUri = new Uri(temporary.Resolve("en.rmf2")).AbsoluteUri;
            using Session session = new(temporary.Path);
            session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
            session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = sourceUri, ["version"] = 1, ["text"] = "x = Unsaved\n" } });
            session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = configUri, ["version"] = 1, ["text"] = config } });
            JsonNode command = session.Request("workspace/executeCommand", new JsonObject {
                ["command"] = "runic.renameResource", ["arguments"] = new JsonArray(sourceUri, new JsonArray("x"), "renamed") });
            JsonNode symbol = session.Request("textDocument/rename", new JsonObject {
                ["textDocument"] = new JsonObject { ["uri"] = sourceUri }, ["position"] = Position(0, 0), ["newName"] = "renamed" });
            foreach ((string operation, JsonNode response) in new[] { ("runic.renameResource", command), ("textDocument/rename", symbol) })
                Assert.Equal(expected, response["error"]?["message"]?.GetValue<string>() ?? response.ToJsonString(), operation + " with markup " + markup);
        }
    }

    private const string PlainProject = """{"schemaVersion":1,"catalog":"app","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","locales":["en","de"]}""";

    [Test, DisplayName("LSP publishes catalog diagnostics of closed project files with documentation links")]
    public void ProjectFileDiagnostics()
    {
        using TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("runic.json"), PlainProject);
        File.WriteAllText(temporary.Resolve("en.rmf2"), "title = Hello {$name}\n");
        const string brokenGerman = "title = Hallo {$name} {$extra}\n";
        File.WriteAllText(temporary.Resolve("de.rmf2"), brokenGerman);
        string enUri = new Uri(temporary.Resolve("en.rmf2")).AbsoluteUri, deUri = new Uri(temporary.Resolve("de.rmf2")).AbsoluteUri;
        using Session session = new(temporary.Path);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject {
            ["textDocument"] = new JsonObject { ["publishDiagnostics"] = new JsonObject { ["codeDescriptionSupport"] = true } } } });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = enUri, ["version"] = 1, ["text"] = "title = Hello {$name}\n" } });
        Flush(session, enUri);

        // The German file is closed, yet its catalog error reaches the client with a link to its catalog entry.
        JsonArray closed = LastDiagnostics(session, deUri);
        Assert.True(closed.Count > 0, "The closed translation's catalog diagnostic was not published.");
        JsonNode diagnostic = closed[0]!;
        string code = diagnostic["code"]!.GetValue<string>();
        Assert.True(code.StartsWith("RTR", StringComparison.Ordinal), closed.ToJsonString());
        Assert.Equal(0, diagnostic["range"]!["start"]!["line"]!.GetValue<int>());
        // The range is mapped from the saved file: the message name `title`.
        Assert.Equal(5, diagnostic["range"]!["end"]!["character"]!.GetValue<int>(), closed.ToJsonString());
        string href = diagnostic["codeDescription"]?["href"]?.GetValue<string>() ?? "";
        Assert.True(href.StartsWith("https://github.com/Runic-Artifex/runic-translations-sdk/blob/v", StringComparison.Ordinal) &&
            href.EndsWith("/docs/guides/translations/diagnostics.md#" + code.ToLowerInvariant(), StringComparison.Ordinal), href);
        int before = session.Publications.Count;
        // An unrelated keystroke revalidates the project without republishing unchanged closed files.
        session.Notify("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = enUri, ["version"] = 2 },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "title = Hello, {$name}\n" }) });
        Flush(session, enUri);
        Assert.False(session.Publications.Skip(before).Any(frame => frame["params"]?["uri"]?.GetValue<string>() == deUri), "An unchanged closed file was republished.");

        // Opening the file hands its diagnostics to the buffer; closing it returns them to the saved file.
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = deUri, ["version"] = 1, ["text"] = "title = Hallo {$name}\n" } });
        Flush(session, enUri);
        Assert.Equal(0, LastDiagnostics(session, deUri).Count, "The fixed open buffer kept the saved file's diagnostics.");
        session.Notify("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = deUri } });
        Flush(session, enUri);
        Assert.True(LastDiagnostics(session, deUri).Count > 0, "Closing the unsaved fix did not restore the saved file's diagnostics.");

        // Fixing the saved file clears it after the watched-file notification.
        File.WriteAllText(temporary.Resolve("de.rmf2"), "title = Hallo {$name}\n");
        session.Notify("workspace/didChangeWatchedFiles", new JsonObject { ["changes"] = new JsonArray(new JsonObject { ["uri"] = deUri, ["type"] = 2 }) });
        Flush(session, enUri);
        Assert.Equal(0, LastDiagnostics(session, deUri).Count, "The fixed saved file kept its diagnostics.");

        // When the last buffer of the project closes, its closed files are cleared too.
        File.WriteAllText(temporary.Resolve("de.rmf2"), brokenGerman);
        session.Notify("workspace/didChangeWatchedFiles", new JsonObject { ["changes"] = new JsonArray(new JsonObject { ["uri"] = deUri, ["type"] = 2 }) });
        Flush(session, enUri);
        Assert.True(LastDiagnostics(session, deUri).Count > 0, "The saved regression was not published.");
        session.Notify("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = enUri } });
        session.Request("textDocument/hover", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = enUri }, ["position"] = Position(0, 0) });
        Assert.Equal(0, LastDiagnostics(session, deUri).Count, "The closed project's file diagnostics were not cleared.");
    }

    [Test, DisplayName("LSP sends documentation links only to clients that support them")]
    public void CodeDescriptionCapability()
    {
        using TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("runic.json"), PlainProject);
        File.WriteAllText(temporary.Resolve("en.rmf2"), "title = Hello {$name}\n");
        File.WriteAllText(temporary.Resolve("de.rmf2"), "title = Hallo {$name} {$extra}\n");
        string enUri = new Uri(temporary.Resolve("en.rmf2")).AbsoluteUri, deUri = new Uri(temporary.Resolve("de.rmf2")).AbsoluteUri;
        using Session session = new(temporary.Path);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = enUri, ["version"] = 1, ["text"] = "title = Hello {$name}\n" } });
        Flush(session, enUri);
        JsonArray diagnostics = LastDiagnostics(session, deUri);
        Assert.True(diagnostics.Count > 0, "The closed translation's catalog diagnostic was not published.");
        Assert.True(diagnostics.All(item => item?["codeDescription"] is null), diagnostics.ToJsonString());
    }

    [Test, DisplayName("LSP document info names the owning catalog and the file's locale")]
    public void DocumentInfo()
    {
        using TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("runic.json"), PlainProject);
        File.WriteAllText(temporary.Resolve("en.rmf2"), "title = Hello\n");
        string deUri = new Uri(temporary.Resolve("de.rmf2")).AbsoluteUri, configUri = new Uri(temporary.Resolve("runic.json")).AbsoluteUri;
        using Session session = new(temporary.Path);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
        // A new unsaved translation already belongs to its catalog.
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = deUri, ["version"] = 1, ["text"] = "title = Hallo\n" } });
        JsonNode info = session.Request("runic/documentInfo", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = deUri } })["result"]!;
        Assert.Equal("app", info["catalog"]?.GetValue<string>(), info.ToJsonString());
        Assert.Equal("de", info["locale"]?.GetValue<string>(), info.ToJsonString());
        Assert.Equal("en", info["baseLocale"]?.GetValue<string>(), info.ToJsonString());
        Assert.Equal(configUri, info["project"]?.GetValue<string>(), info.ToJsonString());
        // An unsaved catalog rename in the open manifest is what the status shows.
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = configUri, ["version"] = 1, ["text"] = PlainProject.Replace("\"app\"", "\"shop\"", StringComparison.Ordinal) } });
        JsonNode config = session.Request("runic/documentInfo", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = configUri } })["result"]!;
        Assert.Equal("shop", config["catalog"]?.GetValue<string>(), config.ToJsonString());
        Assert.True(config["locale"] is null, config.ToJsonString());
    }

    [Test, DisplayName("LSP hover on a translation shows the base text and its description")]
    public void TranslatorContextHover()
    {
        using TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("runic.json"), PlainProject);
        File.WriteAllText(temporary.Resolve("en.rmf2"), "# Shown on the toolbar.\n@param $name - Who is greeted\ntitle = Hello {$name}\n");
        const string german = "# Kurz halten.\ntitle = Hallo {$name}\n";
        File.WriteAllText(temporary.Resolve("de.rmf2"), german);
        string enUri = new Uri(temporary.Resolve("en.rmf2")).AbsoluteUri, deUri = new Uri(temporary.Resolve("de.rmf2")).AbsoluteUri;
        using Session session = new(temporary.Path);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = deUri, ["version"] = 1, ["text"] = german } });
        string hover = session.Request("textDocument/hover", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = deUri }, ["position"] = Position(1, 1) })
            ["result"]!["contents"]!["value"]!.GetValue<string>();
        Assert.Contains("Base text (en): Hello {$name}", hover);
        Assert.Contains("Description: Shown on the toolbar.", hover);
        Assert.Contains("@param $name - Who is greeted", hover);
        Assert.Contains("Kurz halten.", hover);
        Assert.Contains("Content locale: de", hover);
        // An unsaved base buffer is the base text the translator sees.
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = enUri, ["version"] = 1, ["text"] = "# Shown on the toolbar.\ntitle =\n    Hello\n    {$name}\n" } });
        hover = session.Request("textDocument/hover", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = deUri }, ["position"] = Position(1, 1) })
            ["result"]!["contents"]!["value"]!.GetValue<string>();
        Assert.Contains("Base text (en):\n  Hello\n  {$name}", hover);
        // The base file itself repeats no base text.
        string baseHover = session.Request("textDocument/hover", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = enUri }, ["position"] = Position(1, 1) })
            ["result"]!["contents"]!["value"]!.GetValue<string>();
        Assert.False(baseHover.Contains("Base text", StringComparison.Ordinal), baseHover);
        Assert.Contains("Shown on the toolbar.", baseHover);
    }

    [Test, DisplayName("LSP keeps an opened file's diagnostics when the client encodes its URI differently")]
    public void EncodedClientUris()
    {
        // VS Code writes `file:///c%3A/...` for Windows drives and percent-encodes `+`, spaces and
        // `:` in every path segment; .NET writes `C:/` and `c++%20app`. Off Windows a directory named
        // `c:` produces the same `c%3A` spelling.
        using TemporaryDirectory temporary = new();
        string project = OperatingSystem.IsWindows() ? temporary.Resolve("c++ app") : temporary.Resolve("c:", "c++ app");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "runic.json"), PlainProject);
        File.WriteAllText(Path.Combine(project, "en.rmf2"), "title = Hello {$name}\n");
        const string brokenGerman = "title = Hallo {$name} {$extra}\n";
        File.WriteAllText(Path.Combine(project, "de.rmf2"), brokenGerman);
        string root = VsCodeUri(project), enUri = VsCodeUri(Path.Combine(project, "en.rmf2")), deUri = VsCodeUri(Path.Combine(project, "de.rmf2"));
        string configUri = VsCodeUri(Path.Combine(project, "runic.json")), dePath = Path.Combine(project, "de.rmf2");
        Assert.Contains("c%3A", deUri);
        Assert.Contains("c%2B%2B%20app", deUri);
        using Session session = new(temporary.Path);
        session.Request("initialize", new JsonObject { ["rootUri"] = root, ["capabilities"] = new JsonObject() });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = enUri, ["version"] = 1, ["text"] = "title = Hello {$name}\n" } });
        Flush(session, enUri);
        Assert.True(ResourceDiagnostics(session, dePath).Count > 0, "The closed translation's catalog diagnostic was not published.");

        // Opening the closed file (from the Problems view) keeps its diagnostics: no clear under
        // another spelling of its URI follows the buffer's publication.
        int before = session.Publications.Count;
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = deUri, ["version"] = 1, ["text"] = brokenGerman } });
        Flush(session, enUri);
        JsonNode last = session.Publications.Skip(before).Last(frame => IsPublicationFor(frame, dePath));
        Assert.Equal(deUri, last["params"]!["uri"]!.GetValue<string>(), "The open file was not published under the client's URI.");
        Assert.True(last["params"]!["diagnostics"]!.AsArray().Count > 0, "Opening the file erased its diagnostics: " + last.ToJsonString());
        // Another keystroke in the project leaves it alone too.
        session.Notify("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = enUri, ["version"] = 2 },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "title = Hello, {$name}\n" }) });
        Flush(session, enUri);
        Assert.True(ResourceDiagnostics(session, dePath).Count > 0, "A keystroke erased the open file's diagnostics.");

        // The unsaved runic.json is found under the client's spelling of its URI.
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = configUri, ["version"] = 1, ["text"] = PlainProject.Replace("\"app\"", "\"shop\"", StringComparison.Ordinal) } });
        JsonNode info = session.Request("runic/documentInfo", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = deUri } })["result"]!;
        Assert.Equal("shop", info["catalog"]?.GetValue<string>(), info.ToJsonString());
        Assert.Equal("de", info["locale"]?.GetValue<string>(), info.ToJsonString());
        Assert.Equal(configUri, info["project"]?.GetValue<string>(), info.ToJsonString());

        // Closing the buffer returns the diagnostics to the saved file.
        session.Notify("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = deUri } });
        Flush(session, enUri);
        Assert.True(ResourceDiagnostics(session, dePath).Count > 0, "Closing the file did not restore the saved file's diagnostics.");
    }

    [Test, DisplayName("LSP publishes the union of every project's diagnostics for a shared closed file")]
    public void SharedProjectFileDiagnostics()
    {
        // Two catalogs mount one source root. Each reports the shared German file; closing the last
        // buffer of one project must not erase what the other still reports.
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("shared"));
        File.WriteAllText(temporary.Resolve("shared", "en.rmf2"), "title = Hello {$name}\n");
        File.WriteAllText(temporary.Resolve("shared", "de.rmf2"), "title = Hallo {$name} {$extra}\n");
        foreach (string name in new[] { "one", "two" })
        {
            Directory.CreateDirectory(temporary.Resolve(name, "own"));
            File.WriteAllText(temporary.Resolve(name, "runic.json"), "{\"schemaVersion\":1,\"catalog\":\"" + name + "\",\"code\":{\"namespace\":\"Example\",\"className\":\"Text\"},\"baseLocale\":\"en\",\"locales\":[\"en\",\"de\"]," +
                "\"sourceRoots\":[{\"path\":\"../shared\",\"namespace\":[\"shop\"]},{\"path\":\"own\",\"namespace\":[\"" + name + "\"]}]}");
            File.WriteAllText(temporary.Resolve(name, "own", "en.rmf2"), "label = " + name + "\n");
            File.WriteAllText(temporary.Resolve(name, "own", "de.rmf2"), "label = " + name + "\n");
        }
        string oneUri = new Uri(temporary.Resolve("one", "own", "en.rmf2")).AbsoluteUri, twoUri = new Uri(temporary.Resolve("two", "own", "en.rmf2")).AbsoluteUri;
        string sharedPath = temporary.Resolve("shared", "de.rmf2");
        using Session session = new(temporary.Path);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = oneUri, ["version"] = 1, ["text"] = "label = one\n" } });
        Flush(session, oneUri);
        JsonArray fromOne = ResourceDiagnostics(session, sharedPath);
        Assert.True(fromOne.Count > 0, "Project one's diagnostic for the shared file was not published.");
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = twoUri, ["version"] = 1, ["text"] = "label = two\n" } });
        Flush(session, twoUri);
        // Both projects report the same diagnostic; the union shows it once.
        Assert.Equal(fromOne.ToJsonString(), ResourceDiagnostics(session, sharedPath).ToJsonString());

        session.Notify("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = oneUri } });
        Flush(session, twoUri);
        Assert.True(ResourceDiagnostics(session, sharedPath).Count > 0, "Closing project one's last buffer erased project two's diagnostics.");
        // Project two's next validation (an unrelated keystroke) keeps them.
        session.Notify("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = twoUri, ["version"] = 2 },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "label = Two\n" }) });
        Flush(session, twoUri);
        Assert.True(ResourceDiagnostics(session, sharedPath).Count > 0, "Project two's update lost its diagnostics for the shared file.");
        session.Notify("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = twoUri } });
        // A request is answered after the close is processed (this one fails: the file is closed).
        session.Request("textDocument/hover", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = twoUri }, ["position"] = Position(0, 0) });
        Assert.Equal(0, ResourceDiagnostics(session, sharedPath).Count, "The shared file kept diagnostics after both projects closed.");
    }

    [Test, DisplayName("LSP sends clears made while a newer revision was queued")]
    public void SupersededRevisionClears()
    {
        // Project A's last buffer closes while a keystroke in project B is already queued. The
        // revision gate holds A's clears back while B is newer; they must still reach the client.
        using TemporaryDirectory temporary = new();
        foreach (string name in new[] { "a", "b" })
        {
            Directory.CreateDirectory(temporary.Resolve(name));
            File.WriteAllText(temporary.Resolve(name, "runic.json"), PlainProject);
            File.WriteAllText(temporary.Resolve(name, "en.rmf2"), "title = Hello {$name}\n");
            File.WriteAllText(temporary.Resolve(name, "de.rmf2"), name == "a" ? "title = Hallo {$name} {$extra}\n" : "title = Hallo {$name}\n");
        }
        string aUri = new Uri(temporary.Resolve("a", "en.rmf2")).AbsoluteUri, bUri = new Uri(temporary.Resolve("b", "en.rmf2")).AbsoluteUri;
        string aGermanUri = new Uri(temporary.Resolve("a", "de.rmf2")).AbsoluteUri;
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var cancelled = new ManualResetEventSlim(false);
        int hold = 0;
        void BeforeRequest(string method)
        {
            if (method != "textDocument/didClose" || Volatile.Read(ref hold) == 0) return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The didClose barrier was not released.");
        }
        using Session session = new(BeforeRequest, _ => cancelled.Set());
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
        // A's closed German file has a catalog error.
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = aUri, ["version"] = 1, ["text"] = "title = Hello {$name}\n" } });
        Flush(session, aUri);
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = bUri, ["version"] = 1, ["text"] = "title = Hello {$name}\n" } });
        Flush(session, bUri);
        Assert.True(LastDiagnostics(session, aGermanUri).Count > 0, "A's closed-file diagnostic was not published.");

        Volatile.Write(ref hold, 1);
        session.Notify("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = aUri } });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "The didClose barrier was not reached.");
        session.Notify("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = bUri, ["version"] = 2 },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "title = Hello, {$name}\n" }) });
        // The reader reads in order: once it has seen this cancellation, the keystroke is queued.
        int marker = session.Start("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = bUri } });
        session.Notify("$/cancelRequest", new JsonObject { ["id"] = marker });
        Assert.True(cancelled.Wait(TimeSpan.FromSeconds(10)), "The reader did not reach the queued keystroke.");
        release.Set();
        session.Wait(marker);
        Flush(session, bUri);
        Assert.Equal(0, LastDiagnostics(session, aGermanUri).Count, "The closed project's file diagnostics were not cleared.");
    }

    // A file URI spelled as VS Code (vscode-uri) spells it: lower-case drive letter, and every
    // character other than unreserved ones and `/` percent-encoded, `:` included.
    private static string VsCodeUri(string path)
    {
        string full = Path.GetFullPath(path).Replace('\\', '/');
        if (full.Length > 1 && full[1] == ':') full = "/" + char.ToLowerInvariant(full[0]) + full[1..];
        var builder = new StringBuilder("file://");
        foreach (byte value in Encoding.UTF8.GetBytes(full))
            builder.Append(value is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~' or (byte)'/'
                ? ((char)value).ToString() : "%" + value.ToString("X2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    // Whether a publication is for a file, whatever spelling of its URI it uses (clients map
    // every spelling of one file to one resource).
    private static bool IsPublicationFor(JsonNode frame, string path) =>
        frame["method"]?.GetValue<string>() == "textDocument/publishDiagnostics" && frame["params"]?["uri"]?.GetValue<string>() is { } uri &&
        string.Equals(Path.GetFullPath(new Uri(uri).LocalPath), Path.GetFullPath(path), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // The diagnostics a client that normalizes URIs shows for a file.
    private static JsonArray ResourceDiagnostics(Session session, string path) =>
        session.Publications.Last(frame => IsPublicationFor(frame, path))["params"]!["diagnostics"]!.AsArray();

    // Requests are answered in order, so every notification sent before is processed once this returns.
    private static void Flush(Session session, string uri) =>
        session.Request("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } });

    private static JsonArray LastDiagnostics(Session session, string uri) =>
        session.Publications.Last(frame => frame["method"]?.GetValue<string>() == "textDocument/publishDiagnostics" &&
            frame["params"]?["uri"]?.GetValue<string>() == uri)["params"]!["diagnostics"]!.AsArray();

    private static JsonObject Position(int line, int character) => new() { ["line"] = line, ["character"] = character };

    internal sealed class Session : IDisposable
    {
        private readonly Process? _process;
        private readonly Task<string> _errors;
        // In-process mode: the server runs on this test's streams with request hooks.
        private readonly Rmf2LspBenchmark.BlockingByteStream? _serverInput;
        private readonly Rmf2LspBenchmark.BlockingByteStream? _serverOutput;
        private readonly Task<int>? _server;
        private int _nextId;
        private bool _disposed;
        internal List<JsonNode> Publications { get; } = new();
        /// <summary>Server stderr; complete only after <see cref="Dispose"/>.</summary>
        internal string StandardError => _errors.IsCompletedSuccessfully ? _errors.Result : "";
        internal Session(string directory)
        {
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add(RepositoryPaths.ToolAssembly); start.ArgumentList.Add("lsp");
            _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start LSP.");
            _errors = _process.StandardError.ReadToEndAsync();
        }
        internal Session(Action<string> beforeRequest, Action<string> requestCancelled)
        {
            _serverInput = new Rmf2LspBenchmark.BlockingByteStream();
            Rmf2LspBenchmark.BlockingByteStream output = _serverOutput = new Rmf2LspBenchmark.BlockingByteStream();
            var server = new Rmf2LanguageServer(_serverInput, output, beforeRequest, requestCancelled);
            _server = Task.Run(() => {
                try { return server.Run(); }
                finally { output.CompleteWriting(); }
            });
            _errors = Task.FromResult("");
        }
        internal void Notify(string method, JsonObject args) => Send(method, args, null);
        internal JsonNode Request(string method, JsonObject args) => Wait(Start(method, args));
        // Sends a request without waiting for its response.
        internal int Start(string method, JsonObject args)
        {
            int id = ++_nextId;
            Send(method, args, id);
            return id;
        }
        internal JsonNode Wait(int id)
        {
            while (true)
            {
                JsonNode frame = Task.Run(ReadFrame).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                if (frame["id"]?.GetValue<int>() == id) return frame;
                Publications.Add(frame);
            }
        }
        private void Send(string method, JsonObject args, int? id)
        {
            var request = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = args.DeepClone() };
            if (id is not null) request["id"] = id.Value;
            byte[] bytes = Encoding.UTF8.GetBytes(request.ToJsonString());
            Stream input = _serverInput ?? _process!.StandardInput.BaseStream;
            input.Write(Encoding.ASCII.GetBytes("Content-Length: " + bytes.Length + "\r\n\r\n")); input.Write(bytes); input.Flush();
        }
        private JsonNode ReadFrame()
        {
            Stream output = _serverOutput ?? _process!.StandardOutput.BaseStream;
            var header = new List<byte>();
            while (header.Count < 8192)
            {
                int value = output.ReadByte();
                if (value < 0) throw new EndOfStreamException("LSP exited before responding.");
                header.Add((byte)value);
                if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
            }
            int length = int.Parse(Encoding.ASCII.GetString(header.ToArray()).Substring(16).Trim(), CultureInfo.InvariantCulture);
            byte[] bytes = new byte[length]; output.ReadExactly(bytes);
            return JsonNode.Parse(bytes)!;
        }
        public void Dispose()
        {
            // Tests may dispose explicitly to inspect stderr before `using` ends.
            if (_disposed) return;
            _disposed = true;
            if (_process is null)
            {
                try
                {
                    Request("shutdown", new JsonObject()); Notify("exit", new JsonObject());
                    Assert.Equal(0, _server!.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(), "In-process LSP exit code.");
                }
                finally { _serverInput!.CompleteWriting(); }
                return;
            }
            try
            {
                Request("shutdown", new JsonObject()); Notify("exit", new JsonObject()); _process.StandardInput.Close();
                if (!_process.WaitForExit(10000)) _process.Kill(true);
                Assert.Equal(0, _process.ExitCode, _errors.GetAwaiter().GetResult());
            }
            finally { if (!_process.HasExited) _process.Kill(true); _process.Dispose(); }
        }
    }
}
