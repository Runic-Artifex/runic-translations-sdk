using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Runic.Translations;

/// <summary>
/// Application-owned messages for a catalog that another package owns, such as a component library's catalog.
/// Attach it to a <see cref="TranslationContextBuilder"/> by catalog ID; the library needs no code for it.
/// </summary>
/// <remarks>
/// <para>
/// An overlay is checked key by key, not by the library's whole-catalog contract fingerprint, so it keeps working when a
/// later library release adds messages. Each overlay message must match the library's definition of its key: the same
/// inputs with the same types, the same content kind (inline or document), and markup the library's contract accepts
/// (no markup for a message the library formats as plain text). A message that does not match, or whose key the library
/// no longer has, is skipped and reported in <see cref="TranslationCatalogStatus.OverlayDiagnostics"/>; the library's own
/// text is used instead.
/// </para>
/// <para>
/// Resolution is locale-first: for each locale in the requested locale's fallback chain, the overlay is tried before the
/// library, and only then the next locale. The library's German text therefore beats an English override when German is
/// requested.
/// </para>
/// <para>
/// An overlay may add locales the library lacks (<see cref="AddedLocales"/>). An added locale falls back to the locale
/// the overlay names for it. Without one, it falls back to its nearest parent that the library declares or an overlay
/// adds (<c>de-CH</c> to the library's <c>de</c>), and without such a parent to the library's default locale. From the
/// first library locale on, the library's own fallback chain applies. A locale the library already declares keeps the
/// library's chain. Two overlays that add one locale with different fallbacks are an error.
/// </para>
/// </remarks>
public sealed class TranslationOverlay
{
    private readonly ReadOnlyCollection<TranslationOverlayLocale> _addedLocales;

    private TranslationOverlay(string catalog, IEnumerable<TranslationOverlayLocale>? addedLocales, TranslationOverlaySource source)
    {
        if (!TranslationDataValidation.IsCatalog(catalog))
            throw new ArgumentException("The catalog identifier is not canonical.", nameof(catalog));
        var locales = new List<TranslationOverlayLocale>();
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (TranslationOverlayLocale locale in addedLocales ?? [])
        {
            if (locale is null) throw new ArgumentException("Added locales cannot contain null.", nameof(addedLocales));
            if (!tags.Add(locale.Locale)) throw new ArgumentException($"Locale '{locale.Locale}' is added more than once.", nameof(addedLocales));
            locales.Add(locale);
        }

        Catalog = catalog;
        _addedLocales = locales.AsReadOnly();
        Source = source;
    }

    /// <summary>The ID of the catalog this overlay targets.</summary>
    public string Catalog { get; }

    /// <summary>The locales this overlay adds to the catalog, each with the library locale it falls back to.</summary>
    public IReadOnlyList<TranslationOverlayLocale> AddedLocales => _addedLocales;

    internal TranslationOverlaySource Source { get; }

    /// <summary>
    /// Creates an overlay from locale artifacts v5 (the <c>&lt;catalog&gt;.&lt;locale&gt;.locale-v5.json</c> files that
    /// <c>runic-translations generate</c> writes), loaded from <paramref name="source"/> when a snapshot needs a locale.
    /// </summary>
    /// <param name="catalog">The ID of the catalog to overlay; every pack must name it.</param>
    /// <param name="source">Supplies the pack of each locale in a fallback chain, or <see langword="null"/> when there is none.</param>
    /// <param name="addedLocales">Locales the overlay adds to the catalog.</param>
    /// <param name="limits">Bounds for untrusted pack bytes; the runtime defaults when omitted.</param>
    /// <param name="integrityVerifier">Runs before a pack is parsed and may reject it.</param>
    /// <remarks>
    /// A pack may hold any subset of the catalog's keys and may come from a project built against another release of the
    /// library: its contract fingerprint is not compared. A message whose content locale is not the pack's locale (a fallback
    /// copy that a build with relaxed completeness writes) applies only to a locale that an overlay adds; for a locale the
    /// library declares, the library's text (or the overlay's own pack for that locale) is used, as for a user who requests
    /// that locale. A pack that is malformed, names another catalog or locale,
    /// or is rejected by <paramref name="integrityVerifier"/> fails the locale switch with a
    /// <see cref="TranslationPackException"/>, as an external pack does.
    /// </remarks>
    public static TranslationOverlay FromPacks(
        string catalog,
        IExternalTranslationSource source,
        IEnumerable<TranslationOverlayLocale>? addedLocales = null,
        TranslationPackLimits? limits = null,
        TranslationPackIntegrityVerifier? integrityVerifier = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(source);
        return new TranslationOverlay(catalog, addedLocales, new PackOverlaySource(source, limits ?? new TranslationPackLimits(), integrityVerifier));
    }

    // The seam for overlays compiled into the application (build-time overrides): generated code supplies a source of
    // already parsed messages, which are checked key by key like pack messages.
    internal static TranslationOverlay Create(string catalog, IEnumerable<TranslationOverlayLocale>? addedLocales, TranslationOverlaySource source)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(source);
        return new TranslationOverlay(catalog, addedLocales, source);
    }
}

/// <summary>A locale that an overlay adds to a catalog.</summary>
public sealed class TranslationOverlayLocale
{
    /// <summary>Declares an added locale.</summary>
    /// <param name="locale">The canonical BCP 47 tag of the added locale, such as <c>fr</c>.</param>
    /// <param name="fallbackLocale">
    /// The library locale that keys the overlay lacks fall back to. It must be a locale the library declares. When omitted,
    /// the nearest parent that the library declares or an overlay adds (<c>de</c> for <c>de-CH</c>), else the library's
    /// default locale.
    /// </param>
    public TranslationOverlayLocale(string locale, string? fallbackLocale = null)
    {
        ArgumentNullException.ThrowIfNull(locale);
        if (!IsCanonical(locale)) throw new ArgumentException("The locale must be a canonical BCP 47 tag.", nameof(locale));
        if (fallbackLocale is not null && !IsCanonical(fallbackLocale))
            throw new ArgumentException("The fallback locale must be a canonical BCP 47 tag.", nameof(fallbackLocale));
        if (string.Equals(locale, fallbackLocale, StringComparison.Ordinal))
            throw new ArgumentException("A locale cannot fall back to itself.", nameof(fallbackLocale));
        Locale = locale;
        FallbackLocale = fallbackLocale;
    }

    /// <summary>The canonical tag of the added locale.</summary>
    public string Locale { get; }

    /// <summary>The library locale it falls back to, or <see langword="null"/> for its nearest parent, else the library's default locale.</summary>
    public string? FallbackLocale { get; }

    private static bool IsCanonical(string tag) =>
        LocaleTag.TryCanonicalize(tag, out string canonical) && string.Equals(tag, canonical, StringComparison.Ordinal);
}

/// <summary>Why an overlay message was not used.</summary>
/// <remarks>The set is open: a later release may add reasons, so treat a value you do not know like <see cref="Unknown"/>.</remarks>
public enum TranslationOverlayDiagnosticReason
{
    /// <summary>No reason recorded; the value of a default <see cref="TranslationOverlayDiagnostic"/>.</summary>
    Unknown = 0,
    /// <summary>The catalog has no message with this key, for example because a later library release removed it.</summary>
    UnknownKey,
    /// <summary>The message's inputs differ from the catalog's inputs for the key in name or type.</summary>
    IncompatibleInputs,
    /// <summary>The message is a document where the catalog's message is inline text, or the reverse.</summary>
    ContentKindChanged,
    /// <summary>The message uses markup the catalog's contract does not accept for the key.</summary>
    IncompatibleMarkup,
}

/// <summary>An overlay message that was skipped while composing a snapshot.</summary>
/// <param name="Catalog">The catalog ID.</param>
/// <param name="Locale">The locale the message is written in (its content locale).</param>
/// <param name="Key">The message key as written in the overlay.</param>
/// <param name="Reason">Why the message was skipped.</param>
public readonly record struct TranslationOverlayDiagnostic(string Catalog, string Locale, string Key, TranslationOverlayDiagnosticReason Reason);

/// <summary>Supplies the parsed overlay messages of one locale.</summary>
internal abstract class TranslationOverlaySource
{
    /// <summary>Loads the messages of <paramref name="locale"/>, or returns <see langword="null"/> when the overlay has none.</summary>
    internal abstract ValueTask<OverlayPack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken);
}

internal sealed class PackOverlaySource : TranslationOverlaySource
{
    private readonly IExternalTranslationSource _source;
    private readonly TranslationPackLimits _limits;
    private readonly TranslationPackIntegrityVerifier? _integrityVerifier;

    internal PackOverlaySource(IExternalTranslationSource source, TranslationPackLimits limits, TranslationPackIntegrityVerifier? integrityVerifier)
    {
        _source = source;
        _limits = limits;
        _integrityVerifier = integrityVerifier;
    }

    internal override async ValueTask<OverlayPack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExternalTranslationPack? pack;
        try
        {
            pack = await _source.LoadAsync(catalog, locale, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw TranslationPackFailure.Create("The overlay pack source failed to load a pack.", TranslationPackFailureReason.SourceFailure);
        }

        if (pack is null) return null;
        ReadOnlyMemory<byte> callerContent = pack.Content;
        if (callerContent.Length == 0) throw TranslationPackFailure.Create("The overlay pack is empty.", TranslationPackFailureReason.Malformed);
        if (callerContent.Length > _limits.MaximumDocumentBytes)
            throw TranslationPackFailure.Create("The overlay pack exceeds the configured document limit.", TranslationPackFailureReason.LimitExceeded);

        // As for external packs: parse one owned image and give the verifier an independent copy of it.
        byte[] owned = callerContent.ToArray();
        if (_integrityVerifier is not null)
        {
            bool accepted;
            try
            {
                accepted = await _integrityVerifier(owned.AsSpan().ToArray(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw TranslationPackFailure.Create("Overlay pack integrity verification failed.", TranslationPackFailureReason.IntegrityRejected);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!accepted) throw TranslationPackFailure.Create("The overlay pack was rejected by the integrity policy.", TranslationPackFailureReason.IntegrityRejected);
        }

        return TranslationPackV5Loader.ParseOverlay(owned, catalog, locale, _limits, cancellationToken);
    }
}
