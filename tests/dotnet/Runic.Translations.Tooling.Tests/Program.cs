using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tooling.Tests;

internal static class Program
{
    public static int Main()
    {
        try
        {
            SemanticXliffRoundTripsDirectMf2AndReview();
            SemanticXliffAcceptsGroupedRmf2();
            SemanticXliffReportsStructuredLossAndRefusesImport();
            XliffRefusesStructuredTextWithStaleMetadata();
            XliffPreflightSeparatesTextContractAndFreshness();
            ArtifactInspectionRecognizesXliff();
            ToolRequestHasCanonicalShape();
            ToolCommandHasCanonicalInitShape();
            Console.WriteLine("RESULT 8/8 passed");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void SemanticXliffRoundTripsDirectMf2AndReview()
    {
        object compilation = CompileSemantic("hello = Hello", "hello = Hallo", grouped: false);
        object preflight = Preflight(compilation);
        string fingerprint = Property<string>(preflight, "TextProfileFingerprint");
        var review = new TranslationInterchangeReview("app",
            [new TranslationInterchangeReviewEntry("hello", "de", "approved", "Ready.", fingerprint)]);

        TranslationXliffExportResult first = Export(compilation, review);
        TranslationXliffExportResult second = Export(compilation, review);
        if (first.Documents.Count != 1 || !first.Documents[0].Bytes.SequenceEqual(second.Documents[0].Bytes) || !first.Report.IsLossless)
            throw new InvalidOperationException("Direct MF2 semantic XLIFF export was not deterministic and lossless.");

        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(first.Documents.Single().Bytes);
        TranslationInterchangeReviewEntry importedReview = imported.Review.Entries.Single();
        if (Encoding.UTF8.GetString(imported.Messages.Single().Bytes) != "Hallo\n" ||
            importedReview.State != "approved" || importedReview.SourceFingerprint != fingerprint)
            throw new InvalidOperationException("Direct MF2 semantic XLIFF did not round-trip translator text and review evidence.");
    }

    private static void SemanticXliffAcceptsGroupedRmf2()
    {
        object compilation = CompileSemantic("hello = Hello", "hello = Hallo", grouped: true);
        TranslationXliffExportResult exported = Export(compilation);
        if (exported.Documents.Count != 1 || !exported.Report.IsLossless)
            throw new InvalidOperationException("Grouped RMF2 input did not reach the semantic interchange path.");
    }

    private static void SemanticXliffReportsStructuredLossAndRefusesImport()
    {
        const string english = """
            hello =
              .input {$count :integer}
              .match $count
              one {{One item}}
              * {{{$count} items}}
            """;
        const string german = """
            hello =
              .input {$count :integer}
              .match $count
              one {{Ein Element}}
              * {{{$count} Elemente}}
            """;
        TranslationXliffExportResult exported = Export(CompileSemantic(english, german, grouped: false));
        if (exported.Report.IsLossless ||
            !exported.Report.Losses.Any(loss => loss.Code == "XLIFF21-STRUCTURED-MESSAGE" && loss.SemanticLoss))
            throw new InvalidOperationException("Structured semantic XLIFF loss was not reported.");
        try { _ = TranslationInterchange.ImportXliff21(exported.Documents.Single().Bytes); }
        catch (TranslationInterchangeException exception) when (exception.Code == "XLIFF21-STRUCTURED-IMPORT") { return; }
        throw new InvalidOperationException("Structured XLIFF input was accepted.");
    }

    private static void XliffRefusesStructuredTextWithStaleMetadata()
    {
        byte[] exported = Export(CompileSemantic("hello = Hello", "hello = Hallo", grouped: false)).Documents.Single().Bytes;
        string text = Encoding.UTF8.GetString(exported);
        const string original = "<target>Hallo</target>";
        if (!text.Contains(original, StringComparison.Ordinal))
            throw new InvalidOperationException("The plain XLIFF fixture did not contain the expected target text.");
        byte[] tampered = Encoding.UTF8.GetBytes(text.Replace(original, "<target>{{{|Hallo|}}}</target>", StringComparison.Ordinal));
        try { _ = TranslationInterchange.ImportXliff21(tampered); }
        catch (TranslationInterchangeException exception) when (exception.Code == "XLIFF21-STRUCTURED-IMPORT") { return; }
        throw new InvalidOperationException("Structured XLIFF text bypassed refusal through stale runic:unit metadata.");
    }

    private static void XliffPreflightSeparatesTextContractAndFreshness()
    {
        object first = CompileSemantic("hello = Hello", "hello = Hallo", grouped: false);
        object changed = CompileSemantic("hello = Hello again", "hello = Hallo", grouped: false);
        object firstPreflight = Preflight(first);
        object changedPreflight = Preflight(changed);
        object firstProject = Project(first);
        object changedProject = Project(changed);
        string caller = Property<string>(firstProject, "CallerFingerprint");
        string changedCaller = Property<string>(changedProject, "CallerFingerprint");
        string sourceHash = Property<string>(firstProject, "SourceHash");
        string fingerprint = Property<string>(firstPreflight, "TextProfileFingerprint");
        string freshness = Property<string>(firstPreflight, "SourceFreshness");

        if (!Property<bool>(firstPreflight, "Success") ||
            Property<string>(firstPreflight, "CatalogId") != "app" ||
            Property<string>(firstPreflight, "SourceLocale") != "en" ||
            caller != changedCaller || sourceHash != freshness || fingerprint == caller || fingerprint == sourceHash ||
            fingerprint == Property<string>(changedPreflight, "TextProfileFingerprint") ||
            freshness == Property<string>(changedPreflight, "SourceFreshness"))
            throw new InvalidOperationException("XLIFF preflight conflated caller compatibility, source freshness, or the closed text profile.");
    }

    private static void ArtifactInspectionRecognizesXliff()
    {
        TranslationXliffDocument xliff = Export(CompileSemantic("hello = Hello", "hello = Hallo", grouped: false)).Documents.Single();
        ArtifactInspection inspection = ArtifactInspector.Inspect(xliff.Bytes);
        if (inspection.Kind != "xliff-2.1" || inspection.Catalog != "app" || inspection.Locale != "de" || inspection.Findings.Count != 0)
            throw new InvalidOperationException("Artifact inspection did not recognize the generated XLIFF document.");
    }

    private static void ToolRequestHasCanonicalShape()
    {
        var request = new TranslationsToolCommandRequest("init");
        request.Deconstruct(out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);
        if (typeof(TranslationsToolCommandRequest).GetConstructors().Single().GetParameters().Length != 17)
            throw new InvalidOperationException("TranslationsToolCommandRequest changed its positional constructor shape.");
    }

    private static void ToolCommandHasCanonicalInitShape()
    {
        Type[] expected =
        [
            typeof(ITranslationsToolCommandOperations), typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(string), typeof(IReadOnlyList<string>), typeof(bool),
        ];
        MethodInfo[] initMethods = typeof(TranslationsToolCommandModule).GetMethods()
            .Where(method => method.Name == "Init")
            .ToArray();
        MethodInfo? canonical = initMethods.SingleOrDefault(method => method.GetParameters().Length == expected.Length);
        if (canonical is null || !canonical.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(expected) || initMethods.Length != 1)
            throw new InvalidOperationException("TranslationsToolCommandModule.Init retained an obsolete layout overload.");
    }

    private static object CompileSemantic(string english, string german, bool grouped)
    {
        TranslationSource project = Source("translations/runic.json", """
            {
              "schemaVersion": 1,
              "catalog": "app",
              "code": { "namespace": "App", "className": "Text" },
              "baseLocale": "en",
              "locales": ["en", { "tag": "de", "fallback": "en" }]
            }
            """);
        TranslationSource[] messages = grouped
            ? [Source("translations/en.rmf2", english), Source("translations/de.rmf2", german)]
            : [Source("translations/en/hello.mf2", DirectPattern(english)), Source("translations/de/hello.mf2", DirectPattern(german))];
        Type carrier = ExportMethod().GetParameters()[0].ParameterType;
        MethodInfo compiler = typeof(TranslationCompiler).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .SingleOrDefault(method => method.ReturnType == carrier &&
                method.GetParameters().Length >= 2 && method.GetParameters()[0].ParameterType == typeof(TranslationSource)) ??
            throw new InvalidOperationException("Semantic compiler entry point is missing.");
        object?[] arguments = compiler.GetParameters().Select(parameter => parameter.Position switch
        {
            0 => (object?)project,
            1 => messages,
            _ when parameter.ParameterType == typeof(CancellationToken) => CancellationToken.None,
            _ => null,
        }).ToArray();
        object compilation = compiler.Invoke(null, arguments) ?? throw new InvalidOperationException("Semantic compiler returned no result.");
        if (!Property<bool>(compilation, "Success"))
            throw new InvalidOperationException("Semantic fixture did not compile.");
        return compilation;
    }

    private static string DirectPattern(string source) => source.StartsWith("hello =", StringComparison.Ordinal)
        ? source.Substring("hello =".Length).TrimStart(' ', '\n', '\r')
        : source;

    private static TranslationXliffExportResult Export(object compilation, TranslationInterchangeReview? review = null) =>
        (TranslationXliffExportResult)(ExportMethod().Invoke(null, [compilation, review]) ??
            throw new InvalidOperationException("Semantic XLIFF export returned no result."));

    private static MethodInfo ExportMethod() => typeof(TranslationInterchange).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(candidate => candidate.Name == "ExportXliff21" && candidate.GetParameters().Length == 2 &&
            candidate.GetParameters()[0].ParameterType.Name != "TranslationInterchangeProjection");

    private static object Preflight(object compilation)
    {
        MethodInfo method = typeof(TranslationInterchange).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "PreflightXliff21" && candidate.GetParameters().Length == 1 &&
                candidate.GetParameters()[0].ParameterType.IsInstanceOfType(compilation));
        return method.Invoke(null, [compilation]) ?? throw new InvalidOperationException("XLIFF preflight returned no projection.");
    }

    private static object Project(object compilation)
    {
        PropertyInfo? project = compilation.GetType().GetProperty("Project", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (project?.GetValue(compilation) is { } direct) return direct;
        object rmf2 = Property<object>(compilation, "Rmf2");
        return Property<object>(rmf2, "Project");
    }

    private static T Property<T>(object value, string name) =>
        (T)(value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value) ??
            throw new InvalidOperationException("Missing property '" + name + "'."));

    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
}
