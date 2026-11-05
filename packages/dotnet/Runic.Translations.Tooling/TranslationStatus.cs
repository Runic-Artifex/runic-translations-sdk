using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tooling;

/// <summary>
/// Translation coverage per locale (translated, served by a fallback locale, missing, stale), shared by
/// <c>runic-translations status</c> and the Translations Editor. The caller compiles the project and reads the optional
/// review file; this computes the report.
/// </summary>
internal static class TranslationStatus
{
    // "de=100,fr=80" and "*=90", in any number of values; a locale named twice is an error.
    /// <exception cref="TranslationToolingUsageException">A value is not <c>&lt;locale&gt;=&lt;percent&gt;</c>, or a locale is named twice.</exception>
    internal static Dictionary<string, double> ParseThresholds(IReadOnlyList<string> values)
    {
        var thresholds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (string item in values.SelectMany(static value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
        {
            int separator = item.IndexOf('=');
            string locale = separator <= 0 ? string.Empty : item[..separator].Trim();
            string percent = separator <= 0 ? string.Empty : item[(separator + 1)..].Trim().TrimEnd('%');
            if (locale.Length == 0 || !double.TryParse(percent, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value) || value is < 0 or > 100)
                throw new TranslationToolingUsageException($"--min-coverage expects <locale>=<percent> with a percentage from 0 to 100, such as de=100,fr=80; '{item}' is not.");
            if (!thresholds.TryAdd(locale, value)) throw new TranslationToolingUsageException($"--min-coverage names '{locale}' more than once.");
        }
        return thresholds;
    }

    /// <summary>The report of one compiled project.</summary>
    /// <param name="projectPath">The project's runic.json as reported, such as <c>translations/runic.json</c>.</param>
    /// <param name="compilation">A successful compilation of the project.</param>
    /// <param name="review">
    /// The review file, or null when it does not exist or cannot be read (translations that predate a change to their
    /// source are counted as validate's RTR0090 reports them; without the file the stale column stays empty). A
    /// translation it marks <c>draft</c> is not translated yet: it counts as missing, as the starter copies of the base
    /// text that the Translations Editor adds with a new language are, until it is recorded as translated.
    /// </param>
    /// <param name="reviewFile">The review file's path as reported; used only with <paramref name="review"/>.</param>
    /// <param name="thresholds">The minimum coverage per locale, <c>*</c> for every translated locale (<see cref="ParseThresholds"/>).</param>
    internal static TranslationsStatusProject Report(string projectPath, Rmf2ProjectCompilation compilation, TranslationInterchangeReview? review, string? reviewFile,
        IReadOnlyDictionary<string, double> thresholds)
    {
        Rmf2ProjectV5 project = compilation.Project ?? throw new ArgumentException("The compilation has no project.", nameof(compilation));
        string[] keys = project.CanonicalMessages.Select(static contract => contract.Key).ToArray();
        var locales = project.Locales.ToDictionary(static locale => locale.Tag, StringComparer.Ordinal);
        // A draft (a starter copy of the base text, XLIFF's "initial") is not the locale's own translation yet.
        var drafts = (review?.Entries ?? []).Where(static entry => entry.State == TranslationReviewState.Draft)
            .Select(static entry => TranslationReviewFile.Identity(entry.Key, entry.Locale)).ToHashSet(StringComparer.Ordinal);
        var own = project.Locales.ToDictionary(static locale => locale.Tag, locale => locale.DirectResources.Select(static resource => resource.Key)
            .Where(key => locale.Tag == project.DefaultLocale || !drafts.Contains(TranslationReviewFile.Identity(key, locale.Tag))).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
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
            bool isBase = locale.Tag == project.DefaultLocale;
            int? staleCount = stale is null || isBase ? null : stale[locale.Tag].Count();
            double coverage = keys.Length == 0 ? 100 : Math.Floor((translated + fallback) * 10000.0 / keys.Length) / 100;
            double? minimum = isBase ? null
                : thresholds.TryGetValue(locale.Tag, out double exact) ? exact : thresholds.TryGetValue("*", out double all) ? all : null;
            reports.Add(new TranslationsStatusLocale(locale.Tag, locale.FallbackTag, translated, fallback, keys.Length - translated - fallback, coverage)
            {
                Stale = staleCount,
                MinimumCoverage = minimum,
                BelowThreshold = minimum is { } threshold && coverage < threshold,
                Completeness = isBase ? null : PolicyName(project.CompletenessPolicies.GetValueOrDefault(locale.Tag, TranslationPolicy.Error)),
            });
        }
        return new TranslationsStatusProject(projectPath.Replace('\\', '/'), project.Id, project.DefaultLocale, keys.Length, reports)
        {
            ReviewFile = review is null ? null : reviewFile,
        };
    }

    /// <summary>
    /// The compilation a status report counts from: <paramref name="compilation"/> when it succeeded; else the same sources
    /// compiled without <c>validation</c> policies, as review data is (<see cref="TranslationReviewFile.ForReview"/>), so a
    /// catalog whose translations are incomplete (RTR0010, an error under the default <c>translationCompleteness</c>) or
    /// break another policy still reports its coverage. Null when that compilation fails too: runic.json, the base locale
    /// or a translation's syntax has errors.
    /// </summary>
    internal static Rmf2ProjectCompilation? ForStatus(Rmf2ProjectCompilation compilation, TranslationSource project, IEnumerable<TranslationSource> sources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (compilation.Success && compilation.Project is not null) return compilation;
        // RTR0052: a source-root layout error the caller added (TranslationManifestReader); the sources are incomplete.
        if (compilation.Diagnostics.Any(static diagnostic => diagnostic.Id == "RTR0052")) return null;
        Rmf2ProjectCompilation relaxed = TranslationCompiler.CompileRmf2ProjectV5(project, sources, new TranslationCompilerOptions { RelaxedValidation = true }, cancellationToken);
        return relaxed.Success && relaxed.Project is not null ? relaxed : null;
    }

    /// <summary>
    /// The base messages each translated locale shows in the base locale (status's missing column without a review file):
    /// neither the locale nor a fallback locale other than the base translates them. By locale, then key, in ordinal order;
    /// locales that miss nothing are left out.
    /// </summary>
    internal static IReadOnlyList<(string Locale, IReadOnlyList<string> Keys)> MissingKeys(Rmf2ProjectV5 project)
    {
        var locales = project.Locales.ToDictionary(static locale => locale.Tag, StringComparer.Ordinal);
        var own = project.Locales.ToDictionary(static locale => locale.Tag, static locale => locale.DirectResources.Select(static resource => resource.Key).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var result = new List<(string, IReadOnlyList<string>)>();
        foreach (Rmf2LocaleV5 locale in project.Locales.Where(locale => locale.Tag != project.DefaultLocale).OrderBy(static locale => locale.Tag, StringComparer.Ordinal))
        {
            var missing = new List<string>();
            foreach (string key in project.CanonicalMessages.Select(static contract => contract.Key).Order(StringComparer.Ordinal))
            {
                bool served = own[locale.Tag].Contains(key);
                var visited = new HashSet<string>(StringComparer.Ordinal) { locale.Tag };
                for (string? next = locale.FallbackTag; !served && next is not null && next != project.DefaultLocale && visited.Add(next) && locales.TryGetValue(next, out Rmf2LocaleV5? parent); next = parent.FallbackTag)
                    served = own[next].Contains(key);
                if (!served) missing.Add(key);
            }
            if (missing.Count != 0) result.Add((locale.Tag, missing));
        }
        return result;
    }

    internal static string Percent(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    // As runic.json spells the policy.
    internal static string PolicyName(TranslationPolicy policy) => policy switch { TranslationPolicy.Allow => "allow", TranslationPolicy.Warning => "warning", _ => "error" };
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
                text.Append(TranslationStatus.Percent(locale.Coverage).PadLeft(10));
                if (locale.MinimumCoverage is { } minimum) text.Append(locale.BelowThreshold ? "  below " : "  min ").Append(TranslationStatus.Percent(minimum));
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
/// <param name="Translated">Messages the locale translates itself, except those the review file marks <c>draft</c>.</param>
/// <param name="Fallback">Messages a fallback locale other than the base locale serves.</param>
/// <param name="Missing">Messages shown in the base locale, and translations the review file marks <c>draft</c> (starter copies of the base text).</param>
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

    /// <summary>
    /// The locale's effective <c>validation.translationCompleteness</c> policy as runic.json spells it (<c>allow</c>,
    /// <c>warning</c> or <c>error</c>): how validate reports its missing messages. Absent for the base locale.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Completeness { get; init; }
}
