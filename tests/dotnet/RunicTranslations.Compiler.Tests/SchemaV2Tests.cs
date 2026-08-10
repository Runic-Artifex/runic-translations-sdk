using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using RunicTranslations.Compiler.Generation;

namespace RunicTranslations.Compiler.Tests;

internal static class SchemaV2Tests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("schema v2 lowers simple and plural variant messages for .NET and ESM", LowersVariants);
        runner.Add("schema v2 executes declarations multi-selectors formats relative time and safe markup", StructuredFeatures);
        runner.Add("generated ESM plural selection matches the shared v2 corpus", PluralCorpus);
        runner.Add("schema v2 requires deterministic catch-all coverage", RequiresCatchAll);
        runner.Add("schema v2 permits an explicitly empty default locale document", EmptyCatalog);
    }

    private static void EmptyCatalog()
    {
        const string manifest = """
            { "schemaVersion":2, "catalog":"empty", "code":{"namespace":"Tests","className":"EmptyText"},
              "defaultLocale":"de", "locales":[{"tag":"de"}], "layers":[{"name":"base","priority":0}] }
            """;
        const string document = """
            { "schemaVersion":2, "catalog":"empty", "locale":"de", "layer":"base", "resources":{} }
            """;
        TranslationCompilation compilation = RunicTranslations.Compiler.TranslationCompiler.Compile(
            [CompilerTests.Source("manifest.json", manifest)],
            [CompilerTests.Source("de.json", document)]);
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
        CompiledTextCatalog catalog = Assert.Single(compilation.Catalogs);
        Assert.Equal(0, catalog.CanonicalResources.Count);
        Assert.True(TranslationOutputRenderer.RenderCSharpKeys(catalog).Text.Length > 0, "Empty catalog did not produce C# keys.");
        Assert.True(TranslationOutputRenderer.RenderCSharpAccessors(catalog).Text.Length > 0, "Empty catalog did not produce C# accessors.");
        Assert.True(TranslationOutputRenderer.RenderCSharpCatalogData(catalog).Text.Length > 0, "Empty catalog did not produce C# data.");
        Assert.True(TranslationOutputRenderer.RenderCSharpRegistration(catalog).Text.Length > 0, "Empty catalog did not produce C# registration.");
        Assert.True(TranslationOutputRenderer.RenderEsmModules(catalog).Count > 0, "Empty catalog did not produce its ESM runtime surface.");
        CompileGeneratedCSharp(catalog);
    }

    private static void PluralCorpus()
    {
        const string cardinalManifest = """
            {"schemaVersion":2,"catalog":"cardinal","code":{"namespace":"Tests","className":"CardinalText"},"defaultLocale":"en",
             "locales":[{"tag":"en"},{"tag":"de","fallback":"en"},{"tag":"es","fallback":"en"},{"tag":"fr","fallback":"en"},{"tag":"it","fallback":"en"},{"tag":"nl","fallback":"en"},{"tag":"sv","fallback":"en"},{"tag":"no","fallback":"en"},{"tag":"da","fallback":"en"}],
             "layers":[{"name":"base","priority":0}],"validation":{"translationCompleteness":"allow"}}
            """;
        const string ordinalManifest = """
            {"schemaVersion":2,"catalog":"ordinal","code":{"namespace":"Tests","className":"OrdinalText"},"defaultLocale":"en",
             "locales":[{"tag":"en"}],"layers":[{"name":"base","priority":0}]}
            """;
        const string cardinal = """
            {"schemaVersion":2,"catalog":"cardinal","locale":"en","layer":"base","resources":{"Category":{"$value":{
              "inputs":{"value":{"type":"decimal"}},"selectors":[{"name":"category","input":"value","function":"plural"}],
              "variants":[{"match":{"category":"one"},"value":"one"},{"match":{"category":"*"},"value":"other"}]}}}}
            """;
        const string ordinal = """
            {"schemaVersion":2,"catalog":"ordinal","locale":"en","layer":"base","resources":{"Category":{"$value":{
              "inputs":{"value":{"type":"int64"}},"selectors":[{"name":"category","input":"value","function":"ordinal"}],
              "variants":[{"match":{"category":"one"},"value":"one"},{"match":{"category":"two"},"value":"two"},{"match":{"category":"few"},"value":"few"},{"match":{"category":"*"},"value":"other"}]}}}}
            """;
        var compilation = RunicTranslations.Compiler.TranslationCompiler.Compile(
            [CompilerTests.Source("cardinal-manifest.json", cardinalManifest), CompilerTests.Source("ordinal-manifest.json", ordinalManifest)],
            [CompilerTests.Source("cardinal-en.json", cardinal), CompilerTests.Source("ordinal-en.json", ordinal)]);
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
        string directory = Path.Combine(Path.GetTempPath(), "runic-v2-plural-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (CompiledTextCatalog catalog in compilation.Catalogs)
                foreach (TranslationGeneratedOutput output in TranslationOutputRenderer.RenderEsmModules(catalog))
                {
                    string path = Path.Combine(directory, output.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, output.GetUtf8Bytes());
                }
            File.Copy(Path.Combine(RepositoryPaths.RepositoryRoot, "spec", "corpus", "v2-plural-conformance.json"), Path.Combine(directory, "cases.json"));
            string script = Path.Combine(directory, "test.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { m$Category as cardinal } from "./cardinal.esm/messages.js";
                import { m$Category as ordinal } from "./ordinal.esm/messages.js";
                const corpus = JSON.parse(await readFile(new URL("./cases.json", import.meta.url), "utf8"));
                for (const item of corpus.cases) {
                  const actual = item.ordinal ? ordinal({ value: BigInt(item.value) }) : cardinal({ value: Number(item.value) }, { locale: item.locale });
                  if (actual !== item.expected) throw new Error(`${item.locale}/${item.value}: expected ${item.expected}; actual ${actual}`);
                }
                """, new UTF8Encoding(false));
            var start = new ProcessStartInfo("node", script) { RedirectStandardError = true, UseShellExecute = false };
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js.");
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode, error);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void StructuredFeatures()
    {
        const string manifest = """
            { "schemaVersion": 2, "catalog": "features", "code": { "namespace": "Tests", "className": "FeatureText" },
              "defaultLocale": "en", "locales": [{"tag":"en"}], "layers": [{"name":"base","priority":0}] }
            """;
        const string english = """
            { "schemaVersion":2, "catalog":"features", "locale":"en", "layer":"base", "resources": {
              "Dashboard": { "Summary": { "$value": {
                "inputs": { "count":{"type":"int64"}, "delta":{"type":"decimal"}, "owner":{"type":"string"} },
                "declarations": [
                  {"name":"groupedCount","input":"count","function":"integer","format":"grouped"},
                  {"name":"relativeDelta","input":"delta","function":"relativeTime","unit":"day","numeric":"auto"}
                ],
                "selectors": [
                  {"name":"quantity","input":"count","function":"plural"},
                  {"name":"ownerKind","input":"owner","function":"literal"}
                ],
                "variants": [
                  {"match":{"quantity":"one","ownerKind":"admin"},"value":["Exactly ",{"local":"groupedCount"}]},
                  {"match":{"quantity":"*","ownerKind":"*"},"value":[
                    {"markup":{"name":"strong","attributes":{"tone":"critical"},"children":[{"local":"groupedCount"}," items for ",{"input":"owner"}]}},
                    ", ", {"local":"relativeDelta"}
                  ]}
                ]
              } } }
            } }
            """;
        var compilation = RunicTranslations.Compiler.TranslationCompiler.Compile(
            [CompilerTests.Source("manifest.json", manifest)], [CompilerTests.Source("en.json", english)]);
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
        CompiledTextCatalog catalog = Assert.Single(compilation.Catalogs);
        TranslationGeneratedOutput accessors = TranslationOutputRenderer.RenderCSharpAccessors(catalog);
        TranslationGeneratedOutput data = TranslationOutputRenderer.RenderCSharpCatalogData(catalog);
        Assert.True(accessors.Text.Contains("LocalizedTextContent", StringComparison.Ordinal), "Structured C# accessor did not expose the safe result type.");
        Assert.True(data.Text.Contains("CompiledTextMessageNodeKind.RelativeTime", StringComparison.Ordinal), "C# data omitted relative-time AST.");
        Assert.True(data.Text.Contains("CompiledTextMessageNodeKind.MarkupStart", StringComparison.Ordinal), "C# data omitted markup AST.");
        CompileGeneratedCSharp(catalog);

        IReadOnlyList<TranslationGeneratedOutput> esm = TranslationOutputRenderer.RenderEsmModules(catalog);
        string directory = Path.Combine(Path.GetTempPath(), "runic-v2-features-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (TranslationGeneratedOutput output in esm)
            {
                string path = Path.Combine(directory, output.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, output.GetUtf8Bytes());
            }
            TranslationGeneratedOutput localeArtifact = TranslationOutputRenderer.RenderLocaleJson(catalog, "en");
            Assert.True(localeArtifact.RelativePath.EndsWith("locale-v2.json", StringComparison.Ordinal), "Schema v2 was emitted as a v1 locale artifact.");
            File.WriteAllBytes(Path.Combine(directory, "artifact.json"), localeArtifact.GetUtf8Bytes());
            string script = Path.Combine(directory, "test.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { m$Dashboard$Summary } from "./features.esm/messages.js";
                import { decodeLocaleArtifact, formatDynamicMessage } from "./features.esm/dynamic.js";
                const exact = m$Dashboard$Summary({ count: 1n, delta: -1, owner: "admin" });
                if (exact.kind !== "localized-content" || exact.nodes.map(node => node.value).join("") !== "Exactly 1") throw new Error("multi-selector exact variant failed");
                const content = m$Dashboard$Summary({ count: 1234n, delta: -1, owner: "guest" });
                if (content.kind !== "localized-content" || content.nodes.length !== 3) throw new Error("structured result failed");
                const strong = content.nodes[0];
                if (strong.kind !== "element" || strong.name !== "strong" || strong.attributes.tone !== "critical") throw new Error("semantic markup failed");
                if (strong.children.map(node => node.value).join("") !== "1,234 items for guest") throw new Error("local declaration failed");
                if (content.nodes[2].value !== "yesterday") throw new Error("relative time failed");
                if (JSON.stringify(content).includes("<strong")) throw new Error("markup became trusted HTML");
                const artifact = JSON.parse(await readFile(new URL("./artifact.json", import.meta.url), "utf8"));
                if (decodeLocaleArtifact({ ...artifact, artifactVersion: 1 }).ok) throw new Error("dynamic version skew accepted");
                if (decodeLocaleArtifact({ ...artifact, contractFingerprint: "sha256:" + "0".repeat(64) }).ok) throw new Error("dynamic fingerprint skew accepted");
                const hostile = structuredClone(artifact);
                hostile.messages["Dashboard.Summary"].variants[0].nodes.push({ kind: "markup", name: "script", attributes: {}, children: "not-an-array" });
                if (decodeLocaleArtifact(hostile).ok) throw new Error("hostile dynamic node accepted");
                const decoded = decodeLocaleArtifact(artifact);
                if (!decoded.ok) throw new Error(`dynamic artifact rejected: ${decoded.reason}`);
                const dynamic = formatDynamicMessage(decoded.value, "Dashboard.Summary", { count: 1234n, delta: -1, owner: "guest" });
                if (JSON.stringify(dynamic) !== JSON.stringify(content)) throw new Error("compiled and dynamic modes diverged");
                """, new UTF8Encoding(false));
            var start = new ProcessStartInfo("node", script) { RedirectStandardError = true, UseShellExecute = false };
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js.");
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode, error);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void CompileGeneratedCSharp(CompiledTextCatalog catalog)
    {
        string directory = Path.Combine(Path.GetTempPath(), "runic-v2-csharp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (TranslationGeneratedOutput output in new[]
            {
                TranslationOutputRenderer.RenderCSharpKeys(catalog),
                TranslationOutputRenderer.RenderCSharpAccessors(catalog),
                TranslationOutputRenderer.RenderCSharpCatalogData(catalog),
                TranslationOutputRenderer.RenderCSharpRegistration(catalog),
            }) File.WriteAllBytes(Path.Combine(directory, output.RelativePath), output.GetUtf8Bytes());
            string runtimeProject = Path.Combine(RepositoryPaths.RepositoryRoot, "dotnet", "src", "RunicTranslations", "RunicTranslations.csproj");
            string project = Path.Combine(directory, "Generated.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><LangVersion>14.0</LangVersion><ImplicitUsings>disable</ImplicitUsings><GenerateDocumentationFile>false</GenerateDocumentationFile></PropertyGroup><ItemGroup><ProjectReference Include=\"" + runtimeProject + "\" /></ItemGroup></Project>", new UTF8Encoding(false));
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = directory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (string argument in new[] { "build", project, "-c", "Release", "--nologo" }) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
            string buildOutput = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode, buildOutput);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void LowersVariants()
    {
        var compilation = Compile(includeCatchAll: true);
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
        var catalog = Assert.Single(compilation.Catalogs);
        Assert.Equal(2, catalog.SchemaVersion);
        Assert.Equal(2, catalog.MessageGrammarVersion);
        TranslationGeneratedOutput accessors = TranslationOutputRenderer.RenderCSharpAccessors(catalog);
        TranslationGeneratedOutput catalogData = TranslationOutputRenderer.RenderCSharpCatalogData(catalog);
        Assert.True(catalogData.Text.Contains("CompiledTextMessageSelectorKind.CardinalPlural", StringComparison.Ordinal), "C# catalog data did not carry the v2 selector AST.");
        Assert.True(!accessors.Text.Contains("TextPatternFormatter.Format", StringComparison.Ordinal), "Generated accessors still parse selected pattern strings.");

        IReadOnlyList<TranslationGeneratedOutput> esm = TranslationOutputRenderer.RenderEsmModules(catalog);
        string directory = Path.Combine(Path.GetTempPath(), "runic-v2-esm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (TranslationGeneratedOutput output in esm)
            {
                string path = Path.Combine(directory, output.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, output.GetUtf8Bytes());
            }
            string script = Path.Combine(directory, "test.mjs");
            File.WriteAllText(script, """
                import { m$Files$Deleted } from "./v2.esm/messages.js";
                if (m$Files$Deleted({ count: 1n }) !== "One file") throw new Error("cardinal one failed");
                if (m$Files$Deleted({ count: 3n }) !== "3 files") throw new Error("catch-all failed");
                if (m$Files$Deleted({ count: 1n }, { locale: "de" }) !== "Eine Datei") throw new Error("localized variant failed");
                """, new UTF8Encoding(false));
            var start = new ProcessStartInfo("node", script) { RedirectStandardError = true, UseShellExecute = false };
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js.");
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode, error);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void RequiresCatchAll()
    {
        var compilation = Compile(includeCatchAll: false);
        Assert.True(!compilation.Success, "A selector without catch-all coverage was accepted.");
        bool found = false;
        foreach (var diagnostic in compilation.Diagnostics) found |= diagnostic.Id == "RTR0030" && diagnostic.Message.Contains("catch-all", StringComparison.Ordinal);
        Assert.True(found, CompilerTests.DiagnosticsText(compilation.Diagnostics));
    }

    private static RunicTranslations.Compiler.TranslationCompilation Compile(bool includeCatchAll)
    {
        const string manifest = """
            { "schemaVersion": 2, "catalog": "v2", "code": { "namespace": "Tests", "className": "V2Text" },
              "defaultLocale": "en", "locales": [{"tag":"en"},{"tag":"de","fallback":"en"}],
              "layers": [{"name":"base","priority":0}] }
            """;
        string otherEn = includeCatchAll ? ", { " + "\"match\":{\"quantity\":\"*\"},\"value\":\"{count} files\"}" : string.Empty;
        string otherDe = includeCatchAll ? ", { " + "\"match\":{\"quantity\":\"*\"},\"value\":\"{count} Dateien\"}" : string.Empty;
        string english = "{\"schemaVersion\":2,\"catalog\":\"v2\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"Files\":{\"Deleted\":{\"$value\":{\"inputs\":{\"count\":{\"type\":\"int64\"}},\"selectors\":[{\"name\":\"quantity\",\"input\":\"count\",\"function\":\"plural\"}],\"variants\":[{\"match\":{\"quantity\":\"one\"},\"value\":\"One file\"}" + otherEn + "]}}}}}";
        string german = "{\"schemaVersion\":2,\"catalog\":\"v2\",\"locale\":\"de\",\"layer\":\"base\",\"resources\":{\"Files\":{\"Deleted\":{\"$value\":{\"inputs\":{\"count\":{\"type\":\"int64\"}},\"selectors\":[{\"name\":\"quantity\",\"input\":\"count\",\"function\":\"plural\"}],\"variants\":[{\"match\":{\"quantity\":\"one\"},\"value\":\"Eine Datei\"}" + otherDe + "]}}}}}";
        return RunicTranslations.Compiler.TranslationCompiler.Compile(
            [CompilerTests.Source("manifest.json", manifest)],
            [CompilerTests.Source("en.json", english), CompilerTests.Source("de.json", german)]);
    }
}
