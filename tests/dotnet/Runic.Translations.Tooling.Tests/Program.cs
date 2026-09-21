using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tooling.Tests;

internal static class Program
{
    public static int Main()
    {
        try
        {
            Mf2ProjectCompilesThroughToolingFacade();
            XliffRoundTripsPlainMf2AndReview();
            XliffReportsStructuredMf2Loss();
            V2XliffIsDeterministicAndRoundTripsApprovedReview();
            V2QuotedTextXliffRoundTrips();
            V2XliffReportsStructuredLossAndRefusesImport();
            XliffRefusesStructuredTextWithStaleMetadata();
            XliffPreflightSeparatesTextContractAndFreshness();
            LocalePackUsesCanonicalCompilerBytes();
            ArtifactInspectionRecognizesGeneratedOutputs();
            Rmf2PacksAndInspection();
            ToolRequestKeepsLegacyPositionalShape();
            ToolCommandKeepsLegacyInitShape();
            Console.WriteLine("RESULT 13/13 passed");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Mf2ProjectCompilesThroughToolingFacade()
    {
        TranslationCompilation compilation = CompilePlainFixture();
        if (!compilation.Success || compilation.Catalogs.Single().CanonicalResources.Single().Key != "common_hello")
            throw new InvalidOperationException("The Tooling facade did not compile the conventional MF2 project.");
    }

    private static void XliffRoundTripsPlainMf2AndReview()
    {
        TranslationCompilation compilation = CompilePlainFixture();
        var review = new TranslationInterchangeReview("app", [new TranslationInterchangeReviewEntry("common_hello", "de", "needs-review", "Check formality.", "sha256:test")]);
        TranslationXliffExportResult first = TranslationInterchange.ExportXliff21(compilation, review);
        TranslationXliffExportResult second = TranslationInterchange.ExportXliff21(compilation, review);
        if (first.Documents.Count != 1 || !first.Documents[0].Bytes.SequenceEqual(second.Documents[0].Bytes) || !first.Report.IsLossless)
            throw new InvalidOperationException("Plain MF2 XLIFF export was not canonical and lossless.");

        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(first.Documents[0].Bytes);
        string message = Encoding.UTF8.GetString(imported.Messages.Single().Bytes);
        if (imported.Messages.Single().MessageId != "common_hello" || !message.Contains("Hallo", StringComparison.Ordinal) || imported.Review.Entries.Single().Note != "Check formality.")
            throw new InvalidOperationException("XLIFF import did not preserve translator text and review note.");

        byte[] reviewJson = TranslationInterchange.ExportReviewJson(review);
        if (!reviewJson.SequenceEqual(TranslationInterchange.ExportReviewJson(TranslationInterchange.ImportReviewJson(reviewJson))))
            throw new InvalidOperationException("Portable review JSON was not canonical.");
    }

    private static void XliffReportsStructuredMf2Loss()
    {
        TranslationCompilation compilation = Compile(
            """
            .input {$count :integer select=plural}
            .match $count
            one {{One item}}
            * {{{$count} items}}
            """,
            """
            .input {$count :integer select=plural}
            .match $count
            one {{Ein Element}}
            * {{{$count} Elemente}}
            """);
        TranslationXliffExportResult exported = TranslationInterchange.ExportXliff21(compilation);
        if (exported.Report.IsLossless || !exported.Report.Losses.Any(loss => loss.Code == "XLIFF21-STRUCTURED-MESSAGE"))
            throw new InvalidOperationException("Structured MF2 XLIFF export was not reported as lossy.");
        try { _ = TranslationInterchange.ImportXliff21(exported.Documents.Single().Bytes); }
        catch (TranslationInterchangeException exception) when (exception.Code == "XLIFF21-STRUCTURED-IMPORT") { return; }
        throw new InvalidOperationException("Structured XLIFF input was accepted.");
    }

    private static void V2XliffIsDeterministicAndRoundTripsApprovedReview()
    {
        object compilation = CompileV2("hello = Hello", "hello = Hallo");
        object preflight = Preflight(compilation);
        string fingerprint = Property<string>(preflight, "TextProfileFingerprint");
        var review = new TranslationInterchangeReview("app",
            [new TranslationInterchangeReviewEntry("hello", "de", "approved", "Ready.", fingerprint)]);

        TranslationXliffExportResult first = ExportProfile(compilation, review);
        TranslationXliffExportResult second = ExportProfile(compilation, review);
        if (first.Documents.Count != 1 ||
            !first.Documents[0].Bytes.SequenceEqual(second.Documents[0].Bytes) ||
            !first.Report.IsLossless)
            throw new InvalidOperationException("Execution-v2 XLIFF export was not deterministic and lossless.");

        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(first.Documents[0].Bytes);
        TranslationInterchangeReviewEntry importedReview = imported.Review.Entries.Single();
        if (Encoding.UTF8.GetString(imported.Messages.Single().Bytes) != "Hallo\n" ||
            importedReview.State != "approved" ||
            importedReview.SourceFingerprint != fingerprint)
            throw new InvalidOperationException("Execution-v2 XLIFF did not round-trip plain text and approved review evidence.");
    }

    private static void V2XliffReportsStructuredLossAndRefusesImport()
    {
        const string english = """
            items =
              .input {$count :integer select=plural}
              .match $count
              one {{One item}}
              * {{{$count} items}}
            """;
        const string german = """
            items =
              .input {$count :integer select=plural}
              .match $count
              one {{Ein Element}}
              * {{{$count} Elemente}}
            """;
        TranslationXliffExportResult exported = ExportProfile(CompileV2(english, german));
        if (exported.Report.IsLossless ||
            !exported.Report.Losses.Any(loss => loss.Code == "XLIFF21-STRUCTURED-MESSAGE" && loss.SemanticLoss))
            throw new InvalidOperationException("Execution-v2 structured XLIFF loss was not reported.");
        try { _ = TranslationInterchange.ImportXliff21(exported.Documents.Single().Bytes); }
        catch (TranslationInterchangeException exception) when (exception.Code == "XLIFF21-STRUCTURED-IMPORT") { return; }
        throw new InvalidOperationException("Execution-v2 structured XLIFF input was accepted.");
    }

    private static void V2QuotedTextXliffRoundTrips()
    {
        TranslationXliffExportResult exported = ExportProfile(CompileV2("hello = {{Hello}}", "hello = {{Hallo}}"));
        if (!exported.Report.IsLossless)
            throw new InvalidOperationException("Execution-v2 text-only quoted syntax was reported as lossy.");
        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(exported.Documents.Single().Bytes);
        if (Encoding.UTF8.GetString(imported.Messages.Single().Bytes) != "{{Hallo}}\n")
            throw new InvalidOperationException("Execution-v2 text-only quoted syntax did not round-trip exactly.");
    }

    private static void XliffRefusesStructuredTextWithStaleMetadata()
    {
        byte[] v4 = TranslationInterchange.ExportXliff21(Compile("Hello", "Hallo")).Documents.Single().Bytes;
        AssertStructuredTamperRefused(v4, "<source>Hello</source>", "<source>{{{|Hello|}}}</source>");

        byte[] v5 = ExportProfile(CompileV2("hello = Hello", "hello = Hallo")).Documents.Single().Bytes;
        AssertStructuredTamperRefused(v5, "<target>Hallo</target>", "<target>{{{|Hallo|}}}</target>");
    }

    private static void AssertStructuredTamperRefused(byte[] exported, string original, string replacement)
    {
        string text = Encoding.UTF8.GetString(exported);
        if (!text.Contains(original, StringComparison.Ordinal))
            throw new InvalidOperationException("The plain XLIFF fixture did not contain the expected text node.");
        byte[] tampered = Encoding.UTF8.GetBytes(text.Replace(original, replacement, StringComparison.Ordinal));
        try { _ = TranslationInterchange.ImportXliff21(tampered); }
        catch (TranslationInterchangeException exception) when (exception.Code == "XLIFF21-STRUCTURED-IMPORT") { return; }
        throw new InvalidOperationException("Structured XLIFF text bypassed refusal through stale runic:unit metadata.");
    }

    private static void XliffPreflightSeparatesTextContractAndFreshness()
    {
        object first = CompileV2("hello = Hello", "hello = Hallo");
        object changed = CompileV2("hello = Hello again", "hello = Hallo");
        object firstPreflight = Preflight(first);
        object changedPreflight = Preflight(changed);
        object firstProject = Property<object>(Property<object>(first, "Rmf2"), "Project");
        object changedProject = Property<object>(Property<object>(changed, "Rmf2"), "Project");
        string caller = Property<string>(firstProject, "CallerFingerprint");
        string changedCaller = Property<string>(changedProject, "CallerFingerprint");
        string sourceHash = Property<string>(firstProject, "SourceHash");
        string fingerprint = Property<string>(firstPreflight, "TextProfileFingerprint");
        string freshness = Property<string>(firstPreflight, "SourceFreshness");
        TranslationCompilation v4 = Compile("Hello", "Hallo");
        TranslationCompilation v4TargetChanged = Compile("Hello", "Guten Tag");
        object v4Preflight = Preflight(v4);
        object v4TargetChangedPreflight = Preflight(v4TargetChanged);

        if (!Property<bool>(firstPreflight, "Success") ||
            Property<string>(firstPreflight, "CatalogId") != "app" ||
            Property<string>(firstPreflight, "SourceLocale") != "en" ||
            caller != changedCaller ||
            sourceHash != freshness ||
            fingerprint == caller ||
            fingerprint == sourceHash ||
            fingerprint == Property<string>(changedPreflight, "TextProfileFingerprint") ||
            freshness == Property<string>(changedPreflight, "SourceFreshness") ||
            Property<string>(v4Preflight, "TextProfileFingerprint") != Property<string>(v4TargetChangedPreflight, "TextProfileFingerprint") ||
            Property<string>(v4Preflight, "SourceFreshness") == Property<string>(v4TargetChangedPreflight, "SourceFreshness") ||
            Property<string>(v4Preflight, "SourceFreshness") == v4.Catalogs.Single().Fingerprint ||
            Property<string>(v4Preflight, "TextProfileFingerprint") == v4.Catalogs.Single().Fingerprint)
            throw new InvalidOperationException("XLIFF preflight conflated caller compatibility, source freshness, or the closed text profile.");
    }

    private static void LocalePackUsesCanonicalCompilerBytes()
    {
        LocalePackV2BuildResult result = TranslationsTooling.BuildLocalePackV2(CompilePlainFixture());
        if (result.Documents.Count != 2 || result.Documents.Any(document => document.Kind != TranslationGeneratedOutputKind.LocaleJson) || result.Documents.Any(document => !document.Text.Contains("\"artifactVersion\":2", StringComparison.Ordinal)))
            throw new InvalidOperationException("Locale pack output was not rendered from canonical compiler artifacts.");
    }

    private static void ArtifactInspectionRecognizesGeneratedOutputs()
    {
        TranslationCompilation compilation = CompilePlainFixture();
        TranslationGeneratedOutput german = TranslationsTooling.BuildLocalePackV2(compilation).Documents.Single(document => document.RelativePath == "app.de.locale-v2.json");
        ArtifactInspection pack = ArtifactInspector.Inspect(german.GetUtf8Bytes());
        if (pack.Kind != "locale-pack-v2" || pack.Catalog != "app" || pack.Locale != "de" || pack.Findings.Count != 0)
            throw new InvalidOperationException("Artifact inspection did not recognize the generated locale pack.");

        TranslationXliffDocument xliff = TranslationInterchange.ExportXliff21(compilation).Documents.Single();
        ArtifactInspection interchange = ArtifactInspector.Inspect(xliff.Bytes);
        if (interchange.Kind != "xliff-2.1" || interchange.Findings.Count != 0)
            throw new InvalidOperationException("Artifact inspection did not recognize the generated XLIFF document.");
    }

    private static void Rmf2PacksAndInspection()
    {
        var compilation = TranslationsTooling.CompileProject(
            Source("translations/runic.json", """{"schemaVersion":1,"sourceLayout":"rmf2-v1","catalog":"app","code":{"namespace":"Example","className":"AppText"},"baseLocale":"en"}"""),
            [Source("translations/en.rmf2", "hello = {#strong}Hello{/strong}")]);
        var artifact = TranslationsTooling.BuildRmf2LocalePacks(compilation).Single();
        var inspection = ArtifactInspector.Inspect(artifact.GetUtf8Bytes());
        if (inspection.Kind != "locale-artifact-v4" || inspection.Findings.Count != 0) throw new InvalidOperationException("RMF2 artifact inspection failed.");
    }

    private static void ToolRequestKeepsLegacyPositionalShape()
    {
        var request = new TranslationsToolCommandRequest("init");
        request.Deconstruct(out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);
        if (typeof(TranslationsToolCommandRequest).GetConstructors().Single().GetParameters().Length != 17)
            throw new InvalidOperationException("TranslationsToolCommandRequest changed its legacy positional constructor shape.");
        if (request.Layout != "locale-toml" || request with { Layout = "rmf2-v1" } is not { Layout: "rmf2-v1" })
            throw new InvalidOperationException("TranslationsToolCommandRequest layout compatibility property failed.");
    }

    private static void ToolCommandKeepsLegacyInitShape()
    {
        Type[] expected =
        [
            typeof(ITranslationsToolCommandOperations), typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(string), typeof(IReadOnlyList<string>), typeof(bool),
        ];
        var initMethods = typeof(TranslationsToolCommandModule).GetMethods()
            .Where(method => method.Name == "Init")
            .ToArray();
        var legacy = initMethods.SingleOrDefault(method => method.GetParameters().Length == expected.Length);
        if (legacy is null || !legacy.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(expected))
            throw new InvalidOperationException("TranslationsToolCommandModule.Init changed its legacy eight-parameter method shape.");
        if (initMethods.Length != 2 || initMethods.Single(method => method.GetParameters().Length == expected.Length + 1).GetParameters().Last().ParameterType != typeof(string))
            throw new InvalidOperationException("TranslationsToolCommandModule.Init layout-aware overload is missing.");
    }

    private static TranslationCompilation CompilePlainFixture() => Compile("Hello", "Hallo");

    private static object CompileV2(string english, string german)
    {
        TranslationSource project = Source("translations/runic.json", """
            {
              "schemaVersion": 1,
              "catalog": "app",
              "code": { "namespace": "App", "className": "Text" },
              "baseLocale": "en",
              "sourceLayout": "rmf2-v1",
              "executionProfile": "rmf2-execution-v2",
              "locales": ["en", { "tag": "de", "fallback": "en" }]
            }
            """);
        MethodInfo method = typeof(TranslationCompiler).GetMethod(
            "CompileProjectForSelectedProfile",
            BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("Profile compiler entry point is missing.");
        object compilation = method.Invoke(null,
            [project, new[] { Source("translations/en.rmf2", english), Source("translations/de.rmf2", german) }, null, CancellationToken.None]) ??
            throw new InvalidOperationException("Profile compiler returned no result.");
        if (!Property<bool>(compilation, "Success"))
            throw new InvalidOperationException("Execution-v2 fixture did not compile.");
        return compilation;
    }

    private static object Preflight(object compilation)
    {
        MethodInfo method = typeof(TranslationInterchange).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "PreflightXliff21" && candidate.GetParameters().Length == 1 &&
                candidate.GetParameters()[0].ParameterType.IsInstanceOfType(compilation));
        return method.Invoke(null, [compilation]) ??
            throw new InvalidOperationException("XLIFF preflight returned no projection.");
    }

    private static TranslationXliffExportResult ExportProfile(
        object compilation,
        TranslationInterchangeReview? review = null)
    {
        MethodInfo method = typeof(TranslationInterchange).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "ExportXliff21" &&
                candidate.GetParameters().Length == 2 &&
                candidate.GetParameters()[0].ParameterType.Name == "TranslationProfileCompilation");
        return (TranslationXliffExportResult)(method.Invoke(null, [compilation, review]) ??
            throw new InvalidOperationException("Profile XLIFF export returned no result."));
    }

    private static T Property<T>(object value, string name) =>
        (T)(value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value) ??
            throw new InvalidOperationException("Missing property '" + name + "'."));

    private static TranslationCompilation Compile(string english, string german)
    {
        TranslationCompilation compilation = TranslationsTooling.CompileProject(
            Source("translations/runic.json", """
                {
                  "schemaVersion": 1,
                  "catalog": "app",
                  "code": { "namespace": "App", "className": "Text" },
                  "baseLocale": "en",
                  "locales": ["en", { "tag": "de", "fallback": "en" }]
                }
                """),
            [
                Source("translations/en/common_hello.mf2", english),
                Source("translations/de/common_hello.mf2", german),
            ]);
        if (!compilation.Success)
            throw new InvalidOperationException(string.Join("; ", compilation.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
        return compilation;
    }

    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
}
