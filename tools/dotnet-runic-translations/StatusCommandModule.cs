using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
        LongDescription = "For every locale: messages translated in the locale, served by a fallback locale other than the base locale, missing (shown in the base locale), and stale (translations whose source changed since they were recorded in <catalog>.review.json, when that file exists). Coverage is translated plus fallback, as a percentage of the base locale's messages. --min-coverage <locale>=<percent> (repeatable or comma-separated; * for every translated locale) fails the command with exit 1 when a locale is below its threshold.\n\nExit codes: 0 success; 1 a locale is below its --min-coverage, or a file is inaccessible; 2 invalid invocation or a catalog that does not compile (run validate); 3 internal failure; 130 cancelled.",
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
            try { thresholds = ParseThresholds(minimumCoverage); }
            catch (ToolUsageException exception) { return Usage(exception.Message); }

            var reports = new List<TranslationsStatusProject>();
            var warnings = new List<CommandDiagnostic>();
            foreach (string path in paths)
            {
                CompilerInputs inputs = InputFiles.ReadProject(path);
                Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(inputs.Project, inputs.Messages);
                compilation = TranslationManifestReader.WithManifestErrors(compilation, inputs.Project.Path, inputs.ManifestError);
                if (!compilation.Success || compilation.Project is null)
                {
                    int errors = compilation.Diagnostics.Count(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error);
                    return CommandOutcome.Failure<TranslationsStatusResult>(CommandExitCategory.Validation,
                        new CommandFault(ToolCodes.CommandFailed, $"The catalog of {inputs.Project.Path} does not compile ({errors} error(s)); run runic-translations validate."),
                        diagnostics: [], humanOutput: $"runic-translations: the catalog of {inputs.Project.Path} does not compile ({errors} error(s)); run runic-translations validate.\n");
                }
                reports.Add(Report(inputs, compilation, thresholds, warnings));
            }
            foreach (string locale in thresholds.Keys)
                if (locale != "*" && !reports.Any(report => report.Locales.Any(item => string.Equals(item.Locale, locale, StringComparison.OrdinalIgnoreCase) && item.Locale != report.BaseLocale)))
                    return Usage($"--min-coverage names '{locale}', which is not a translated locale of the project(s).");

            var result = new TranslationsStatusResult(reports) { BelowThreshold = reports.Any(static report => report.Locales.Any(static locale => locale.BelowThreshold)) };
            if (!result.BelowThreshold) return CommandOutcome.Success(result, warnings);
            string below = string.Join(", ", reports.SelectMany(static report => report.Locales.Where(static locale => locale.BelowThreshold)
                .Select(locale => report.Catalog + " " + locale.Locale + " " + Percent(locale.Coverage) + " < " + Percent(locale.MinimumCoverage!.Value))));
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

    // "de=100,fr=80" and "*=90", in any number of values; a locale named twice is an error.
    internal static Dictionary<string, double> ParseThresholds(IReadOnlyList<string> values)
    {
        var thresholds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (string item in values.SelectMany(static value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
        {
            int separator = item.IndexOf('=');
            string locale = separator <= 0 ? string.Empty : item[..separator].Trim();
            string percent = separator <= 0 ? string.Empty : item[(separator + 1)..].Trim().TrimEnd('%');
            if (locale.Length == 0 || !double.TryParse(percent, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value) || value is < 0 or > 100)
                throw new ToolUsageException($"--min-coverage expects <locale>=<percent> with a percentage from 0 to 100, such as de=100,fr=80; '{item}' is not.");
            if (!thresholds.TryAdd(locale, value)) throw new ToolUsageException($"--min-coverage names '{locale}' more than once.");
        }
        return thresholds;
    }

    private static TranslationsStatusProject Report(CompilerInputs inputs, Rmf2ProjectCompilation compilation, Dictionary<string, double> thresholds, List<CommandDiagnostic> warnings)
    {
        Rmf2ProjectV5 project = compilation.Project!;
        string[] keys = project.CanonicalMessages.Select(static contract => contract.Key).ToArray();
        var locales = project.Locales.ToDictionary(static locale => locale.Tag, StringComparer.Ordinal);
        var own = project.Locales.ToDictionary(static locale => locale.Tag, static locale => locale.DirectResources.Select(static resource => resource.Key).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

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
        ILookup<string, TranslationReviewStaleEntry>? stale = review is null ? null
            : TranslationReviewFile.Stale(compilation, review).ToLookup(static item => item.Entry.Locale, StringComparer.Ordinal);

        var reports = new List<TranslationsStatusLocale>();
        foreach (Rmf2LocaleV5 locale in project.Locales.OrderBy(locale => locale.Tag == project.DefaultLocale ? 0 : 1).ThenBy(static locale => locale.Tag, StringComparer.Ordinal))
        {
            int translated = 0, fallback = 0;
            foreach (string key in keys)
            {
                if (own[locale.Tag].Contains(key)) { translated++; continue; }
                // A fallback locale other than the base serves it: de-CH falls back to de.
                var visited = new HashSet<string>(StringComparer.Ordinal) { locale.Tag };
                for (string? next = locale.FallbackTag; next is not null && next != project.DefaultLocale && visited.Add(next) && locales.TryGetValue(next, out Rmf2LocaleV5? parent); next = parent.FallbackTag)
                    if (own[next].Contains(key)) { fallback++; break; }
            }
            int? staleCount = stale is null || locale.Tag == project.DefaultLocale ? null : stale[locale.Tag].Count();
            double coverage = keys.Length == 0 ? 100 : Math.Floor((translated + fallback) * 10000.0 / keys.Length) / 100;
            double? minimum = locale.Tag == project.DefaultLocale ? null
                : thresholds.TryGetValue(locale.Tag, out double exact) ? exact : thresholds.TryGetValue("*", out double all) ? all : null;
            reports.Add(new TranslationsStatusLocale(locale.Tag, locale.FallbackTag, translated, fallback, keys.Length - translated - fallback, coverage)
            {
                Stale = staleCount,
                MinimumCoverage = minimum,
                BelowThreshold = minimum is { } threshold && coverage < threshold,
            });
        }
        return new TranslationsStatusProject(inputs.Project.Path.Replace('\\', '/'), project.Id, project.DefaultLocale, keys.Length, reports)
        {
            ReviewFile = review is null ? null : InputFiles.DisplayPath(reviewPath, Path.GetFullPath(Environment.CurrentDirectory)),
        };
    }

    internal static string Percent(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "%";
}

/// <summary>The <c>runic.translations.status/1</c> payload of <c>status</c>.</summary>
/// <param name="Projects">One report per project, in the order of <c>--project</c>.</param>
internal sealed record TranslationsStatusResult(IReadOnlyList<TranslationsStatusProject> Projects)
{
    /// <summary>Whether any locale is below its <c>--min-coverage</c>; the command then exits with 1.</summary>
    public bool BelowThreshold { get; init; }

    internal string ToHumanOutput()
    {
        var text = new StringBuilder();
        foreach (TranslationsStatusProject project in Projects)
        {
            if (text.Length != 0) text.Append('\n');
            text.Append(project.Catalog).Append(" (").Append(project.Project).Append("): ").Append(project.Messages.ToString(CultureInfo.InvariantCulture))
                .Append(" message(s), base locale ").Append(project.BaseLocale).Append('\n');
            bool review = project.ReviewFile is not null;
            text.Append("  locale      translated  fallback  missing").Append(review ? "  stale" : "").Append("  coverage\n");
            foreach (TranslationsStatusLocale locale in project.Locales)
            {
                text.Append("  ").Append(locale.Locale.PadRight(10)).Append(locale.Translated.ToString(CultureInfo.InvariantCulture).PadLeft(12))
                    .Append(locale.Fallback.ToString(CultureInfo.InvariantCulture).PadLeft(10)).Append(locale.Missing.ToString(CultureInfo.InvariantCulture).PadLeft(9));
                if (review) text.Append((locale.Stale?.ToString(CultureInfo.InvariantCulture) ?? "-").PadLeft(7));
                text.Append(StatusCommandModule.Percent(locale.Coverage).PadLeft(10));
                if (locale.MinimumCoverage is { } minimum) text.Append(locale.BelowThreshold ? "  below " : "  min ").Append(StatusCommandModule.Percent(minimum));
                text.Append('\n');
            }
        }
        return text.ToString().TrimEnd('\n');
    }
}

/// <param name="Project">The project's runic.json, relative to the working directory where possible.</param>
/// <param name="Catalog">The catalog identifier.</param>
/// <param name="BaseLocale">The base locale.</param>
/// <param name="Messages">The base locale's messages, the total of every locale.</param>
/// <param name="Locales">The base locale first, then the others by tag.</param>
internal sealed record TranslationsStatusProject(string Project, string Catalog, string BaseLocale, int Messages, IReadOnlyList<TranslationsStatusLocale> Locales)
{
    /// <summary>The review file that stale counts come from, when it exists.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReviewFile { get; init; }
}

/// <param name="Locale">The locale tag.</param>
/// <param name="FallbackLocale">The locale it falls back to; absent for the base locale.</param>
/// <param name="Translated">Messages the locale translates itself.</param>
/// <param name="Fallback">Messages a fallback locale other than the base locale serves.</param>
/// <param name="Missing">Messages shown in the base locale.</param>
/// <param name="Coverage">Translated plus fallback, as a percentage of <see cref="TranslationsStatusProject.Messages"/>, rounded down to two decimals.</param>
internal sealed record TranslationsStatusLocale(string Locale,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FallbackLocale,
    int Translated, int Fallback, int Missing, double Coverage)
{
    /// <summary>Translations whose source changed since the review file recorded them; absent without a review file and for the base locale.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Stale { get; init; }

    /// <summary>The <c>--min-coverage</c> that applies to the locale, if any.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinimumCoverage { get; init; }

    /// <summary>Whether <see cref="Coverage"/> is below <see cref="MinimumCoverage"/>.</summary>
    public bool BelowThreshold { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TranslationsStatusResult))]
internal sealed partial class StatusCommandJsonContext : JsonSerializerContext;
