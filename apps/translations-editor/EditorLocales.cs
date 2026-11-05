using Runic.Translations.Compiler;

namespace Runic.Translations.Editor;

/// <summary>
/// The locales the add-language picker offers and what a typed tag means, from the SDK's pinned CLDR tables: the same
/// data the compiler checks plural categories with and the runtime formats numbers and dates with.
/// </summary>
internal static class EditorLocales
{
    // At most this many requested tags are listed per locale; the count says how many match in all.
    private const int MaximumMatches = 12;

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
    /// <remarks>
    /// For a region tag whose CLDR data is in a script the tag does not name (<c>zh-TW</c>, <c>zh-HK</c>: Traditional;
    /// <c>sr-ME</c>: Latin), the script-level locale (<c>zh-Hant</c>, <c>sr-Latn</c>) is suggested: the CLDR parent
    /// that runtime negotiation resolves every region writing that script to. With the catalog's
    /// <paramref name="context"/>, the description lists the requested tags that would reach the typed and the
    /// suggested locale under the catalog's <c>unsupportedLocale</c> policy, by the runtime's own resolution: under
    /// <c>exact</c> only the tag itself, so a platform's <c>zh-TW</c> request needs <c>zh-TW</c>.
    /// </remarks>
    internal static EditorLocaleDescription Describe(string? tag, EditorLocaleContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(tag) || !TranslationCompiler.TryCanonicalizeLocale(tag.Trim(), out string canonical))
            return new EditorLocaleDescription(false, null, null, null, "ltr", [], [], EditorNotice.Create("ui_backend_locale_invalid", ("locale", tag?.Trim() ?? "")));
        string dataId = CldrLocales.DataId(canonical);
        // The nearest CLDR locale with number data; a language CLDR does not know has none (root only).
        string? cldr = CldrLocales.Find(canonical, GeneratedLocaleTables.NumberIds, truncate: false) is { } found && found != "root" ? found : null;
        string? suggested = ScriptLocale(canonical, dataId, context);
        return new EditorLocaleDescription(true, canonical, dataId, cldr,
            LocaleDirection.ToHtml(LocaleDirection.Get(canonical)),
            TranslationCapabilityRegistry.PluralCategories(canonical, ordinal: false),
            TranslationCapabilityRegistry.PluralCategories(canonical, ordinal: true),
            cldr is null ? EditorNotice.Create("ui_backend_locale_unknown", ("locale", canonical)) : null)
        {
            Suggested = suggested,
            UnsupportedLocale = context is null ? null : PolicyName(context.Policy),
            Matches = context is null ? null : Matches(canonical, context),
            SuggestedMatches = context is null || suggested is null ? null : Matches(suggested, context),
        };
    }

    // The script-level CLDR locale of a region tag without a script whose data is in another script: zh-TW → zh-Hant.
    // None when the catalog already declares it (a regional addition next to it is deliberate).
    private static string? ScriptLocale(string canonical, string dataId, EditorLocaleContext? context)
    {
        CldrLocales.Parts typed = CldrLocales.Parse(canonical), data = CldrLocales.Parse(dataId);
        if (typed.Script is not null || typed.Region is null || data.Script is null) return null;
        string script = data.Language + "-" + data.Script;
        if (Array.BinarySearch(GeneratedLocaleTables.NumberIds, script, StringComparer.Ordinal) < 0) return null;
        return context?.Locales.Contains(script, StringComparer.OrdinalIgnoreCase) == true ? null : script;
    }

    // Every tag the pinned CLDR tables name: the locales with data, the likely-subtag keys and values (zh-HK,
    // zh-Hant-HK) and the explicit parent locales.
    private static readonly Lazy<string[]> s_knownTags = new(() =>
        GeneratedLocaleTables.NumberIds.Concat(GeneratedLocaleTables.LikelyKeys).Concat(GeneratedLocaleTables.LikelyValues)
            .Concat(GeneratedLocaleTables.ParentKeys).Concat(GeneratedLocaleTables.ParentValues)
            .Where(static tag => tag != "root" && !tag.StartsWith("und", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToArray());

    // The requested tags of the locale's language (the CLDR locales, and the language-region tags platforms send) that
    // the runtime resolves to it once the catalog declares it, under the catalog's unsupportedLocale policy.
    private static EditorLocaleMatches Matches(string locale, EditorLocaleContext context)
    {
        string[] available = context.Locales.Append(locale).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string language = CldrLocales.Maximize(CldrLocales.Parse(locale)).Language;
        var candidates = new SortedSet<string>(StringComparer.Ordinal) { locale };
        void Add(string value) { if (TranslationCompiler.TryCanonicalizeLocale(value, out string candidate)) candidates.Add(candidate); }
        foreach (string id in s_knownTags.Value.Append(locale))
        {
            CldrLocales.Parts parts = CldrLocales.Parse(id);
            if (CldrLocales.Maximize(parts).Language != language) continue;
            Add(id);
            CldrLocales.Parts maximized = CldrLocales.Maximize(parts);
            if (maximized.Region is not null) Add(maximized.Language + "-" + maximized.Region);
        }
        var matches = new List<string>();
        foreach (string candidate in candidates)
        {
            try
            {
                LocaleRequest request = LocaleRequestResolver.Resolve(candidate, available, context.Policy, context.DefaultLocale, context.Catalog);
                if (request.Locale == locale && request.Resolution != LocaleResolution.Default) matches.Add(candidate);
            }
            catch (TranslationNotFoundException) { /* exact: an undeclared tag is not served */ }
        }
        // The tag itself, the tags that maximize alike (zh-TW for zh-Hant), then the tags of regions with CLDR data of
        // their own (zh-HK, zh-MO), the tags platforms send (no script) before the CLDR IDs, then the rest.
        CldrLocales.Parts maximizedLocale = CldrLocales.Maximize(CldrLocales.Parse(locale));
        int Rank(string match)
        {
            if (match == locale) return 0;
            CldrLocales.Parts parts = CldrLocales.Parse(match);
            if (CldrLocales.Maximize(parts) == maximizedLocale) return 1;
            string data = CldrLocales.DataId(match);
            bool own = Array.BinarySearch(GeneratedLocaleTables.NumberIds, data, StringComparer.Ordinal) >= 0 ||
                Array.BinarySearch(GeneratedLocaleTables.ParentKeys, data, StringComparer.Ordinal) >= 0;
            return (own ? 2 : 4) + (parts.Script is null ? 0 : 1);
        }
        string[] ordered = matches.OrderBy(Rank).ThenBy(static match => match.Length).ThenBy(static match => match, StringComparer.Ordinal).ToArray();
        return new EditorLocaleMatches(ordered.Take(MaximumMatches).ToArray(), ordered.Length);
    }

    private static string PolicyName(UnsupportedLocalePolicy policy) => policy switch
    {
        UnsupportedLocalePolicy.Exact => "exact",
        UnsupportedLocalePolicy.Default => "default",
        _ => "parentsThenDefault",
    };
}

/// <summary>The catalog a typed locale would join: its declared locales (canonical), default locale and policy.</summary>
internal sealed record EditorLocaleContext(IReadOnlyList<string> Locales, string DefaultLocale, UnsupportedLocalePolicy Policy, string Catalog);
