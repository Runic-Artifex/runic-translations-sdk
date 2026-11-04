using System;
using System.Collections.Generic;

namespace Runic.Translations;

/// <summary>
/// Chooses a locale from an ordered preference list with the pinned CLDR likely subtags and parent locales,
/// exactly as the generated ESM runtime's <c>negotiateLocale</c> does.
/// </summary>
/// <remarks>
/// <para>
/// A preference matches a locale with the same tag first. Otherwise both are maximized with CLDR likely subtags
/// and must share the language and script, as ICU's <c>LocaleMatcher</c> requires: equal maximized tags match best
/// (<c>zh-TW</c> → <c>zh-Hant</c>, <c>de</c> → <c>de-DE</c>), the one nearest in the preference's CLDR parent chain first
/// (<c>en-US-posix</c> → <c>en-US</c> before <c>en</c>). Then the nearest CLDR parent of the preference that a
/// locale shares decides, the parent itself before its other descendants: <c>es-MX</c> → <c>es-419</c>, then
/// <c>es-AR</c>, then <c>es</c>; <c>en-AU</c> → <c>en-GB</c> (both under <c>en-001</c>) before <c>en-US</c>;
/// <c>de-CH</c> → <c>de-DE</c> (the likely region of <c>de</c>) before <c>de-AT</c>. <c>nb</c> and
/// <c>nn</c> match <c>no</c> (and <c>no</c> matches <c>nb</c>) after every same-language locale, and <c>nb</c> and
/// <c>nn</c> match each other after that. Deprecated and legacy codes match their replacements: <c>iw</c> → <c>he</c>,
/// <c>tl</c> → <c>fil</c>, <c>mo</c> → <c>ro</c>, <c>sh</c> → <c>sr-Latn</c>. Otherwise the nearest locale left by
/// removing subtags matches, but only with the same likely script: <c>tlh-x-a</c> → <c>tlh</c>, while
/// <c>sr-Latn-RS</c> does not match a Cyrillic <c>sr</c> and <c>zh-Hant</c> does not match <c>zh</c>.
/// </para>
/// <para>
/// The first preference that matches decides, so a script mismatch reaches the next preference:
/// <c>[sr-Latn-RS, en]</c> chooses <c>en</c> from <c>en</c> and <c>sr</c>. Ties keep the ordinal order of the locales.
/// Preferences that are not valid tags are skipped, and so are those of the undetermined language (<c>und</c>,
/// <c>und-DE</c>) unless a locale is exactly that tag. POSIX forms are accepted: <c>de_DE</c>, <c>de_DE.UTF-8</c> and
/// <c>de_DE@euro</c> name <c>de-DE</c>.
/// </para>
/// </remarks>
public static class LocaleNegotiation
{
    /// <summary>Returns the best of <paramref name="availableLocales"/> for the first matching preference, else <paramref name="defaultLocale"/>.</summary>
    /// <param name="preferredLocales">The preferences, most preferred first, such as the operating system's user languages.</param>
    /// <param name="availableLocales">The locales to choose from, such as a generated catalog's <c>SupportedLocales</c>.</param>
    /// <param name="defaultLocale">The locale returned when no preference matches.</param>
    /// <returns>A canonical tag from <paramref name="availableLocales"/>, or the canonical <paramref name="defaultLocale"/>.</returns>
    public static string Negotiate(IEnumerable<string> preferredLocales, IEnumerable<string> availableLocales, string defaultLocale)
    {
        ArgumentNullException.ThrowIfNull(preferredLocales);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultLocale);
        if (!LocaleTag.TryCanonicalize(defaultLocale, out string canonicalDefault))
            throw new ArgumentException("The default locale is not a valid BCP 47 tag.", nameof(defaultLocale));
        string[] available = CanonicalSet(availableLocales);
        return Negotiate(preferredLocales, available) ?? canonicalDefault;
    }

    /// <summary>Returns the best of <paramref name="availableLocales"/> for one requested locale, or null when none matches.</summary>
    /// <param name="requestedLocale">The requested tag; POSIX forms are accepted.</param>
    /// <param name="availableLocales">The locales to choose from.</param>
    public static string? Match(string requestedLocale, IEnumerable<string> availableLocales)
    {
        ArgumentNullException.ThrowIfNull(requestedLocale);
        string[] available = CanonicalSet(availableLocales);
        return Canonical(requestedLocale) is { } canonical ? Match(canonical, available) : null;
    }

    internal static string? Negotiate(IEnumerable<string> preferredLocales, string[] available)
    {
        foreach (string? preference in preferredLocales)
        {
            if (preference is null || Canonical(preference) is not { } canonical) continue;
            if (Match(canonical, available) is { } match) return match;
        }

        return null;
    }

    /// <summary>The best locale for a canonical tag: exact, CLDR likely-subtag matching, then truncation within the same script.</summary>
    internal static string? Match(string canonical, string[] available) =>
        CldrLocales.Match(canonical, available) ?? CldrLocales.Truncate(canonical, available);

    // A POSIX locale name may carry a code set and a modifier: de_DE.UTF-8@euro.
    private static string? Canonical(string preference)
    {
        int end = preference.IndexOfAny(['.', '@']);
        return LocaleTag.TryCanonicalize(end < 0 ? preference : preference[..end], out string canonical) ? canonical : null;
    }

    private static string[] CanonicalSet(IEnumerable<string> availableLocales)
    {
        ArgumentNullException.ThrowIfNull(availableLocales);
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string locale in availableLocales)
        {
            if (locale is null || !LocaleTag.TryCanonicalize(locale, out string canonical))
                throw new ArgumentException("Every available locale must be a valid BCP 47 tag.", nameof(availableLocales));
            set.Add(canonical);
        }

        string[] result = new string[set.Count];
        set.CopyTo(result);
        return result;
    }
}
