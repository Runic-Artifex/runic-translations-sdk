using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;
using TUnit.Core;

namespace Runic.Translations.Tooling.Tests;

// Runs specs/translations/corpus/xliff-v3: every case imports its file against the corpus project, checks the
// outcome of every unit and the report notices, and applies the translations with the source writer.
internal sealed class XliffCorpusTests
{
    private static readonly string Corpus = Path.Combine(XliffProfileV3Tests.RepositoryRoot(), "specs", "translations", "corpus", "xliff-v3");

    public static IEnumerable<string> Cases()
    {
        using JsonDocument index = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Corpus, "index.json")));
        return index.RootElement.GetProperty("cases").EnumerateArray().Select(static item => item.GetProperty("id").GetString()!).ToArray();
    }

    [Test, MethodDataSource(nameof(Cases)), DisplayName("XLIFF v3 corpus case $id")]
    public void CorpusCase(string id)
    {
        using JsonDocument index = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Corpus, "index.json")));
        JsonElement item = index.RootElement.GetProperty("cases").EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == id);
        (TranslationSource project, TranslationSource[] sources, Rmf2ProjectCompilation compilation) = CompileProject();
        var options = new TranslationXliffImportOptions
        {
            SourceChangePolicy = item.TryGetProperty("sourceChangePolicy", out JsonElement policy) && policy.GetString() == "refuse"
                ? TranslationSourceChangePolicy.Refuse : TranslationSourceChangePolicy.NeedsReview,
        };
        TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(File.ReadAllBytes(Path.Combine(Corpus, item.GetProperty("file").GetString()!)), compilation, options);

        string[] expectedUnits = item.GetProperty("units").EnumerateObject().Select(static unit => unit.Name + "=" + unit.Value.GetString()).Order(StringComparer.Ordinal).ToArray();
        string[] actualUnits = imported.Units.Select(static unit => unit.Key + "=" + unit.Outcome switch
        {
            TranslationXliffUnitOutcome.Applied => "applied:" + Wire(unit.State!.Value),
            TranslationXliffUnitOutcome.Skipped => "skipped:" + unit.Code,
            _ => "refused:" + unit.Code,
        }).Order(StringComparer.Ordinal).ToArray();
        Require(actualUnits.SequenceEqual(expectedUnits), id + ": unit outcomes differ.\nexpected: " + string.Join(", ", expectedUnits) + "\nactual:   " + string.Join(", ", actualUnits));

        string[] expectedNotices = item.GetProperty("notices").EnumerateArray().Select(static notice => notice.GetString()!).Order(StringComparer.Ordinal).ToArray();
        string[] actualNotices = imported.Report.Losses.Select(static loss => loss.Code + " " + loss.Location).Distinct().Order(StringComparer.Ordinal).ToArray();
        Require(actualNotices.SequenceEqual(expectedNotices), id + ": notices differ: " + string.Join(", ", actualNotices));
        if (item.TryGetProperty("profileVersion", out JsonElement version))
            Require(imported.ProfileVersion == version.GetInt32(), id + ": profile version " + imported.ProfileVersion);
        if (item.TryGetProperty("sourceChanged", out JsonElement changed))
            Require(imported.Units.Where(static unit => unit.SourceChanged).Select(static unit => unit.Key).SequenceEqual(changed.EnumerateArray().Select(static key => key.GetString()!)),
                id + ": source-changed units differ.");

        // Apply the changed translations as xliff import does and compare de.rmf2.
        Rmf2LocaleV5 german = compilation.Project!.Locales.Single(locale => locale.Tag == "de");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (TranslationMf2Document message in imported.Messages)
        {
            string pattern = Encoding.UTF8.GetString(message.Bytes.Span).TrimEnd('\n');
            Rmf2TranslationV5? existing = german.DirectResources.SingleOrDefault(resource => resource.Key == message.MessageId);
            if (existing is not null && TranslationInterchangePattern.Canonical(Encoding.UTF8.GetString(existing.Message.Syntax.Source.GetUtf8Bytes())) == TranslationInterchangePattern.Canonical(pattern)) continue;
            values[message.MessageId] = pattern;
        }
        IReadOnlyList<TranslationSourceEdit> edits = TranslationInterchangeSourceWriter.Plan(Corpus, project, sources, compilation, "de", values);
        JsonElement result = item.GetProperty("result");
        if (result.ValueKind == JsonValueKind.Null)
        {
            Require(edits.Count == 0, id + ": the import changed sources: " + string.Join(", ", edits.Select(static edit => edit.Path)));
        }
        else
        {
            string expected = File.ReadAllText(Path.Combine(Corpus, result.GetString()!));
            string actual = Encoding.UTF8.GetString(edits.Single(edit => edit.Path == "project/de.rmf2").Bytes);
            Require(actual == expected, id + ": de.rmf2 differs.\n--- expected\n" + expected + "--- actual\n" + actual);
            Require(TranslationCompiler.CompileProject(project, TranslationInterchangeSourceWriter.Apply(sources, edits)).Success, id + ": the written sources do not compile.");
        }

        if (item.TryGetProperty("exportNotices", out JsonElement exportNotices))
        {
            TranslationInterchangeReview review = TranslationInterchange.ImportReviewJson(File.ReadAllBytes(Path.Combine(Corpus, "project", "shop.review.json")));
            TranslationXliffExportResult export = TranslationInterchange.ExportXliff21(compilation, review);
            Require(export.Documents.Single().Bytes.Span.SequenceEqual(File.ReadAllBytes(Path.Combine(Corpus, item.GetProperty("file").GetString()!))),
                id + ": the export differs from the golden file.");
            Require(export.Report.Losses.Select(static loss => loss.Code + " " + loss.Location).Distinct().Order(StringComparer.Ordinal)
                .SequenceEqual(exportNotices.EnumerateArray().Select(static notice => notice.GetString()!).Order(StringComparer.Ordinal)), id + ": export notices differ.");
        }
    }

    private static (TranslationSource Project, TranslationSource[] Sources, Rmf2ProjectCompilation Compilation) CompileProject()
    {
        TranslationSource project = Read("project/runic.json");
        TranslationSource[] sources = [Read("project/de.rmf2"), Read("project/en.rmf2")];
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileProject(project, sources);
        Require(compilation.Success, "The corpus project does not compile.");
        return (project, sources, compilation);
    }

    private static TranslationSource Read(string path) => new(path, File.ReadAllBytes(Path.Combine(Corpus, path)));

    private static string Wire(TranslationReviewState state) => state switch
    {
        TranslationReviewState.Draft => "draft",
        TranslationReviewState.Translated => "translated",
        TranslationReviewState.NeedsReview => "needs-review",
        _ => "approved",
    };

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
