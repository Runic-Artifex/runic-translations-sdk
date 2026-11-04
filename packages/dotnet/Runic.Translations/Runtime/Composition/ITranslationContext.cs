using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Runic.Translations;

/// <summary>
/// One requested locale for several catalogs, such as an application's catalog and the catalogs of the libraries it uses.
/// Switching the locale switches every catalog together.
/// </summary>
/// <remarks>
/// <para>
/// Use one context per host, window or request. Generated text classes take it in their constructor
/// (<c>new GridText(context)</c>) and use the manager of their own catalog, so a library never creates a manager itself.
/// </para>
/// <para>
/// Applications may implement this interface. A minor release adds members to it only with a default implementation, which
/// may throw <see cref="NotSupportedException"/> or report that the feature is not supported.
/// </para>
/// </remarks>
public interface ITranslationContext
{
    /// <summary>The requested locale, canonicalized; empty when the invariant culture's name was requested.</summary>
    string RequestedLocale { get; }

    /// <summary>The state of every registered catalog for <see cref="RequestedLocale"/>, in registration order.</summary>
    IReadOnlyList<TranslationCatalogStatus> Catalogs { get; }

    /// <summary>Raised once after <see cref="SetLocaleAsync"/> published new snapshots for every catalog.</summary>
    event EventHandler<TranslationContextLocaleChangedEventArgs>? LocaleChanged;

    /// <summary>Gets the manager of a registered catalog.</summary>
    /// <exception cref="KeyNotFoundException">The catalog is not registered with this context.</exception>
    ITranslationManager GetManager(string catalogId);

    /// <summary>Attempts to get the manager of a registered catalog.</summary>
    bool TryGetManager(string catalogId, [NotNullWhen(true)] out ITranslationManager? manager);

    /// <summary>
    /// Prepares a snapshot of every catalog for <paramref name="locale"/>, then publishes them together. When any catalog
    /// fails, or the operation is canceled, no catalog changes.
    /// </summary>
    /// <remarks>Each catalog resolves the locale under its own <see cref="UnsupportedLocalePolicy"/>; see <see cref="Catalogs"/>.</remarks>
    ValueTask SetLocaleAsync(string locale, CancellationToken cancellationToken = default);

    /// <summary>Recomposes every catalog's snapshot for the requested locale, for example after overlay packs changed.</summary>
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a stored or transported reference of any registered catalog in that catalog's current snapshot.</summary>
    /// <remarks>A reference of a catalog that is not registered shows its fallback text with a <c>catalog-mismatch</c> diagnostic.</remarks>
    TranslationReferenceResolution Resolve(TranslationReference reference);
}

/// <summary>The state of one catalog in a translation context.</summary>
public sealed class TranslationCatalogStatus
{
    private readonly ReadOnlyCollection<string> _supportedLocales;
    private readonly ReadOnlyCollection<TranslationOverlayDiagnostic> _overlayDiagnostics;

    /// <summary>Creates status data for a catalog's active snapshot.</summary>
    public TranslationCatalogStatus(
        ITranslationSnapshot snapshot,
        IReadOnlyList<string>? supportedLocales = null,
        IReadOnlyList<TranslationOverlayDiagnostic>? overlayDiagnostics = null)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _supportedLocales = new ReadOnlyCollection<string>([.. supportedLocales ?? []]);
        _overlayDiagnostics = new ReadOnlyCollection<TranslationOverlayDiagnostic>([.. overlayDiagnostics ?? []]);
    }

    /// <summary>The catalog ID.</summary>
    public string CatalogId => Snapshot.Catalog;

    /// <summary>The catalog's active snapshot.</summary>
    public ITranslationSnapshot Snapshot { get; }

    /// <summary>The requested locale the snapshot resolved.</summary>
    public string RequestedLocale => Snapshot.RequestedLocale;

    /// <summary>The locale of the snapshot.</summary>
    public string Locale => Snapshot.Locale;

    /// <summary>
    /// How the requested locale resolved: anything but <see cref="LocaleResolution.Exact"/> means this catalog shows text in
    /// another locale than the one requested.
    /// </summary>
    public LocaleResolution LocaleResolution => Snapshot.LocaleResolution;

    /// <summary>The locales the catalog declares, including locales its overlays add, in ordinal order; empty when unknown.</summary>
    public IReadOnlyList<string> SupportedLocales => _supportedLocales;

    /// <summary>The overlay messages skipped while composing the snapshot.</summary>
    public IReadOnlyList<TranslationOverlayDiagnostic> OverlayDiagnostics => _overlayDiagnostics;
}

/// <summary>Describes a locale switch of a translation context.</summary>
public sealed class TranslationContextLocaleChangedEventArgs : EventArgs
{
    /// <summary>Creates event data.</summary>
    public TranslationContextLocaleChangedEventArgs(string oldRequestedLocale, string requestedLocale, IReadOnlyList<TranslationCatalogStatus> catalogs)
    {
        OldRequestedLocale = oldRequestedLocale ?? throw new ArgumentNullException(nameof(oldRequestedLocale));
        RequestedLocale = requestedLocale ?? throw new ArgumentNullException(nameof(requestedLocale));
        Catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));
    }

    /// <summary>The previously requested locale.</summary>
    public string OldRequestedLocale { get; }

    /// <summary>The newly requested locale.</summary>
    public string RequestedLocale { get; }

    /// <summary>The state of every catalog after the switch, in registration order.</summary>
    public IReadOnlyList<TranslationCatalogStatus> Catalogs { get; }
}
