using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Runic.Translations.Build.Tests;

internal static class Rmf2IntegrationTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 payment fixture generates and verifies all supported outputs", PaymentExample);
        runner.Add("RMF2 CLI discovers feature mounts and produces version 4 packs", MountedCli);
        runner.Add("RMF2 CLI project activation emits and verifies the cohesive v5 contract", ActivatedV5Cli);
        runner.Add("RMF2 MSBuild discovers mounted sources and membership", MountedBuild);
        runner.Add("RMF2 migration previews and preserves backups", Migration);
        runner.Add("RMF2 LSP negotiates Unicode positions and returns versioned rename edits", Lsp);
    }
    private const string Project = """{"schemaVersion":1,"catalog":"app","code":{"namespace":"Example","className":"AppText"},"baseLocale":"en","sourceLayout":"rmf2-v1"}""";
    private static void PaymentExample()
    {
        using TemporaryDirectory temporary = new();
        string project = RepositoryPaths.Resolve("specs/translations/examples/rmf2");
        var generate = TestFixture.RunTool(temporary, "generate", "--project", project, "--output", "generated");
        Assert.Equal(0, generate.ExitCode, generate.Combined);
        var verify = TestFixture.RunTool(temporary, "verify", "--project", project, "--output", "generated");
        Assert.Equal(0, verify.ExitCode, verify.Combined);
        string german = File.ReadAllText(temporary.Resolve("generated/checkout.de.locale-v4.json"));
        Assert.Contains("account_heading", german); Assert.Contains("runic:action", german);
    }
    private static void MountedCli()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("translations")); Directory.CreateDirectory(temporary.Resolve("feature"));
        File.WriteAllText(temporary.Resolve("translations/runic.json"), Project.Replace("\"sourceLayout\":\"rmf2-v1\"", "\"sourceLayout\":\"rmf2-v1\",\"sourceRoots\":[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]", StringComparison.Ordinal));
        File.WriteAllText(temporary.Resolve("feature/en.rmf2"), "title = {#strong}Shop{/strong}\n");
        var generated = TestFixture.RunTool(temporary, "generate", "--project", "translations", "--output", "out", "--emit-json", "--emit-esm");
        Assert.Equal(0, generated.ExitCode, generated.Combined);
        string json = File.ReadAllText(temporary.Resolve("out/app.en.locale-v4.json"));
        Assert.Contains("shop_title", json); Assert.Contains("runic:strong", json);
    }
    private static void ActivatedV5Cli()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("translations"));
        string config = Project.Replace("\"sourceLayout\":\"rmf2-v1\"",
            "\"sourceLayout\":\"rmf2-v1\",\"executionProfile\":\"rmf2-execution-v2\"", StringComparison.Ordinal);
        string path = temporary.Resolve("translations/runic.json");
        File.WriteAllText(path, config);
        File.WriteAllText(temporary.Resolve("translations/en.rmf2"), "literal = {1e+2 :number}\n");

        ProcessResult validate = TestFixture.RunTool(temporary, "validate", "--project", "translations");
        Assert.Equal(0, validate.ExitCode, validate.Combined);
        ProcessResult generate = TestFixture.RunTool(temporary, "generate", "--project", "translations", "--output", "out");
        Assert.Equal(0, generate.ExitCode, generate.Combined);
        Assert.True(File.Exists(temporary.Resolve("out/app.en.locale-v5.json")), "v5 locale artifact missing");
        string manifestPath = temporary.Resolve("out/app.esm-v5/web-module-manifest-v3.json");
        Assert.True(File.Exists(manifestPath), "v5 web manifest missing");
        string manifest = File.ReadAllText(manifestPath);
        Assert.Contains("\"esmAbiVersion\":4", manifest);
        Assert.Contains("\"profile\":\"rmf2-execution-v2\"", manifest);
        Assert.False(Directory.EnumerateFiles(temporary.Resolve("out"), "*.locale-v4.json", SearchOption.AllDirectories).Any(), "v5 activation emitted v4 artifacts");
        Assert.True(Directory.EnumerateFiles(temporary.Resolve("out"), "*.g.cs", SearchOption.TopDirectoryOnly).Count() == 4, "v5 typed C# output is incomplete");
        ProcessResult verify = TestFixture.RunTool(temporary, "verify", "--project", "translations", "--output", "out");
        Assert.Equal(0, verify.ExitCode, verify.Combined);
        ProcessResult cpp = TestFixture.RunTool(temporary, "generate", "--project", "translations", "--output", "cpp", "--emit-cpp");
        Assert.Equal(1, cpp.ExitCode, cpp.Combined);
        Assert.Contains("RTR0065", cpp.Combined);
        Assert.False(Directory.Exists(temporary.Resolve("cpp")), "v5 activation emitted C++ output");

        File.WriteAllText(path, config.Replace("rmf2-execution-v2", "future-profile", StringComparison.Ordinal));
        ProcessResult invalid = TestFixture.RunTool(temporary, "validate", "--project", "translations");
        Assert.Equal(1, invalid.ExitCode, invalid.Combined);
        Assert.Contains("RTR0065", invalid.Combined);
    }
    private static void MountedBuild()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("translations")); Directory.CreateDirectory(temporary.Resolve("feature"));
        File.WriteAllText(temporary.Resolve("translations/runic.json"), Project.Replace("\"sourceLayout\":\"rmf2-v1\"", "\"sourceLayout\":\"rmf2-v1\",\"sourceRoots\":[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]", StringComparison.Ordinal));
        File.WriteAllText(temporary.Resolve("feature/en.rmf2"), "title = Shop\n");
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        string targets = RepositoryPaths.Resolve("packages/dotnet/Runic.Translations.Build/build/Runic.Translations.Build.targets");
        File.WriteAllText(temporary.Resolve("Consumer.proj"), $$"""
            <Project><PropertyGroup><Configuration>{{configuration}}</Configuration></PropertyGroup>
            <ItemGroup><TranslationProject Include="translations/runic.json" /></ItemGroup>
            <Import Project="{{targets}}" />
            <Target Name="Dump" DependsOnTargets="_RunicTranslationsDiscoverTranslationSources">
              <WriteLinesToFile File="sources.txt" Lines="@(TranslationMf2)" Overwrite="true" />
            </Target></Project>
            """);
        var first = Processes.DotNet(temporary.Path, "msbuild", "Consumer.proj", "/t:Dump", "/nologo");
        Assert.Equal(0, first.ExitCode, first.Combined); Assert.Contains("feature/en.rmf2", File.ReadAllText(temporary.Resolve("sources.txt")).Replace('\\', '/'));
        File.WriteAllText(temporary.Resolve("feature/de.rmf2"), "title = Laden\n");
        var second = Processes.DotNet(temporary.Path, "msbuild", "Consumer.proj", "/t:Dump", "/nologo");
        Assert.Equal(0, second.ExitCode, second.Combined); Assert.Contains("feature/de.rmf2", File.ReadAllText(temporary.Resolve("sources.txt")).Replace('\\', '/'));
    }
    private static void Migration()
    {
        using TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("runic.json"), Project.Replace("rmf2-v1", "locale-toml", StringComparison.Ordinal));
        const string before = "# original\n[shop]\ntitle='Shop'\n";
        File.WriteAllText(temporary.Resolve("en.toml"), before);
        var preview = TestFixture.RunTool(temporary, "migrate-rmf2", "--project", ".", "--dry-run");
        Assert.Equal(0, preview.ExitCode, preview.Combined); Assert.False(File.Exists(temporary.Resolve("en.rmf2")), "Preview wrote files.");
        var migrate = TestFixture.RunTool(temporary, "migrate-rmf2", "--project", ".");
        Assert.Equal(0, migrate.ExitCode, migrate.Combined);
        Assert.Equal(before, File.ReadAllText(temporary.Resolve("en.toml.bak")));
        Assert.Equal(0, TestFixture.RunTool(temporary, "validate", "--project", ".").ExitCode);
    }
    private static void Lsp()
    {
        foreach (string encoding in new[] { "utf-8", "utf-16", "utf-32" })
        {
            using TemporaryDirectory temporary = new();
            File.WriteAllText(temporary.Resolve("runic.json"), Project);
            File.WriteAllText(temporary.Resolve("en.rmf2"), "x = Hello\n");
            File.WriteAllText(temporary.Resolve("de.rmf2"), "x = Guten Tag\n");
            string uri = new Uri(temporary.Resolve("en.rmf2")).AbsoluteUri;
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = temporary.Path, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(RepositoryPaths.ToolAssembly); start.ArgumentList.Add("lsp");
            using var process = Process.Start(start)!;
            var frames = new List<JsonNode>(); var frameGate = new object();
            var output = Task.Run(() => {
                var stream = process.StandardOutput.BaseStream;
                while (true)
                {
                    var header = new List<byte>(); int value;
                    while ((value = stream.ReadByte()) >= 0) { header.Add((byte)value); if (header.Count >= 4 && header.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break; }
                    if (value < 0) return;
                    int size = int.Parse(Encoding.ASCII.GetString(header.ToArray()).Substring(16).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    byte[] payload = new byte[size]; stream.ReadExactly(payload);
                    lock (frameGate) { frames.Add(JsonNode.Parse(payload)!); System.Threading.Monitor.PulseAll(frameGate); }
                }
            });
            var errors = process.StandardError.ReadToEndAsync();
            void Send(string method, JsonObject args, int? id = null)
            {
                var request = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = args };
                if (id.HasValue) request["id"] = id.Value;
                string json = request.ToJsonString(); process.StandardInput.Write("Content-Length: " + Encoding.UTF8.GetByteCount(json) + "\r\n\r\n" + json); process.StandardInput.Flush();
                if (id is > 0 and < 100)
                {
                    var deadline = Stopwatch.StartNew();
                    lock (frameGate) while (!frames.Any(frame => frame["id"]?.ToString() == id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    {
                        if (deadline.Elapsed.TotalSeconds > 10) throw new TimeoutException("LSP response was not received.");
                        System.Threading.Monitor.Wait(frameGate, 100);
                    }
                }
            }
            JsonObject Document() => new() { ["uri"] = uri };
            Send("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Resolve(".")).AbsoluteUri, ["capabilities"] = new JsonObject { ["general"] = new JsonObject { ["positionEncodings"] = new JsonArray(encoding) } } }, 1);
            var doc = Document(); doc["version"] = 1; doc["text"] = "x = 😀 {unfinished\n";
            Send("textDocument/didOpen", new JsonObject { ["textDocument"] = doc });
            Send("textDocument/documentSymbol", new JsonObject { ["textDocument"] = Document() }, 23);
            doc = Document(); doc["version"] = 2;
            Send("textDocument/didChange", new JsonObject { ["textDocument"] = doc, ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "x = Hello\n" }) });
            Send("workspace/executeCommand", new JsonObject { ["command"] = "runic.preview", ["arguments"] = new JsonArray(uri, "x", "en") }, 20);
            Send("workspace/executeCommand", new JsonObject { ["command"] = "runic.renderPreview", ["arguments"] = new JsonArray(uri, "x", "de", new JsonObject()) }, 28);
            Send("workspace/executeCommand", new JsonObject { ["command"] = "runic.renderPreview", ["arguments"] = new JsonArray(new Uri(RepositoryPaths.Resolve("specs/translations/examples/rmf2/en.rmf2")).AbsoluteUri, "payment", "de", new JsonObject { ["count"] = "1", ["tone"] = "positive" }) }, 29);
            Send("textDocument/rename", new JsonObject { ["textDocument"] = Document(), ["position"] = new JsonObject { ["line"] = 0, ["character"] = 0 }, ["newName"] = "greeting" }, 2);
            doc = Document(); doc["version"] = 3;
            Send("textDocument/didChange", new JsonObject { ["textDocument"] = doc, ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "x = {#link ref=help}Help{/link} {$name} |$literal|\n" }) });
            Send("textDocument/completion", new JsonObject { ["textDocument"] = Document(), ["position"] = new JsonObject { ["line"] = 0, ["character"] = 8 } }, 4);
            Send("textDocument/hover", new JsonObject { ["textDocument"] = Document(), ["position"] = new JsonObject { ["line"] = 0, ["character"] = 7 } }, 5);
            string germanUri = new Uri(temporary.Resolve("de.rmf2")).AbsoluteUri;
            Send("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = germanUri, ["version"] = 1, ["text"] = "x =\n  .input {$name :string}\n  {{Hallo {$name}}}\n" } });
            int inputColumn = "x = {#link ref=help}Help{/link} {$name} |$literal|".IndexOf("$name", StringComparison.Ordinal) + 1;
            Send("textDocument/references", new JsonObject { ["textDocument"] = Document(), ["position"] = new JsonObject { ["line"] = 0, ["character"] = inputColumn }, ["context"] = new JsonObject { ["includeDeclaration"] = true } }, 8);
            Send("textDocument/definition", new JsonObject { ["textDocument"] = Document(), ["position"] = new JsonObject { ["line"] = 0, ["character"] = inputColumn } }, 9);
            doc = Document(); doc["version"] = 4;
            Send("textDocument/didChange", new JsonObject { ["textDocument"] = doc, ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "x =\n  .local $label = {|Help|}\n  {{{$label}}}\n" }) });
            Send("textDocument/definition", new JsonObject { ["textDocument"] = Document(), ["position"] = new JsonObject { ["line"] = 2, ["character"] = 7 } }, 6);
            Send("textDocument/references", new JsonObject { ["textDocument"] = Document(), ["position"] = new JsonObject { ["line"] = 2, ["character"] = 7 }, ["context"] = new JsonObject { ["includeDeclaration"] = false } }, 7);
            Send("textDocument/semanticTokens/full", new JsonObject { ["textDocument"] = Document() }, 21);
            for (int requestId = 100; requestId < 116; requestId++)
                Send("textDocument/completion", new JsonObject { ["textDocument"] = Document(), ["position"] = new JsonObject { ["line"] = 2, ["character"] = 7 } }, requestId);
            for (int requestId = 100; requestId < 116; requestId++)
                Send("$/cancelRequest", new JsonObject { ["id"] = requestId });
            doc = Document(); doc["version"] = 5;
            Send("textDocument/didChange", new JsonObject { ["textDocument"] = doc, ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "x = Hello\n" + string.Join('\n', Enumerable.Range(0, 1000).Select(index => "item_" + index + " = Value")) }) });
            for (int requestId = 200; requestId < 216; requestId++)
                Send("textDocument/semanticTokens/full", new JsonObject { ["textDocument"] = Document() }, requestId);
            doc = Document(); doc["version"] = 6;
            Send("textDocument/didChange", new JsonObject { ["textDocument"] = doc, ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "x = Current\n" }) });
            Send("textDocument/semanticTokens/full", new JsonObject { ["textDocument"] = Document() }, 22);
            Send("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = germanUri } });
            string frenchUri = new Uri(temporary.Resolve("fr.rmf2")).AbsoluteUri;
            Send("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = frenchUri, ["version"] = 1, ["text"] = "x = Bonjour\n" } });
            Send("workspace/executeCommand", new JsonObject { ["command"] = "runic.preview", ["arguments"] = new JsonArray(frenchUri, "x", "fr") }, 24);
            File.WriteAllText(temporary.Resolve("App.cs"), "class App { }\n");
            Send("textDocument/rename", new JsonObject { ["textDocument"] = Document(), ["position"] = new JsonObject { ["line"] = 0, ["character"] = 0 }, ["newName"] = "unsafe" }, 25);
            Send("workspace/executeCommand", new JsonObject { ["command"] = "runic.renameResource", ["arguments"] = new JsonArray(uri, new JsonArray("x"), "intentional") }, 26);
            Send("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = frenchUri } });
            File.WriteAllText(temporary.Resolve("runic.json"), Project[..^1] + """, "markup":{"slots":{"x":{"retry":{"min":1,"max":1}}}}}""");
            File.WriteAllText(temporary.Resolve("de.rmf2"), "x = {#action ref=retry}Retry{/action}\n");
            doc = Document(); doc["version"] = 7;
            Send("textDocument/didChange", new JsonObject { ["textDocument"] = doc, ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "x = {#action ref=retry}Retry{/action}\n" }) });
            Send("workspace/executeCommand", new JsonObject { ["command"] = "runic.renameResource", ["arguments"] = new JsonArray(uri, new JsonArray("x"), "requiresConfig") }, 27);
            Send("shutdown", new JsonObject(), 3); Send("exit", new JsonObject()); process.StandardInput.Close();
            if (!process.WaitForExit(15000)) { process.Kill(true); throw new TimeoutException("LSP did not exit."); }
            Task.WaitAll(output, errors); Assert.Equal(0, process.ExitCode, errors.Result);
            Assert.Contains("Bereit", frames.Single(frame => frame["id"]?.ToString() == "29").ToJsonString());
            Assert.Contains("Guten Tag", frames.Single(frame => frame["id"]?.ToString() == "28").ToJsonString());
            Assert.Contains("Bonjour", frames.Single(frame => frame["id"]?.ToString() == "24")["result"]!.ToJsonString());
            Assert.Contains("Rename refused", frames.Single(frame => frame["id"]?.ToString() == "25")["error"]!["message"]!.ToString());
            Assert.Contains("intentional", frames.Single(frame => frame["id"]?.ToString() == "26")["result"]!.ToJsonString());
            Assert.Contains("does not synchronize", frames.Single(frame => frame["id"]?.ToString() == "27")["error"]!["message"]!.ToString());
            var tokens = frames.Single(frame => frame["id"]?.ToString() == "21")["result"]!["data"]!.AsArray();
            Assert.True(tokens.Count > 0 && tokens.Count % 5 == 0, "Semantic highlighting did not return LSP token tuples.");
            int[] tokenValues = tokens.Select(token => token!.GetValue<int>()).ToArray();
            for (int index = 0; index < tokenValues.Length; index += 5)
            {
                Assert.True(tokenValues[index] >= 0 && tokenValues[index + 1] >= 0, "Semantic token positions must use non-negative integer deltas.");
                Assert.True(tokenValues[index + 2] > 0, "Semantic token lengths must be positive integers.");
                Assert.True(tokenValues[index + 3] is >= 0 and <= 7, "Semantic token types must index the advertised legend.");
                Assert.Equal(0, tokenValues[index + 4], "RMF2 semantic tokens do not advertise modifiers.");
            }
            Assert.Equal(4, frames.Single(n => n["id"]?.ToString() == "20")["result"]!["ast"]!["astVersion"]!.GetValue<int>());
            Assert.Equal(encoding, frames.Single(n => n["id"]?.ToString() == "1")["result"]!["capabilities"]!["positionEncoding"]!.ToString());
            var diagnostic = frames.First(n => n["method"]?.ToString() == "textDocument/publishDiagnostics")["params"]!["diagnostics"]![0]!;
            Assert.Equal(encoding == "utf-8" ? 9 : encoding == "utf-16" ? 7 : 6, diagnostic["range"]!["start"]!["character"]!.GetValue<int>());
            var documentChanges = frames.Single(n => n["id"]?.ToString() == "2")["result"]!["documentChanges"]!.AsArray();
            Assert.True(documentChanges.All(change => change is JsonObject), "Workspace edits must contain JSON objects.");
            var rename = documentChanges.Single(n => n!["textDocument"]?["uri"]?.ToString() == uri)!;
            Assert.Equal(2, rename["textDocument"]!["version"]!.GetValue<int>());
            var textEdits = rename["edits"]!.AsArray();
            Assert.Equal(1, textEdits.Count, "A resource rename should replace each affected document once.");
            Assert.Equal(0, textEdits[0]!["range"]!["start"]!["line"]!.GetValue<int>());
            Assert.Equal(0, textEdits[0]!["range"]!["start"]!["character"]!.GetValue<int>());
            Assert.Contains("greeting = Hello", textEdits[0]!["newText"]!.GetValue<string>());
            var completion = frames.Single(n => n["id"]?.ToString() == "4")["result"]!.AsArray();
            Assert.True(completion.Any(n => n!["label"]!.ToString() == "$name"), "Missing semantic input completion.");
            Assert.True(completion.All(n => n!["label"]!.ToString() != "$literal" && n["label"]!.ToString() != "/br"), "Invalid completion leaked into LSP.");
            Assert.Contains("interactive", frames.Single(n => n["id"]?.ToString() == "5")["result"]!["contents"]!["value"]!.ToString());
            var definition = frames.Single(n => n["id"]?.ToString() == "6")["result"]!.AsArray();
            Assert.Equal(1, definition.Count);
            Assert.Equal(1, definition[0]!["range"]!["start"]!["line"]!.GetValue<int>());
            Assert.Equal(9, definition[0]!["range"]!["start"]!["character"]!.GetValue<int>());
            var references = frames.Single(n => n["id"]?.ToString() == "7")["result"]!.AsArray();
            Assert.Equal(1, references.Count);
            Assert.Equal(2, references[0]!["range"]!["start"]!["line"]!.GetValue<int>());
            var catalogReferences = frames.Single(n => n["id"]?.ToString() == "8")["result"]!.AsArray();
            Assert.Equal(3, catalogReferences.Count);
            Assert.Equal(2, catalogReferences.Count(n => n!["uri"]!.ToString() == germanUri));
            var catalogDefinition = frames.Single(n => n["id"]?.ToString() == "9")["result"]!.AsArray();
            Assert.Equal(1, catalogDefinition.Count);
            Assert.Equal(germanUri, catalogDefinition[0]!["uri"]!.ToString());
            Assert.Equal("6", frames.Single(frame => frame["id"]?.ToString() == "22")["result"]!["resultId"]!.GetValue<string>());
            Assert.True(frames.Any(frame => int.TryParse(frame["id"]?.ToString(), out int id) && id >= 200 && id < 216 && frame["error"]?["code"]?.GetValue<int>() == -32801), "Obsolete pending requests were not rejected.");
            var cancelledBatch = frames.Where(n => int.TryParse(n["id"]?.ToString(), out int value) && value >= 100 && value < 116).ToArray();
            Assert.Equal(16, cancelledBatch.Length);
            Assert.True(cancelledBatch.Any(n => n["error"]?["code"]?.GetValue<int>() == -32800), "Cancellation was not processed while requests were queued.");
        }
    }
}
