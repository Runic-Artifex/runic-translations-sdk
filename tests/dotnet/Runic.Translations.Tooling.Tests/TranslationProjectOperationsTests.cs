using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;
using TUnit.Core;

namespace Runic.Translations.Tooling.Tests;

// The status report, locale changes and catalog import plans that the command line and the Translations Editor share.
internal sealed class TranslationProjectOperationsTests
{
    private const string Project = """
        {
          "schemaVersion": 1,
          "catalog": "app",
          "code": { "namespace": "App", "className": "Text" },
          "baseLocale": "en",
          "locales": [
            "en",
            "de",
            "fr",
            { "tag": "de-CH", "fallback": "de", "validation": { "translationCompleteness": "warning" } }
          ],
          "validation": { "translationCompleteness": "allow" }
        }

        """;

    [Test, DisplayName("status counts translated, fallback and missing messages, applies thresholds and reports each locale's completeness policy")]
    public void StatusReport()
    {
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(Source("translations/runic.json", Project),
        [
            Source("translations/en.rmf2", "a = A\nb = B\nc = C\n"),
            Source("translations/de.rmf2", "a = A de\nb = B de\n"),
            Source("translations/de-CH.rmf2", "a = A ch\n"),
            Source("translations/fr.rmf2", "a = A fr\nb = B fr\nc = C fr\n"),
        ]);
        Require(compilation.Success, Messages(compilation.Diagnostics));
        Dictionary<string, double> thresholds = TranslationStatus.ParseThresholds(["de=50", "*=90"]);
        TranslationsStatusProject report = TranslationStatus.Report("translations\\runic.json", compilation, null, "ignored", thresholds);

        Require(report is { Project: "translations/runic.json", Catalog: "app", BaseLocale: "en", Messages: 3, ReviewFile: null }, "The project report differs.");
        Require(string.Join(",", report.Locales.Select(static locale => locale.Locale)) == "en,de,de-CH,fr", "The locales are not base first, then by tag.");
        Require(report.Locales[0] is { Translated: 3, Coverage: 100, Completeness: null, MinimumCoverage: null }, "The base locale differs.");
        Require(report.Locales[1] is { Translated: 2, Fallback: 0, Missing: 1, Coverage: 66.66, Completeness: "allow", MinimumCoverage: 50, BelowThreshold: false }, "de differs.");
        Require(report.Locales[2] is { FallbackLocale: "de", Translated: 1, Fallback: 1, Missing: 1, Completeness: "warning", MinimumCoverage: 90, BelowThreshold: true }, "de-CH differs.");
        Require(report.Locales[3] is { Translated: 3, Missing: 0, Completeness: "allow", BelowThreshold: false }, "fr differs.");
        Require(new TranslationsStatusResult([report]).ToHumanOutput().Contains("  de-CH                1         1        1    66.66%  below 90%", StringComparison.Ordinal), "The table row differs.");

        Throws<TranslationToolingUsageException>(() => TranslationStatus.ParseThresholds(["de=101"]), "A percentage above 100 was accepted.");
        Throws<TranslationToolingUsageException>(() => TranslationStatus.ParseThresholds(["de=50,de=60"]), "A locale named twice was accepted.");
    }

    [Test, DisplayName("locale add with copyFrom seeds the new locale's files with the copied locale's sources")]
    public void AddCopiesLocale()
    {
        // Completeness warnings, so the plan counts the new locale's untranslated messages (RTR0010).
        string project = Project.Replace("\"validation\": { \"translationCompleteness\": \"allow\" }", "\"validation\": { \"translationCompleteness\": \"warning\" }", StringComparison.Ordinal);
        using var directory = new TemporaryProject();
        directory.Write("translations/runic.json", project);
        directory.Write("translations/en.rmf2", "a = A\nb = B\n");
        directory.Write("translations/de.rmf2", "a = A de\nb = B de\n");
        directory.Write("translations/fr.rmf2", "a = A fr\n");
        directory.Write("translations/feature/en.rmf2", "c = C\n");

        TranslationLocalePlan plan = TranslationLocaleChanges.PlanAdd(directory.Sources(), "it", "fr", copyFrom: "de");
        Require(plan is { Locale: "it", Fallback: "fr" } && plan.Errors.Count == 0, Messages(plan.Errors));
        Require(plan.ProjectPath == directory.Resolve("translations/runic.json"), "The project path is not resolved against the base directory: " + plan.ProjectPath);
        Require(Encoding.UTF8.GetString(plan.ProjectBytes) == project.Replace(
            "{ \"tag\": \"de-CH\", \"fallback\": \"de\", \"validation\": { \"translationCompleteness\": \"warning\" } }",
            "{ \"tag\": \"de-CH\", \"fallback\": \"de\", \"validation\": { \"translationCompleteness\": \"warning\" } },\n    { \"tag\": \"it\", \"fallback\": \"fr\" }", StringComparison.Ordinal),
            Encoding.UTF8.GetString(plan.ProjectBytes));
        Require(plan.CreatedFiles.Count == 2, "Not one file per base-locale file: " + string.Join(", ", plan.CreatedFiles.Select(static file => file.Path)));
        TranslationLocaleFile copied = plan.CreatedFiles.Single(file => file.Path == directory.Resolve("translations/it.rmf2"));
        Require(Encoding.UTF8.GetString(copied.Bytes) == "a = A de\nb = B de\n", "The file is not a copy of de.rmf2.");
        // A directory without a de file gets an empty one, as without copyFrom.
        Require(plan.CreatedFiles.Single(file => file.Path == directory.Resolve("translations/feature/it.rmf2")).Bytes.Length == 0, "The feature file is not empty.");
        Require(plan.Untranslated == 1, "The untranslated count differs: " + plan.Untranslated);

        TranslationLocalePlan empty = TranslationLocaleChanges.PlanAdd(directory.Sources(), "it", null);
        Require(empty.CreatedFiles.All(static file => file.Bytes.Length == 0) && empty.Untranslated == 3, "Without copyFrom the files are not empty.");
        Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanAdd(directory.Sources(), "it", null, copyFrom: "es"), "An undeclared copyFrom locale was accepted.");
    }

    [Test, DisplayName("locale add with copyFrom copies a direct .mf2 locale directory")]
    public void AddCopiesDirectLocale()
    {
        using var directory = new TemporaryProject();
        directory.Write("translations/runic.json", """
            {
              "schemaVersion": 1,
              "catalog": "app",
              "code": { "namespace": "App", "className": "Text" },
              "baseLocale": "en",
              "locales": ["en", "de"],
              "validation": { "translationCompleteness": "allow" }
            }
            """);
        directory.Write("translations/en/greet.mf2", "Hello");
        directory.Write("translations/de/greet.mf2", "Hallo");

        TranslationLocalePlan plan = TranslationLocaleChanges.PlanAdd(directory.Sources(), "fr", null, copyFrom: "de");
        Require(plan.Errors.Count == 0, Messages(plan.Errors));
        Require(plan.CreatedFiles.Single() is { } file && file.Path == directory.Resolve("translations/fr/greet.mf2") && Encoding.UTF8.GetString(file.Bytes) == "Hallo",
            "The direct file was not copied: " + string.Join(", ", plan.CreatedFiles.Select(static item => item.Path)));
        Require(TranslationLocaleChanges.PlanAdd(directory.Sources(), "fr", null).CreatedFiles.Count == 0, "Without copyFrom a direct project gained a file.");
    }

    [Test, DisplayName("locale remove with a replacement fallback re-links the dependent locales instead of refusing")]
    public void RemoveReplacesFallback()
    {
        using var directory = new TemporaryProject();
        directory.Write("translations/runic.json", Project);
        directory.Write("translations/en.rmf2", "a = A\n");
        directory.Write("translations/de.rmf2", "a = A de\n");
        directory.Write("translations/de-CH.rmf2", "a = A ch\n");
        directory.Write("translations/fr.rmf2", "a = A fr\n");

        Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanRemove(directory.Sources(), "de"), "A fallback locale was removed without a replacement.");
        Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanRemove(directory.Sources(), "de", replacementFallback: "de-CH"), "A dependent became its own fallback.");
        TranslationLocalePlan plan = TranslationLocaleChanges.PlanRemove(directory.Sources(), "de", replacementFallback: "fr");
        Require(plan.Errors.Count == 0 && plan.Compilation.Success, Messages(plan.Compilation.Diagnostics));
        Require(Encoding.UTF8.GetString(plan.ProjectBytes) == Project
            .Replace("    \"de\",\n", "", StringComparison.Ordinal)
            .Replace("\"fallback\": \"de\"", "\"fallback\": \"fr\"", StringComparison.Ordinal), Encoding.UTF8.GetString(plan.ProjectBytes));
        Require(plan.ChangedFallbacks.SequenceEqual(["de-CH"]), "The changed fallbacks differ.");
        Require(plan.DeletedFiles.SequenceEqual([directory.Resolve("translations/de.rmf2")]), "The deleted files differ: " + string.Join(", ", plan.DeletedFiles));
        Require(plan.ReviewPath == directory.Resolve("translations/app.review.json") && plan.ReviewEntries == 0, "The review file differs.");
    }

    [Test, DisplayName("locale remove and set fallback refuse a fallback cycle anywhere along the chain")]
    public void FallbackCyclesAlongTheChain()
    {
        // en; de → en; de-AT → de; x → de-AT.
        using var directory = new TemporaryProject();
        directory.Write("translations/runic.json", """
            {
              "schemaVersion": 1,
              "catalog": "app",
              "code": { "namespace": "App", "className": "Text" },
              "baseLocale": "en",
              "locales": ["en", "de", { "tag": "de-AT", "fallback": "de" }, { "tag": "it", "fallback": "de-AT" }, "fr"],
              "validation": { "translationCompleteness": "allow" }
            }
            """);
        directory.Write("translations/en.rmf2", "a = A\n");

        // Removing de re-links de-AT to it, which falls back to de-AT: it → de-AT → it.
        string removal = Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanRemove(directory.Sources(), "de", replacementFallback: "it"), "A cycle two levels down was accepted.").Message;
        Require(removal.Contains("it → de-AT → it", StringComparison.Ordinal), removal);
        Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanRemove(directory.Sources(), "de", replacementFallback: "de-AT"), "A dependent became its own fallback.");
        Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanRemove(directory.Sources(), "de", replacementFallback: "de"), "The removed locale became the replacement.");
        Require(TranslationLocaleChanges.PlanRemove(directory.Sources(), "de", replacementFallback: "fr").Errors.Count == 0, "A replacement without a cycle was refused.");

        // de → it comes back to de through de-AT; fr → it does not.
        string set = Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanSetFallback(directory.Sources(), "de", "it"), "A cycle through two locales was accepted.").Message;
        Require(set.Contains("de → it → de-AT → de", StringComparison.Ordinal), set);
        Require(TranslationLocaleChanges.PlanSetFallback(directory.Sources(), "fr", "it").Errors.Count == 0, "A fallback without a cycle was refused.");
    }

    [Test, DisplayName("set fallback edits one locale entry and keeps the rest of runic.json byte for byte")]
    public void SetFallbackPreservesFormatting()
    {
        const string plain = "{\r\n  \"schemaVersion\": 1,\r\n  \"catalog\": \"app\",\r\n  \"code\": { \"namespace\": \"App\", \"className\": \"Text\" },\r\n  \"baseLocale\": \"en\",\r\n  \"locales\": [\"en\",  \"de\", \"fr\"],\r\n  \"validation\": { \"translationCompleteness\": \"allow\" }\r\n}\r\n";
        using var directory = new TemporaryProject();
        directory.Write("translations/runic.json", plain);
        directory.Write("translations/en.rmf2", "a = A\n");
        directory.Write("translations/de.rmf2", "a = A de\n");

        // A plain tag becomes an object, and back to the plain tag when it falls back to the base locale again.
        TranslationLocalePlan toGerman = TranslationLocaleChanges.PlanSetFallback(directory.Sources(), "fr", "de");
        string linked = plain.Replace("\"de\", \"fr\"]", "\"de\", { \"tag\": \"fr\", \"fallback\": \"de\" }]", StringComparison.Ordinal);
        Require(Encoding.UTF8.GetString(toGerman.ProjectBytes) == linked && toGerman.Errors.Count == 0 && toGerman.Untranslated == 0, Encoding.UTF8.GetString(toGerman.ProjectBytes));
        directory.Write("translations/runic.json", linked);
        TranslationLocalePlan toBase = TranslationLocaleChanges.PlanSetFallback(directory.Sources(), "fr", "en");
        Require(Encoding.UTF8.GetString(toBase.ProjectBytes) == plain, Encoding.UTF8.GetString(toBase.ProjectBytes));

        // An object with other members keeps them; only the fallback value changes.
        string withPolicy = plain.Replace("\"fr\"]", "{ \"tag\": \"fr\",   \"fallback\": \"de\", \"validation\": { \"translationCompleteness\": \"warning\" } }]", StringComparison.Ordinal);
        directory.Write("translations/runic.json", withPolicy);
        TranslationLocalePlan kept = TranslationLocaleChanges.PlanSetFallback(directory.Sources(), "fr", "en");
        Require(Encoding.UTF8.GetString(kept.ProjectBytes) == withPolicy.Replace("\"fallback\": \"de\"", "\"fallback\": \"en\"", StringComparison.Ordinal), Encoding.UTF8.GetString(kept.ProjectBytes));

        Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanSetFallback(directory.Sources(), "en", "de"), "The base locale got a fallback.");
        Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanSetFallback(directory.Sources(), "fr", "fr"), "A locale fell back to itself.");
        Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanSetFallback(directory.Sources(), "fr", "es"), "An undeclared fallback was accepted.");
    }

    [Test, DisplayName("locale add, set fallback and remove span every source root of a mounted project")]
    public void LocalesAcrossSourceRoots()
    {
        const string project = """
            {
              "schemaVersion": 1,
              "catalog": "app",
              "code": { "namespace": "App", "className": "Text" },
              "baseLocale": "en",
              "sourceRoots": [
                { "path": "../base", "namespace": [] },
                { "path": "../feature", "namespace": ["shop"] }
              ],
              "locales": [
                { "tag": "en" },
                { "tag": "de", "fallback": "en" },
                { "tag": "fr", "fallback": "de" }
              ]
            }

            """;
        using var directory = new TemporaryProject();
        directory.Write("translations/runic.json", project);
        directory.Write("base/en.rmf2", "amount = Amount\n");
        directory.Write("base/de.rmf2", "amount = Betrag\n");
        directory.Write("base/fr.rmf2", "amount = Montant\n");
        directory.Write("feature/en.rmf2", "title = Shop\n");
        directory.Write("feature/de.rmf2", "title = Laden\n");
        directory.Write("feature/fr.rmf2", "title = Boutique\n");
        TranslationProjectSources sources = directory.Sources("base", "feature");

        // Copies keep each source root's directory and bytes.
        TranslationLocalePlan add = TranslationLocaleChanges.PlanAdd(sources, "it", "de", copyFrom: "en");
        Require(add.Errors.Count == 0 && add.Compilation.Success, Messages(add.Compilation.Diagnostics));
        Require(add.CreatedFiles.Select(static file => file.Path).Order(StringComparer.Ordinal)
            .SequenceEqual([directory.Resolve("base/it.rmf2"), directory.Resolve("feature/it.rmf2")]), "The copies are not beside the base-locale files: " + string.Join(", ", add.CreatedFiles.Select(static file => file.Path)));
        Require(add.CreatedFiles.All(file => file.Bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(file.Path)!, "en.rmf2")))), "A copy changed bytes.");
        Require(Encoding.UTF8.GetString(add.ProjectBytes).Contains("\"sourceRoots\": [\n    { \"path\": \"../base\"", StringComparison.Ordinal), "The source roots changed.");

        // Fallback changes check the whole graph and keep the source roots.
        Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanSetFallback(sources, "de", "fr"), "A fallback cycle was accepted.");
        TranslationLocalePlan fallback = TranslationLocaleChanges.PlanSetFallback(sources, "fr", "en");
        Require(Encoding.UTF8.GetString(fallback.ProjectBytes) == project.Replace("{ \"tag\": \"fr\", \"fallback\": \"de\" }", "{ \"tag\": \"fr\", \"fallback\": \"en\" }", StringComparison.Ordinal),
            Encoding.UTF8.GetString(fallback.ProjectBytes));

        // Removal deletes the locale's file in every source root and re-links its dependents.
        Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanRemove(sources, "en"), "The base locale was removed.");
        TranslationLocalePlan remove = TranslationLocaleChanges.PlanRemove(sources, "de", replacementFallback: "en");
        Require(remove.Errors.Count == 0 && remove.Compilation.Success, Messages(remove.Compilation.Diagnostics));
        Require(remove.DeletedFiles.Order(StringComparer.Ordinal).SequenceEqual([directory.Resolve("base/de.rmf2"), directory.Resolve("feature/de.rmf2")]),
            "The deleted files differ: " + string.Join(", ", remove.DeletedFiles));
        Require(remove.ChangedFallbacks.SequenceEqual(["fr"]), "The dependent was not re-linked.");
        string removed = Encoding.UTF8.GetString(remove.ProjectBytes);
        Require(!removed.Contains("\"de\"", StringComparison.Ordinal) && removed.Contains("{ \"tag\": \"fr\", \"fallback\": \"en\" }", StringComparison.Ordinal), removed);
    }

    [Test, DisplayName("locale changes refuse a runic.json whose root is not an object")]
    public void LocalesRefuseNonObjectProject()
    {
        using var directory = new TemporaryProject();
        directory.Write("translations/runic.json", """["not", "an", "object"]""");
        directory.Write("translations/en.rmf2", "a = A\n");
        directory.Write("translations/de.rmf2", "a = A de\n");
        Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanAdd(directory.Sources(), "fr", null), "A locale was added to a non-object project.");
        Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanSetFallback(directory.Sources(), "de", "en"), "A fallback was set in a non-object project.");
        Throws<TranslationToolingUsageException>(() => TranslationLocaleChanges.PlanRemove(directory.Sources(), "de"), "A locale was removed from a non-object project.");
    }

    [Test, DisplayName("an import plan resolves the .resx source against the base directory, not the working directory")]
    public void ResxImportPlan()
    {
        using TemporaryProject directory = MigrationCorpus();
        TranslationCatalogImportPlan plan = TranslationCatalogImport.Plan(directory.Sources(), new("resx", "resx/Strings.resx", [], null));
        Require(plan is { Failure: TranslationCatalogImportFailure.None, Catalog: "app", Added: 13, Skipped: 2, Refused: 2 }, $"The plan differs: {plan.Failure} {plan.Added} {plan.Skipped} {plan.Refused}");
        Require(plan.Documents.Select(static document => document.Path).SequenceEqual(["resx/Strings.resx", "resx/Strings.de.resx"]), "The documents are not relative to the base directory.");
        Require(plan.Notices.Any(static notice => notice is { Location: "resx/Strings.fr.resx", Code: "IMPORT-LOCALE" }), "The undeclared locale was not noticed.");
        Require(plan.Entries.Any(static entry => entry is { Locale: "de", Key: "OnlyGerman", Outcome: "refused", Code: "IMPORT-NO-BASE" }), "The translation without a base was not refused.");
        RequireWrites(directory, plan, "resx");
    }

    [Test, DisplayName("an i18next import plan reads a locales directory below the base directory")]
    public void I18nextImportPlan()
    {
        using TemporaryProject directory = MigrationCorpus();
        TranslationCatalogImportPlan plan = TranslationCatalogImport.Plan(directory.Sources(), new("i18next", "i18next/locales", [], null));
        Require(plan.Failure == TranslationCatalogImportFailure.None, plan.Failure + ": " + Messages(plan.Diagnostics));
        Require(string.Join("; ", plan.Locales.Select(static locale => $"{locale.Locale} {locale.Added}")) == "en 13; de 6" && plan.Skipped == 2 && plan.Refused == 2,
            string.Join("; ", plan.Locales.Select(static locale => $"{locale.Locale} {locale.Added}")));
        RequireWrites(directory, plan, "i18next");
        Throws<TranslationToolingUsageException>(() => TranslationCatalogImport.Plan(directory.Sources(), new("i18next", "missing", [], null)), "A missing source was accepted.");
        Throws<TranslationToolingUsageException>(() => TranslationCatalogImport.Plan(directory.Sources(), new("po", "i18next/locales", [], null)), "An unknown format was accepted.");
    }

    [Test, DisplayName("an import into a catalog under the default completeness policy writes every locale and counts it in status")]
    public void ImportIntoIncompleteCatalog()
    {
        using TemporaryProject directory = MigrationCorpus();
        // The default translationCompleteness (error): the corpus's de.resx lacks most messages, so the imported catalog
        // has untranslated messages (RTR0010) that neither refuse a message nor drop the translations it has.
        directory.Write("translations/runic.json", "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Migrated\",\"className\":\"AppText\"},\"baseLocale\":\"en\",\"locales\":[\"en\",{\"tag\":\"de\",\"fallback\":\"en\"}]}");
        TranslationProjectSources sources = directory.Sources();
        TranslationCatalogImportPlan plan = TranslationCatalogImport.Plan(sources, new("resx", "resx/Strings.resx", [], null));
        Require(plan is { Failure: TranslationCatalogImportFailure.None, Added: 13 }, $"The plan differs: {plan.Failure} {plan.Added} {Messages(plan.Diagnostics)}");
        RequireWrites(directory, plan, "resx");

        var after = sources.Messages.ToDictionary(static source => source.Path, StringComparer.Ordinal);
        foreach (TranslationImportSourceWrite write in plan.Writes) after[write.DisplayPath] = new TranslationSource(write.DisplayPath, write.Bytes);
        Rmf2ProjectCompilation strict = TranslationCompiler.CompileRmf2ProjectV5(sources.Project, after.Values.ToArray());
        Require(!strict.Success && strict.Diagnostics.Any(static diagnostic => diagnostic.Id == "RTR0010"), "The fixture must leave untranslated messages.");
        Rmf2ProjectCompilation counted = TranslationStatus.ForStatus(strict, sources.Project, after.Values.ToArray())
            ?? throw new InvalidOperationException("status refused a catalog whose only errors are untranslated messages.");
        TranslationsStatusLocale german = TranslationStatus.Report("translations/runic.json", counted, null, null, new Dictionary<string, double>()).Locales.Single(static locale => locale.Locale == "de");
        Require(german is { Completeness: "error" } && german.Translated > 0 && german.Missing > 0, $"status: {german}");
    }

    [Test, DisplayName("an import lists what it leaves untranslated and reports errors the catalog already had as pre-existing, not as the import's")]
    public void ImportWithPreexistingErrors()
    {
        using TemporaryProject directory = MigrationCorpus();
        // extraLocaleKeys is an error by default: de's obsolete message (RTR0011) is the catalog's own error.
        directory.Write("translations/de.rmf2", "title = Laden\nobsolete = Alt\n");
        TranslationProjectSources sources = directory.Sources();
        TranslationCatalogImportPlan plan = TranslationCatalogImport.Plan(sources, new("resx", "resx/Strings.resx", [], null));
        Require(plan is { Failure: TranslationCatalogImportFailure.None, Added: 13 }, $"A pre-existing error failed the import: {plan.Failure} {Messages(plan.Diagnostics)}");
        Require(plan.Preexisting.Single() is { Id: "RTR0011" } && plan.Notices.Any(static notice => notice is { Code: "IMPORT-PREEXISTING", Location: "translations/de.rmf2" } && notice.Message.Contains("RTR0011", StringComparison.Ordinal)),
            "The pre-existing error wasn't reported: " + string.Join("; ", plan.Notices.Select(static notice => notice.Code + " " + notice.Message)));
        // The corpus's de.resx translates only part of the base messages; the rest is listed per locale.
        TranslationImportUntranslated german = plan.Untranslated.Single();
        Require(german.Locale == "de" && german.Keys.Count > 0 && german.Keys.SequenceEqual(german.Keys.Order(StringComparer.Ordinal)) && !german.Keys.Contains("title"),
            "The untranslated messages differ: " + string.Join(", ", german.Keys));
    }

    [Test, DisplayName("status counts a translation the review file marks draft (an editor starter copy) as missing")]
    public void StatusDrafts()
    {
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(Source("translations/runic.json", Project),
        [
            Source("translations/en.rmf2", "a = A\nb = B\nc = C\n"),
            Source("translations/de.rmf2", "a = A\nb = B\nc = C de\n"),
            Source("translations/de-CH.rmf2", "a = A\n"),
            Source("translations/fr.rmf2", "a = A fr\nb = B fr\nc = C fr\n"),
        ]);
        Require(compilation.Success, Messages(compilation.Diagnostics));
        var review = new TranslationInterchangeReview("app",
        [
            new TranslationInterchangeReviewEntry("a", "de", TranslationReviewState.Draft),
            new TranslationInterchangeReviewEntry("b", "de", TranslationReviewState.Draft),
            new TranslationInterchangeReviewEntry("a", "de-CH", TranslationReviewState.Draft),
            new TranslationInterchangeReviewEntry("a", "fr", TranslationReviewState.Approved),
        ]);
        TranslationsStatusProject report = TranslationStatus.Report("translations/runic.json", compilation, review, "translations/app.review.json", new Dictionary<string, double>());
        Require(report.Locales.Single(static locale => locale.Locale == "de") is { Translated: 1, Missing: 2, Coverage: 33.33 }, "de's drafts counted as translated.");
        // de-CH's draft a and de's draft a: neither serves it; c comes from de.
        Require(report.Locales.Single(static locale => locale.Locale == "de-CH") is { Translated: 0, Fallback: 1, Missing: 2 }, "de-CH's draft counted.");
        Require(report.Locales.Single(static locale => locale.Locale == "fr") is { Translated: 3, Missing: 0 }, "An approved entry changed fr's count.");
    }

    // The writes are the corpus's expected sources, at paths below the base directory.
    private static void RequireWrites(TemporaryProject directory, TranslationCatalogImportPlan plan, string expected)
    {
        string corpus = Path.Combine(XliffProfileV3Tests.RepositoryRoot(), "specs", "translations", "corpus", "migration", "expected");
        Require(plan.Writes.Select(static write => write.DisplayPath).OrderBy(static path => path, StringComparer.Ordinal).SequenceEqual(["translations/de.rmf2", "translations/en.rmf2"]),
            "The writes differ: " + string.Join(", ", plan.Writes.Select(static write => write.DisplayPath)));
        foreach (TranslationImportSourceWrite write in plan.Writes)
        {
            Require(write.FullPath == directory.Resolve(write.DisplayPath), "A write is not below the base directory: " + write.FullPath);
            string locale = Path.GetFileNameWithoutExtension(write.DisplayPath);
            Require(write.Bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(corpus, expected + "." + locale + ".rmf2"))), $"{write.DisplayPath} differs from the expected {expected} import.");
        }
    }

    private static TemporaryProject MigrationCorpus()
    {
        string corpus = Path.Combine(XliffProfileV3Tests.RepositoryRoot(), "specs", "translations", "corpus", "migration");
        var directory = new TemporaryProject();
        directory.Copy(Path.Combine(corpus, "project"), "translations");
        directory.Copy(Path.Combine(corpus, "resx"), "resx");
        directory.Copy(Path.Combine(corpus, "i18next"), "i18next");
        return directory;
    }

    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));

    private static string Messages(IEnumerable<TranslationDiagnostic> diagnostics) => string.Join("; ", diagnostics.Select(static diagnostic => diagnostic.Id + ": " + diagnostic.Message));

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static TException Throws<TException>(Func<object?> action, string message) where TException : Exception
    {
        try { _ = action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException(message);
    }

    // A project in a temporary directory that is not the working directory; sources are read relative to it.
    private sealed class TemporaryProject : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "runic-operations-" + Guid.NewGuid().ToString("N"));

        internal TemporaryProject()
        {
            Directory.CreateDirectory(root);
            if (Path.GetFullPath(Environment.CurrentDirectory) == root) throw new InvalidOperationException("The base directory is the working directory.");
        }

        internal string Resolve(string path) => Path.GetFullPath(path, root);

        internal void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Resolve(path))!);
            File.WriteAllText(Resolve(path), text, new UTF8Encoding(false));
        }

        internal void Copy(string source, string target)
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string destination = Resolve(Path.Combine(target, Path.GetRelativePath(source, file)));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }

        // translations/runic.json and every .rmf2/.mf2 file below the source roots (default translations/), with paths
        // relative to the base directory.
        internal TranslationProjectSources Sources(params string[] sourceRoots)
        {
            string[] roots = (sourceRoots.Length == 0 ? ["translations"] : sourceRoots).Select(Resolve).ToArray();
            TranslationSource[] messages = roots.SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                .Where(static file => file.EndsWith(".rmf2", StringComparison.Ordinal) || file.EndsWith(".mf2", StringComparison.Ordinal))
                .Select(file => new TranslationSource(Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllBytes(file)))
                .OrderBy(static source => source.Path, StringComparer.Ordinal).ToArray();
            return new TranslationProjectSources(root, new TranslationSource("translations/runic.json", File.ReadAllBytes(Resolve("translations/runic.json"))), messages, roots);
        }

        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
