using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Runic.CommandLine;
using Runic.Translations.Tooling;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Tool;

/// <summary>File-bound adapters for the closed v0.2 Tooling operations.</summary>
internal static class ToolingOperations
{
    private const int Success = 0;
    private const int DiagnosticFailure = 1;
    private const int InvocationFailure = 2;

    internal static void Migrate(string source, string output, string? report, ToolOperationResult operationResult)
    {
        SourceV3MigrationResult migration = TranslationsTooling.MigrateV2ToV3(File.ReadAllBytes(source));
        Write(output, migration.DocumentBytes);
        if (report is not null) Write(report, Encoding.UTF8.GetBytes(migration.Report.ToJson()));
        operationResult.WriteOutputLine($"migrated '{source}' to schema v3.");
    }

    internal static void ExportXliff(string catalog, IReadOnlyList<string> documents, string output, string? reviewPath, ToolOperationResult operationResult)
    {
        TranslationCompilation compilation = Compile(catalog, documents);
        if (!compilation.Success) { WriteCompilerDiagnostics(compilation, operationResult); operationResult.ExitCode = DiagnosticFailure; return; }
        TranslationInterchangeReview? review = reviewPath is not null
            ? TranslationInterchange.ImportReviewJson(File.ReadAllBytes(reviewPath))
            : null;
        TranslationXliffExportResult export = TranslationInterchange.ExportXliff21(compilation, review);
        Directory.CreateDirectory(output);
        foreach (TranslationXliffDocument document in export.Documents)
            Write(Path.Combine(output, document.CatalogId + "." + document.TargetLocale + ".xliff"), document.Bytes);
        Write(Path.Combine(output, "runic-xliff-report.json"), RenderReport(export.Report));
        operationResult.WriteOutputLine($"exported {export.Documents.Count} XLIFF document(s).");
        operationResult.ExitCode = export.Report.IsLossless ? Success : DiagnosticFailure;
    }

    internal static void ImportXliff(string source, string output, string? reviewOutput, ToolOperationResult operationResult)
    {
        TranslationXliffImportResult import = TranslationInterchange.ImportXliff21(File.ReadAllBytes(source));
        Directory.CreateDirectory(output);
        Write(Path.Combine(output, import.CatalogId + "." + import.TargetLocale + ".json"), import.ResourceDocumentBytes);
        string review = reviewOutput ?? Path.Combine(output, "runic-review.json");
        Write(review, TranslationInterchange.ExportReviewJson(import.Review));
        operationResult.WriteOutputLine($"imported XLIFF for {import.CatalogId}/{import.TargetLocale}.");
    }

    internal static void ExportReview(string catalog, string output, ToolOperationResult operationResult)
    {
        Write(output, TranslationInterchange.ExportReviewJson(new TranslationInterchangeReview(catalog, [])));
        operationResult.WriteOutputLine("exported empty review ledger.");
    }

    internal static void ImportReview(string source, ToolOperationResult operationResult)
    {
        TranslationInterchangeReview review = TranslationInterchange.ImportReviewJson(File.ReadAllBytes(source));
        operationResult.WriteOutput(Encoding.UTF8.GetString(TranslationInterchange.ExportReviewJson(review)));
    }

    internal static void ReportReview(string source, ToolOperationResult operationResult)
    {
        TranslationInterchangeReview review = TranslationInterchange.ImportReviewJson(File.ReadAllBytes(source));
        foreach (IGrouping<string, TranslationInterchangeReviewEntry> group in review.Entries.GroupBy(static entry => entry.State).OrderBy(static group => group.Key, StringComparer.Ordinal))
            operationResult.WriteOutputLine($"{group.Key}: {group.Count()}");
    }

    internal static void BuildLocalePack(string catalog, IReadOnlyList<string> documents, string output, ToolOperationResult operationResult)
    {
        TranslationCompilation compilation = Compile(catalog, documents);
        if (!compilation.Success) { WriteCompilerDiagnostics(compilation, operationResult); operationResult.ExitCode = DiagnosticFailure; return; }
        LocalePackV2BuildResult pack = TranslationsTooling.BuildLocalePackV2(compilation);
        Directory.CreateDirectory(output);
        foreach (TranslationGeneratedOutput document in pack.Documents)
            Write(Path.Combine(output, document.RelativePath), document.GetUtf8Bytes());
        operationResult.WriteOutputLine($"built {pack.Documents.Count} locale-pack-v2 document(s).");
    }

    internal static void Inspect(string source, ToolOperationResult operationResult)
    {
        ArtifactInspection inspection = ArtifactInspector.Inspect(File.ReadAllBytes(source));
        operationResult.WriteOutput(inspection.ToReport());
        if (inspection.Findings.Count > 0)
        {
            operationResult.ExitCode = DiagnosticFailure;
            operationResult.ExitCategory = CommandExitCategory.Validation;
        }
    }

    private static TranslationCompilation Compile(string catalog, IReadOnlyList<string> documents)
    {
        if (documents.Count == 0) throw new ToolUsageException("xliff-export requires --documents <path-or-glob...>.");
        CompilerInputs inputs = InputFiles.Read(catalog, documents);
        return TranslationsTooling.Compile([inputs.Catalog], inputs.Documents);
    }

    private static void WriteCompilerDiagnostics(TranslationCompilation compilation, ToolOperationResult result)
    {
        foreach (TranslationDiagnostic diagnostic in compilation.Diagnostics)
        {
            TextSourceLocation location = diagnostic.Location;
            result.AddDiagnostic("RCLI9012", "translation-diagnostic", $"{location.Path.Replace('\\', '/')}({location.Line},{location.Column},{location.EndLine},{location.EndColumn}): {diagnostic.Severity.ToString().ToLowerInvariant()}: {diagnostic.Id}: {diagnostic.Message}", diagnostic.Severity == TranslationDiagnosticSeverity.Error ? CommandDiagnosticSeverity.Error : CommandDiagnosticSeverity.Warning, diagnostic.Id);
        }
    }

    private static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null) Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, bytes.ToArray());
    }

    private static byte[] RenderReport(TranslationInterchangeReport report)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("isLossless", report.IsLossless);
            writer.WritePropertyName("losses");
            writer.WriteStartArray();
            foreach (TranslationInterchangeLoss loss in report.Losses)
            {
                writer.WriteStartObject();
                writer.WriteString("code", loss.Code);
                writer.WriteString("location", loss.Location);
                writer.WriteString("message", loss.Message);
                writer.WriteBoolean("semanticLoss", loss.SemanticLoss);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

}
