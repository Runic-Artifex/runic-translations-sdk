using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;
using TUnit.Core;

namespace Runic.Translations.Tooling.Tests;

internal sealed class ToolingTests
{
    [Test]
    public void SemanticXliffRoundTripsDirectMf2AndReview()
    {
        Rmf2ProjectCompilation compilation = CompileSemantic("hello = Hello", "hello = Hallo", grouped: false);
        object preflight = Preflight(compilation);
        string fingerprint = Property<string>(preflight, "TextProfileFingerprint");
        var review = new TranslationInterchangeReview("app",
            [new TranslationInterchangeReviewEntry("hello", "de", TranslationReviewState.Approved, "Ready.", fingerprint)]);

        TranslationXliffExportResult first = Export(compilation, review);
        TranslationXliffExportResult second = Export(compilation, review);
        if (first.Documents.Count != 1 || !first.Documents[0].Bytes.Span.SequenceEqual(second.Documents[0].Bytes.Span) || !first.Report.IsLossless)
            throw new InvalidOperationException("Direct MF2 semantic XLIFF export was not deterministic and lossless.");

        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(first.Documents.Single().Bytes);
        TranslationInterchangeReviewEntry importedReview = imported.Review.Entries.Single();
        if (Encoding.UTF8.GetString(imported.Messages.Single().Bytes.Span) != "Hallo\n" ||
            importedReview.State != TranslationReviewState.Approved || importedReview.SourceFingerprint != fingerprint)
            throw new InvalidOperationException("Direct MF2 semantic XLIFF did not round-trip translator text and review evidence.");
    }

    // The review states keep their sidecar spelling: draft, translated, needs-review, approved.
    [Test, DisplayName("review states round-trip with their wire spelling")]
    public void ReviewStatesRoundTripWithTheirWireSpelling()
    {
        (TranslationReviewState State, string Wire)[] states =
        [
            (TranslationReviewState.Draft, "draft"), (TranslationReviewState.Translated, "translated"),
            (TranslationReviewState.NeedsReview, "needs-review"), (TranslationReviewState.Approved, "approved"),
        ];
        if (default(TranslationReviewState) != TranslationReviewState.Draft)
            throw new InvalidOperationException("The default review state must be Draft.");
        foreach ((TranslationReviewState state, string wire) in states)
        {
            var review = new TranslationInterchangeReview("app", [new TranslationInterchangeReviewEntry("hello", "de", state)]);
            byte[] json = TranslationInterchange.ExportReviewJson(review);
            if (!Encoding.UTF8.GetString(json).Contains("\"state\":\"" + wire + "\"", StringComparison.Ordinal))
                throw new InvalidOperationException($"{state} was not written as '{wire}': {Encoding.UTF8.GetString(json)}");
            if (TranslationInterchange.ImportReviewJson(json).Entries.Single().State != state)
                throw new InvalidOperationException($"'{wire}' did not read back as {state}.");
        }
        byte[] unknown = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(TranslationInterchange.ExportReviewJson(
            new TranslationInterchangeReview("app", [new TranslationInterchangeReviewEntry("hello", "de", TranslationReviewState.Draft)]))).Replace("\"draft\"", "\"final\"", StringComparison.Ordinal));
        try { TranslationInterchange.ImportReviewJson(unknown); throw new InvalidOperationException("An unknown review state was accepted."); }
        catch (TranslationInterchangeException exception) when (exception.Code == "REVIEW-INVALID") { }
        try { _ = new TranslationInterchangeReviewEntry("hello", "de", (TranslationReviewState)42); throw new InvalidOperationException("An undefined review state was accepted."); }
        catch (ArgumentOutOfRangeException) { }
    }

    [Test, DisplayName("artifact kinds report their wire spelling and default to Unknown")]
    public void ArtifactKindsReportTheirWireSpelling()
    {
        if (default(ArtifactKind) != ArtifactKind.Unknown) throw new InvalidOperationException("The default artifact kind must be Unknown.");
        ArtifactInspection empty = ArtifactInspector.Inspect(ReadOnlyMemory<byte>.Empty);
        if (empty.Kind != ArtifactKind.Unknown || !empty.ToReport().StartsWith("kind: unknown\n", StringComparison.Ordinal))
            throw new InvalidOperationException("An empty artifact did not report kind unknown: " + empty.ToReport());
        ArtifactInspection malformed = ArtifactInspector.Inspect(Encoding.UTF8.GetBytes("<xliff version=\"2.1\""));
        if (malformed.Kind != ArtifactKind.Xliff || !malformed.ToReport().StartsWith("kind: xliff\n", StringComparison.Ordinal))
            throw new InvalidOperationException("A malformed XLIFF document did not report kind xliff: " + malformed.ToReport());
        TranslationXliffDocument xliff = Export(CompileSemantic("hello = Hello", "hello = Hallo", grouped: false)).Documents.Single();
        if (!ArtifactInspector.Inspect(xliff.Bytes).ToReport().StartsWith("kind: xliff-2.1\n", StringComparison.Ordinal))
            throw new InvalidOperationException("An XLIFF 2.1 document did not report kind xliff-2.1.");
    }

    [Test]
    public void SemanticXliffAcceptsGroupedRmf2()
    {
        Rmf2ProjectCompilation compilation = CompileSemantic("hello = Hello", "hello = Hallo", grouped: true);
        TranslationXliffExportResult exported = Export(compilation);
        if (exported.Documents.Count != 1 || !exported.Report.IsLossless)
            throw new InvalidOperationException("Grouped RMF2 input did not reach the semantic interchange path.");
    }

    [Test]
    public void SemanticXliffReportsStructuredLossAndRefusesImport()
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

    [Test]
    public void SemanticXliffNamesTheDocumentProfile()
    {
        TranslationXliffExportResult exported = Export(CompileSemantic(
            "notice = {#p}Read this.{/p}\ninline = See {#strong}this{/strong}.",
            "notice = {#p}Lies das.{/p}\ninline = Siehe {#strong}das{/strong}.", grouped: true));
        TranslationInterchangeLoss document = exported.Report.Losses.Single(loss => loss.Location == "/notice");
        TranslationInterchangeLoss inline = exported.Report.Losses.Single(loss => loss.Location == "/inline");
        if (document.Code != "XLIFF21-STRUCTURED-MESSAGE" || !document.SemanticLoss || !document.Message.Contains("document profile", StringComparison.Ordinal) ||
            inline.Code != "XLIFF21-STRUCTURED-MESSAGE" || inline.Message.Contains("document profile", StringComparison.Ordinal))
            throw new InvalidOperationException("The XLIFF loss report does not name the document profile for document messages only.");
        try { _ = TranslationInterchange.ImportXliff21(exported.Documents.Single().Bytes); }
        catch (TranslationInterchangeException exception) when (exception.Code == "XLIFF21-STRUCTURED-IMPORT") { return; }
        throw new InvalidOperationException("A document message was imported from the XLIFF text profile.");
    }

    [Test]
    public void XliffRefusesStructuredTextWithStaleMetadata()
    {
        byte[] exported = Export(CompileSemantic("hello = Hello", "hello = Hallo", grouped: false)).Documents.Single().Bytes.ToArray();
        string text = Encoding.UTF8.GetString(exported);
        const string original = "<target>Hallo</target>";
        if (!text.Contains(original, StringComparison.Ordinal))
            throw new InvalidOperationException("The plain XLIFF fixture did not contain the expected target text.");
        byte[] tampered = Encoding.UTF8.GetBytes(text.Replace(original, "<target>{{{|Hallo|}}}</target>", StringComparison.Ordinal));
        try { _ = TranslationInterchange.ImportXliff21(tampered); }
        catch (TranslationInterchangeException exception) when (exception.Code == "XLIFF21-STRUCTURED-IMPORT") { return; }
        throw new InvalidOperationException("Structured XLIFF text bypassed refusal through stale runic:unit metadata.");
    }

    [Test]
    public void XliffPreflightSeparatesTextContractAndFreshness()
    {
        Rmf2ProjectCompilation first = CompileSemantic("hello = Hello", "hello = Hallo", grouped: false);
        Rmf2ProjectCompilation changed = CompileSemantic("hello = Hello again", "hello = Hallo", grouped: false);
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

    [Test]
    public void XliffRequiresSelectedV5ContractIdentity()
    {
        byte[] exported = Export(CompileSemantic("hello = Hello", "hello = Hallo", grouped: false)).Documents.Single().Bytes.ToArray();
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

    [Test]
    public void PublicCompilationOverloadsHonorCancellation()
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

    [Test]
    public void ArtifactInspectionRecognizesXliff()
    {
        TranslationXliffDocument xliff = Export(CompileSemantic("hello = Hello", "hello = Hallo", grouped: false)).Documents.Single();
        ArtifactInspection inspection = ArtifactInspector.Inspect(xliff.Bytes);
        if (inspection.Kind != ArtifactKind.Xliff21 || inspection.Catalog != "app" || inspection.Locale != "de" || inspection.Findings.Count != 0)
            throw new InvalidOperationException("Artifact inspection did not recognize the generated XLIFF document.");
    }

    // The package's stable surface: compile, interchange, inspection and scaffolding. The tool's command plumbing,
    // the workspace transactions, the editor state, the MF2 syntax tree and the language service are internal.
    [Test, DisplayName("Tooling exports only the stable compile, interchange, inspection and scaffolding surface")]
    public void ExportsOnlyTheStableSurface()
    {
        string[] expected =
        [
            "Runic.Translations.Authoring.TranslationAuthoringException",
            "Runic.Translations.Authoring.TranslationProjectCreationRequest",
            "Runic.Translations.Authoring.TranslationProjectFile",
            "Runic.Translations.Authoring.TranslationProjectLocale",
            "Runic.Translations.Authoring.TranslationProjectPlan",
            "Runic.Translations.Authoring.TranslationProjectScaffolder",
            "Runic.Translations.Authoring.TranslationProjectWriter",
            "Runic.Translations.Compiler.Rmf2ProjectCompilation",
            "Runic.Translations.Compiler.TextSourceLocation",
            "Runic.Translations.Compiler.TranslationCompiler",
            "Runic.Translations.Compiler.TranslationCompilerOptions",
            "Runic.Translations.Compiler.TranslationDiagnostic",
            "Runic.Translations.Compiler.TranslationDiagnosticSeverity",
            "Runic.Translations.Compiler.TranslationSource",
            "Runic.Translations.Tooling.ArtifactInspection",
            "Runic.Translations.Tooling.ArtifactInspectionFinding",
            "Runic.Translations.Tooling.ArtifactInspector",
            "Runic.Translations.Tooling.ArtifactKind",
            "Runic.Translations.Tooling.TranslationInterchange",
            "Runic.Translations.Tooling.TranslationInterchangeException",
            "Runic.Translations.Tooling.TranslationInterchangeLoss",
            "Runic.Translations.Tooling.TranslationInterchangeReport",
            "Runic.Translations.Tooling.TranslationInterchangeReview",
            "Runic.Translations.Tooling.TranslationInterchangeReviewEntry",
            "Runic.Translations.Tooling.TranslationMf2Document",
            "Runic.Translations.Tooling.TranslationReviewState",
            "Runic.Translations.Tooling.TranslationXliffDocument",
            "Runic.Translations.Tooling.TranslationXliffExportResult",
            "Runic.Translations.Tooling.TranslationXliffImportResult",
        ];
        string[] exported = new[] { typeof(TranslationInterchange).Assembly, typeof(TranslationCompiler).Assembly, Assembly.Load("Runic.Translations.Authoring") }
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!exported.SequenceEqual(expected))
            throw new InvalidOperationException("The Tooling package exports another surface: " + string.Join(", ", exported.Except(expected)) +
                " added; " + string.Join(", ", expected.Except(exported)) + " missing.");
        if (typeof(TranslationInterchange).Assembly.GetReferencedAssemblies().Any(name => name.Name!.StartsWith("Runic.CommandLine", StringComparison.Ordinal)))
            throw new InvalidOperationException("Runic.Translations.Tooling must not reference Runic.CommandLine.");
    }

    // The harmless level is zero, as in Runic SDK: an uninitialized severity never reads as an error.
    [Test, DisplayName("diagnostic severity orders Information, Warning, Error from zero")]
    public void SeverityIsOrderedFromInformation()
    {
        if (default(TranslationDiagnosticSeverity) != TranslationDiagnosticSeverity.Information ||
            (int)TranslationDiagnosticSeverity.Warning != 1 || (int)TranslationDiagnosticSeverity.Error != 2)
            throw new InvalidOperationException("TranslationDiagnosticSeverity must be Information = 0, Warning = 1, Error = 2.");
    }

    private static Rmf2ProjectCompilation CompileSemantic(string english, string german, bool grouped)
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
        Rmf2ProjectCompilation compilation = grouped
            ? TranslationCompiler.CompileProject(project, messages)
            : TranslationCompiler.CompileMf2Project(project, messages);
        if (!compilation.Success)
            throw new InvalidOperationException("Semantic fixture did not compile.");
        if (compilation.CatalogId != "app" || compilation.DefaultLocale != "en" ||
            !compilation.Locales.SequenceEqual(["de", "en"]) ||
            compilation.CallerFingerprint is null || compilation.SourceHash is null ||
            compilation.ExecutionProfile != "rmf2-execution-v2" ||
            compilation.MessageGrammarVersion != 5 ||
            compilation.RuntimeAbiVersion != 3)
            throw new InvalidOperationException("The public compilation result omitted selected contract metadata.");
        return compilation;
    }

    private static string DirectPattern(string source) => source.StartsWith("hello =", StringComparison.Ordinal)
        ? source.Substring("hello =".Length).TrimStart(' ', '\n', '\r')
        : source;

    private static TranslationXliffExportResult Export(Rmf2ProjectCompilation compilation, TranslationInterchangeReview? review = null) =>
        TranslationInterchange.ExportXliff21(compilation, review);

    private static object Preflight(Rmf2ProjectCompilation compilation)
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
