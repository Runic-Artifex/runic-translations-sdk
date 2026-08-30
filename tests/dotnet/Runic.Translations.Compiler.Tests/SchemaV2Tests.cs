using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Compiler.Tests;

internal static class SchemaV2Tests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("schema v2 lowers simple and plural variant messages for .NET and ESM", LowersVariants);
        runner.Add("schema v2 executes declarations multi-selectors formats relative time and safe markup", StructuredFeatures);
        runner.Add("generated ESM plural selection matches the shared v2 corpus", PluralCorpus);
        runner.Add("generated ESM locale-pack decoder matches the shared rejection corpus", LocalePackRejectionParity);
        runner.Add("schema v2 requires deterministic catch-all coverage", RequiresCatchAll);
        runner.Add("schema v2 permits an explicitly empty default locale document", EmptyCatalog);
        runner.Add("schema v3 MF2 adapter rejects unsupported local format operands", V3AdapterRejectsLocalFormatOperand);
        runner.Add("schema v3 MF2 adapter rejects unknown members", V3AdapterRejectsUnknownMember);
        runner.Add("schema v3 MF2 adapter enforces required input and profile bounds", V3AdapterEnforcesInputProfile);
        runner.Add("schema v3 MF2 adapter permits the schema's optional canonical URI", V3AdapterPermitsOptionalSchemaUri);
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
        TranslationCompilation compilation = Runic.Translations.Compiler.TranslationCompiler.Compile(
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
             "locales":[{"tag":"en"},{"tag":"de","fallback":"en"},{"tag":"es","fallback":"en"},{"tag":"fr","fallback":"en"},{"tag":"it","fallback":"en"},{"tag":"nl","fallback":"en"},{"tag":"sv","fallback":"en"},{"tag":"no","fallback":"en"},{"tag":"da","fallback":"en"}],
             "layers":[{"name":"base","priority":0}],"validation":{"translationCompleteness":"allow"}}
            """;
        const string cardinal = """
            {"schemaVersion":2,"catalog":"cardinal","locale":"en","layer":"base","resources":{"Category":{"$value":{
              "inputs":{"value":{"type":"decimal"}},"selectors":[{"name":"category","input":"value","function":"plural"}],
              "variants":[{"match":{"category":"one"},"value":"one"},{"match":{"category":"many"},"value":"many"},{"match":{"category":"*"},"value":"other"}]}}}}
            """;
        const string ordinal = """
            {"schemaVersion":2,"catalog":"ordinal","locale":"en","layer":"base","resources":{"Category":{"$value":{
              "inputs":{"value":{"type":"int64"}},"selectors":[{"name":"category","input":"value","function":"ordinal"}],
              "variants":[{"match":{"category":"one"},"value":"one"},{"match":{"category":"two"},"value":"two"},{"match":{"category":"few"},"value":"few"},{"match":{"category":"many"},"value":"many"},{"match":{"category":"*"},"value":"other"}]}}}}
            """;
        var compilation = Runic.Translations.Compiler.TranslationCompiler.Compile(
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
                import { m as cardinalMessages } from "./cardinal.esm/messages.js";
                import { m as ordinalMessages } from "./ordinal.esm/messages.js";
                const cardinal = cardinalMessages.Category;
                const ordinal = ordinalMessages.Category;
                const corpus = JSON.parse(await readFile(new URL("./cases.json", import.meta.url), "utf8"));
                for (const item of corpus.cases) {
                  const actual = item.ordinal
                    ? ordinal({ value: BigInt(item.value) }, { locale: item.locale })
                    : cardinal({ value: item.integer ? BigInt(item.value) : Number(item.value) }, { locale: item.locale });
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

    private static void LocalePackRejectionParity()
    {
        const string manifest = """
            { "schemaVersion": 2, "catalog": "parity", "code": { "namespace": "Tests", "className": "ParityText" },
              "defaultLocale": "en-US", "locales": [{"tag":"en-US"}], "layers": [{"name":"base","priority":0}] }
            """;
        const string document = """
            { "schemaVersion":2, "catalog":"parity", "locale":"en-US", "layer":"base", "resources":{"Parity":{"Message":{"$value":{
              "inputs":{"name":{"type":"string"}},
              "selectors":[{"name":"who","input":"name","function":"literal"}],
              "variants":[{"match":{"who":"*"},"value":["Hello ",{"input":"name"}]}] } } } } }
            """;
        var compilation = Runic.Translations.Compiler.TranslationCompiler.Compile(
            [CompilerTests.Source("manifest.json", manifest)], [CompilerTests.Source("en-US.json", document)]);
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
        CompiledTextCatalog catalog = Assert.Single(compilation.Catalogs);
        IReadOnlyList<TranslationGeneratedOutput> esm = TranslationOutputRenderer.RenderEsmModules(catalog);
        string directory = Path.Combine(Path.GetTempPath(), "runic-v2-rejections-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (TranslationGeneratedOutput output in esm)
            {
                string path = Path.Combine(directory, output.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, output.GetUtf8Bytes());
            }
            File.Copy(Path.Combine(RepositoryPaths.RepositoryRoot, "spec", "corpus", "locale-pack-v2-parity.json"), Path.Combine(directory, "locale-pack-v2-parity.json"));
            string identity = System.Text.Json.JsonSerializer.Serialize(
                new { catalog = catalog.Id, fingerprint = catalog.Fingerprint, locale = "en-US" });
            string script = Path.Combine(directory, "test.mjs");
            string source = """
                import { readFile } from "node:fs/promises";
                import { decodeLocalePackV2 } from "./parity.esm/dynamic.js";
                const identity = __IDENTITY__;
                const bindings = new Map(Object.entries({
                  "%VERSION%": "2",
                  "%GRAMMAR%": "2",
                  "%BAD_VERSION%": "3",
                  "%BAD_GRAMMAR%": "1",
                  "%CATALOG%": identity.catalog,
                  "%LOCALE%": "en-US",
                  "%FINGERPRINT%": identity.fingerprint,
                  "%OTHER_CATALOG%": "other-catalog",
                  "%OTHER_LOCALE%": "de-DE",
                  "%OTHER_FINGERPRINT%": "sha256:" + "f".repeat(64),
                }));
                const encoder = new TextEncoder();
                const bind = template => { let text = template; for (const [token, value] of bindings) text = text.replaceAll(token, value); return text; };
                const corpus = JSON.parse(await readFile(new URL("./locale-pack-v2-parity.json", import.meta.url), "utf8"));
                if (corpus.corpusVersion !== 1 || corpus.contract !== "locale-pack-v2") throw new Error("locale-pack parity corpus identity changed");
                const limits = corpus.limits;
                if (limits.maximumDocumentBytes !== 8388608 || limits.maximumDepth !== 64 || limits.maximumMessages !== 50000 ||
                    limits.maximumPatternBytes !== 65536 || limits.maximumArgumentsPerMessage !== 32) throw new Error("locale-pack parity bounds changed");
                if (!Array.isArray(corpus.rejectionParity) || corpus.rejectionParity.length < 12) throw new Error("locale-pack rejection parity corpus lost cases");
                for (const entry of corpus.rejectionParity) {
                  let bytes = encoder.encode(bind(entry.template));
                  if (entry.truncateFromEnd !== undefined) bytes = bytes.subarray(0, bytes.length - entry.truncateFromEnd);
                  if (entry.padTo !== undefined) { const padded = new Uint8Array(entry.padTo); padded.set(bytes); padded.fill(32, bytes.length); bytes = padded; }
                  const expectedLocale = entry.expectedLocale === undefined ? identity.locale : bind(entry.expectedLocale);
                  const verifier = entry.verifier === "reject" ? () => false : undefined;
                  const result = await decodeLocalePackV2(bytes, expectedLocale, verifier);
                  if (entry.expected === "accepted") { if (!result.ok) throw new Error(`${entry.id}: expected acceptance; actual ${result.reason}`); continue; }
                  if (result.ok || result.reason !== entry.expected) throw new Error(`${entry.id}: expected ${entry.expected}; actual ${result.ok ? "accepted" : result.reason}`);
                }
                """;
            File.WriteAllText(script, source.Replace("__IDENTITY__", identity, StringComparison.Ordinal), new UTF8Encoding(false));
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
              "defaultLocale": "en", "locales": [{"tag":"en"},{"tag":"de","fallback":"en"},{"tag":"es","fallback":"en"},{"tag":"fr","fallback":"en"},{"tag":"it","fallback":"en"}],
              "layers": [{"name":"base","priority":0}], "validation":{"translationCompleteness":"allow"} }
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
        var compilation = Runic.Translations.Compiler.TranslationCompiler.Compile(
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
            File.Copy(Path.Combine(RepositoryPaths.RepositoryRoot, "spec", "corpus", "v2-relative-time-conformance.json"), Path.Combine(directory, "relative-cases.json"));
            File.Copy(Path.Combine(RepositoryPaths.RepositoryRoot, "spec", "corpus", "locale-pack-v2-parity.json"), Path.Combine(directory, "locale-pack-v2-parity.json"));
            string script = Path.Combine(directory, "test.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { m } from "./features.esm/messages.js";
                import { formatRelativeTime } from "./features.esm/runtime.js";
                import { decodeLocaleArtifact, decodeLocalePackV2, formatDynamicMessage } from "./features.esm/dynamic.js";
                const exact = m["Dashboard.Summary"]({ count: 1n, delta: -1, owner: "admin" });
                if (exact.kind !== "localized-content" || exact.nodes.map(node => node.value).join("") !== "Exactly 1") throw new Error("multi-selector exact variant failed");
                const content = m["Dashboard.Summary"]({ count: 1234n, delta: -1, owner: "guest" });
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
                const bytes = new TextEncoder().encode(JSON.stringify(artifact));
                const byteDecoded = await decodeLocalePackV2(bytes, "en", verified => { verified[0] = 0; return true; });
                if (!byteDecoded.ok) throw new Error(`bytes-first artifact rejected: ${byteDecoded.reason}`);
                const integrityRejected = await decodeLocalePackV2(bytes, "en", () => false);
                if (integrityRejected.ok || integrityRejected.reason !== "RTR0023/integrity-rejected") throw new Error("bytes-first integrity rejection diverged");
                const localeRejected = await decodeLocalePackV2(bytes, "de", () => true);
                if (localeRejected.ok || localeRejected.reason !== "RTR0023/locale-mismatch") throw new Error("bytes-first locale rejection diverged");
                const duplicateBytes = new TextEncoder().encode(JSON.stringify(artifact).replace("{\"artifactVersion\":2", "{\"artifactVersion\":2,\"artifactVersion\":2"));
                const duplicateRejected = await decodeLocalePackV2(duplicateBytes, "en");
                if (duplicateRejected.ok || duplicateRejected.reason !== "RTR0023/malformed") throw new Error("bytes-first duplicate property accepted");
                const unknownRoot = decodeLocaleArtifact({ ...artifact, future: true });
                if (unknownRoot.ok || unknownRoot.reason !== "RTR0023/unknown-member") throw new Error("dynamic unknown root member diverged");
                const inheritedName = structuredClone(artifact); inheritedName.messages.toString = structuredClone(artifact.messages["Dashboard.Summary"]);
                const inheritedRejected = decodeLocaleArtifact(inheritedName);
                if (inheritedRejected.ok || inheritedRejected.reason !== "RTR0023/unknown-key") throw new Error("inherited contract name bypassed own-property validation");
                const packCorpus = JSON.parse(await readFile(new URL("./locale-pack-v2-parity.json", import.meta.url), "utf8"));
                if (packCorpus.limits.maximumDocumentBytes !== 8388608 || packCorpus.limits.maximumDepth !== 64) throw new Error("locale-pack parity bounds changed");
                if (![integrityRejected, localeRejected, duplicateRejected, unknownRoot, inheritedRejected].every(result => packCorpus.rejections.some(item => item.id === result.reason))) throw new Error("locale-pack rejection IDs diverged from corpus");
                const astCase = (name, mutate) => { const candidate = structuredClone(artifact); mutate(candidate.messages["Dashboard.Summary"]); const result = decodeLocaleArtifact(candidate); const expected = packCorpus.astParity.find(item => item.mutation === name)?.id; if (result.ok || result.reason !== expected) throw new Error(`${name}: expected ${expected}; actual ${result.reason}`); };
                astCase("descriptorDrift", message => { message.inputs.count.type = "string"; });
                astCase("formatDescriptorDrift", message => { message.variants[0].nodes = [{ kind: "format", input: "count", function: "number", format: "plain" }]; });
                astCase("unknownMessageMember", message => { message.future = true; });
                astCase("unknownInput", message => { message.variants[0].nodes = [{ kind: "input", input: "missing" }]; });
                astCase("selectorShape", message => { message.selectors = [{ name: "quantity", input: "count" }]; });
                astCase("selectorLimit", message => { message.selectors = Array.from({ length: 17 }, (_, index) => ({ name: `s${index}`, input: "count", function: "plural" })); });
                astCase("variantShape", message => { message.variants[0].matches = {}; });
                astCase("variantLimit", message => { message.variants = Array.from({ length: 257 }, () => structuredClone(message.variants[0])); });
                astCase("nodeShape", message => { message.variants[0].nodes = [{ kind: "script" }]; });
                astCase("nodeLimit", message => { message.variants[0].nodes = Array.from({ length: 4097 }, () => ({ kind: "text", value: "x" })); });
                astCase("textLimit", message => { message.variants[0].nodes = [{ kind: "text", value: "x".repeat(65537) }]; });
                astCase("markupDepth", message => { let node = { kind: "text", value: "x" }; for (let depth = 0; depth < 17; depth++) node = { kind: "markup", name: "m", attributes: {}, children: [node] }; message.variants[0].nodes = [node]; });
                const dynamic = formatDynamicMessage(decoded.value, "Dashboard.Summary", { count: 1234n, delta: -1, owner: "guest" });
                if (JSON.stringify(dynamic) !== JSON.stringify(content)) throw new Error("compiled and dynamic modes diverged");
                const relativeCorpus = JSON.parse(await readFile(new URL("./relative-cases.json", import.meta.url), "utf8"));
                for (const item of relativeCorpus.cases) {
                  const actual = formatRelativeTime({ value: Number(item.value) }, "value", item.unit, item.numeric, item.locale);
                  if (actual !== item.expected) throw new Error(`${item.locale}/${item.value}/${item.unit}: expected ${item.expected}; actual ${actual}`);
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
            string runtimeProject = Path.Combine(RepositoryPaths.RepositoryRoot, "dotnet", "src", "Runic.Translations", "Runic.Translations.csproj");
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
                import { m } from "./v2.esm/messages.js";
                if (m["Files.Deleted"]({ count: 1n }) !== "One file") throw new Error("cardinal one failed");
                if (m["Files.Deleted"]({ count: 3n }) !== "3 files") throw new Error("catch-all failed");
                if (m["Files.Deleted"]({ count: 1n }, { locale: "de" }) !== "Eine Datei") throw new Error("localized variant failed");
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

    private static void V3AdapterRejectsLocalFormatOperand()
    {
        const string manifest = """{"schemaVersion":2,"catalog":"v3reject","code":{"namespace":"Tests","className":"V3RejectText"},"defaultLocale":"en","locales":[{"tag":"en"}],"layers":[{"name":"base","priority":0}]}""";
        const string document = """
            {"$schema":"https://runic-artifex.eu/schemas/translations/resources-v3.schema.json","schemaVersion":3,"catalog":"v3reject","locale":"en","layer":"base","resources":{
              "Value":{"$value":{"mf2":{"profile":"runic-mf2-subset/1","ast":{
                "astVersion":3,"profile":"runic-mf2-subset/1","inputs":{},"declarations":[],"selectors":[],
                "variants":[{"matches":{},"pattern":[{"kind":"format","function":"string","operand":{"kind":"local","name":"unknown"},"options":{}}]}]
              }}}}
            }}
            """;
        TranslationCompilation compilation = Runic.Translations.Compiler.TranslationCompiler.Compile([CompilerTests.Source("manifest.json", manifest)], [CompilerTests.Source("v3.json", document)]);
        Assert.True(!compilation.Success, "The v3 adapter accepted an unsupported local format operand.");
        bool rejected = false;
        foreach (TranslationDiagnostic diagnostic in compilation.Diagnostics) rejected |= diagnostic.Id == "RTR0030";
        Assert.True(rejected, CompilerTests.DiagnosticsText(compilation.Diagnostics));
    }

    private static void V3AdapterRejectsUnknownMember()
    {
        TranslationCompilation compilation = CompileV3("""{"astVersion":3,"profile":"runic-mf2-subset/1","inputs":{},"declarations":[],"selectors":[],"variants":[{"matches":{},"pattern":[{"kind":"text","value":"ok","extension":true}]}]}""");
        Assert.True(!compilation.Success, "The v3 adapter accepted an unknown node member.");
        bool rejected = false;
        foreach (TranslationDiagnostic diagnostic in compilation.Diagnostics) rejected |= diagnostic.Id == "RTR0019";
        Assert.True(rejected, CompilerTests.DiagnosticsText(compilation.Diagnostics));
    }

    private static void V3AdapterEnforcesInputProfile()
    {
        TranslationCompilation missingFormat = CompileV3("""{"astVersion":3,"profile":"runic-mf2-subset/1","inputs":{"name":{"type":"string"}},"declarations":[],"selectors":[],"variants":[{"matches":{},"pattern":[{"kind":"text","value":"ok"}]}]}""");
        Assert.True(!missingFormat.Success, "The v3 adapter defaulted a required input format.");
        Assert.True(missingFormat.Diagnostics.Count != 0 && missingFormat.Diagnostics[0].Id == "RTR0019", CompilerTests.DiagnosticsText(missingFormat.Diagnostics));

        var inputs = new StringBuilder();
        for (int index = 0; index < 33; index++)
        {
            if (index != 0) inputs.Append(',');
            inputs.Append("\"value").Append(index).Append("\":{\"type\":\"string\",\"format\":\"none\"}");
        }
        TranslationCompilation overProfile = CompileV3("{\"astVersion\":3,\"profile\":\"runic-mf2-subset/1\",\"inputs\":{" + inputs + "},\"declarations\":[],\"selectors\":[],\"variants\":[{\"matches\":{},\"pattern\":[{\"kind\":\"text\",\"value\":\"ok\"}]}]}");
        bool profileLimit = false;
        foreach (TranslationDiagnostic diagnostic in overProfile.Diagnostics) profileLimit |= diagnostic.Id == "RTR0022";
        Assert.True(!overProfile.Success && profileLimit, CompilerTests.DiagnosticsText(overProfile.Diagnostics));

        TranslationCompilation relativeTimeFormat = CompileV3("""{"astVersion":3,"profile":"runic-mf2-subset/1","inputs":{"delta":{"type":"decimal","format":"plain"}},"declarations":[],"selectors":[],"variants":[{"matches":{},"pattern":[{"kind":"format","function":"relativeTime","operand":{"kind":"input","name":"delta"},"options":{"format":"hostile","unit":"day","numeric":"auto"}}]}]}""");
        bool rejectedFormat = false;
        foreach (TranslationDiagnostic diagnostic in relativeTimeFormat.Diagnostics) rejectedFormat |= diagnostic.Id == "RTR0030";
        Assert.True(!relativeTimeFormat.Success && rejectedFormat, CompilerTests.DiagnosticsText(relativeTimeFormat.Diagnostics));
    }

    private static void V3AdapterPermitsOptionalSchemaUri()
    {
        TranslationCompilation compilation = CompileV3("""{"astVersion":3,"profile":"runic-mf2-subset/1","inputs":{},"declarations":[],"selectors":[],"variants":[{"matches":{},"pattern":[{"kind":"text","value":"ok"}]}]}""", includeSchemaUri: false);
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
    }

    private static TranslationCompilation CompileV3(string ast, bool includeSchemaUri = true)
    {
        const string manifest = """{"schemaVersion":2,"catalog":"v3checks","code":{"namespace":"Tests","className":"V3ChecksText"},"defaultLocale":"en","locales":[{"tag":"en"}],"layers":[{"name":"base","priority":0}]}""";
        string schema = includeSchemaUri ? "\"$schema\":\"https://runic-artifex.eu/schemas/translations/resources-v3.schema.json\"," : string.Empty;
        string document = "{" + schema + "\"schemaVersion\":3,\"catalog\":\"v3checks\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"Value\":{\"$value\":{\"mf2\":{\"profile\":\"runic-mf2-subset/1\",\"ast\":" + ast + "}}}}}";
        return Runic.Translations.Compiler.TranslationCompiler.Compile([CompilerTests.Source("manifest.json", manifest)], [CompilerTests.Source("v3.json", document)]);
    }

    private static Runic.Translations.Compiler.TranslationCompilation Compile(bool includeCatchAll)
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
        return Runic.Translations.Compiler.TranslationCompiler.Compile(
            [CompilerTests.Source("manifest.json", manifest)],
            [CompilerTests.Source("en.json", english), CompilerTests.Source("de.json", german)]);
    }
}
