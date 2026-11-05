using Runic.Translations.Compiler;

namespace Runic.Translations.Editor;

/// <summary>
/// The locales the add-language picker offers and what a typed tag means, from the SDK's pinned CLDR tables: the same
/// data the compiler checks plural categories with and the runtime formats numbers and dates with.
/// </summary>
internal static class EditorLocales
{
    private static readonly Lazy<EditorLocaleCatalog> s_catalog = new(() => new EditorLocaleCatalog(
        TranslationCapabilityRegistry.CldrVersion,
        // Every locale with CLDR number data, the widest pinned table; root is not a locale to add.
        GeneratedLocaleTables.NumberIds.Where(static id => id != "root").Order(StringComparer.Ordinal).ToArray()));

    /// <summary>The CLDR locales, by data ID (<c>zh-Hant</c>, not <c>zh-TW</c>), in ordinal order.</summary>
    internal static EditorLocaleCatalog Catalog => s_catalog.Value;

    /// <summary>
    /// A typed tag: its canonical BCP 47 form; the CLDR locale its data comes from, through likely subtags
    /// (<c>zh-TW</c> → <c>zh-Hant-TW</c>, <c>iw</c> → <c>he</c>); its text direction (the runtime's
    /// <see cref="LocaleDirection"/>); and the plural and ordinal categories a translation in it needs.
    /// </summary>
    internal static EditorLocaleDescription Describe(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || !TranslationCompiler.TryCanonicalizeLocale(tag.Trim(), out string canonical))
            return new EditorLocaleDescription(false, null, null, null, "ltr", [], [], EditorNotice.Create("ui_backend_locale_invalid", ("locale", tag?.Trim() ?? "")));
        string dataId = CldrLocales.DataId(canonical);
        // The nearest CLDR locale with number data; a language CLDR does not know has none (root only).
        string? cldr = CldrLocales.Find(canonical, GeneratedLocaleTables.NumberIds, truncate: false) is { } found && found != "root" ? found : null;
        return new EditorLocaleDescription(true, canonical, dataId, cldr,
            LocaleDirection.ToHtml(LocaleDirection.Get(canonical)),
            TranslationCapabilityRegistry.PluralCategories(canonical, ordinal: false),
            TranslationCapabilityRegistry.PluralCategories(canonical, ordinal: true),
            cldr is null ? EditorNotice.Create("ui_backend_locale_unknown", ("locale", canonical)) : null);
    }
}
