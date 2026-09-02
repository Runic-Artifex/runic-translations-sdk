using System;
using System.Linq;
using System.Text;
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
            LocalePackUsesCanonicalCompilerBytes();
            ArtifactInspectionRecognizesGeneratedOutputs();
            Console.WriteLine("RESULT 5/5 passed");
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

    private static TranslationCompilation CompilePlainFixture() => Compile("Hello", "Hallo");

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
