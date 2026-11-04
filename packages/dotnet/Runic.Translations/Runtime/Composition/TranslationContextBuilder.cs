using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Runic.Translations;

/// <summary>Collects the catalogs and overlays of a <see cref="TranslationContext"/>.</summary>
/// <remarks>
/// Register each catalog once, typically the application's own catalog first and then its libraries'. Overlays are
/// attached by catalog ID and may be added before or after their catalog. A builder can build several independent
/// contexts, for example one per window.
/// </remarks>
public sealed class TranslationContextBuilder
{
    private readonly List<(TranslationCatalogRegistration Registration, TranslationOptions? Options)> _catalogs = [];
    private readonly List<TranslationOverlay> _overlays = [];
    private ITextValueFormatter? _valueFormatter;

    internal TranslationContextBuilder()
    {
    }

    /// <summary>Registers a catalog, such as <c>GridTextCatalog.Registration</c>.</summary>
    /// <param name="registration">The generated registration of the catalog.</param>
    /// <param name="options">The catalog's runtime options; its generated policies when omitted.</param>
    /// <remarks>
    /// Registering the same catalog again, with the same contract fingerprint, is ignored, so independent libraries can
    /// register a catalog they share. A second registration with another fingerprint is an error.
    /// </remarks>
    /// <exception cref="ArgumentException">The catalog is already registered with another contract fingerprint.</exception>
    public TranslationContextBuilder Add(TranslationCatalogRegistration registration, TranslationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registration);
        foreach ((TranslationCatalogRegistration existing, _) in _catalogs)
        {
            if (!string.Equals(existing.CatalogId, registration.CatalogId, StringComparison.Ordinal)) continue;
            if (string.Equals(existing.ContractFingerprint, registration.ContractFingerprint, StringComparison.Ordinal)) return this;
            throw new ArgumentException(
                $"Catalog '{registration.CatalogId}' is already registered with another contract fingerprint.", nameof(registration));
        }

        _catalogs.Add((registration, options));
        return this;
    }

    /// <summary>Attaches an overlay to the catalog it names.</summary>
    /// <remarks>
    /// Overlays of one catalog are tried in the order they were added, all of them at each locale before the catalog's
    /// own text at that locale.
    /// </remarks>
    public TranslationContextBuilder AddOverlay(TranslationOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _overlays.Add(overlay);
        return this;
    }

    /// <summary>Uses <paramref name="valueFormatter"/> for every catalog instead of the default formatter.</summary>
    public TranslationContextBuilder UseValueFormatter(ITextValueFormatter valueFormatter)
    {
        _valueFormatter = valueFormatter ?? throw new ArgumentNullException(nameof(valueFormatter));
        return this;
    }

    /// <summary>Builds a context and composes every catalog's snapshot for <paramref name="initialLocale"/>.</summary>
    /// <param name="initialLocale">The requested locale; the default locale of the first registered catalog when omitted.</param>
    /// <param name="cancellationToken">Cancels composition.</param>
    /// <exception cref="InvalidOperationException">No catalog is registered, or an overlay names a catalog that is not.</exception>
    public ValueTask<TranslationContext> BuildAsync(string? initialLocale = null, CancellationToken cancellationToken = default)
    {
        TranslationContext.Entry[] entries = CreateEntries();
        string requested = LocaleRequestResolver.Canonicalize(initialLocale ?? entries[0].Registration.DefaultLocale, nameof(initialLocale));
        return TranslationContext.CreateAsync(entries, requested, cancellationToken);
    }

    /// <summary>Builds a context without awaiting, for startup code that cannot await.</summary>
    /// <remarks>
    /// Generated catalogs compose without I/O, so this completes synchronously for them. Overlays whose sources do I/O
    /// block the caller until they are loaded; prefer <see cref="BuildAsync"/> for them.
    /// </remarks>
    /// <inheritdoc cref="BuildAsync" path="/param"/>
    /// <inheritdoc cref="BuildAsync" path="/exception"/>
    public TranslationContext Build(string? initialLocale = null)
    {
        ValueTask<TranslationContext> build = BuildAsync(initialLocale);
        return build.IsCompletedSuccessfully ? build.Result : build.AsTask().GetAwaiter().GetResult();
    }

    private TranslationContext.Entry[] CreateEntries()
    {
        if (_catalogs.Count == 0) throw new InvalidOperationException("A translation context needs at least one catalog.");
        var overlaysById = new Dictionary<string, List<TranslationOverlay>>(StringComparer.Ordinal);
        foreach (TranslationOverlay overlay in _overlays)
        {
            if (!_catalogs.Exists(item => string.Equals(item.Registration.CatalogId, overlay.Catalog, StringComparison.Ordinal)))
                throw new InvalidOperationException($"An overlay names catalog '{overlay.Catalog}', which is not registered.");
            if (!overlaysById.TryGetValue(overlay.Catalog, out List<TranslationOverlay>? list)) overlaysById.Add(overlay.Catalog, list = []);
            list.Add(overlay);
        }

        var entries = new TranslationContext.Entry[_catalogs.Count];
        for (int index = 0; index < entries.Length; index++)
        {
            (TranslationCatalogRegistration registration, TranslationOptions? options) = _catalogs[index];
            ITranslationProvider provider = registration.CreateProvider(_valueFormatter, null, options);
            string[] supported = provider is CompiledTranslationProvider compiled ? compiled.Catalog.LocaleTags() : [];
            if (overlaysById.TryGetValue(registration.CatalogId, out List<TranslationOverlay>? overlays))
            {
                if (provider is not CompiledTranslationProvider { UsesDefaultSnapshotFactory: true } generated)
                    throw new InvalidOperationException(
                        $"Catalog '{registration.CatalogId}' has overlays, but its provider factory does not create a generated catalog provider.");
                var overlaid = new OverlayTranslationProvider(generated.Catalog, registration.Rmf2MarkupContract, generated.ValueFormatter, overlays);
                provider = overlaid;
                supported = overlaid.SupportedLocales;
            }

            entries[index] = new TranslationContext.Entry(registration, provider, supported);
        }

        return entries;
    }
}
