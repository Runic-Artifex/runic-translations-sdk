using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runic.CommandLine;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

/// <summary>
/// <c>runic-translations import --from resx|i18next</c>: writes and reports the plan of
/// <see cref="TranslationCatalogImport"/>, which migrates an existing catalog into the project's <c>.rmf2</c> sources.
/// </summary>
internal static class CatalogImport
{
    private const int MaximumEntries = 1000;
    private const int MaximumLines = 32;

    internal static int Run(string projectPath, TranslationCatalogImportRequest request, bool dryRun, ToolOperationResult result)
    {
        ToolingUsage.Run(() => { TranslationCatalogImport.Validate(request); return 0; });

        CompilerInputs inputs = InputFiles.ReadProject(projectPath);
        string currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        var sources = new TranslationProjectSources(currentDirectory, inputs.Project, inputs.Messages, inputs.SourceRoots ?? []);
        TranslationCatalogImportPlan plan = ToolingUsage.Run(() => TranslationCatalogImport.Plan(sources, request,
            compilation => TranslationManifestReader.WithManifestErrors(compilation, inputs.Project.Path, inputs.ManifestError)));
        switch (plan.Failure)
        {
            case TranslationCatalogImportFailure.ProjectDoesNotCompile:
                Program.ReportDiagnostics(plan.Diagnostics, result);
                result.WriteOutputLine("The project does not compile; fix it before importing. No files were written.");
                return 1;
            case TranslationCatalogImportFailure.SourceUnreadable:
                result.AddDiagnostic(ToolCodes.InterchangeDocument, "import", $"{plan.FailureCode}: {plan.FailureMessage}", CommandDiagnosticSeverity.Error);
                result.Fault = new CommandFault(ToolCodes.InterchangeRejected, "The source could not be imported; nothing was written.");
                result.WriteOutputLine("The source could not be imported; nothing was written.");
                result.ExitCategory = CommandExitCategory.Validation;
                return 1;
            case TranslationCatalogImportFailure.MessagesDoNotCompile:
                Program.ReportDiagnostics(plan.Diagnostics, result);
                result.Fault = new CommandFault(ToolCodes.InterchangeRejected, "The imported messages do not compile; nothing was written.");
                result.WriteOutputLine("The imported messages do not compile; nothing was written.");
                result.ExitCategory = CommandExitCategory.Validation;
                return 1;
        }

        var writes = plan.Writes.Select(static write => new InterchangeOperations.SourceWrite(write.FullPath, write.DisplayPath, write.OriginalBytes, write.Bytes)).ToList();
        if (!dryRun)
        {
            try { InterchangeOperations.WriteSources(writes); }
            catch (InterchangeOperations.SourceChangedException exception)
            {
                result.AddDiagnostic(ToolCodes.InterchangeDocument, "import", exception.Message, CommandDiagnosticSeverity.Error);
                result.Fault = new CommandFault(ToolCodes.InterchangeRejected, "A file changed on disk during the import; nothing was written. Run the import again.");
                result.WriteOutputLine("A file changed on disk during the import; nothing was written.");
                result.ExitCategory = CommandExitCategory.Validation;
                return 1;
            }
        }

        IEnumerable<string> summary = plan.Locales.Select(static locale =>
            $"{locale.Locale} {locale.Added} added, {locale.Changed} changed, {locale.Unchanged} unchanged" + (locale.Kept != 0 ? $", {locale.Kept} kept" : string.Empty));
        string verb = dryRun ? "would write" : "wrote";
        result.WriteOutputLine($"{(dryRun ? "dry run: " : string.Empty)}imported {request.From}: {string.Join("; ", summary)}; {plan.Skipped} skipped, {plan.Refused} refused.");
        foreach (InterchangeOperations.SourceWrite write in writes) result.WriteOutputLine($"{verb} {write.DisplayPath}.");
        if (plan.Kept != 0)
            result.WriteOutputLine($"{plan.Kept} existing message(s) differ from the source and were kept; --overwrite replaces them.");
        foreach (TranslationImportEntry entry in plan.Entries.Take(MaximumLines))
            result.WriteOutputLine($"{entry.Outcome} {entry.Locale} {entry.Key}: {entry.Code}: {entry.Message}");
        foreach (TranslationImportNotice notice in plan.Notices.Take(MaximumLines))
            result.WriteOutputLine($"notice {notice.Location}: {notice.Code}: {notice.Message}");
        int hidden = Math.Max(0, plan.Entries.Count - MaximumLines) + Math.Max(0, plan.Notices.Count - MaximumLines);
        if (hidden != 0) result.WriteOutputLine($"{hidden} more line(s); --format json lists them.");
        foreach (TranslationImportEntry entry in plan.Entries.Where(static entry => entry.Outcome == "refused").Take(MaximumLines))
            result.AddDiagnostic(ToolCodes.InterchangeUnit, "import-message", $"{entry.Locale} {entry.Key}: {entry.Code}: {entry.Message}", CommandDiagnosticSeverity.Warning);

        List<TranslationsToolInterchangeEntry> entries = [.. plan.Applied.Concat(plan.Entries).Select(static entry => new TranslationsToolInterchangeEntry(entry.Key, entry.Outcome, entry.Code, entry.Message) { Locale = entry.Locale })];
        result.Interchange = new TranslationsToolInterchange("import", plan.Catalog!)
        {
            Format = request.From,
            Locale = plan.Locales.Count == 1 ? plan.Locales[0].Locale : null,
            DryRun = dryRun,
            Documents = plan.Documents.Select(static document => new TranslationsToolInterchangeDocument(document.Locale, document.Path, document.Read)).ToArray(),
            Units = new TranslationsToolInterchangeCounts
            {
                Total = plan.Added + plan.Changed + plan.Unchanged + plan.Kept + plan.Skipped + plan.Refused,
                Applied = plan.Added + plan.Changed + plan.Unchanged, Added = plan.Added, Changed = plan.Changed, Unchanged = plan.Unchanged, Kept = plan.Kept,
                Skipped = plan.Skipped, Refused = plan.Refused,
            },
            Entries = entries.Take(MaximumEntries).ToArray(),
            EntriesTruncated = entries.Count > MaximumEntries,
            Notices = plan.Notices.Take(MaximumEntries).Select(static notice => new TranslationsToolInterchangeNotice(notice.Code, notice.Location, notice.Message)).ToArray(),
            Files = writes.Select(static write => new TranslationsToolInterchangeFile(write.DisplayPath, write.OriginalBytes is null ? "added" : "changed")).ToArray(),
        };
        return 0;
    }
}
