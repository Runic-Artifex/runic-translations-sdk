using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AnalysisModel = Runic.Translations.Compiler.Analysis;
using CompilerModel = Runic.Translations.Compiler;

namespace Runic.Translations.Compiler.Tests;

internal static class AnalysisTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("analysis reports compiler-owned completeness and artifact state", CompletenessAndArtifacts);
        runner.Add("analysis proves generated C# key and accessor usage", CSharpUsage);
        runner.Add("analysis proves TypeScript m namespace and generated ABI usage", TypeScriptUsage);
        runner.Add("analysis keeps dynamic access conservative", DynamicUsage);
        runner.Add("analysis ignores comments and unrelated string literals", FalsePositiveBoundaries);
        runner.Add("analysis keeps multi-catalog usage scoped", MultiCatalog);
        runner.Add("analysis reports placeholder contract drift", ContractDrift);
        runner.Add("analysis machine and human reports match goldens", GoldenReports);
    }

    private static void CompletenessAndArtifacts()
    {
        CompilerModel.TranslationCompilation compilation = CompileCatalog("app", "ProductText");
        AnalysisModel.TranslationAnalysisReport initial = AnalysisModel.TranslationAnalyzer.Analyze(
            compilation,
            Array.Empty<AnalysisModel.TranslationUsageSource>());
        AnalysisModel.TranslationCatalogAnalysis initialCatalog = Assert.Single(initial.Catalogs);
        Assert.Equal(AnalysisModel.TranslationArtifactStatus.Unknown, initialCatalog.ArtifactStatus);

        AnalysisModel.TranslationAnalysisReport report = AnalysisModel.TranslationAnalyzer.Analyze(
            compilation,
            Array.Empty<AnalysisModel.TranslationUsageSource>(),
            new[] { new AnalysisModel.TranslationArtifactSnapshot("app", "sha256:stale", "generated/analysis.json") });
        AnalysisModel.TranslationCatalogAnalysis catalog = Assert.Single(report.Catalogs);
        Assert.Equal(AnalysisModel.TranslationArtifactStatus.Stale, catalog.ArtifactStatus);
        Assert.True(catalog.RequiresRegeneration, "A stale source fingerprint must require regeneration.");

        AnalysisModel.TranslationKeyAnalysis key = Find(catalog, "Common.Bye");
        AnalysisModel.TranslationLocaleAnalysis de = Find(key, "de");
        AnalysisModel.TranslationLocaleAnalysis fr = Find(key, "fr");
        Assert.Equal(AnalysisModel.TranslationLocaleAvailability.FallbackOnly, de.Availability);
        Assert.Equal("en", de.ResolvedFromLocale);
        Assert.Equal(AnalysisModel.TranslationLocaleAvailability.Missing, fr.Availability);
        Assert.Equal(AnalysisModel.TranslationContractStatus.Missing, fr.ContractStatus);

        AnalysisModel.TranslationAnalysisReport current = AnalysisModel.TranslationAnalyzer.Analyze(
            compilation,
            Array.Empty<AnalysisModel.TranslationUsageSource>(),
            new[] { new AnalysisModel.TranslationArtifactSnapshot("app", initialCatalog.SourceFingerprint, "generated/analysis.json") });
        Assert.Equal(AnalysisModel.TranslationArtifactStatus.Current, Assert.Single(current.Catalogs).ArtifactStatus);
    }

    private static void CSharpUsage()
    {
        CompilerModel.TranslationCompilation compilation = CompileCatalog("app", "ProductText");
        const string source = """
            var key = ProductTextKeys.Common.Hello;
            ProductText texts = Resolve();
            var value = texts.Common.Bye;
            "ProductTextKeys.False.Positive";
            // ProductTextKeys.Dynamic.Value;
            """;
        AnalysisModel.TranslationCatalogAnalysis catalog = Analyze(compilation,
            new AnalysisModel.TranslationUsageSource("Consumer.cs", source, AnalysisModel.TranslationUsageSourceLanguage.CSharp, "app"));
        AssertProven(catalog, "Common.Hello", AnalysisModel.TranslationUsageLanguage.CSharp,
            AnalysisModel.TranslationUsageEvidenceKind.CSharpGeneratedKey);
        AssertProven(catalog, "Common.Bye", AnalysisModel.TranslationUsageLanguage.CSharp,
            AnalysisModel.TranslationUsageEvidenceKind.CSharpGeneratedAccessor);
        Assert.Equal(AnalysisModel.TranslationUsageClassification.Unknown, Find(catalog, "False.Positive").Usage);
        Assert.Equal(AnalysisModel.TranslationUsageClassification.Unknown, Find(catalog, "Dynamic.Value").Usage);
    }

    private static void TypeScriptUsage()
    {
        CompilerModel.TranslationCompilation compilation = CompileCatalog("app", "ProductText");
        const string source = """
            import { m } from "virtual:runic-translations/app/messages";
            m["Common.Hello"]();
            m.Bye();
            m$Common$Bye();
            """;
        AnalysisModel.TranslationCatalogAnalysis catalog = Analyze(compilation,
            new AnalysisModel.TranslationUsageSource("consumer.ts", source, AnalysisModel.TranslationUsageSourceLanguage.TypeScript, "app"));
        AssertProven(catalog, "Common.Hello", AnalysisModel.TranslationUsageLanguage.TypeScript,
            AnalysisModel.TranslationUsageEvidenceKind.TypeScriptMessageNamespace);
        AssertProven(catalog, "Common.Bye", AnalysisModel.TranslationUsageLanguage.TypeScript,
            AnalysisModel.TranslationUsageEvidenceKind.TypeScriptGeneratedIdentifier);
    }

    private static void DynamicUsage()
    {
        CompilerModel.TranslationCompilation compilation = CompileCatalog("app", "ProductText");
        AnalysisModel.TranslationUsageSource source = new(
            "dynamic.ts",
            "formatDynamicMessage(artifact, routeKey);\nm[key](inputs);",
            AnalysisModel.TranslationUsageSourceLanguage.TypeScript,
            "app");
        AnalysisModel.TranslationCatalogAnalysis conservative = Analyze(compilation, source);
        foreach (AnalysisModel.TranslationKeyAnalysis key in conservative.Keys)
        {
            Assert.Equal(AnalysisModel.TranslationUsageClassification.PossibleDynamic, key.Usage);
            Assert.True(!key.IsDeletionCandidate, "Dynamic access must block deletion candidates by default.");
        }

        AnalysisModel.TranslationAnalysisReport explicitPolicy = AnalysisModel.TranslationAnalyzer.Analyze(
            compilation,
            new[] { source },
            new AnalysisModel.TranslationAnalysisOptions(AnalysisModel.TranslationDynamicUsagePolicy.IgnoreForDeletionCandidates));
        foreach (AnalysisModel.TranslationKeyAnalysis key in Assert.Single(explicitPolicy.Catalogs).Keys)
            Assert.True(key.IsDeletionCandidate, "The explicit unsafe policy should allow review candidates.");
    }

    private static void FalsePositiveBoundaries()
    {
        CompilerModel.TranslationCompilation compilation = CompileCatalog("app", "ProductText");
        const string csharp = """
            // ProductTextKeys.Common.Hello
            var example = "ProductTextKeys.Common.Bye";
            /* new TranslationKey("app", 0, "Dynamic.Value") */
            """;
        const string typescript = """
            // m["Common.Hello"]()
            const example = 'm["Common.Bye"]()';
            const object = { "Dynamic.Value": true };
            """;
        AnalysisModel.TranslationAnalysisReport report = AnalysisModel.TranslationAnalyzer.Analyze(
            compilation,
            new[]
            {
                new AnalysisModel.TranslationUsageSource("false.cs", csharp, AnalysisModel.TranslationUsageSourceLanguage.CSharp, "app"),
                new AnalysisModel.TranslationUsageSource("false.ts", typescript, AnalysisModel.TranslationUsageSourceLanguage.TypeScript, "app"),
            });
        foreach (AnalysisModel.TranslationKeyAnalysis key in Assert.Single(report.Catalogs).Keys)
            Assert.Equal(AnalysisModel.TranslationUsageClassification.Unknown, key.Usage);
    }

    private static void MultiCatalog()
    {
        CompilerModel.TranslationCompilation compilation = CompileCatalogPair();
        AnalysisModel.TranslationAnalysisReport report = AnalysisModel.TranslationAnalyzer.Analyze(
            compilation,
            new[]
            {
                new AnalysisModel.TranslationUsageSource("alpha.ts", "m[\"Shared.Key\"]();", AnalysisModel.TranslationUsageSourceLanguage.TypeScript, "alpha"),
                new AnalysisModel.TranslationUsageSource("beta.cs", "var key = BetaTextKeys.Shared.Key;", AnalysisModel.TranslationUsageSourceLanguage.CSharp, "beta"),
            });
        Assert.Equal(2, report.Catalogs.Count);
        Assert.Equal(AnalysisModel.TranslationUsageLanguage.TypeScript, Find(report.Catalogs[0], "Shared.Key").UsageLanguages);
        Assert.Equal(AnalysisModel.TranslationUsageLanguage.CSharp, Find(report.Catalogs[1], "Shared.Key").UsageLanguages);

        AnalysisModel.TranslationAnalysisReport ambiguous = AnalysisModel.TranslationAnalyzer.Analyze(
            compilation,
            new[] { new AnalysisModel.TranslationUsageSource("ambiguous.ts", "m[\"Shared.Key\"]();", AnalysisModel.TranslationUsageSourceLanguage.TypeScript) });
        Assert.Equal(AnalysisModel.TranslationUsageClassification.PossibleDynamic, Find(ambiguous.Catalogs[0], "Shared.Key").Usage);
        Assert.Equal(AnalysisModel.TranslationUsageClassification.PossibleDynamic, Find(ambiguous.Catalogs[1], "Shared.Key").Usage);
    }

    private static void ContractDrift()
    {
        string manifest = Manifest("drift", "DriftText", "en", "{\"tag\":\"en\"},{\"tag\":\"de\",\"fallback\":\"en\"}");
        string en = Document("drift", "en", "{\"Message\":{\"$value\":\"Hello {name}\",\"$placeholders\":{\"name\":{\"type\":\"string\"}}}}");
        string de = Document("drift", "de", "{\"Message\":{\"$value\":\"Hallo {name}\",\"$placeholders\":{\"name\":{\"type\":\"int\"}}}}");
        CompilerModel.TranslationCompilation compilation = Compile(
            new[] { Source("drift/manifest.json", manifest) },
            new[] { Source("drift/en.json", en), Source("drift/de.json", de) });
        Assert.True(!compilation.Success, "Contract drift fixture should retain the compiler diagnostic.");
        AnalysisModel.TranslationCatalogAnalysis catalog = Analyze(compilation);
        Assert.Equal(AnalysisModel.TranslationContractStatus.Drift, Find(Find(catalog, "Message"), "de").ContractStatus);
    }

    private static void GoldenReports()
    {
        CompilerModel.TranslationCompilation compilation = CompileCatalogPair();
        AnalysisModel.TranslationAnalysisReport report = AnalysisModel.TranslationAnalyzer.Analyze(
            compilation, new[]
            {
                new AnalysisModel.TranslationUsageSource("src/alpha.cs", "var key = AlphaTextKeys.Shared.Key;", AnalysisModel.TranslationUsageSourceLanguage.CSharp, "alpha"),
                new AnalysisModel.TranslationUsageSource("src/alpha.ts", "m[\"Shared.Key\"]();", AnalysisModel.TranslationUsageSourceLanguage.TypeScript, "alpha"),
                new AnalysisModel.TranslationUsageSource("src/beta.ts", "const sample = \"BetaTextKeys.Shared.Key\";\nm[key]();", AnalysisModel.TranslationUsageSourceLanguage.TypeScript, "beta"),
            });
        AssertGolden("analysis.json", AnalysisModel.TranslationAnalysisRenderer.RenderJson(report), report);
        AssertGolden("analysis.txt", AnalysisModel.TranslationAnalysisRenderer.RenderText(report), report);
    }

    private static CompilerModel.TranslationCompilation CompileCatalog(string catalog, string className)
    {
        string manifest = Manifest(catalog, className, "en",
            "{\"tag\":\"en\"},{\"tag\":\"de\",\"fallback\":\"en\"},{\"tag\":\"fr\"}");
        string en = Document(catalog, "en", """
            {"Common":{"Hello":{"$value":"Hello"},"Bye":{"$value":"Bye"}},"Dynamic":{"Value":{"$value":"Dynamic"}},"False":{"Positive":{"$value":"False"}}}
            """);
        string de = Document(catalog, "de", "{\"Common\":{\"Hello\":{\"$value\":\"Hallo\"}}}");
        string fr = Document(catalog, "fr", "{}");
        return Compile(
            new[] { Source(catalog + "/manifest.json", manifest) },
            new[] { Source(catalog + "/en.json", en), Source(catalog + "/de.json", de), Source(catalog + "/fr.json", fr) });
    }

    private static CompilerModel.TranslationCompilation CompileCatalogPair()
    {
        string alphaManifest = Manifest("alpha", "AlphaText", "en", "{\"tag\":\"en\"}");
        string betaManifest = Manifest("beta", "BetaText", "en", "{\"tag\":\"en\"}");
        return Compile(
            new[] { Source("alpha/manifest.json", alphaManifest), Source("beta/manifest.json", betaManifest) },
            new[]
            {
                Source("alpha/en.json", Document("alpha", "en", "{\"Shared\":{\"Key\":{\"$value\":\"Alpha\"}}}")),
                Source("beta/en.json", Document("beta", "en", "{\"Shared\":{\"Key\":{\"$value\":\"Beta\"}}}")),
            });
    }

    private static CompilerModel.TranslationCompilation Compile(
        IEnumerable<CompilerModel.TranslationSource> manifests,
        IEnumerable<CompilerModel.TranslationSource> documents)
    {
        CompilerModel.TranslationCompilation compilation = CompilerModel.TranslationCompiler.Compile(manifests, documents);
        Assert.True(compilation.Catalogs.Count != 0, "Analysis fixture did not compile a semantic catalog.\n" + CompilerTests.DiagnosticsText(compilation.Diagnostics));
        return compilation;
    }

    private static string Manifest(string catalog, string className, string defaultLocale, string locales) =>
        "{\"schemaVersion\":1,\"catalog\":\"" + catalog + "\",\"code\":{\"namespace\":\"Analysis.Fixtures\",\"className\":\"" + className +
        "\",\"visibility\":\"public\"},\"defaultLocale\":\"" + defaultLocale + "\",\"locales\":[" + locales +
        "],\"layers\":[{\"name\":\"base\",\"priority\":0}],\"validation\":{\"translationCompleteness\":\"allow\",\"extraLocaleKeys\":\"error\",\"emptyValues\":\"allow\"},\"runtime\":{\"unsupportedLocale\":\"parentsThenDefault\",\"missingKey\":\"throw\"}}";

    private static string Document(string catalog, string locale, string resources) =>
        "{\"schemaVersion\":1,\"catalog\":\"" + catalog + "\",\"locale\":\"" + locale + "\",\"layer\":\"base\",\"resources\":" + resources + "}";

    private static CompilerModel.TranslationSource Source(string path, string json) =>
        new(path, Encoding.UTF8.GetBytes(json));

    private static AnalysisModel.TranslationCatalogAnalysis Analyze(
        CompilerModel.TranslationCompilation compilation,
        params AnalysisModel.TranslationUsageSource[] sources) =>
        Assert.Single(AnalysisModel.TranslationAnalyzer.Analyze(compilation, sources).Catalogs);

    private static AnalysisModel.TranslationKeyAnalysis Find(AnalysisModel.TranslationCatalogAnalysis catalog, string key)
    {
        foreach (AnalysisModel.TranslationKeyAnalysis item in catalog.Keys)
            if (string.Equals(item.Key, key, StringComparison.Ordinal)) return item;
        throw new InvalidOperationException("Analysis key not found: " + key);
    }

    private static AnalysisModel.TranslationLocaleAnalysis Find(AnalysisModel.TranslationKeyAnalysis key, string locale)
    {
        foreach (AnalysisModel.TranslationLocaleAnalysis item in key.Locales)
            if (string.Equals(item.Locale, locale, StringComparison.Ordinal)) return item;
        throw new InvalidOperationException("Analysis locale not found: " + locale);
    }

    private static void AssertProven(
        AnalysisModel.TranslationCatalogAnalysis catalog,
        string key,
        AnalysisModel.TranslationUsageLanguage language,
        AnalysisModel.TranslationUsageEvidenceKind evidenceKind)
    {
        AnalysisModel.TranslationKeyAnalysis item = Find(catalog, key);
        Assert.Equal(AnalysisModel.TranslationUsageClassification.Proven, item.Usage);
        Assert.Equal(language, item.UsageLanguages);
        bool found = false;
        foreach (AnalysisModel.TranslationUsageEvidence evidence in item.Evidence)
            if (evidence.Kind == evidenceKind) found = true;
        Assert.True(found, "Expected usage evidence was not reported for " + key + ".");
    }

    private static void AssertGolden(
        string fileName,
        string actual,
        AnalysisModel.TranslationAnalysisReport report)
    {
        string path = RepositoryPaths.Resolve("dotnet", "tests", "Runic.Translations.Compiler.Tests", "Golden", fileName);
        string expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (AnalysisModel.TranslationCatalogAnalysis catalog in report.Catalogs)
        {
            expected = expected
                .Replace("{{" + catalog.CatalogId + "ContractFingerprint}}", catalog.ContractFingerprint, StringComparison.Ordinal)
                .Replace("{{" + catalog.CatalogId + "SourceFingerprint}}", catalog.SourceFingerprint, StringComparison.Ordinal);
        }
        Assert.Equal(expected, actual, fileName);
    }
}
