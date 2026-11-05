using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runic.CommandLine;
using Runic.Translations.Compiler;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

/// <summary><c>runic-translations locale add|remove</c>: declare or drop a locale in runic.json and its source files.</summary>
[CommandGroup(Group, Description = "Add or remove a locale of a translation project.")]
internal static class LocaleCommandModule
{
    internal const string Group = "locale";
    private const string ProjectDescription = "Translations directory or its runic.json. Defaults to ./runic.json, then ./translations/runic.json.";

    [Command("locale add", Description = "Declare a locale in runic.json and create its empty source files.",
        LongDescription = "Adds the locale to runic.json's locales (as \"tag\", or { \"tag\", \"fallback\" } with --fallback or when the other entries are objects) and creates an empty <tag>.rmf2 beside every base-locale .rmf2 file. runic.json keeps its formatting. The locale starts untranslated: validate reports RTR0010 for each message until it is translated, or set the locale entry's validation.translationCompleteness to warning. Nothing is written when the result would not compile.\n\nExit codes: 0 success; 1 command failure, such as an inaccessible file; 2 invalid invocation, an invalid or declared tag, or translation diagnostics; 3 internal failure; 130 cancelled.",
        Examples = ["runic-translations locale add fr", "runic-translations locale add de-CH --fallback de", "runic-translations locale add pt-BR --project src/App/translations --dry-run"])]
    [CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    internal static CommandOutcome<TranslationsToolCommandResult> Add(
        [FromServices] ITranslationsToolCommandOperations operations,
        [Argument(Required = true, ValueName = "tag", Description = "The BCP 47 locale tag to add, such as fr or de-CH.")] string tag,
        [Option("--fallback", ValueName = "locale", Description = "The declared locale it falls back to; defaults to the base locale.")] string? fallback,
        [Option("--project", Description = ProjectDescription, ValueName = "directory")] string? project,
        [Option("--dry-run", Description = "Report the changes without writing them.")] bool dryRun) =>
        operations.Execute(new("locale add", Project: project, DryRun: dryRun) { Tag = tag, Fallback = fallback });

    [Command("locale remove", Description = "Remove a locale from runic.json, delete its source files and its review entries.",
        LongDescription = "Removes the locale's entry from runic.json (keeping its formatting), deletes its <tag>.rmf2 files or <tag>/ .mf2 files and its entries in <catalog>.review.json. The base locale and a locale that another locale falls back to cannot be removed. The files are deleted first, then runic.json is updated; if a file cannot be deleted, the locale stays declared and the command can be run again. Use --dry-run to list the files and directories first; files outside the runic.json directory (from sourceRoots) are marked.\n\nExit codes: 0 success; 1 command failure, such as an inaccessible file; 2 invalid invocation or a locale that cannot be removed; 3 internal failure; 130 cancelled.",
        Examples = ["runic-translations locale remove fr --dry-run", "runic-translations locale remove fr"])]
    [CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    internal static CommandOutcome<TranslationsToolCommandResult> Remove(
        [FromServices] ITranslationsToolCommandOperations operations,
        [Argument(Required = true, ValueName = "tag", Description = "The declared locale to remove.")] string tag,
        [Option("--project", Description = ProjectDescription, ValueName = "directory")] string? project,
        [Option("--dry-run", Description = "Report the changes without writing or deleting anything.")] bool dryRun) =>
        operations.Execute(new("locale remove", Project: project, DryRun: dryRun) { Tag = tag });
}

internal static class LocaleOperations
{
    internal static int Add(string projectPath, string tag, string? fallback, bool dryRun, ToolOperationResult result)
    {
        CompilerInputs inputs = InputFiles.ReadProject(projectPath);
        string current = Path.GetFullPath(Environment.CurrentDirectory);
        TranslationLocalePlan plan = ToolingUsage.Run(() => TranslationLocaleChanges.PlanAdd(Sources(inputs, current), tag, fallback));
        string locale = plan.Locale;
        if (plan.Errors.Count != 0)
        {
            Program.ReportDiagnostics(plan.Errors, result);
            result.WriteOutputLine($"locale '{locale}' was not added: the project would not compile.");
            return 1;
        }
        if (!dryRun)
        {
            InterchangeOperations.WriteFile(plan.ProjectPath, plan.ProjectBytes);
            foreach (TranslationLocaleFile file in plan.CreatedFiles) InterchangeOperations.WriteFile(file.Path, file.Bytes);
        }
        string verb = dryRun ? "would add" : "added";
        result.WriteOutputLine($"{verb} locale {locale} (fallback {plan.Fallback}) to {inputs.Project.Path}.");
        foreach (TranslationLocaleFile file in plan.CreatedFiles) result.WriteOutputLine($"{(dryRun ? "would create" : "created")} {InputFiles.DisplayPath(file.Path, current)}.");
        if (plan.Untranslated != 0 || inputs.Messages.Count + plan.CreatedFiles.Count != 0)
            result.WriteOutputLine($"The messages of {locale} are not translated yet; validate reports RTR0010 for them until they are, unless the locale entry sets validation.translationCompleteness.");
        return 0;
    }

    internal static int Remove(string projectPath, string tag, bool dryRun, ToolOperationResult result)
    {
        CompilerInputs inputs = InputFiles.ReadProject(projectPath);
        string current = Path.GetFullPath(Environment.CurrentDirectory);
        TranslationLocalePlan plan = ToolingUsage.Run(() => TranslationLocaleChanges.PlanRemove(Sources(inputs, current), tag));
        string locale = plan.Locale;
        string projectDirectory = Path.GetDirectoryName(plan.ProjectPath)!;
        if (!dryRun)
        {
            // The files go first: if one cannot be deleted, runic.json still declares the locale, and running the
            // command again finishes the removal. A read-only file is refused before anything is deleted.
            foreach (string path in plan.DeletedFiles)
                if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
                    throw new IOException($"{InputFiles.DisplayPath(path, current)} is read-only; nothing was deleted.");
            foreach (string path in plan.DeletedFiles) File.Delete(path);
            foreach (string directory in plan.EmptiedDirectories)
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            InterchangeOperations.WriteFile(plan.ProjectPath, plan.ProjectBytes);
            // Compare-and-swap (TranslationReviewFile.Update): an editor or language server write in between is kept.
            if (plan.Review is { } review && plan.ReviewEntries != 0)
                TranslationReviewFile.Update(plan.ReviewPath!, review.CatalogId, existing => existing is null ? null
                    : new TranslationInterchangeReview(existing.CatalogId, existing.Entries.Where(entry => !string.Equals(entry.Locale, locale, StringComparison.OrdinalIgnoreCase))));
        }
        result.WriteOutputLine($"{(dryRun ? "would remove" : "removed")} locale {locale} from {inputs.Project.Path}.");
        // Sources outside the runic.json directory (sourceRoots such as ../shared) are part of the project, and marked.
        foreach (string path in plan.DeletedFiles)
            result.WriteOutputLine($"{(dryRun ? "would delete" : "deleted")} {InputFiles.DisplayPath(path, current)}{(TranslationLocaleChanges.IsWithin(path, projectDirectory) ? "" : " (outside the project directory)")}.");
        foreach (string directory in plan.EmptiedDirectories)
            result.WriteOutputLine($"{(dryRun ? "would remove the directory" : "removed the directory")} {InputFiles.DisplayPath(directory, current)}/{(TranslationLocaleChanges.IsWithin(directory, projectDirectory) ? "" : " (outside the project directory)")}.");
        if (plan.ReviewEntries != 0) result.WriteOutputLine($"{(dryRun ? "would remove" : "removed")} {plan.ReviewEntries} review entr{(plan.ReviewEntries == 1 ? "y" : "ies")} from {InputFiles.DisplayPath(plan.ReviewPath!, current)}.");
        return 0;
    }

    private static TranslationProjectSources Sources(CompilerInputs inputs, string current) =>
        new(current, inputs.Project, inputs.Messages, inputs.SourceRoots ?? []);
}
