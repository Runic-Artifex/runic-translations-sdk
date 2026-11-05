using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tooling;

/// <summary>A review entry whose source changed after it was recorded, with the translation it describes (null when removed).</summary>
internal sealed record TranslationReviewStaleEntry(TranslationInterchangeReviewEntry Entry, Rmf2TranslationV5? Translation, TextSourceLocation Location);

/// <summary>
/// The optional, SDK-owned review file <c>&lt;catalog&gt;.review.json</c> beside runic.json
/// (<c>runic.translations.interchange-review/2</c>), shared by the command line, the Translations Editor and the
/// language server. Each entry records the per-unit fingerprint of the source its translation was made or approved
/// for (<see cref="TranslationInterchange.ComputeSourceFingerprints"/>); an entry whose fingerprint no longer matches
/// is stale (<c>RTR0090</c>).
/// </summary>
internal static class TranslationReviewFile
{
    internal const string StaleCode = "RTR0090";
    internal const string InvalidCode = "RTR0091";
    internal const int MaximumBytes = 8 * 1024 * 1024;

    /// <summary>The <see cref="TranslationInterchangeException.Code"/> of a write refused because the file changed since it was read.</summary>
    internal const string ChangedCode = "REVIEW-CHANGED";

    /// <summary>The review file of <paramref name="catalog"/> in the directory of the project file <paramref name="projectPath"/>.</summary>
    internal static string PathFor(string projectPath, string catalog)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        if (!IsFileName(catalog))
            throw new ArgumentException($"The catalog identifier '{catalog}' cannot name a review file.", nameof(catalog));
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, catalog + ".review.json");
    }

    internal static bool IsFileName(string catalog) =>
        !string.IsNullOrEmpty(catalog) && catalog.IndexOfAny(['/', '\\']) < 0 && catalog is not ("." or "..") && catalog.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>Reads the review file, or returns null when it does not exist.</summary>
    /// <exception cref="TranslationInterchangeException">The file is invalid, larger than 8 MiB or belongs to another catalog.</exception>
    internal static TranslationInterchangeReview? Read(string path, string catalog) => ReadSnapshot(path, catalog).Review;

    /// <summary>
    /// Reads the review file with the bytes it was read from (both null when it does not exist), for a later
    /// <see cref="Write(string, TranslationInterchangeReview, byte[])"/> that refuses to replace a file changed since.
    /// </summary>
    /// <exception cref="TranslationInterchangeException">The file is invalid, larger than 8 MiB or belongs to another catalog.</exception>
    internal static (TranslationInterchangeReview? Review, byte[]? Bytes) ReadSnapshot(string path, string catalog)
    {
        byte[]? bytes = ReadBytes(path);
        return bytes is null ? (null, null) : (Parse(bytes, catalog), bytes);
    }

    /// <summary>The bytes of the review file, or null when it does not exist.</summary>
    /// <exception cref="TranslationInterchangeException">The file is larger than 8 MiB.</exception>
    internal static byte[]? ReadBytes(string path)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaximumBytes)
            throw new TranslationInterchangeException("REVIEW-SIZE", "The review file exceeds 8 MiB.");
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > MaximumBytes)
            throw new TranslationInterchangeException("REVIEW-SIZE", "The review file exceeds 8 MiB.");
        return bytes;
    }

    /// <exception cref="TranslationInterchangeException">The bytes are not a review file of <paramref name="catalog"/>.</exception>
    internal static TranslationInterchangeReview Parse(ReadOnlyMemory<byte> bytes, string catalog)
    {
        if (bytes.Length > MaximumBytes)
            throw new TranslationInterchangeException("REVIEW-SIZE", "The review file exceeds 8 MiB.");
        TranslationInterchangeReview review = TranslationInterchange.ImportReviewJson(bytes);
        if (!string.Equals(review.CatalogId, catalog, StringComparison.Ordinal))
            throw new TranslationInterchangeException("REVIEW-CATALOG", $"The review file is for catalog '{review.CatalogId}', not '{catalog}'.");
        return review;
    }

    internal static byte[] Render(TranslationInterchangeReview review) => TranslationInterchange.ExportReviewJson(review);

    /// <summary>Replaces the review file atomically through a temporary file in the same directory, whatever it contains.</summary>
    internal static void Write(string path, TranslationInterchangeReview review) => Replace(path, Render(review), expected: null, compare: false);

    /// <summary>
    /// Replaces the review file atomically, only while it still holds <paramref name="expected"/> (null: while it does
    /// not exist). The command line, the Translations Editor and the language server all write the file, so each write
    /// compares the file it read with the one it replaces and refuses to overwrite another writer's change.
    /// </summary>
    /// <exception cref="TranslationInterchangeException"><see cref="ChangedCode"/>: the file changed since <paramref name="expected"/> was read.</exception>
    internal static void Write(string path, TranslationInterchangeReview review, byte[]? expected) => Replace(path, Render(review), expected, compare: true);

    /// <summary>
    /// Reads the review file, applies <paramref name="change"/> to it and writes the result with
    /// <see cref="Write(string, TranslationInterchangeReview, byte[])"/>; when another writer replaced the file in between,
    /// reads it again and re-applies <paramref name="change"/> to the new content (entries are independent per key and
    /// locale, so this merges per entry). <paramref name="change"/> returns null to write nothing. Returns the review written,
    /// or the current one when nothing was written.
    /// </summary>
    /// <exception cref="TranslationInterchangeException">The file cannot be read, or (<see cref="ChangedCode"/>) it kept changing while it was being written.</exception>
    internal static TranslationInterchangeReview? Update(string path, string catalog, Func<TranslationInterchangeReview?, TranslationInterchangeReview?> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        for (int attempt = 1; ; attempt++)
        {
            (TranslationInterchangeReview? current, byte[]? bytes) = ReadSnapshot(path, catalog);
            if (change(current) is not { } next) return current;
            try
            {
                Write(path, next, bytes);
                return next;
            }
            catch (TranslationInterchangeException exception) when (exception.Code == ChangedCode && attempt < 5) { }
        }
    }

    private static void Replace(string path, byte[] bytes, byte[]? expected, bool compare)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (compare)
            {
                byte[]? current = File.Exists(path) ? File.ReadAllBytes(path) : null;
                bool unchanged = current is null ? expected is null : expected is not null && current.AsSpan().SequenceEqual(expected);
                if (!unchanged)
                    throw new TranslationInterchangeException(ChangedCode, $"The review file '{Path.GetFileName(path)}' changed on disk after it was read (the command line, the Translations Editor and the language server all write it); nothing was written. Try again.");
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>
    /// The compilation review data is computed from while <paramref name="compilation"/> has errors: the same sources
    /// compiled without <c>validation</c> policies, else the base locale's sources alone. Source fingerprints depend only
    /// on the base messages and their caller contracts, so they are the same as a successful compilation's; an incomplete
    /// or invalid translation (the normal state of a catalog being translated) does not block review data. Returns
    /// <paramref name="compilation"/> when it succeeded, and a failed compilation only when runic.json or the base
    /// locale has errors. A base-locale-only result has no translations, so <see cref="Stale"/> locates its entries at
    /// the base message.
    /// </summary>
    internal static Rmf2ProjectCompilation ForReview(Rmf2ProjectCompilation compilation, TranslationSource project,
        IEnumerable<TranslationSource> sources, TranslationCompilerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (compilation.Success && compilation.Project is not null) return compilation;
        TranslationSource[] all = sources.ToArray();
        TranslationCompilerOptions basis = options ?? new TranslationCompilerOptions();
        Rmf2ProjectCompilation relaxed = TranslationCompiler.CompileRmf2ProjectV5(project, all, With(basis, baseLocaleOnly: false), cancellationToken);
        if (relaxed.Success && relaxed.Project is not null) return relaxed;
        return TranslationCompiler.CompileRmf2ProjectV5(project, all, With(basis, baseLocaleOnly: true), cancellationToken);

        static TranslationCompilerOptions With(TranslationCompilerOptions options, bool baseLocaleOnly) => new()
        {
            MaximumDocumentBytes = options.MaximumDocumentBytes,
            MaximumDepth = options.MaximumDepth,
            MaximumKeysPerCatalog = options.MaximumKeysPerCatalog,
            MaximumValueBytes = options.MaximumValueBytes,
            MaximumPlaceholdersPerValue = options.MaximumPlaceholdersPerValue,
            MaximumLocalesPerCatalog = options.MaximumLocalesPerCatalog,
            RelaxedValidation = true,
            BaseLocaleOnly = baseLocaleOnly,
        };
    }

    /// <summary>
    /// The per-unit source fingerprints of the catalog of <paramref name="compilation"/> (see <see cref="ForReview"/>),
    /// or null when runic.json or the base locale has errors.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? Fingerprints(Rmf2ProjectCompilation compilation, TranslationSource project,
        IEnumerable<TranslationSource> sources, TranslationCompilerOptions? options = null, CancellationToken cancellationToken = default)
    {
        Rmf2ProjectCompilation review = ForReview(compilation, project, sources, options, cancellationToken);
        return review.Success && review.Project is not null ? TranslationInterchange.ComputeSourceFingerprints(review) : null;
    }

    /// <summary>Whether <paramref name="entry"/> was recorded for a source other than the current one.</summary>
    internal static bool IsStale(TranslationInterchangeReviewEntry entry, IReadOnlyDictionary<string, string> fingerprints) =>
        entry.SourceFingerprint is not null && fingerprints.TryGetValue(entry.Key, out string? current) &&
        !string.Equals(entry.SourceFingerprint, current, StringComparison.Ordinal);

    /// <summary>
    /// The review entries of translated locales whose source changed since they were recorded, in key and locale order:
    /// <c>validate</c> warns about each (RTR0090), <c>status</c> counts them, and the editor and language server mark them.
    /// A removed translation still counts unless its entry is a draft. The location is the translation, else the base message.
    /// </summary>
    internal static IReadOnlyList<TranslationReviewStaleEntry> Stale(Rmf2ProjectCompilation compilation, TranslationInterchangeReview review)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(review);
        if (compilation.Project is not { } project) return [];
        IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(compilation);
        var locales = project.Locales.ToDictionary(static locale => locale.Tag, StringComparer.Ordinal);
        var baseResources = locales[project.DefaultLocale].DirectResources.ToDictionary(static resource => resource.Key, StringComparer.Ordinal);
        var stale = new List<TranslationReviewStaleEntry>();
        foreach (TranslationInterchangeReviewEntry entry in review.Entries.OrderBy(static entry => entry.Key, StringComparer.Ordinal).ThenBy(static entry => entry.Locale, StringComparer.Ordinal))
        {
            if (!IsStale(entry, fingerprints) || entry.Locale == project.DefaultLocale || !locales.TryGetValue(entry.Locale, out Rmf2LocaleV5? locale) ||
                !baseResources.TryGetValue(entry.Key, out Rmf2TranslationV5? origin)) continue;
            Rmf2TranslationV5? translation = locale.DirectResources.FirstOrDefault(resource => resource.Key == entry.Key);
            if (translation is null && entry.State == TranslationReviewState.Draft) continue;
            stale.Add(new TranslationReviewStaleEntry(entry, translation, translation?.SourceLocation ?? origin.SourceLocation));
        }
        return stale;
    }

    /// <summary>The <c>RTR0090</c> warnings of <see cref="Stale"/>, each naming its message and locale.</summary>
    internal static IReadOnlyList<TranslationDiagnostic> StaleDiagnostics(Rmf2ProjectCompilation compilation, TranslationInterchangeReview review) =>
        Stale(compilation, review).Select(static stale => new TranslationDiagnostic(StaleCode, TranslationDiagnosticSeverity.Warning,
            $"The '{stale.Entry.Locale}' translation of '{stale.Entry.Key}' ({StateText(stale.Entry.State)}) predates a change to its source; review it, then record it with runic-translations review mark --key {stale.Entry.Key} --locale {stale.Entry.Locale} --state <state>.",
            stale.Location) { MessageKey = stale.Entry.Key, Locale = stale.Entry.Locale }).ToArray();

    /// <summary>The <c>RTR0091</c> warning for a review file that cannot be read.</summary>
    internal static TranslationDiagnostic InvalidDiagnostic(string displayPath, TranslationInterchangeException exception) =>
        new(InvalidCode, TranslationDiagnosticSeverity.Warning, $"The review file cannot be read ({exception.Code}): {exception.Message}",
            new TextSourceLocation(displayPath, 0, 0, 1, 1, 1, 1));

    /// <summary>
    /// Records <paramref name="state"/> for <paramref name="keys"/> in <paramref name="locale"/>, each with the fingerprint of
    /// its current source (<c>review mark</c>). A null <paramref name="note"/> keeps an entry's note; other entries are kept.
    /// </summary>
    internal static TranslationInterchangeReview Mark(TranslationInterchangeReview? existing, string catalog, IEnumerable<string> keys, string locale,
        TranslationReviewState state, string? note, IReadOnlyDictionary<string, string> fingerprints)
    {
        var merged = (existing?.Entries ?? []).ToDictionary(static entry => Identity(entry.Key, entry.Locale), StringComparer.Ordinal);
        foreach (string key in keys.Distinct(StringComparer.Ordinal))
        {
            if (!fingerprints.TryGetValue(key, out string? fingerprint)) throw new ArgumentException($"'{key}' is not a message of catalog '{catalog}'.", nameof(keys));
            merged.TryGetValue(Identity(key, locale), out TranslationInterchangeReviewEntry? current);
            merged[Identity(key, locale)] = new TranslationInterchangeReviewEntry(key, locale, state, note ?? current?.Note, fingerprint);
        }
        return new TranslationInterchangeReview(catalog, merged.Values);
    }

    /// <summary>
    /// The review file after an XLIFF import (<c>xliff import</c> and the editor): every applied unit whose translation or
    /// explicit state changed gets an entry with the fingerprint of the source it was translated for. Unchanged text in the
    /// default <c>translated</c> state keeps its entry; an approval is made for the source the file showed. Returns whether
    /// an entry changed.
    /// </summary>
    internal static (TranslationInterchangeReview Review, bool Changed) MergeImport(TranslationInterchangeReview? existing, string catalog,
        TranslationXliffImportResult import, TranslationXliffImportPlan plan)
    {
        var merged = (existing?.Entries ?? []).ToDictionary(static entry => Identity(entry.Key, entry.Locale), StringComparer.Ordinal);
        var explicitEntries = import.Review.Entries.ToDictionary(static entry => entry.Key, StringComparer.Ordinal);
        bool changed = false;
        foreach (TranslationXliffUnitResult unit in import.Units.Where(static unit => unit.Outcome == TranslationXliffUnitOutcome.Applied))
        {
            bool explicitState = explicitEntries.TryGetValue(unit.Key, out TranslationInterchangeReviewEntry? stated);
            string identity = Identity(unit.Key, import.TargetLocale);
            merged.TryGetValue(identity, out TranslationInterchangeReviewEntry? current);
            bool written = plan.Values.ContainsKey(unit.Key);
            if (!explicitState && !written && current is not null) continue;
            TranslationReviewState state = unit.State ?? TranslationReviewState.Translated;
            string? fingerprint = state != TranslationReviewState.Approved && !written && current?.SourceFingerprint is { } kept ? kept : unit.SourceFingerprint;
            var entry = new TranslationInterchangeReviewEntry(unit.Key, import.TargetLocale, state, stated?.Note ?? current?.Note, fingerprint);
            if (current is null || current.State != entry.State || current.Note != entry.Note || current.SourceFingerprint != entry.SourceFingerprint) changed = true;
            merged[identity] = entry;
        }
        return (new TranslationInterchangeReview(catalog, merged.Values), changed);
    }

    internal static string StateText(TranslationReviewState state) => state switch
    {
        TranslationReviewState.Draft => "draft",
        TranslationReviewState.Translated => "translated",
        TranslationReviewState.NeedsReview => "needs-review",
        _ => "approved",
    };

    internal static bool TryParseState(string? value, out TranslationReviewState state)
    {
        switch (value)
        {
            case "draft": state = TranslationReviewState.Draft; return true;
            case "translated": state = TranslationReviewState.Translated; return true;
            case "needs-review": state = TranslationReviewState.NeedsReview; return true;
            case "approved": state = TranslationReviewState.Approved; return true;
            default: state = default; return false;
        }
    }

    internal static string Identity(string key, string locale) => key + "\0" + locale;
}

/// <summary>What an XLIFF import does to one unit: <c>added</c>, <c>changed</c>, <c>unchanged</c>, <c>skipped</c> or <c>refused</c>.</summary>
internal sealed record TranslationXliffImportUnitPlan(string Key, string Outcome, string? Before, string? After,
    TranslationReviewState? State, bool SourceChanged, string? Code, string? Message);

/// <summary>
/// The per-unit plan of an XLIFF import against the current catalog (<see cref="Create"/>): the translations to write
/// (<see cref="Values"/>, the target MF2 pattern by key) and one row per unit. Shared by <c>xliff import</c> and the
/// editor, so both apply, skip and refuse the same units.
/// </summary>
internal sealed class TranslationXliffImportPlan
{
    private TranslationXliffImportPlan(IReadOnlyDictionary<string, string> values, IReadOnlyList<TranslationXliffImportUnitPlan> units)
    {
        Values = values;
        Units = units;
    }

    internal IReadOnlyDictionary<string, string> Values { get; }
    internal IReadOnlyList<TranslationXliffImportUnitPlan> Units { get; }
    internal int Count(string outcome) => Units.Count(unit => unit.Outcome == outcome);
    internal int SourceChanged => Units.Count(static unit => unit.SourceChanged && unit.Outcome is "added" or "changed" or "unchanged");

    /// <summary>Compares each applied unit with the target locale's current translation; equivalent MF2 spellings are unchanged.</summary>
    internal static TranslationXliffImportPlan Create(Rmf2ProjectCompilation compilation, TranslationXliffImportResult import)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(import);
        Rmf2ProjectV5 project = compilation.Project ?? throw new ArgumentException("The catalog does not compile.", nameof(compilation));
        Rmf2LocaleV5 target = project.Locales.SingleOrDefault(locale => locale.Tag == import.TargetLocale)
            ?? throw new TranslationInterchangeException("XLIFF21-TARGET-LOCALE", $"The catalog has no locale '{import.TargetLocale}'.");
        var existing = target.DirectResources.ToDictionary(static resource => resource.Key,
            static resource => System.Text.Encoding.UTF8.GetString(resource.Message.Syntax.Source.GetUtf8Bytes()), StringComparer.Ordinal);
        var messages = import.Messages.ToDictionary(static message => message.MessageId,
            static message => System.Text.Encoding.UTF8.GetString(message.Bytes.Span).TrimEnd('\n'), StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var units = new List<TranslationXliffImportUnitPlan>(import.Units.Count);
        foreach (TranslationXliffUnitResult unit in import.Units)
        {
            if (unit.Outcome == TranslationXliffUnitOutcome.Skipped) { units.Add(new(unit.Key, "skipped", null, null, null, false, unit.Code, unit.Message)); continue; }
            if (unit.Outcome == TranslationXliffUnitOutcome.Refused) { units.Add(new(unit.Key, "refused", null, null, null, false, unit.Code, unit.Message)); continue; }
            string pattern = messages[unit.Key];
            TranslationReviewState state = unit.State ?? TranslationReviewState.Translated;
            string? code = unit.SourceChanged ? "XLIFF21-SOURCE-CHANGED" : null;
            if (!existing.TryGetValue(unit.Key, out string? before))
            {
                values[unit.Key] = pattern;
                units.Add(new(unit.Key, "added", null, pattern, state, unit.SourceChanged, code, null));
            }
            else if (TranslationInterchangePattern.Canonical(before) != TranslationInterchangePattern.Canonical(pattern))
            {
                values[unit.Key] = pattern;
                units.Add(new(unit.Key, "changed", before, pattern, state, unit.SourceChanged, code, null));
            }
            else units.Add(new(unit.Key, "unchanged", before, pattern, state, unit.SourceChanged, code, null));
        }
        return new TranslationXliffImportPlan(values, units);
    }
}
