using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Runic.Translations;

/// <summary>
/// Composes a generated catalog with application overlays, locale-first: for each locale of the chain, the overlays in
/// the order they were attached, then the catalog, and only then the next locale.
/// </summary>
internal sealed class OverlayTranslationProvider : ITranslationProvider, ITranslationSnapshotCacheInvalidator
{
    private readonly CompiledTranslationCatalog _catalog;
    private readonly Rmf2MarkupContract _markup;
    private readonly ITextValueFormatter _valueFormatter;
    private readonly TranslationOverlay[] _overlays;
    // An added locale and the next locale of its chain: its explicit fallback, else its nearest declared or added CLDR
    // parent, else the library locale CLDR likely subtags match, else the catalog's default locale.
    private readonly Dictionary<string, string> _addedLocales = new(StringComparer.Ordinal);
    // Each snapshot with the catalog generation and contract link version it was composed from (Hot Reload).
    private readonly ConcurrentDictionary<string, Composed> _snapshots = new(StringComparer.Ordinal);

    internal OverlayTranslationProvider(CompiledTranslationCatalog catalog, Rmf2MarkupContract markup, ITextValueFormatter valueFormatter,
        IReadOnlyList<TranslationOverlay> overlays)
    {
        _catalog = catalog;
        _markup = markup;
        _valueFormatter = valueFormatter;
        _overlays = new TranslationOverlay[overlays.Count];
        // Explicit fallbacks per added locale; null when no overlay names one.
        var explicitFallbacks = new Dictionary<string, string?>(StringComparer.Ordinal);
        // The locales the overlays add, which an explicit fallback may name as well as a catalog locale (de-CH to an added de).
        var addedTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (TranslationOverlay overlay in overlays)
            foreach (TranslationOverlayLocale added in overlay.AddedLocales)
                if (!catalog.DeclaresLocale(added.Locale)) addedTags.Add(added.Locale);
        for (int index = 0; index < overlays.Count; index++)
        {
            TranslationOverlay overlay = overlays[index];
            if (!string.Equals(overlay.Catalog, catalog.Catalog, StringComparison.Ordinal))
                throw new ArgumentException($"An overlay of catalog '{overlay.Catalog}' cannot be attached to catalog '{catalog.Catalog}'.", nameof(overlays));
            foreach (TranslationOverlayLocale added in overlay.AddedLocales)
            {
                // A locale the catalog declares keeps the catalog's chain, so a library release that adds the locale
                // does not break the application's overlay.
                if (catalog.DeclaresLocale(added.Locale)) continue;
                if (added.FallbackLocale is { } fallback && !catalog.DeclaresLocale(fallback) && !addedTags.Contains(fallback))
                    throw new ArgumentException(
                        $"Overlay locale '{added.Locale}' of catalog '{catalog.Catalog}' falls back to '{fallback}', which neither the catalog declares nor an overlay adds.",
                        nameof(overlays));
                if (!explicitFallbacks.TryGetValue(added.Locale, out string? existing) || existing is null)
                    explicitFallbacks[added.Locale] = added.FallbackLocale;
                else if (added.FallbackLocale is not null && !string.Equals(existing, added.FallbackLocale, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"Two overlays of catalog '{catalog.Catalog}' add locale '{added.Locale}' with different fallbacks ('{existing}' and '{added.FallbackLocale}').",
                        nameof(overlays));
            }

            _overlays[index] = overlay;
        }

        var supported = new List<string>(catalog.LocaleTags());
        supported.AddRange(explicitFallbacks.Keys);
        supported.Sort(StringComparer.Ordinal);
        SupportedLocales = supported.ToArray();

        // Without an explicit fallback, an added locale falls back to its nearest CLDR parent that the library declares or
        // an overlay adds (de-CH to the library's de, es-MX to es-419, zh-TW to zh-Hant, never sr-Latn-RS to a Cyrillic sr),
        // then to the library locale that CLDR likely subtags match (de-CH to de-DE), then to the library's default locale.
        foreach ((string locale, string? fallback) in explicitFallbacks)
        {
            _addedLocales.Add(locale, fallback ??
                LocaleRequestResolver.NearestParent(locale, SupportedLocales) ??
                LocaleNegotiation.Match(locale, catalog.LocaleTags()) ??
                catalog.DefaultLocale);
        }

        // Explicit fallbacks between added locales must reach a catalog locale.
        foreach (string locale in _addedLocales.Keys)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { locale };
            for (string current = _addedLocales[locale]; _addedLocales.TryGetValue(current, out string? next); current = next)
                if (!visited.Add(current))
                    throw new ArgumentException($"The added locales of catalog '{catalog.Catalog}' fall back in a cycle through '{locale}'.", nameof(overlays));
        }
    }

    /// <summary>The catalog's locales and the locales the overlays add, in ordinal order.</summary>
    internal string[] SupportedLocales { get; }

    public async ValueTask<ITranslationSnapshot> GetSnapshotAsync(string requestedLocale, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LocaleRequest request = LocaleRequestResolver.Resolve(requestedLocale, SupportedLocales, _catalog.UnsupportedLocale,
            _catalog.DefaultLocale, _catalog.Catalog);
        if (!_snapshots.TryGetValue(request.Locale, out Composed? composed) || !composed.IsCurrent(this))
        {
            // Read before composing, so a snapshot that may have read replaced data is never current.
            int generation = _catalog.Generation, linkVersion = _markup.LinkVersion;
            CompiledTranslationSnapshot snapshot = await ComposeAsync(request.Locale, cancellationToken).ConfigureAwait(false);
            var created = new Composed(snapshot, generation, linkVersion);
            // A caller that composed the same locale first wins, unless its composition is out of date.
            composed = _snapshots.AddOrUpdate(request.Locale, created, (_, existing) => existing.IsCurrent(this) ? existing : created);
        }

        return composed.Snapshot.ForRequest(request);
    }

    private sealed record Composed(CompiledTranslationSnapshot Snapshot, int Generation, int LinkVersion)
    {
        internal bool IsCurrent(OverlayTranslationProvider owner) =>
            Generation == owner._catalog.Generation && LinkVersion == owner._markup.LinkVersion;
    }

    /// <summary>Chooses among the catalog's and the overlay-added locales, else the catalog's default locale.</summary>
    public string NegotiateLocale(IEnumerable<string> preferredLocales)
    {
        ArgumentNullException.ThrowIfNull(preferredLocales);
        return LocaleNegotiation.Negotiate(preferredLocales, SupportedLocales) ?? _catalog.DefaultLocale;
    }

    void ITranslationSnapshotCacheInvalidator.InvalidateSnapshot(string canonicalLocale) => _snapshots.TryRemove(canonicalLocale, out _);

    // The requested locale, then (for an added locale) its added parents and the library locale they fall back to, then the
    // library chain. Every added step without an explicit fallback is a strict CLDR ancestor or a shorter tag, and the
    // constructor rejects a cycle of explicit fallbacks, so the chain is finite; the visited check only guards that invariant.
    private List<string> Chain(string locale)
    {
        var chain = new List<string>();
        string? current = locale;
        while (_addedLocales.TryGetValue(current, out string? next))
        {
            chain.Add(current);
            current = next;
            if (chain.Contains(current))
            {
                current = _catalog.DefaultLocale;
                break;
            }
        }

        for (; current is not null; current = _catalog.GetFallbackLocale(current)) chain.Add(current);
        return chain;
    }

    private async ValueTask<CompiledTranslationSnapshot> ComposeAsync(string locale, CancellationToken cancellationToken)
    {
        List<string> chain = Chain(locale);
        var step = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < chain.Count; index++) step.Add(chain[index], index);
        CompiledTranslationDefinition[] definitions = _catalog.DefinitionArray;

        // overlayValues[overlay][step][key]: the checked overlay message for a key at a chain locale.
        var overlayValues = new CompiledTextMessage?[_overlays.Length][][];
        var diagnostics = new List<TranslationOverlayDiagnostic>();
        var reported = new HashSet<TranslationOverlayDiagnostic>();
        for (int overlay = 0; overlay < _overlays.Length; overlay++)
        {
            var packs = new List<OverlayPack>(chain.Count);
            foreach (string chainLocale in chain)
            {
                OverlayPack? pack = await _overlays[overlay].Source.LoadAsync(_catalog.Catalog, chainLocale, cancellationToken).ConfigureAwait(false);
                if (pack is not null) packs.Add(pack);
            }

            var values = overlayValues[overlay] = new CompiledTextMessage?[chain.Count][];
            // A locale's own pack wins over a fallback copy of its messages in a more specific locale's pack. A fallback copy
            // (a message whose content locale is not the pack's locale, written by a build with relaxed completeness) is
            // not application text for a locale the library declares: the library's own text for that locale, or the
            // application's own pack for it, is used, exactly as for a user who requests that locale. Copies apply only to
            // locales the overlays add.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (OverlayPack pack in packs)
                {
                    foreach (OverlayPackMessage message in pack.Messages)
                    {
                        bool own = string.Equals(message.ContentLocale, pack.Locale, StringComparison.Ordinal);
                        if (own != (pass == 0) || !step.TryGetValue(message.ContentLocale, out int at)) continue;
                        if (!own && _catalog.DeclaresLocale(message.ContentLocale)) continue;
                        if (!TryCheck(message, out int id, out CompiledTextMessage? checkedMessage, out TranslationOverlayDiagnosticReason reason))
                        {
                            var diagnostic = new TranslationOverlayDiagnostic(_catalog.Catalog, message.ContentLocale, message.Key, reason);
                            if (reported.Add(diagnostic)) diagnostics.Add(diagnostic);
                            continue;
                        }

                        CompiledTextMessage?[] row = values[at] ??= new CompiledTextMessage?[definitions.Length];
                        row[id] ??= checkedMessage;
                    }
                }
            }
        }

        // The library values of every chain locale from the same generated data: read again if Hot Reload replaced it.
        var library = new LocaleValues?[chain.Count];
        int generation;
        do
        {
            generation = _catalog.Generation;
            for (int index = 0; index < chain.Count; index++)
                if (_catalog.DeclaresLocale(chain[index])) library[index] = _catalog.GetDirect(chain[index]);
        }
        while (generation != _catalog.Generation);

        // An overlay message without its own currency, unit or compact data formats with the catalog's.
        Rmf2FormatData? catalogData = null;
        foreach (LocaleValues? values in library) if (catalogData is null && values is not null) catalogData = CompiledTranslationCatalog.FormatData(values);
        var patterns = new string?[definitions.Length];
        var messages = new CompiledTextMessage?[definitions.Length];
        for (int id = 0; id < definitions.Length; id++)
        {
            for (int index = 0; index < chain.Count && messages[id] is null; index++)
            {
                for (int overlay = 0; overlay < overlayValues.Length; overlay++)
                {
                    if (overlayValues[overlay][index]?[id] is { } value)
                    {
                        value.Rmf2V5?.BindCatalogFormatData(catalogData);
                        messages[id] = value;
                        patterns[id] = string.Empty;
                        break;
                    }
                }

                if (messages[id] is null && library[index] is { } direct && direct.Messages[id] is { } compiled)
                {
                    messages[id] = compiled;
                    patterns[id] = direct.Patterns[id];
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new CompiledTranslationSnapshot(_catalog, locale, patterns, messages, _valueFormatter, diagnostics.ToArray());
    }

    private bool TryCheck(OverlayPackMessage message, out int id, out CompiledTextMessage? checkedMessage, out TranslationOverlayDiagnosticReason reason)
    {
        checkedMessage = null;
        // Keys are matched exactly: a dotted message path is not a key name.
        if (!_catalog.TryGetKey(message.Key, out TranslationKey key) || !string.Equals(key.Name, message.Key, StringComparison.Ordinal) ||
            !_catalog.TryResolveKey(key, out id))
        {
            id = -1;
            reason = TranslationOverlayDiagnosticReason.UnknownKey;
            return false;
        }

        var compiled = CompiledTextMessage.FromRmf2(message.Message);
        if (!CompiledTextMessageRuntime.MatchesContract(compiled, _catalog.DefinitionArray[id].PlaceholderArray))
        {
            reason = TranslationOverlayDiagnosticReason.IncompatibleInputs;
            return false;
        }

        if (!_markup.Messages.TryGetValue(message.Key, out Rmf2MarkupContract.Message? linked))
        {
            reason = TranslationOverlayDiagnosticReason.IncompatibleMarkup;
            return false;
        }

        if (linked.Document != message.Document)
        {
            reason = TranslationOverlayDiagnosticReason.ContentKindChanged;
            return false;
        }

        // Generated accessors format a message without markup as a string, so an overlay cannot add markup to it.
        if (!linked.Structured && compiled.HasMarkup)
        {
            reason = TranslationOverlayDiagnosticReason.IncompatibleMarkup;
            return false;
        }

        try
        {
            _markup.ValidatePlanV5(message.Key, message.Message);
        }
        catch (Exception exception) when (exception is TranslationFormatException or Rmf2DocumentPlanException)
        {
            reason = TranslationOverlayDiagnosticReason.IncompatibleMarkup;
            return false;
        }

        checkedMessage = compiled;
        reason = TranslationOverlayDiagnosticReason.Unknown;
        return true;
    }
}
