using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Threading.Tasks;
using Runic.Translations.Tool;

namespace Runic.Translations.Build.Tests;

internal static class Rmf2IntegrationTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 payment fixture generates and verifies all supported outputs", PaymentExample);
        runner.Add("RMF2 --emit-cpp fails before creating output", CppEmissionIsUnsupported);
        runner.Add("RMF2 CLI discovers feature mounts and produces version 4 packs", MountedCli);
        runner.Add("RMF2 CLI project activation emits and verifies the cohesive v5 contract", ActivatedV5Cli);
        runner.Add("RMF2 v5 validate permits empty scaffolds while generate and verify reject them", EmptyV5CliBoundary);
        runner.Add("RMF2 CLI re-discovers mounted add, change, rename, and delete", MountedCliMembership);
        runner.Add("RMF2 MSBuild discovers mounted sources and membership", MountedBuild);
        runner.Add("RMF2 migration previews and preserves backups", Migration);
        runner.Add("RMF2 LSP negotiates Unicode positions and returns versioned rename edits", Lsp);
        runner.Add("RMF2 LSP rescans watched files and configuration with unsaved overlays", LspWatchRescan);
        runner.Add("RMF2 LSP isolates watched diagnostics by project", LspWatchProjectIsolation);
        runner.Add("RMF2 workspace project indexing is entry-bounded, cancellable, and atomic", ProjectIndexBounds);
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
    private static void CppEmissionIsUnsupported()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("translations"));
        File.WriteAllText(temporary.Resolve("translations/runic.json"), Project);
        File.WriteAllText(temporary.Resolve("translations/en.rmf2"), "hello = Hello\n");
        ProcessResult result = TestFixture.RunTool(temporary, "generate", "--project", "translations", "--output", "generated", "--emit-cpp", "--runic-output", "json");
        Assert.Equal(1, result.ExitCode, result.Combined);
        Assert.Contains("RCLI9013", result.Combined);
        Assert.Contains("--emit-cpp is not supported for RMF2 projects", result.Combined);
        Assert.False(Directory.Exists(temporary.Resolve("generated")), "Unsupported RMF2 output created artifacts.");
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
        Assert.True(File.Exists(temporary.Resolve("out/app.asset-manifest-v1.json")), "v5 asset manifest missing");
        string manifestPath = temporary.Resolve("out/app.esm-v5/web-module-manifest-v3.json");
        Assert.True(File.Exists(manifestPath), "v5 web manifest missing");
        string manifest = File.ReadAllText(manifestPath);
        Assert.Contains("\"esmAbiVersion\":4", manifest);
        Assert.Contains("\"profile\":\"rmf2-execution-v2\"", manifest);
        Assert.False(Directory.EnumerateFiles(temporary.Resolve("out"), "*.locale-v4.json", SearchOption.AllDirectories).Any(), "v5 activation emitted v4 artifacts");
        Assert.False(File.Exists(temporary.Resolve("out/app.translations-v1.d.ts")), "v5 default emitted the v4 TypeScript contract");
        Assert.False(Directory.EnumerateFiles(temporary.Resolve("out"), "app.template-manifest-*.json", SearchOption.TopDirectoryOnly).Any(), "v5 default emitted a v4 template manifest");
        Assert.True(Directory.EnumerateFiles(temporary.Resolve("out"), "*.g.cs", SearchOption.TopDirectoryOnly).Count() == 4, "v5 typed C# output is incomplete");
        ProcessResult verify = TestFixture.RunTool(temporary, "verify", "--project", "translations", "--output", "out");
        Assert.Equal(0, verify.ExitCode, verify.Combined);

        ProcessResult csharp = TestFixture.RunTool(temporary, "generate", "--project", "translations", "--output", "csharp", "--emit-csharp");
        Assert.Equal(0, csharp.ExitCode, csharp.Combined);
        Assert.True(TestFixture.RelativeFiles(temporary.Resolve("csharp")).Length == 4 &&
            TestFixture.RelativeFiles(temporary.Resolve("csharp")).All(static file => file.EndsWith(".g.cs", StringComparison.Ordinal)),
            "--emit-csharp produced another output group");
        ProcessResult json = TestFixture.RunTool(temporary, "generate", "--project", "translations", "--output", "json", "--emit-json");
        Assert.Equal(0, json.ExitCode, json.Combined);
        Assert.Equal("app.asset-manifest-v1.json|app.en.locale-v5.json", string.Join('|', TestFixture.RelativeFiles(temporary.Resolve("json"))));
        ProcessResult esm = TestFixture.RunTool(temporary, "generate", "--project", "translations", "--output", "esm", "--emit-esm");
        Assert.Equal(0, esm.ExitCode, esm.Combined);
        Assert.True(TestFixture.RelativeFiles(temporary.Resolve("esm")).All(static file => file.StartsWith("app.esm-v5/", StringComparison.Ordinal)),
            "--emit-esm produced another output group");
        foreach (string flag in new[] { "--emit-typescript", "--emit-template-manifest", "--emit-cpp" })
        {
            string directory = flag.Substring("--emit-".Length);
            ProcessResult unsupported = TestFixture.RunTool(temporary, "generate", "--project", "translations", "--output", directory, flag);
            Assert.Equal(1, unsupported.ExitCode, unsupported.Combined);
            Assert.Contains("RTR0065", unsupported.Combined);
            Assert.Contains(flag, unsupported.Combined);
            Assert.False(Directory.Exists(temporary.Resolve(directory)), "unsupported v5 selection emitted output");
        }
        ProcessResult mixed = TestFixture.RunTool(temporary, "generate", "--project", "translations", "--output", "mixed", "--emit-json", "--emit-typescript");
        Assert.Equal(1, mixed.ExitCode, mixed.Combined);
        Assert.Contains("RTR0065", mixed.Combined);
        Assert.False(Directory.Exists(temporary.Resolve("mixed")), "mixed supported/unsupported v5 selection emitted partial output");
        ProcessResult unsupportedVerify = TestFixture.RunTool(temporary, "verify", "--project", "translations", "--output", "out", "--emit-template-manifest");
        Assert.Equal(1, unsupportedVerify.ExitCode, unsupportedVerify.Combined);
        Assert.Contains("RTR0065", unsupportedVerify.Combined);

        File.WriteAllText(path, config.Replace("rmf2-execution-v2", "future-profile", StringComparison.Ordinal));
        ProcessResult invalid = TestFixture.RunTool(temporary, "validate", "--project", "translations");
        Assert.Equal(1, invalid.ExitCode, invalid.Combined);
        Assert.Contains("RTR0065", invalid.Combined);
    }
    private static void EmptyV5CliBoundary()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("translations"));
        File.WriteAllText(temporary.Resolve("translations/runic.json"), Project.Replace("\"sourceLayout\":\"rmf2-v1\"",
            "\"sourceLayout\":\"rmf2-v1\",\"executionProfile\":\"rmf2-execution-v2\"", StringComparison.Ordinal));
        ProcessResult validate = TestFixture.RunTool(temporary, "validate", "--project", "translations");
        Assert.Equal(0, validate.ExitCode, validate.Combined);
        foreach ((string Command, string? Flag) in new (string, string?)[]
        {
            ("generate", null), ("generate", "--emit-json"), ("generate", "--emit-esm"), ("verify", null),
        })
        {
            string output = Flag is null ? Command : $"{Command}{Flag.AsSpan("--emit-".Length)}";
            ProcessResult result = Flag is null
                ? TestFixture.RunTool(temporary, Command, "--project", "translations", "--output", output)
                : TestFixture.RunTool(temporary, Command, "--project", "translations", "--output", output, Flag);
            Assert.Equal(1, result.ExitCode, result.Combined);
            Assert.Contains("RTR0009", result.Combined);
            Assert.False(Directory.Exists(temporary.Resolve(output)), "empty v5 project emitted " + output);
        }
    }
    private static void MountedCliMembership()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("translations")); Directory.CreateDirectory(temporary.Resolve("feature"));
        File.WriteAllText(temporary.Resolve("translations/runic.json"), Project.Replace("\"sourceLayout\":\"rmf2-v1\"", "\"sourceLayout\":\"rmf2-v1\",\"sourceRoots\":[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]", StringComparison.Ordinal));
        string english = temporary.Resolve("feature/en.rmf2");
        File.WriteAllText(english, "title = Shop\n");
        Assert.Equal(0, TestFixture.RunTool(temporary, "validate", "--project", "translations").ExitCode);
        ProcessResult fromProjectDirectory = Processes.DotNet(temporary.Resolve("translations"), RepositoryPaths.ToolAssembly, "validate", "--project", ".");
        Assert.Equal(0, fromProjectDirectory.ExitCode, fromProjectDirectory.Combined);
        File.WriteAllText(english, "title = Store\n");
        Assert.Equal(0, TestFixture.RunTool(temporary, "validate", "--project", "translations").ExitCode);
        string german = temporary.Resolve("feature/de.rmf2");
        File.WriteAllText(german, "title = Laden\n");
        Assert.Equal(0, TestFixture.RunTool(temporary, "validate", "--project", "translations").ExitCode);
        string french = temporary.Resolve("feature/fr.rmf2");
        File.Move(german, french);
        Assert.Equal(0, TestFixture.RunTool(temporary, "validate", "--project", "translations").ExitCode);
        File.Delete(french);
        Assert.Equal(0, TestFixture.RunTool(temporary, "validate", "--project", "translations").ExitCode);
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
        File.Delete(temporary.Resolve("feature/de.rmf2"));
        var third = Processes.DotNet(temporary.Path, "msbuild", "Consumer.proj", "/t:Dump", "/nologo");
        Assert.Equal(0, third.ExitCode, third.Combined); Assert.False(File.ReadAllText(temporary.Resolve("sources.txt")).Replace('\\', '/').Contains("feature/de.rmf2", StringComparison.Ordinal), "Deleted mounted source remained in MSBuild discovery.");
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
            int publicationsBeforeWatch;
            lock (frameGate) publicationsBeforeWatch = frames.Count(frame => frame["method"]?.ToString() == "textDocument/publishDiagnostics");
            Send("workspace/didChangeWatchedFiles", new JsonObject {
                ["changes"] = new JsonArray(new JsonObject { ["uri"] = new Uri(temporary.Resolve("runic.json")).AbsoluteUri, ["type"] = 2 }, new JsonObject { ["uri"] = new Uri(temporary.Resolve("de.rmf2")).AbsoluteUri, ["type"] = 2 }),
            });
            Send("workspace/didChangeConfiguration", new JsonObject { ["settings"] = new JsonObject { ["runicTranslations"] = new JsonObject { ["sourceLayout"] = "rmf2-v1" } } });
            var watchDeadline = Stopwatch.StartNew();
            lock (frameGate) while (frames.Count(frame => frame["method"]?.ToString() == "textDocument/publishDiagnostics") <= publicationsBeforeWatch)
            {
                if (watchDeadline.Elapsed.TotalSeconds > 10) throw new TimeoutException("LSP did not rescan after a workspace watch/configuration notification.");
                System.Threading.Monitor.Wait(frameGate, 100);
            }
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

    private static void LspWatchRescan()
    {
        using TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("runic.json"), Project);
        string sourcePath = temporary.Resolve("en.rmf2");
        File.WriteAllText(sourcePath, "title = Disk\n");
        string uri = new Uri(sourcePath).AbsoluteUri;
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
        int Publications()
        {
            lock (frameGate) return frames.Count(frame => frame["method"]?.ToString() == "textDocument/publishDiagnostics");
        }
        JsonObject LatestDiagnostics()
        {
            lock (frameGate) return frames.Last(frame => frame["method"]?.ToString() == "textDocument/publishDiagnostics").AsObject();
        }
        void WaitForPublication(int previous)
        {
            var deadline = Stopwatch.StartNew();
            lock (frameGate) while (frames.Count(frame => frame["method"]?.ToString() == "textDocument/publishDiagnostics") <= previous)
            {
                if (deadline.Elapsed.TotalSeconds > 10) throw new TimeoutException("LSP did not publish diagnostics after a workspace notification.");
                System.Threading.Monitor.Wait(frameGate, 100);
            }
        }

        Send("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Resolve(".")).AbsoluteUri, ["capabilities"] = new JsonObject() }, 1);
        Send("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = 1, ["text"] = "title = {unfinished\n" } });
        // Fence the initial didOpen publication before testing each workspace
        // notification independently; no earlier notification may satisfy the
        // subsequent assertion.
        Send("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } }, 2);
        JsonObject initial = LatestDiagnostics();
        string initialMessage = initial["params"]!["diagnostics"]![0]!["message"]!.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(initialMessage), "The unsaved RMF2 overlay did not publish a diagnostic.");

        File.WriteAllText(sourcePath, "title = Disk is now valid\n");
        int beforeWatched = Publications();
        Send("workspace/didChangeWatchedFiles", new JsonObject { ["changes"] = new JsonArray(new JsonObject { ["uri"] = uri, ["type"] = 2 }) });
        Send("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } }, 3);
        WaitForPublication(beforeWatched);
        JsonObject watched = LatestDiagnostics();
        Assert.Equal(initialMessage, watched["params"]!["diagnostics"]![0]!["message"]!.GetValue<string>());

        int beforeConfiguration = Publications();
        Send("workspace/didChangeConfiguration", new JsonObject { ["settings"] = new JsonObject { ["runicTranslations"] = new JsonObject { ["sourceLayout"] = "rmf2-v1" } } });
        Send("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } }, 4);
        WaitForPublication(beforeConfiguration);
        JsonObject configured = LatestDiagnostics();
        Assert.Equal(initialMessage, configured["params"]!["diagnostics"]![0]!["message"]!.GetValue<string>());

        Send("shutdown", new JsonObject(), 5); Send("exit", new JsonObject()); process.StandardInput.Close();
        if (!process.WaitForExit(15000)) { process.Kill(true); throw new TimeoutException("LSP did not exit."); }
        Task.WaitAll(output, errors); Assert.Equal(0, process.ExitCode, errors.Result);
    }

    private static void LspWatchProjectIsolation()
    {
        using TemporaryDirectory temporary = new();
        using TemporaryDirectory external = new();
        string one = temporary.Resolve("one"), two = temporary.Resolve("two"), zeta = temporary.Resolve("zeta");
        string sharedOne = temporary.Resolve("shared-one"), sharedTwo = temporary.Resolve("shared-two");
        foreach (string directory in new[] { one, two, zeta, sharedOne, sharedTwo }) Directory.CreateDirectory(directory);
        static string Config(string catalog, string className, string sourceRoot, string slot) =>
            """{"schemaVersion":1,"catalog":"$catalog","code":{"namespace":"Example","className":"$class"},"baseLocale":"en","sourceLayout":"rmf2-v1","sourceRoots":[{"path":"../$root","namespace":[]}],"markup":{"slots":{"x":{"$slot":{"min":1,"max":1}}}}}"""
                .Replace("$catalog", catalog, StringComparison.Ordinal)
                .Replace("$class", className, StringComparison.Ordinal)
                .Replace("$root", sourceRoot, StringComparison.Ordinal)
                .Replace("$slot", slot, StringComparison.Ordinal);
        string oneConfig = Config("one", "OneText", "shared-one", "retry");
        string oneRemounted = Config("one", "OneText", "shared-two", "retry");
        string twoConfig = Config("two", "TwoText", "shared-two", "confirm");
        string twoRemounted = Config("two", "TwoText", "shared-one", "confirm");
        File.WriteAllText(Path.Combine(one, "runic.json"), oneConfig);
        File.WriteAllText(Path.Combine(two, "runic.json"), twoConfig);
        // Equal-specificity ownership is deterministic: /one sorts before
        // /zeta and therefore owns shared-one while both manifests are closed.
        File.WriteAllText(Path.Combine(zeta, "runic.json"), Config("zeta", "ZetaText", "shared-one", "zeta"));
        string onePath = Path.Combine(sharedOne, "en.rmf2"), twoPath = Path.Combine(sharedTwo, "en.rmf2");
        File.WriteAllText(onePath, "x = Disk one\n"); File.WriteAllText(twoPath, "x = Disk two\n");
        string oneUri = new Uri(onePath).AbsoluteUri, twoUri = new Uri(twoPath).AbsoluteUri;
        string oneConfigUri = new Uri(Path.Combine(one, "runic.json")).AbsoluteUri;
        string twoConfigUri = new Uri(Path.Combine(two, "runic.json")).AbsoluteUri;
        var linkedCases = new List<(string SourceUri, string? ConfigUri)>();
        string? staleManifest = null;
        if (!OperatingSystem.IsWindows())
        {
            File.WriteAllText(external.Resolve("runic.json"), Config("linked", "LinkedText", ".", "linked"));
            string linked = temporary.Resolve("linked");
            Directory.CreateDirectory(linked);
            File.CreateSymbolicLink(Path.Combine(linked, "runic.json"), external.Resolve("runic.json"));
            string linkedPath = Path.Combine(linked, "en.rmf2");
            File.WriteAllText(linkedPath, "x = Linked\n");
            linkedCases.Add((new Uri(linkedPath).AbsoluteUri, null));

            string dangling = temporary.Resolve("dangling");
            Directory.CreateDirectory(dangling);
            string danglingManifest = Path.Combine(dangling, "runic.json");
            File.CreateSymbolicLink(danglingManifest, external.Resolve("missing.json"));
            string danglingPath = Path.Combine(dangling, "en.rmf2");
            File.WriteAllText(danglingPath, "x = Dangling\n");
            linkedCases.Add((new Uri(danglingPath).AbsoluteUri, new Uri(danglingManifest).AbsoluteUri));

            string stale = temporary.Resolve("stale-linked");
            Directory.CreateDirectory(stale);
            staleManifest = Path.Combine(stale, "runic.json");
            File.WriteAllText(staleManifest, Config("stale", "StaleText", ".", "stale"));
            string stalePath = Path.Combine(stale, "en.rmf2");
            File.WriteAllText(stalePath, "x = Stale\n");
            linkedCases.Add((new Uri(stalePath).AbsoluteUri, null));
        }

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
            if (id.HasValue)
            {
                var deadline = Stopwatch.StartNew();
                lock (frameGate) while (!frames.Any(frame => frame["id"]?.ToString() == id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    if (deadline.Elapsed.TotalSeconds > 10) throw new TimeoutException("LSP response was not received.");
                    System.Threading.Monitor.Wait(frameGate, 100);
                }
            }
        }
        string[] Messages(string uri)
        {
            lock (frameGate)
            {
                JsonNode publication = frames.Last(frame => frame["method"]?.ToString() == "textDocument/publishDiagnostics" &&
                    frame["params"]?["uri"]?.GetValue<string>() == uri);
                return publication["params"]!["diagnostics"]!.AsArray()
                    .Select(diagnostic => diagnostic!["message"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
            }
        }
        void AssertMessages(string uri, string[] expected, string operation)
        {
            Assert.Equal(string.Join('|', expected), string.Join('|', Messages(uri)), operation);
        }

        Send("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Resolve(".")).AbsoluteUri, ["capabilities"] = new JsonObject() }, 1);
        Send("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = oneUri, ["version"] = 1, ["text"] = "x = Unsaved one\n" } });
        Send("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = twoUri, ["version"] = 1, ["text"] = "x = Unsaved two\n" } });
        // A later didOpen revision may intentionally suppress the earlier
        // publication. Establish a complete grouped baseline explicitly.
        Send("workspace/didChangeWatchedFiles", new JsonObject { ["changes"] = new JsonArray() });
        Send("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = twoUri } }, 2);
        string[] initialOne = Messages(oneUri), initialTwo = Messages(twoUri);
        Assert.True(initialOne.Length > 0 && initialTwo.Length > 0, "Both projects must begin with distinct catalog diagnostics.");
        Assert.False(initialOne.SequenceEqual(initialTwo), "The two-project fixture did not produce distinguishable diagnostics.");
        Assert.True(initialOne.Any(message => message.Contains("retry", StringComparison.Ordinal)), "Closed mounted project tie-breaking did not select project one.");
        if (staleManifest is not null)
        {
            File.Delete(staleManifest);
            File.CreateSymbolicLink(staleManifest, external.Resolve("runic.json"));
        }
        for (int index = 0; index < linkedCases.Count; index++)
        {
            var linkedCase = linkedCases[index];
            if (linkedCase.ConfigUri is not null)
                Send("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = linkedCase.ConfigUri, ["version"] = 1, ["text"] = Config("dangling", "DanglingText", ".", "dangling") } });
            Send("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = linkedCase.SourceUri, ["version"] = 1, ["text"] = "x = Unsaved linked\n" } });
            int requestId = 40 + index;
            Send("workspace/executeCommand", new JsonObject { ["command"] = "runic.preview", ["arguments"] = new JsonArray(linkedCase.SourceUri, "x", "en") }, requestId);
            Assert.Contains("No runic.json project", frames.Single(frame => frame["id"]?.ToString() == requestId.ToString(System.Globalization.CultureInfo.InvariantCulture))["error"]!["message"]!.GetValue<string>());
            Send("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = linkedCase.SourceUri } });
            if (linkedCase.ConfigUri is not null)
                Send("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = linkedCase.ConfigUri } });
        }

        // Remove the equal-specificity competitor and rebuild the closed
        // manifest index. Ownership and unsaved overlays remain stable.
        string zetaConfigPath = Path.Combine(zeta, "runic.json");
        File.Delete(zetaConfigPath);
        Send("workspace/didChangeWatchedFiles", new JsonObject { ["changes"] = new JsonArray(
            new JsonObject { ["uri"] = new Uri(zetaConfigPath).AbsoluteUri, ["type"] = 3 }) });
        Send("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = oneUri } }, 3);
        AssertMessages(oneUri, initialOne, "closed manifest reindex changed project-one diagnostics.");
        AssertMessages(twoUri, initialTwo, "closed manifest reindex changed project-two diagnostics.");

        // Changing project one's open config removes shared-one and remounts
        // shared-two. Every open buffer must be regrouped in the same pass.
        Send("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = oneConfigUri, ["version"] = 1, ["text"] = oneConfig } });
        Send("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = oneConfigUri, ["version"] = 2 },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = oneRemounted }) });
        // Do not fence the global config refresh: a later resource-local
        // notification must coalesce and flush the dirty all-project state.
        Send("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = oneUri, ["version"] = 2 },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "x = Unsaved one updated\n" }) });
        Send("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = twoUri } }, 4);
        AssertMessages(oneUri, [], "removed source root retained stale diagnostics.");
        AssertMessages(twoUri, initialOne, "remounted source did not move to project one.");

        // An unsaved project-two remount transfers shared-one to project two.
        Send("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = twoConfigUri, ["version"] = 1, ["text"] = twoRemounted } });
        Send("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = twoUri, ["version"] = 2 },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "x = Unsaved two updated\n" }) });
        Send("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = oneUri } }, 5);
        AssertMessages(oneUri, initialTwo, "unsaved source-root remount did not assign project two.");
        AssertMessages(twoUri, initialOne, "project-one remount lost ownership after project-two config opened.");

        // Closing each config reverts to its disk mapping and must clear or
        // restore diagnostics for every affected source buffer.
        Send("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = twoConfigUri } });
        Send("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = oneUri, ["version"] = 3 },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "x = Unsaved one latest\n" }) });
        Send("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = oneUri } }, 6);
        AssertMessages(oneUri, [], "didClose retained project-two remount diagnostics.");
        AssertMessages(twoUri, initialOne, "didClose disturbed the remaining project-one remount.");
        Send("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = oneConfigUri } });
        Send("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = twoUri, ["version"] = 3 },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = "x = Unsaved two latest\n" }) });
        Send("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = twoUri } }, 7);
        AssertMessages(oneUri, initialOne, "didClose did not restore project-one disk ownership.");
        AssertMessages(twoUri, initialTwo, "didClose did not restore project-two disk ownership.");

        Send("shutdown", new JsonObject(), 8); Send("exit", new JsonObject()); process.StandardInput.Close();
        if (!process.WaitForExit(15000)) { process.Kill(true); throw new TimeoutException("LSP did not exit."); }
        Task.WaitAll(output, errors); Assert.Equal(0, process.ExitCode, errors.Result);
    }

    private static void ProjectIndexBounds()
    {
        using TemporaryDirectory temporary = new();
        string stableProject = temporary.Resolve("middle-stable"), partialProject = temporary.Resolve("aaa-partial");
        string hostile = temporary.Resolve("zzz-hostile");
        foreach (string directory in new[] { stableProject, partialProject, hostile }) Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(stableProject, "runic.json"), Project);
        string partialManifest = Path.Combine(partialProject, "runic.json");
        File.WriteAllText(partialManifest, Project);
        // A flat hostile directory exercises per-entry counting without a deep
        // tree or an unreasonable test fixture. Production always uses the
        // fixed 100,000-entry cap; only this internal scanner test lowers it.
        for (int index = 0; index < 512; index++) File.WriteAllText(Path.Combine(hostile, $"flat-{index:D4}.txt"), "");

        var retainedAfterBound = new SortedSet<string>(StringComparer.Ordinal) { stableProject };
        bool observedPartialBeforeBound = false, bounded = false;
        try
        {
            Rmf2LanguageServer.ReplaceProjectIndex(
                [temporary.Path], retainedAfterBound, 8, new object(),
                entry => observedPartialBeforeBound |= string.Equals(Path.GetFullPath(entry), Path.GetFullPath(partialManifest), StringComparison.Ordinal),
                System.Threading.CancellationToken.None);
        }
        catch (InvalidOperationException error)
        {
            Assert.Contains("bounded entry limit of 8", error.Message);
            bounded = true;
        }
        Assert.True(observedPartialBeforeBound && bounded, "Hostile discovery did not cross the partial-manifest barrier before enforcing the entry bound.");
        Assert.Equal(stableProject, retainedAfterBound.Single(), "Bounded discovery replaced the retained project index.");

        var retainedAfterCancellation = new SortedSet<string>(StringComparer.Ordinal) { stableProject };
        using var cancellation = new System.Threading.CancellationTokenSource();
        bool observedPartialManifest = false, cancelled = false;
        try
        {
            Rmf2LanguageServer.ReplaceProjectIndex(
                [temporary.Path], retainedAfterCancellation, 10_000, new object(),
                entry => {
                    if (!string.Equals(Path.GetFullPath(entry), Path.GetFullPath(partialManifest), StringComparison.Ordinal)) return;
                    observedPartialManifest = true;
                    cancellation.Cancel();
                }, cancellation.Token);
        }
        catch (OperationCanceledException) { cancelled = true; }
        Assert.True(observedPartialManifest && cancelled, "Cancellation barrier did not stop discovery after the partial manifest was observed.");
        Assert.Equal(stableProject, retainedAfterCancellation.Single(), "Cancelled discovery replaced the retained project index.");
    }
}
