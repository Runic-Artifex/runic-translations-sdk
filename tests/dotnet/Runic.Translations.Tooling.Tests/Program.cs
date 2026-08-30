using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Schema;
using Runic.Translations.Tooling;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Tooling.Tests;

internal static class Program
{
    public static int Main()
    {
        try
        {
            MigrationIsCanonicalAndLossReportIsInspectable();
            MigrationCompilesToTheExistingExecutionAst();
            RejectsNonV2Inputs();
            RejectsUnknownV2Members();
            RejectsV3ProfileBoundViolations();
            XliffRoundTripsPlainCompilerModelAndReview();
            XliffReportsStructuredLossAndRejectsHostileInput();
            XliffRejectsExpandedMetadataBeforeAllocation();
            XliffPreservesV1ApprovalFingerprint();
            LocalePackV2UsesCompilerCanonicalBytes();
            InspectReportsValidLocalePackIdentityCountsAndFingerprint();
            InspectNormalizesTruncatedLocalePackRejection();
            InspectRejectsOversizedLocalePackBounds();
            InspectNormalizesTamperedArtifactVersionRejection();
            InspectReportsV3ResourceProfileAndCounts();
            InspectReportsUnsupportedXliffMetadata();
            InspectClassifiesUnknownBytes();
            Console.WriteLine("RESULT 17/17 passed");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void MigrationIsCanonicalAndLossReportIsInspectable()
    {
        const string document = """
            {"schemaVersion":2,"catalog":"app","locale":"en","layer":"base","resources":{"Message":{"$value":{"inputs":{"count":{"type":"int64"}},"selectors":[{"name":"quantity","input":"count","function":"plural"}],"variants":[{"match":{"quantity":"*"},"value":["Count: ",{"input":"count"}]}]}}}}
            """;
        SourceV3MigrationResult first = TranslationsTooling.MigrateV2ToV3(Encoding.UTF8.GetBytes(document));
        SourceV3MigrationResult second = TranslationsTooling.MigrateV2ToV3(Encoding.UTF8.GetBytes(document));
        string firstJson = Encoding.UTF8.GetString(first.DocumentBytes);
        if (firstJson != Encoding.UTF8.GetString(second.DocumentBytes)) throw new InvalidOperationException("Migration output was not deterministic.");
        if (!firstJson.Contains("\"schemaVersion\":3", StringComparison.Ordinal) || !firstJson.Contains("\"runic-mf2-subset/1\"", StringComparison.Ordinal))
            throw new InvalidOperationException("The v3 MF2-subset envelope was not emitted.");
        if (!firstJson.Contains("\"$schema\":\"https://runic-artifex.eu/schemas/translations/resources-v3.schema.json\"", StringComparison.Ordinal))
            throw new InvalidOperationException("The legacy schema URI was not replaced with the canonical v3 URI.");
        if (first.Report.StructuredMessages != 1 || first.Report.InputLeaves != 1 || !first.Report.IsLossless)
            throw new InvalidOperationException("The migration report did not describe the materialized input default.");
        SourceV3MigrationInspection inspection = TranslationsTooling.InspectV2ToV3(Encoding.UTF8.GetBytes(document));
        if (inspection.EventCount != first.Report.Losses.Count || !inspection.ReportJson.Contains("MIGV3-DEFAULT-FORMAT-MATERIALIZED", StringComparison.Ordinal))
            throw new InvalidOperationException("The machine-readable migration inspection is incomplete.");
    }

    private static void MigrationCompilesToTheExistingExecutionAst()
    {
        const string manifest = """{"schemaVersion":2,"catalog":"mf2","code":{"namespace":"Tests","className":"Mf2Text"},"defaultLocale":"en","locales":[{"tag":"en"}],"layers":[{"name":"base","priority":0}]}""";
        const string document = """
            {"schemaVersion":2,"catalog":"mf2","locale":"en","layer":"base","resources":{"Message":{"$value":{
              "inputs":{"count":{"type":"int64"},"delta":{"type":"decimal"},"owner":{"type":"string"}},
              "declarations":[{"name":"grouped","input":"count","function":"integer","format":"grouped"},{"name":"relative","input":"delta","function":"relativeTime","unit":"day","numeric":"auto"}],
              "selectors":[{"name":"quantity","input":"count","function":"plural"}],
              "variants":[{"match":{"quantity":"one"},"value":["Exactly ",{"local":"grouped"}]},{"match":{"quantity":"*"},"value":[{"markup":{"name":"strong","attributes":{"tone":"critical"},"children":[{"local":"grouped"}," items for ",{"input":"owner"}]}},", ",{"local":"relative"}]}]
            }}}}
            """;
        TranslationCompilation v2 = TranslationsTooling.Compile([new TranslationSource("manifest.json", Encoding.UTF8.GetBytes(manifest))], [new TranslationSource("v2.json", Encoding.UTF8.GetBytes(document))]);
        SourceV3MigrationResult migration = TranslationsTooling.MigrateV2ToV3(Encoding.UTF8.GetBytes(document));
        TranslationCompilation v3 = TranslationsTooling.Compile([new TranslationSource("manifest.json", Encoding.UTF8.GetBytes(manifest))], [new TranslationSource("v3.json", migration.DocumentBytes)]);
        if (!v2.Success || !v3.Success) throw new InvalidOperationException("Migrated v3 input did not compile: " + string.Join("; ", v3.Diagnostics.Select(static item => item.Id + " " + item.Message)));
        CompiledTextCatalog v2Catalog = v2.Catalogs.Single();
        CompiledTextCatalog v3Catalog = v3.Catalogs.Single();
        if (TranslationOutputRenderer.RenderCSharpCatalogData(v2Catalog).Text != TranslationOutputRenderer.RenderCSharpCatalogData(v3Catalog).Text ||
            TranslationOutputRenderer.RenderLocaleJson(v2Catalog, "en").Text != TranslationOutputRenderer.RenderLocaleJson(v3Catalog, "en").Text)
            throw new InvalidOperationException("Migrated v3 input changed generated C# or runtime locale output.");
        IReadOnlyList<TranslationGeneratedOutput> v2Esm = TranslationOutputRenderer.RenderEsmModules(v2Catalog);
        IReadOnlyList<TranslationGeneratedOutput> v3Esm = TranslationOutputRenderer.RenderEsmModules(v3Catalog);
        if (v2Esm.Count != v3Esm.Count || v2Esm.Where((output, index) => output.Text != v3Esm[index].Text).Any())
            throw new InvalidOperationException("Migrated v3 input changed generated ESM output.");
    }

    private static void RejectsNonV2Inputs()
    {
        try { _ = TranslationsTooling.MigrateV2ToV3(Encoding.UTF8.GetBytes("{\"schemaVersion\":3}")); }
        catch (SourceMigrationException exception) when (exception.Code == "MIGV3-UNSUPPORTED-SOURCE") { return; }
        throw new InvalidOperationException("The migration accepted a non-v2 source document.");
    }

    private static void RejectsUnknownV2Members()
    {
        const string unknownRoot = "{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{},\"future\":true}";
        const string unknownAst = "{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"M\":{\"$value\":{\"inputs\":{},\"selectors\":[],\"variants\":[{\"match\":{},\"value\":\"ok\",\"annotation\":true}]}}}}";
        const string duplicateRoot = "{\"schemaVersion\":2,\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{}}";
        AssertMigrationCode(unknownRoot, "MIGV3-UNKNOWN-MEMBER");
        AssertMigrationCode(unknownAst, "MIGV3-UNKNOWN-MEMBER");
        AssertMigrationCode(duplicateRoot, "MIGV3-DUPLICATE-MEMBER");
    }

    private static void RejectsV3ProfileBoundViolations()
    {
        string selectors = string.Join(',', Enumerable.Range(0, 17).Select(index => "{\"name\":\"s" + index + "\",\"input\":\"count\",\"function\":\"plural\"}"));
        string tooManySelectors = "{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"M\":{\"$value\":{\"inputs\":{\"count\":{\"type\":\"int64\"}},\"selectors\":[" + selectors + "],\"variants\":[]}}}}";
        const string invalidFunction = "{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"M\":{\"$value\":{\"inputs\":{\"count\":{\"type\":\"int64\"}},\"declarations\":[{\"name\":\"x\",\"input\":\"count\",\"function\":\"number\"}],\"selectors\":[],\"variants\":[{\"match\":{},\"value\":\"ok\"}]}}}}";
        const string duplicateTags = "{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"M\":{\"$value\":\"ok\",\"$tags\":[\"a\",\"a\"]}}}";
        AssertMigrationCode(tooManySelectors, "MIGV3-LIMIT");
        AssertMigrationCode(invalidFunction, "MIGV3-DECLARATION");
        AssertMigrationCode(duplicateTags, "MIGV3-LEAF");
    }

    private static void AssertMigrationCode(string document, string expectedCode)
    {
        try { _ = TranslationsTooling.MigrateV2ToV3(Encoding.UTF8.GetBytes(document)); }
        catch (SourceMigrationException exception) when (exception.Code == expectedCode) { return; }
        throw new InvalidOperationException("The migration did not reject the hostile source with " + expectedCode + ".");
    }

    private static void XliffRoundTripsPlainCompilerModelAndReview()
    {
        TranslationCompilation compilation = CompileInterchangeFixture();
        var review = new TranslationInterchangeReview("app", [new TranslationInterchangeReviewEntry("Common.Hello", "de", "needs-review", "Check formality.", "sha256:test")]);
        TranslationXliffExportResult first = TranslationInterchange.ExportXliff21(compilation, review);
        TranslationXliffExportResult second = TranslationInterchange.ExportXliff21(compilation, review);
        if (first.Documents.Count != 1 || !first.Documents[0].Bytes.SequenceEqual(second.Documents[0].Bytes) || !first.Report.IsLossless)
            throw new InvalidOperationException("Plain XLIFF export was not canonical and lossless.");
        ValidateOfficialCoreSchema(first.Documents);
        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(first.Documents[0].Bytes);
        string resource = Encoding.UTF8.GetString(imported.ResourceDocumentBytes);
        if (!resource.Contains("Hallo {name}", StringComparison.Ordinal) || imported.Review.Entries.Single().Note != "Check formality.")
            throw new InvalidOperationException("XLIFF import did not preserve translator text and review note.");
        TranslationCompilation roundTrip = TranslationsTooling.Compile(
            [new TranslationSource("app.catalog.json", Encoding.UTF8.GetBytes(Manifest()))],
            [new TranslationSource("app.en.json", Encoding.UTF8.GetBytes(English())), new TranslationSource("app.de.json", imported.ResourceDocumentBytes)]);
        if (!roundTrip.Success) throw new InvalidOperationException("Imported XLIFF resource is not compiler-valid.");
        byte[] reviewJson = TranslationInterchange.ExportReviewJson(review);
        if (!reviewJson.SequenceEqual(TranslationInterchange.ExportReviewJson(TranslationInterchange.ImportReviewJson(reviewJson))))
            throw new InvalidOperationException("Portable review JSON was not canonical.");
    }

    private static void XliffReportsStructuredLossAndRejectsHostileInput()
    {
        const string structured = "{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"de\",\"layer\":\"base\",\"resources\":{\"Common\":{\"Hello\":{\"$value\":{\"inputs\":{\"count\":{\"type\":\"int64\"}},\"selectors\":[{\"name\":\"quantity\",\"input\":\"count\",\"function\":\"plural\"}],\"variants\":[{\"match\":{\"quantity\":\"*\"},\"value\":\"Count {count}\"}]}}}}}";
        TranslationCompilation compilation = TranslationsTooling.Compile(
            [new TranslationSource("app.catalog.json", Encoding.UTF8.GetBytes(Manifest()))],
            [new TranslationSource("app.en.json", Encoding.UTF8.GetBytes(structured.Replace("\"locale\":\"de\"", "\"locale\":\"en\"", StringComparison.Ordinal))), new TranslationSource("app.de.json", Encoding.UTF8.GetBytes(structured))]);
        if (!compilation.Success) throw new InvalidOperationException(string.Join("; ", compilation.Diagnostics.Select(static diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
        TranslationXliffExportResult exported = TranslationInterchange.ExportXliff21(compilation);
        if (exported.Report.IsLossless || !exported.Report.Losses.Any(loss => loss.Code == "XLIFF21-STRUCTURED-MESSAGE"))
            throw new InvalidOperationException("Structured XLIFF export was not reported as lossy.");
        ValidateOfficialCoreSchema(exported.Documents);
        AssertInterchangeCode(exported.Documents.Single().Bytes, "XLIFF21-STRUCTURED-IMPORT");
    }

    private static void XliffRejectsExpandedMetadataBeforeAllocation()
    {
        string description = new string('x', 6_500_000);
        string english = "{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"Hello\":{\"$value\":\"Hello\",\"$description\":" + JsonQuote(description) + "}}}";
        const string german = "{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"de\",\"layer\":\"base\",\"resources\":{\"Hello\":\"Hallo\"}}";
        TranslationCompilation compilation = TranslationsTooling.Compile([new TranslationSource("app.catalog.json", Encoding.UTF8.GetBytes(Manifest()))], [new TranslationSource("app.en.json", Encoding.UTF8.GetBytes(english)), new TranslationSource("app.de.json", Encoding.UTF8.GetBytes(german))]);
        try { _ = TranslationInterchange.ExportXliff21(compilation); }
        catch (TranslationInterchangeException exception) when (exception.Code == "XLIFF21-LIMIT") { return; }
        throw new InvalidOperationException("Expanded XLIFF metadata did not fail within the export byte budget.");
    }

    private static void XliffPreservesV1ApprovalFingerprint()
    {
        const string manifest = "{\"schemaVersion\":1,\"catalog\":\"legacy\",\"code\":{\"namespace\":\"Legacy\",\"className\":\"Text\"},\"defaultLocale\":\"en\",\"locales\":[{\"tag\":\"en\"},{\"tag\":\"de\",\"fallback\":\"en\"}],\"layers\":[{\"name\":\"base\",\"priority\":0}]}";
        const string english = "{\"schemaVersion\":1,\"catalog\":\"legacy\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"Hello\":\"Hello\"}}";
        const string german = "{\"schemaVersion\":1,\"catalog\":\"legacy\",\"locale\":\"de\",\"layer\":\"base\",\"resources\":{\"Hello\":\"Hallo\"}}";
        TranslationCompilation compilation = TranslationsTooling.Compile([new TranslationSource("legacy.catalog.json", Encoding.UTF8.GetBytes(manifest))], [new TranslationSource("legacy.en.json", Encoding.UTF8.GetBytes(english)), new TranslationSource("legacy.de.json", Encoding.UTF8.GetBytes(german))]);
        string fingerprint = compilation.Catalogs.Single().Fingerprint;
        var approved = new TranslationInterchangeReview("legacy", [new TranslationInterchangeReviewEntry("Hello", "de", "approved", sourceFingerprint: fingerprint)]);
        TranslationXliffExportResult exported = TranslationInterchange.ExportXliff21(compilation, approved);
        ValidateOfficialCoreSchema(exported.Documents);
        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(exported.Documents.Single().Bytes);
        if (!Encoding.UTF8.GetString(imported.ResourceDocumentBytes).Contains("\"schemaVersion\":2", StringComparison.Ordinal) || imported.Review.Entries.Single().SourceFingerprint != fingerprint)
            throw new InvalidOperationException("Schema-v1 XLIFF approval did not retain the original fingerprint while emitting canonical v2 bytes.");
        try { _ = TranslationInterchange.ExportXliff21(compilation, new TranslationInterchangeReview("legacy", [new TranslationInterchangeReviewEntry("Hello", "de", "approved", sourceFingerprint: "sha256:tampered")])); }
        catch (TranslationInterchangeException exception) when (exception.Code == "REVIEW-FINGERPRINT") { return; }
        throw new InvalidOperationException("Tampered approved schema-v1 fingerprint was accepted.");
    }

    private static void LocalePackV2UsesCompilerCanonicalBytes()
    {
        TranslationCompilation compilation = CompileInterchangeFixture();
        LocalePackV2BuildResult result = TranslationsTooling.BuildLocalePackV2(compilation);
        if (result.Documents.Count != 2 || result.Documents.Any(document => document.Kind != Runic.Translations.Compiler.Generation.TranslationGeneratedOutputKind.LocaleJson) || result.Documents.Any(document => !document.Text.Contains("\"artifactVersion\":2", StringComparison.Ordinal)))
            throw new InvalidOperationException("Locale pack v2 output was not rendered from canonical compiler artifacts.");

        TranslationCompilation legacy = TranslationsTooling.Compile(
            [new TranslationSource("legacy.catalog.json", Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"catalog\":\"legacy\",\"code\":{\"namespace\":\"Legacy\",\"className\":\"Text\"},\"defaultLocale\":\"en\",\"locales\":[{\"tag\":\"en\"}],\"layers\":[{\"name\":\"base\",\"priority\":0}]}"))],
            [new TranslationSource("legacy.en.json", Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"catalog\":\"legacy\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"Hello\":\"Hello\"}}"))]);
        try { _ = TranslationsTooling.BuildLocalePackV2(legacy); }
        catch (LocalePackBuildException exception) when (exception.Code == "LOCALEPACKV2-GRAMMAR") { return; }
        throw new InvalidOperationException("Locale pack v2 accepted a v1 compiler model.");
    }

    private static void InspectReportsValidLocalePackIdentityCountsAndFingerprint()
    {
        TranslationCompilation compilation = CompileInterchangeFixture();
        LocalePackV2BuildResult packs = TranslationsTooling.BuildLocalePackV2(compilation);
        string german = Encoding.UTF8.GetString(packs.Documents.Single(document => document.RelativePath == "app.de.locale-v2.json").GetUtf8Bytes());
        ArtifactInspection first = ArtifactInspector.Inspect(Encoding.UTF8.GetBytes(german));
        ArtifactInspection second = ArtifactInspector.Inspect(Encoding.UTF8.GetBytes(german));
        if (first.Kind != "locale-pack-v2" || first.Catalog != "app" || first.Locale != "de" || first.FormatVersion != 2)
            throw new InvalidOperationException("Locale pack inspection did not identify the artifact.");
        if (first.MessageCount != 1 || !first.HasIntegrityMetadata || first.Findings.Count != 0)
            throw new InvalidOperationException("Locale pack inspection did not report counts and fingerprint presence.");
        if (first.ContractFingerprint != compilation.Catalogs.Single().Fingerprint)
            throw new InvalidOperationException("Locale pack inspection lost the contract fingerprint.");
        if (first.ToReport() != second.ToReport())
            throw new InvalidOperationException("Artifact inspection was not deterministic.");
    }

    private static void InspectNormalizesTruncatedLocalePackRejection()
    {
        LocalePackV2BuildResult packs = TranslationsTooling.BuildLocalePackV2(CompileInterchangeFixture());
        byte[] truncated = Encoding.UTF8.GetBytes(packs.Documents[0].Text);
        ArtifactInspection inspection = ArtifactInspector.Inspect(truncated.AsMemory(0, truncated.Length - 12));
        if (inspection.Kind != "locale-pack-v2" || inspection.Findings.Count != 1 || inspection.Findings[0].Code != "RTR0023/malformed")
            throw new InvalidOperationException("Truncated locale pack did not produce the normalized malformed rejection.");
        if (!inspection.Findings[0].Message.Contains("malformed JSON near byte", StringComparison.Ordinal))
            throw new InvalidOperationException("The normalized rejection lost the loader's malformed-JSON detail.");
    }

    private static void InspectRejectsOversizedLocalePackBounds()
    {
        LocalePackV2BuildResult packs = TranslationsTooling.BuildLocalePackV2(CompileInterchangeFixture());
        byte[] bytes = Encoding.UTF8.GetBytes(packs.Documents[0].Text);
        if (bytes.Length <= 64) throw new InvalidOperationException("The locale pack fixture is too small for a bounded inspection test.");
        ArtifactInspection inspection = ArtifactInspector.Inspect(bytes, maximumBytes: 64);
        if (inspection.Kind != "locale-pack-v2" || inspection.Findings.Count != 1 || inspection.Findings[0].Code != "RTR0023/limit-exceeded")
            throw new InvalidOperationException("Oversized locale pack did not produce the normalized limit rejection.");
    }

    private static void InspectNormalizesTamperedArtifactVersionRejection()
    {
        LocalePackV2BuildResult packs = TranslationsTooling.BuildLocalePackV2(CompileInterchangeFixture());
        string tampered = packs.Documents[0].Text.Replace("\"artifactVersion\":2", "\"artifactVersion\":9", StringComparison.Ordinal);
        if (tampered == packs.Documents[0].Text) throw new InvalidOperationException("The locale pack fixture did not contain an artifact version to tamper with.");
        ArtifactInspection inspection = ArtifactInspector.Inspect(Encoding.UTF8.GetBytes(tampered));
        if (inspection.Kind != "locale-pack-v2" || inspection.FormatVersion != 9 || inspection.Findings.Count != 1 || inspection.Findings[0].Code != "RTR0023/artifact-version-mismatch")
            throw new InvalidOperationException("A tampered artifact version did not produce the normalized artifact-version-mismatch rejection.");
    }

    private static void InspectReportsV3ResourceProfileAndCounts()
    {
        const string document = """
            {"schemaVersion":2,"catalog":"app","locale":"en","layer":"base","resources":{"Message":{"$value":{"inputs":{"count":{"type":"int64"}},"selectors":[{"name":"quantity","input":"count","function":"plural"}],"variants":[{"match":{"quantity":"*"},"value":["Count: ",{"input":"count"}]}]}}}}
            """;
        SourceV3MigrationResult migration = TranslationsTooling.MigrateV2ToV3(Encoding.UTF8.GetBytes(document));
        ArtifactInspection inspection = ArtifactInspector.Inspect(migration.DocumentBytes);
        if (inspection.Kind != "resources-json-v3" || inspection.FormatVersion != 3)
            throw new InvalidOperationException("Resource inspection did not detect the v3 profile.");
        if (inspection.Catalog != "app" || inspection.Locale != "en" || inspection.Layer != "base")
            throw new InvalidOperationException("Resource inspection lost the catalog identity.");
        if (inspection.ResourceCount != 1 || inspection.StructuredMessageCount != 1 || inspection.Findings.Count != 0)
            throw new InvalidOperationException("Resource inspection did not count leaves and structured messages.");
    }

    private static void InspectReportsUnsupportedXliffMetadata()
    {
        TranslationXliffExportResult export = TranslationInterchange.ExportXliff21(CompileInterchangeFixture());
        string xml = Encoding.UTF8.GetString(export.Documents.Single().Bytes);
        int notesEnd = xml.IndexOf("</notes>", StringComparison.Ordinal);
        if (notesEnd < 0) throw new InvalidOperationException("The XLIFF fixture does not contain unit notes.");
        string tampered = xml.Insert(notesEnd, "<note category=\"tool:extra\">unsupported</note>");
        ArtifactInspection inspection = ArtifactInspector.Inspect(Encoding.UTF8.GetBytes(tampered));
        if (inspection.Kind != "xliff-2.1" || inspection.Findings.Count != 1 || inspection.Findings[0].Code != "XLIFF21-METADATA")
            throw new InvalidOperationException("Unsupported XLIFF metadata was not reported as an interchange finding.");
    }

    private static void InspectClassifiesUnknownBytes()
    {
        ArtifactInspection empty = ArtifactInspector.Inspect(ReadOnlyMemory<byte>.Empty);
        byte[] binary = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        ArtifactInspection inspection = ArtifactInspector.Inspect(binary);
        if (empty.Kind != "unknown" || empty.Findings.Single().Code != "INSPECT-UNSUPPORTED-KIND")
            throw new InvalidOperationException("Empty bytes were not classified as unsupported.");
        if (inspection.Kind != "unknown" || inspection.Findings.Single().Code != "INSPECT-UNSUPPORTED-KIND")
            throw new InvalidOperationException("Unknown binary bytes were not classified as unsupported.");
    }

    private static string JsonQuote(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static TranslationCompilation CompileInterchangeFixture() => TranslationsTooling.Compile(
        [new TranslationSource("app.catalog.json", Encoding.UTF8.GetBytes(Manifest()))],
        [new TranslationSource("app.en.json", Encoding.UTF8.GetBytes(English())), new TranslationSource("app.de.json", Encoding.UTF8.GetBytes(German()))]);

    private static string Manifest() => "{\"schemaVersion\":2,\"catalog\":\"app\",\"code\":{\"namespace\":\"App\",\"className\":\"Text\"},\"defaultLocale\":\"en\",\"locales\":[{\"tag\":\"en\"},{\"tag\":\"de\",\"fallback\":\"en\"}],\"layers\":[{\"name\":\"base\",\"priority\":0}]}";
    private static string English() => "{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"Common\":{\"Hello\":{\"$value\":\"Hello {name}\",\"$description\":\"Greeting\",\"$placeholders\":{\"name\":{\"type\":\"string\",\"format\":\"none\"}}}}}}";
    private static string German() => "{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"de\",\"layer\":\"base\",\"resources\":{\"Common\":{\"Hello\":{\"$value\":\"Hallo {name}\",\"$placeholders\":{\"name\":{\"type\":\"string\",\"format\":\"none\"}}}}}}";

    private static void AssertInterchangeCode(byte[] source, string expectedCode)
    {
        try { _ = TranslationInterchange.ImportXliff21(source); }
        catch (TranslationInterchangeException exception) when (exception.Code == expectedCode) { return; }
        throw new InvalidOperationException("The XLIFF profile did not reject hostile input with " + expectedCode + ".");
    }

    private static void ValidateOfficialCoreSchema(IEnumerable<TranslationXliffDocument> documents)
    {
        string root = FindRoot();
        string schema = Path.Combine(root, "dotnet", "tests", "Runic.Translations.Tooling.Tests", "Fixtures", "xliff-2.1", "xliff_core_2.0.xsd");
        string schemaHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(schema)));
        if (schemaHash != "5686d2dbe9dac95e34d1b06a805e1e0f4999db5d5a67dc8bb8514c780592a84d") throw new InvalidOperationException("Pinned OASIS XLIFF core schema integrity check failed.");
        string xmlSchema = Path.Combine(root, "dotnet", "tests", "Runic.Translations.Tooling.Tests", "Fixtures", "xliff-2.1", "informativeCopiesOf3rdPartySchemas", "w3c", "xml.xsd");
        if (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(xmlSchema))) != "61960fb3131e38022caad5360e2f33a3382578ab3c80cd58bd74320ede61b20c") throw new InvalidOperationException("Pinned OASIS W3C XML schema integrity check failed.");
        var schemas = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() }; schemas.Add("urn:oasis:names:tc:xliff:document:2.0", schema); schemas.Compile();
        foreach (TranslationXliffDocument document in documents)
        {
            var errors = new List<string>();
            using var stream = new MemoryStream(document.Bytes);
            var settings = new XmlReaderSettings { Schemas = schemas, ValidationType = ValidationType.Schema, DtdProcessing = DtdProcessing.Prohibit };
            settings.ValidationEventHandler += (_, eventArgs) => errors.Add(eventArgs.Message);
            using XmlReader reader = XmlReader.Create(stream, settings);
            while (reader.Read()) { }
            if (errors.Count > 0) throw new InvalidOperationException("Generated XLIFF does not validate against the pinned OASIS core schema: " + errors[0]);
        }
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? current = new(Directory.GetCurrentDirectory()); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Runic.Translations.slnx"))) return current.FullName;
        throw new InvalidOperationException("Could not find repository root.");
    }
}
