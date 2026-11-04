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
    private readonly Dictionary<string, string> _addedLocales = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CompiledTranslationSnapshot> _snapshots = new(StringComparer.Ordinal);

    internal OverlayTranslationProvider(CompiledTranslationCatalog catalog, string markupContract, ITextValueFormatter valueFormatter,
        IReadOnlyList<TranslationOverlay> overlays)
    {
        _catalog = catalog;
        _markup = Rmf2MarkupContract.Link(markupContract);
        _valueFormatter = valueFormatter;
        _overlays = new TranslationOverlay[overlays.Count];
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
                string fallback = added.FallbackLocale ?? catalog.DefaultLocale;
                if (!catalog.DeclaresLocale(fallback))
                    throw new ArgumentException(
                        $"Overlay locale '{added.Locale}' of catalog '{catalog.Catalog}' falls back to '{fallback}', which the catalog does not declare.",
                        nameof(overlays));
                _addedLocales.TryAdd(added.Locale, fallback);
            }

            _overlays[index] = overlay;
        }

        var supported = new List<string>(catalog.LocaleTags());
        supported.AddRange(_addedLocales.Keys);
        supported.Sort(StringComparer.Ordinal);
        SupportedLocales = supported.ToArray();
    }

    /// <summary>The catalog's locales and the locales the overlays add, in ordinal order.</summary>
    internal string[] SupportedLocales { get; }

    public async ValueTask<ITranslationSnapshot> GetSnapshotAsync(string requestedLocale, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LocaleRequest request = LocaleRequestResolver.Resolve(requestedLocale, Declares, _catalog.UnsupportedLocale, _catalog.DefaultLocale,
            _catalog.Catalog);
        if (!_snapshots.TryGetValue(request.Locale, out CompiledTranslationSnapshot? snapshot))
        {
            snapshot = await ComposeAsync(request.Locale, cancellationToken).ConfigureAwait(false);
            snapshot = _snapshots.GetOrAdd(request.Locale, snapshot);
        }

        return snapshot.ForRequest(request);
    }

    void ITranslationSnapshotCacheInvalidator.InvalidateSnapshot(string canonicalLocale) => _snapshots.TryRemove(canonicalLocale, out _);

    private bool Declares(string locale) => _catalog.DeclaresLocale(locale) || _addedLocales.ContainsKey(locale);

    // The requested locale, then (for an added locale) the one library locale it falls back to, then the library chain.
    private List<string> Chain(string locale)
    {
        var chain = new List<string>();
        string? current = locale;
        if (_addedLocales.TryGetValue(locale, out string? fallback))
        {
            chain.Add(locale);
            current = fallback;
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
            // A locale's own pack wins over a fallback copy of its messages in a more specific locale's pack.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (OverlayPack pack in packs)
                {
                    foreach (OverlayPackMessage message in pack.Messages)
                    {
                        bool own = string.Equals(message.ContentLocale, pack.Locale, StringComparison.Ordinal);
                        if (own != (pass == 0) || !step.TryGetValue(message.ContentLocale, out int at)) continue;
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

        var library = new (string?[] Patterns, CompiledTextMessage?[] Messages)?[chain.Count];
        for (int index = 0; index < chain.Count; index++)
            if (_catalog.DeclaresLocale(chain[index])) library[index] = _catalog.GetDirect(chain[index]);

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
