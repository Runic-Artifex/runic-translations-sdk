using System;

namespace Runic.Translations;

/// <summary>Creates the provider of a generated catalog, with the signature of the generated <c>CreateProvider</c>.</summary>
public delegate ITranslationProvider TranslationProviderFactory(
    ITextValueFormatter? valueFormatter,
    ITranslationSnapshotFactory? snapshotFactory,
    TranslationOptions? options);

/// <summary>
/// Describes a generated catalog to a <see cref="TranslationContext"/>: its ID, its contract and how to create its provider.
/// </summary>
/// <remarks>
/// Generated code exposes one as <c>XCatalog.Registration</c>, public when the catalog is public, so an application can
/// register a library's catalog and attach overlays to it without code in the library. For a catalog generated before
/// <c>Registration</c> existed, create one from the generated constants and <c>CreateProvider</c>:
/// <c>new TranslationCatalogRegistration(XCatalog.CatalogId, XCatalog.DefaultLocale, XCatalog.ContractFingerprint,
/// XCatalog.Rmf2MarkupContract, XCatalog.CreateProvider)</c>.
/// </remarks>
public sealed class TranslationCatalogRegistration
{
    private readonly TranslationProviderFactory _providerFactory;

    /// <summary>Describes a catalog.</summary>
    /// <param name="catalogId">The stable catalog ID (<c>XCatalog.CatalogId</c>).</param>
    /// <param name="defaultLocale">The canonical default locale (<c>XCatalog.DefaultLocale</c>).</param>
    /// <param name="contractFingerprint">The generated contract fingerprint (<c>XCatalog.ContractFingerprint</c>), used to resolve references.</param>
    /// <param name="rmf2MarkupContract">The generated markup contract (<c>XCatalog.Rmf2MarkupContract</c>), used to check overlays.</param>
    /// <param name="providerFactory">Creates the catalog's provider (<c>XCatalog.CreateProvider</c>).</param>
    public TranslationCatalogRegistration(
        string catalogId,
        string defaultLocale,
        string contractFingerprint,
        string rmf2MarkupContract,
        TranslationProviderFactory providerFactory)
    {
        ArgumentNullException.ThrowIfNull(catalogId);
        ArgumentNullException.ThrowIfNull(defaultLocale);
        ArgumentNullException.ThrowIfNull(contractFingerprint);
        ArgumentNullException.ThrowIfNull(rmf2MarkupContract);
        ArgumentNullException.ThrowIfNull(providerFactory);
        if (!TranslationDataValidation.IsCatalog(catalogId))
            throw new ArgumentException("The catalog identifier is not canonical.", nameof(catalogId));
        if (!LocaleTag.TryCanonicalize(defaultLocale, out string canonical) || !string.Equals(canonical, defaultLocale, StringComparison.Ordinal))
            throw new ArgumentException("The default locale must be a canonical BCP 47 tag.", nameof(defaultLocale));
        if (!TranslationPackValidation.IsFingerprint(contractFingerprint))
            throw new ArgumentException("The contract fingerprint must be lowercase sha256 hexadecimal text.", nameof(contractFingerprint));
        CatalogId = catalogId;
        DefaultLocale = defaultLocale;
        ContractFingerprint = contractFingerprint;
        Rmf2MarkupContract = rmf2MarkupContract;
        _providerFactory = providerFactory;
    }

    /// <summary>The stable catalog ID.</summary>
    public string CatalogId { get; }

    /// <summary>The canonical default locale.</summary>
    public string DefaultLocale { get; }

    /// <summary>The generated contract fingerprint.</summary>
    public string ContractFingerprint { get; }

    /// <summary>The generated RMF2 markup contract JSON.</summary>
    public string Rmf2MarkupContract { get; }

    /// <summary>Creates a provider for the catalog, as the generated <c>CreateProvider</c> does.</summary>
    public ITranslationProvider CreateProvider(
        ITextValueFormatter? valueFormatter = null,
        ITranslationSnapshotFactory? snapshotFactory = null,
        TranslationOptions? options = null) =>
        _providerFactory(valueFormatter, snapshotFactory, options) ??
            throw new InvalidOperationException($"The provider factory of catalog '{CatalogId}' returned null.");
}
