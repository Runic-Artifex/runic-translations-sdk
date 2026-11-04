// Linked into Runic.Translations and Runic.Translations.Compiler; each assembly gets its own internal copy.
// The ESM runtime (Rmf2EsmRuntimeV5.js) and eng/cldr-locales.mjs implement the same algorithm.
using System;
using System.Collections.Generic;

#if RUNIC_TRANSLATIONS_COMPILER
namespace Runic.Translations.Compiler;
#else
namespace Runic.Translations;
#endif

/// <summary>
/// Locale identity over the pinned CLDR tables: likely subtags, the CLDR data ID of a tag, its parent
/// chains for data lookup, and the likely-subtag matching that locale negotiation uses.
/// </summary>
/// <remarks>Every method takes a canonical BCP 47 tag. Extensions and private-use subtags are ignored.</remarks>
internal static class CldrLocales
{
    internal readonly record struct Parts(string Language, string? Script, string? Region, string Variants)
    {
        public override string ToString() =>
            Language + (Script is null ? "" : "-" + Script) + (Region is null ? "" : "-" + Region) + Variants;
    }

    /// <summary>Splits a canonical tag into language, script, region and variants (each with a leading '-').</summary>
    internal static Parts Parse(string canonical)
    {
        string[] parts = canonical.Split('-');
        int index = 1;
        if (parts[0].Length <= 3)
            for (int count = 0; count < 3 && index < parts.Length && parts[index].Length == 3 && char.IsAsciiLetter(parts[index][0]); count++) index++;
        string? script = null, region = null;
        if (index < parts.Length && parts[index].Length == 4 && char.IsAsciiLetter(parts[index][0])) script = parts[index++];
        if (index < parts.Length && (parts[index].Length == 2 || (parts[index].Length == 3 && char.IsAsciiDigit(parts[index][0])))) region = parts[index++];
        string variants = "";
        while (index < parts.Length && (parts[index].Length >= 5 || (parts[index].Length == 4 && char.IsAsciiDigit(parts[index][0]))))
            variants += "-" + parts[index++];
        return new Parts(parts[0], script, region, variants);
    }

    /// <summary>The language with deprecated and legacy codes replaced (<c>iw</c> → <c>he</c>).</summary>
    internal static string Language(string language) =>
        Lookup(GeneratedLocaleTables.AliasKeys, GeneratedLocaleTables.AliasValues, language) ?? language;

    /// <summary>Adds likely subtags (UTS #35): the first entry found for language-script-region, language-region, language-script or language fills the missing script and region.</summary>
    internal static Parts Maximize(Parts tag)
    {
        tag = tag with { Language = Language(tag.Language) };
        string? value = null;
        if (tag.Script is not null && tag.Region is not null) value = Likely(tag.Language + "-" + tag.Script + "-" + tag.Region);
        if (value is null && tag.Region is not null) value = Likely(tag.Language + "-" + tag.Region);
        if (value is null && tag.Script is not null) value = Likely(tag.Language + "-" + tag.Script);
        value ??= Likely(tag.Language);
        if (value is null) return tag;
        Parts found = Parse(value);
        return tag with { Script = tag.Script ?? found.Script, Region = tag.Region ?? found.Region };
    }

    /// <summary>
    /// The CLDR data ID: the likely script is added, then omitted again when it is the language's default
    /// script, so <c>zh-TW</c> is <c>zh-Hant-TW</c>, <c>zh-Hans-SG</c> is <c>zh-SG</c> and <c>sr-Latn</c> stays.
    /// </summary>
    internal static string DataId(string canonical)
    {
        Parts tag = Parse(canonical);
        tag = tag with { Language = Language(tag.Language) };
        string? script = tag.Script ?? Maximize(tag).Script;
        string? likely = Likely(tag.Language);
        string? defaultScript = likely is null ? null : Parse(likely).Script;
        return (tag with { Script = script is not null && script == defaultScript ? null : script }).ToString();
    }

    /// <summary>
    /// The parent of a data ID, or null at the root: an explicit CLDR parent locale, else the ID without its last
    /// variant or its region. A language-script ID has the root as parent (CLDR's nonlikelyScript rule). With
    /// <paramref name="truncate"/> (CLDR's plurals component) explicit parents do not apply and the script is dropped.
    /// </summary>
    internal static string? Parent(string id, bool truncate)
    {
        if (!truncate && Lookup(GeneratedLocaleTables.ParentKeys, GeneratedLocaleTables.ParentValues, id) is { } explicitParent)
            return explicitParent == "root" ? null : explicitParent;
        Parts tag = Parse(id);
        if (tag.Variants.Length != 0) return (tag with { Variants = tag.Variants[..tag.Variants.LastIndexOf('-')] }).ToString();
        if (tag.Region is not null) return (tag with { Region = null }).ToString();
        if (tag.Script is not null && truncate) return tag.Language;
        return null;
    }

    /// <summary>The first ID in the parent chain of <paramref name="canonical"/>'s data ID that <paramref name="ids"/> contains.</summary>
    internal static string? Find(string canonical, string[] ids, bool truncate)
    {
        int depth = 0;
        for (string? id = DataId(canonical); id is not null && depth < 16; id = Parent(id, truncate), depth++)
            if (Array.BinarySearch(ids, id, StringComparer.Ordinal) >= 0) return id;
        return null;
    }

    /// <summary>
    /// The plural rule set of a locale, or -1 without CLDR plural rules. CLDR looks plural rules up by truncation
    /// (<c>pt-PT</c> has its own; <c>pt-AO</c> uses <c>pt</c>'s). A language with cardinal but no ordinal rules
    /// selects <c>other</c> (rule 0).
    /// </summary>
    internal static int PluralRule(string canonical, bool ordinal)
    {
        string? cardinal = Find(canonical, GeneratedLocaleTables.CardinalKeys, truncate: true);
        if (cardinal is null) return -1;
        if (!ordinal) return GeneratedLocaleTables.CardinalValues[Array.BinarySearch(GeneratedLocaleTables.CardinalKeys, cardinal, StringComparer.Ordinal)];
        string? id = Find(canonical, GeneratedLocaleTables.OrdinalKeys, truncate: true);
        return id is null ? 0 : GeneratedLocaleTables.OrdinalValues[Array.BinarySearch(GeneratedLocaleTables.OrdinalKeys, id, StringComparer.Ordinal)];
    }

    /// <summary>CLDR <c>minimumGroupingDigits</c> of a locale: 1 without pinned data.</summary>
    internal static int MinimumGroupingDigits(string canonical)
    {
        string? id = Find(canonical, GeneratedLocaleTables.GroupingKeys, truncate: false);
        return id is null ? 1 : GeneratedLocaleTables.GroupingValues[Array.BinarySearch(GeneratedLocaleTables.GroupingKeys, id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The best of <paramref name="available"/> (canonical tags in ordinal order) for <paramref name="desired"/>, or null.
    /// </summary>
    /// <remarks>
    /// Like ICU's <c>LocaleMatcher</c>, both sides are maximized with likely subtags and must share the language and
    /// script. Equal maximized tags match best (<c>zh-TW</c> → <c>zh-Hant</c>, <c>de</c> → <c>de-DE</c>). Then the nearest
    /// CLDR ancestor of the desired tag that a locale shares decides, the ancestor itself before its other descendants:
    /// <c>es-MX</c> → <c>es-419</c>, then <c>es-AR</c>, then <c>es</c>/<c>es-ES</c>; <c>en-AU</c> → <c>en-GB</c> (via
    /// <c>en-001</c>) before <c>en-US</c>; <c>de-CH</c> → <c>de</c>/<c>de-DE</c> before <c>de-AT</c>. A language whose
    /// CLDR parent is another bare language (<c>nb</c> and <c>nn</c> → <c>no</c>) matches it after every same-language
    /// candidate. Ties keep the order of <paramref name="available"/>. When no locale shares the script, the caller falls
    /// back to truncating subtags, as before likely subtags were used.
    /// </remarks>
    internal static string? Match(string desired, IReadOnlyList<string> available)
    {
        for (int index = 0; index < available.Count; index++)
            if (string.Equals(available[index], desired, StringComparison.Ordinal)) return desired;
        Parts wanted = Maximize(Parse(desired));
        List<string> chain = Chain(DataId(desired));
        string? best = null; int bestScore = int.MaxValue;
        for (int index = 0; index < available.Count; index++)
        {
            Parts candidate = Maximize(Parse(available[index]));
            int penalty;
            if (candidate.Language == wanted.Language) penalty = 0;
            else if (Related(wanted.Language, candidate.Language)) penalty = 1000;
            else continue;
            if (candidate.Script != wanted.Script) continue;
            int score;
            if (candidate.Region == wanted.Region) score = 0;
            else
            {
                // The nearest ancestor of the desired tag that the candidate shares; the ancestor itself (or its
                // maximized form, such as de-DE for de) before another of its descendants.
                score = -1;
                string candidateId = DataId(available[index]);
                List<string> candidateChain = Chain(candidateId);
                for (int step = 1; step < chain.Count && score < 0; step++)
                {
                    if (!candidateChain.Contains(chain[step])) continue;
                    Parts ancestor = Maximize(Parse(chain[step]));
                    bool itself = candidateId == chain[step] || (ancestor.Language == candidate.Language && ancestor.Script == candidate.Script && ancestor.Region == candidate.Region);
                    score = 20 + 2 * step + (itself ? 0 : 1);
                }
                if (score < 0)
                    score = candidate.Region == Maximize(new Parts(wanted.Language, wanted.Script, null, "")).Region ? 100 : 101;
            }
            score += penalty;
            if (score < bestScore) { best = available[index]; bestScore = score; }
        }
        return best;
    }

    /// <summary>The parent chain of a data ID, the ID first.</summary>
    internal static List<string> Chain(string id)
    {
        var result = new List<string>();
        for (string? current = id; current is not null && result.Count < 16; current = Parent(current, truncate: false)) result.Add(current);
        return result;
    }

    // nb and nn have no as their CLDR parent; such languages match each other after same-language candidates.
    private static bool Related(string first, string second) =>
        Parent(first, truncate: false) == second || Parent(second, truncate: false) == first;

    private static string? Likely(string key) => Lookup(GeneratedLocaleTables.LikelyKeys, GeneratedLocaleTables.LikelyValues, key);

    private static string? Lookup(string[] keys, string[] values, string key)
    {
        int index = Array.BinarySearch(keys, key, StringComparer.Ordinal);
        return index >= 0 ? values[index] : null;
    }
}
