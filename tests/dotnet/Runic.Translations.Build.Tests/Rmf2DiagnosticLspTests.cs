using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Runic.Translations.Compiler;

namespace Runic.Translations.Build.Tests;

internal static class Rmf2DiagnosticLspTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("LSP mounted Unicode diagnostic quick fixes share compiler edits and refuse stale resolution", CodeActions);
        runner.Add("LSP adds unsaved project-directory buffers only where source discovery would", SourceRootOverlays);
        runner.Add("LSP reports malformed unsaved source roots as configuration diagnostics", MalformedSourceRoots);
        runner.Add("LSP reports a non-object runic.json as a configuration diagnostic", NonObjectConfiguration);
    }

    private static void CodeActions()
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

    private static void SourceRootOverlays()
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

    private static void MalformedSourceRoots()
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

    private static void NonObjectConfiguration()
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

    private static JsonObject Position(int line, int character) => new() { ["line"] = line, ["character"] = character };

    internal sealed class Session : IDisposable
    {
        private readonly Process _process;
        private readonly Task<string> _errors;
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
        internal void Notify(string method, JsonObject args) => Send(method, args, null);
        internal JsonNode Request(string method, JsonObject args)
        {
            int id = ++_nextId;
            Send(method, args, id);
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
            Stream input = _process.StandardInput.BaseStream;
            input.Write(Encoding.ASCII.GetBytes("Content-Length: " + bytes.Length + "\r\n\r\n")); input.Write(bytes); input.Flush();
        }
        private JsonNode ReadFrame()
        {
            Stream output = _process.StandardOutput.BaseStream;
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
