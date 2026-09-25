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
            XliffRequiresSelectedV5ContractIdentity();
            PublicCompilationOverloadsHonorCancellation();
            ArtifactInspectionRecognizesXliff();
            ToolRequestHasCanonicalShape();
            ToolCommandHasCanonicalInitShape();
            Console.WriteLine("RESULT 10/10 passed");
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
        Rmf2ProjectCompilationV5 compilation = CompileSemantic("hello = Hello", "hello = Hallo", grouped: false);
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
        Rmf2ProjectCompilationV5 compilation = CompileSemantic("hello = Hello", "hello = Hallo", grouped: true);
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
        Rmf2ProjectCompilationV5 first = CompileSemantic("hello = Hello", "hello = Hallo", grouped: false);
        Rmf2ProjectCompilationV5 changed = CompileSemantic("hello = Hello again", "hello = Hallo", grouped: false);
        object firstPreflight = Preflight(first);
        object changedPreflight = Preflight(changed);
        string caller = first.CallerFingerprint!;
        string changedCaller = changed.CallerFingerprint!;
        string sourceHash = first.SourceHash!;
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

    private static void XliffRequiresSelectedV5ContractIdentity()
    {
        byte[] exported = Export(CompileSemantic("hello = Hello", "hello = Hallo", grouped: false)).Documents.Single().Bytes;
        string metadata = UnitMetadata(exported);
        if (!metadata.Contains("\"executionProfile\":\"rmf2-execution-v2\"", StringComparison.Ordinal) ||
            !metadata.Contains("\"messageGrammarVersion\":5", StringComparison.Ordinal) ||
            !metadata.Contains("\"interchangeProfile\":\"runic.xliff21.closed-text\"", StringComparison.Ordinal) ||
            !metadata.Contains("\"interchangeProfileVersion\":2", StringComparison.Ordinal) ||
            metadata.Contains("schemaVersion", StringComparison.Ordinal) || metadata.Contains("\"layer\"", StringComparison.Ordinal))
            throw new InvalidOperationException("XLIFF metadata did not identify only the selected v5 execution and interchange contracts.");

        RejectContractMutation(exported, "\"rmf2-execution-v2\"", "\"rmf2-execution-v1\"", "XLIFF21-CONTRACT");
        RejectContractMutation(exported, "\"messageGrammarVersion\":5", "\"messageGrammarVersion\":4", "XLIFF21-CONTRACT");
        RejectContractMutation(exported, "\"runic.xliff21.closed-text\"", "\"runic.xliff21.other\"", "XLIFF21-PROFILE");
        RejectContractMutation(exported, "\"interchangeProfileVersion\":2", "\"interchangeProfileVersion\":1", "XLIFF21-PROFILE");
    }

    private static void PublicCompilationOverloadsHonorCancellation()
    {
        TranslationSource project = Source("translations/runic.json", """
            {"schemaVersion":1,"catalog":"app","code":{"namespace":"App","className":"Text"},"baseLocale":"en"}
            """);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        AssertCanceled(() => TranslationCompiler.CompileProject(project, [], null, cancellation.Token));
        AssertCanceled(() => TranslationCompiler.CompileMf2Project(project, [], null, cancellation.Token));

        static void AssertCanceled(Action action)
        {
            try { action(); }
            catch (OperationCanceledException) { return; }
            throw new InvalidOperationException("A public compilation overload ignored cancellation.");
        }
    }

    private static void RejectContractMutation(byte[] exported, string before, string after, string code)
    {
        byte[] mutated = ReplaceUnitMetadata(exported, metadata => metadata.Replace(before, after, StringComparison.Ordinal));
        try { _ = TranslationInterchange.ImportXliff21(mutated); }
        catch (TranslationInterchangeException exception) when (exception.Code == code) { return; }
        throw new InvalidOperationException("XLIFF accepted mismatched contract metadata: " + before);
    }

    private static string UnitMetadata(byte[] document)
    {
        string xml = Encoding.UTF8.GetString(document);
        const string marker = "<note category=\"runic:unit\">";
        int start = xml.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        int end = xml.IndexOf("</note>", start, StringComparison.Ordinal);
        if (start < marker.Length || end < start) throw new InvalidOperationException("XLIFF unit metadata note is missing.");
        return Encoding.UTF8.GetString(Convert.FromBase64String(xml[start..end]));
    }

    private static byte[] ReplaceUnitMetadata(byte[] document, Func<string, string> mutate)
    {
        string xml = Encoding.UTF8.GetString(document);
        const string marker = "<note category=\"runic:unit\">";
        int start = xml.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        int end = xml.IndexOf("</note>", start, StringComparison.Ordinal);
        string replacement = Convert.ToBase64String(Encoding.UTF8.GetBytes(mutate(UnitMetadata(document))));
        return Encoding.UTF8.GetBytes(xml[..start] + replacement + xml[end..]);
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

    private static Rmf2ProjectCompilationV5 CompileSemantic(string english, string german, bool grouped)
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
        Rmf2ProjectCompilationV5 compilation = grouped
            ? TranslationCompiler.CompileProject(project, messages)
            : TranslationCompiler.CompileMf2Project(project, messages);
        if (!compilation.Success)
            throw new InvalidOperationException("Semantic fixture did not compile.");
        if (compilation.CatalogId != "app" || compilation.DefaultLocale != "en" ||
            !compilation.Locales.SequenceEqual(["de", "en"]) ||
            compilation.CallerFingerprint is null || compilation.SourceHash is null ||
            Rmf2ProjectCompilationV5.ExecutionProfile != "rmf2-execution-v2" ||
            Rmf2ProjectCompilationV5.MessageGrammarVersion != 5 ||
            Rmf2ProjectCompilationV5.RuntimeAbiVersion != 2)
            throw new InvalidOperationException("The public v5 compilation result omitted selected contract metadata.");
        return compilation;
    }

    private static string DirectPattern(string source) => source.StartsWith("hello =", StringComparison.Ordinal)
        ? source.Substring("hello =".Length).TrimStart(' ', '\n', '\r')
        : source;

    private static TranslationXliffExportResult Export(Rmf2ProjectCompilationV5 compilation, TranslationInterchangeReview? review = null) =>
        TranslationInterchange.ExportXliff21(compilation, review);

    private static object Preflight(Rmf2ProjectCompilationV5 compilation)
    {
        MethodInfo method = typeof(TranslationInterchange).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "PreflightXliff21" && candidate.GetParameters().Length == 1 &&
                candidate.GetParameters()[0].ParameterType.IsInstanceOfType(compilation));
        return method.Invoke(null, [compilation]) ?? throw new InvalidOperationException("XLIFF preflight returned no projection.");
    }

    private static T Property<T>(object value, string name) =>
        (T)(value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value) ??
            throw new InvalidOperationException("Missing property '" + name + "'."));

    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
}
