using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Runic.CommandLine;
using Runic.Translations.Compiler;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

/// <summary>
/// <c>runic-translations status</c>: translation coverage per locale (translated, served by a fallback locale, missing,
/// stale), with <c>--min-coverage</c> thresholds for CI. The payload is <c>runic.translations.status/1</c>.
/// </summary>
internal static class StatusCommandModule
{
    internal const string Command = "status";
    internal const string PayloadType = "runic.translations.status/1";

    [Command(Command, Description = "Report translation coverage per locale.",
        LongDescription = "For every locale: messages translated in the locale, served by a fallback locale other than the base locale, missing (shown in the base locale), and stale (translations whose source changed since they were recorded in <catalog>.review.json, when that file exists). Coverage is translated plus fallback, as a percentage of the base locale's messages. --min-coverage <locale>=<percent> (repeatable or comma-separated; * for every translated locale) fails the command with exit 1 when a locale is below its threshold.\n\nExit codes: 0 success; 1 a locale is below its --min-coverage, or a file is inaccessible; 2 invalid invocation, or a catalog whose runic.json, base locale or translation syntax has errors (run validate); missing translations and the other validation policies do not stop the report; 3 internal failure; 130 cancelled.",
        Examples = ["runic-translations status", "runic-translations status --min-coverage de=100,fr=80", "runic-translations status --project src/App/translations --project src/Lib/translations --format json"])]
    [CommandResult(PayloadType, typeof(StatusCommandJsonContext))]
    internal static CommandOutcome<TranslationsStatusResult> Status(
        [Option("--project", AllowMultipleValues = true, ValueName = "directory", Description = "Translations directory or its runic.json; repeatable. Defaults to ./runic.json, then ./translations/runic.json.")] IReadOnlyList<string> projects,
        [Option("--min-coverage", AllowMultipleValues = true, ValueName = "locale=percent", Description = "Minimum coverage of a locale, such as de=100,fr=80 or *=90; repeatable.")] IReadOnlyList<string> minimumCoverage)
    {
        try
        {
            IReadOnlyList<string> paths = projects.Count != 0 ? projects : ToolHostOperations.DefaultProject() is { } found ? [found] : [];
            if (paths.Count == 0) return Usage("no runic.json in the current directory or in ./translations; pass --project <directory>.");
            Dictionary<string, double> thresholds;
            try { thresholds = TranslationStatus.ParseThresholds(minimumCoverage); }
            catch (TranslationToolingUsageException exception) { return Usage(exception.Message); }

            var reports = new List<TranslationsStatusProject>();
            var warnings = new List<CommandDiagnostic>();
            foreach (string path in paths)
            {
                CompilerInputs inputs = InputFiles.ReadProject(path);
                Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(inputs.Project, inputs.Messages);
                compilation = TranslationManifestReader.WithManifestErrors(compilation, inputs.Project.Path, inputs.ManifestError);
                // Incomplete translations (RTR0010, an error by default) and other policy errors do not stop the report.
                Rmf2ProjectCompilation? counted = TranslationStatus.ForStatus(compilation, inputs.Project, inputs.Messages);
                if (counted is null)
                {
                    int errors = compilation.Diagnostics.Count(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error);
                    return CommandOutcome.Failure<TranslationsStatusResult>(CommandExitCategory.Validation,
                        new CommandFault(ToolCodes.CommandFailed, $"The catalog of {inputs.Project.Path} does not compile ({errors} error(s)); run runic-translations validate."),
                        diagnostics: [], humanOutput: $"runic-translations: the catalog of {inputs.Project.Path} does not compile ({errors} error(s)); run runic-translations validate.\n");
                }
                reports.Add(Report(inputs, counted, thresholds, warnings));
            }
            foreach (string locale in thresholds.Keys)
                if (locale != "*" && !reports.Any(report => report.Locales.Any(item => string.Equals(item.Locale, locale, StringComparison.OrdinalIgnoreCase) && item.Locale != report.BaseLocale)))
                    return Usage($"--min-coverage names '{locale}', which is not a translated locale of the project(s).");

            var result = new TranslationsStatusResult(reports) { BelowThreshold = reports.Any(static report => report.Locales.Any(static locale => locale.BelowThreshold)) };
            if (!result.BelowThreshold) return CommandOutcome.Success(result, warnings);
            string below = string.Join(", ", reports.SelectMany(static report => report.Locales.Where(static locale => locale.BelowThreshold)
                .Select(locale => report.Catalog + " " + locale.Locale + " " + TranslationStatus.Percent(locale.Coverage) + " < " + TranslationStatus.Percent(locale.MinimumCoverage!.Value))));
            return CommandOutcome.Failure<TranslationsStatusResult>(CommandExitCategory.CommandFailure,
                new CommandFault(ToolCodes.CoverageBelowThreshold, "Translation coverage is below its minimum: " + below + "."),
                diagnostics: warnings,
                humanOutput: result.ToHumanOutput() + "\n",
                failureData: CommandFailureData.Create(PayloadType, result, StatusCommandJsonContext.Default.TranslationsStatusResult));
        }
        catch (ToolUsageException exception) { return Usage(exception.Message); }
        catch (TranslationInterchangeException exception)
        {
            return CommandOutcome.Failure<TranslationsStatusResult>(CommandExitCategory.Validation, new CommandFault(ToolCodes.InterchangeRejected, $"The review file cannot be read ({exception.Code}): {exception.Message}"),
                diagnostics: [], humanOutput: $"runic-translations: the review file cannot be read ({exception.Code}): {exception.Message}\n");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandOutcome.Failure<TranslationsStatusResult>(CommandExitCategory.CommandFailure, new CommandFault(ToolCodes.Io, "The translations input could not be accessed."),
                diagnostics: [], humanOutput: $"runic-translations: {exception.Message}\n");
        }
    }

    internal static ValueTask PresentAsync(TranslationsStatusResult result, ICommandConsole console, CultureInfo culture, CancellationToken cancellationToken) =>
        console.WriteOutAsync((result.ToHumanOutput() + "\n").AsMemory(), cancellationToken);

    private static CommandOutcome<TranslationsStatusResult> Usage(string message) =>
        CommandOutcome.Failure<TranslationsStatusResult>(CommandExitCategory.Usage, new CommandFault(ToolCodes.Usage, message), diagnostics: [],
            humanOutput: $"runic-translations: {message}\n{CommandHelpFormatter.Format(Program.CreateCatalog(), "runic-translations", new CommandPath([Command])).TrimEnd()}\n");

    private static TranslationsStatusProject Report(CompilerInputs inputs, Rmf2ProjectCompilation compilation, Dictionary<string, double> thresholds, List<CommandDiagnostic> warnings)
    {
        Rmf2ProjectV5 project = compilation.Project!;

        // The optional review file: translations that predate a change to their source, counted as validate's RTR0090
        // reports them. An unreadable file is a warning (RTR0091), as in validate, and the stale column stays empty.
        string reviewPath = ReviewFile.PathFor(inputs, project.Id);
        TranslationInterchangeReview? review = null;
        try { review = ReviewFile.Read(reviewPath, project.Id); }
        catch (TranslationInterchangeException exception)
        {
            // Command diagnostics carry RCLI codes; the message names validate's RTR0091.
            warnings.Add(new CommandDiagnostic(ToolCodes.Diagnostic, "review-file", $"{ReviewFile.InvalidCode}: the review file {InputFiles.DisplayPath(reviewPath, Path.GetFullPath(Environment.CurrentDirectory))} cannot be read ({exception.Code}): {exception.Message}",
                CommandDiagnosticPhase.Execution, CommandDiagnosticSeverity.Warning));
        }
        // Overrides of library catalogs whose library text changed since they were written (RTR0090), as validate reports them.
        foreach (TranslationDiagnostic diagnostic in ReviewFile.Overrides(inputs))
            warnings.Add(new CommandDiagnostic(ToolCodes.Diagnostic, "library-override", $"{diagnostic.Id}: {diagnostic.Location.Path}({diagnostic.Location.Line},{diagnostic.Location.Column}): {diagnostic.Message}",
                CommandDiagnosticPhase.Execution, CommandDiagnosticSeverity.Warning));
        return TranslationStatus.Report(inputs.Project.Path, compilation, review,
            review is null ? null : InputFiles.DisplayPath(reviewPath, Path.GetFullPath(Environment.CurrentDirectory)), thresholds);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TranslationsStatusResult))]
internal sealed partial class StatusCommandJsonContext : JsonSerializerContext;
