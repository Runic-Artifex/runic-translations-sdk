using System;
using System.Collections.Generic;

namespace Runic.Translations;

/// <summary>How a catalog resolved the requested locale to the locale of its snapshot.</summary>
/// <remarks>
/// Under <see cref="UnsupportedLocalePolicy.ParentsThenDefault"/> an undeclared locale is matched with the pinned CLDR
/// likely subtags and parent locales, as <see cref="LocaleNegotiation"/> does: the language and likely script must agree,
/// so <c>sr-Latn-RS</c> never resolves to a Cyrillic <c>sr</c>. The set is closed: no value will be added in a minor release.
/// </remarks>
public enum LocaleResolution
{
    /// <summary>The catalog declares the requested locale (or an overlay adds it).</summary>
    Exact = 0,
    /// <summary>
    /// The catalog lacks the requested locale and uses one of its parents, by CLDR parent locales or by removing subtags:
    /// <c>fr</c> for <c>fr-CA</c>, <c>es-419</c> for <c>es-MX</c>, <c>zh-Hant</c> for <c>zh-TW</c>, <c>no</c> for <c>nb</c>.
    /// </summary>
    Parent = 1,
    /// <summary>The catalog lacks the requested locale and its parents and uses its default locale.</summary>
    Default = 2,
    /// <summary>
    /// The catalog lacks the requested locale and its parents and uses another locale that CLDR likely subtags match
    /// to it: a sibling region (<c>de-DE</c> for <c>de-CH</c>, <c>en-GB</c> for <c>en-AU</c>), a more specific locale
    /// (<c>de-DE</c> for <c>de</c>), a closely related language (<c>nb</c> for <c>nn</c>) or the same locale under
    /// another code or spelling (<c>he</c> for <c>iw</c>, <c>zh-TW</c> for <c>zh-Hant-TW</c>).
    /// </summary>
    Related = 3,
}

/// <summary>A requested locale, the declared locale it resolved to and how.</summary>
internal readonly record struct LocaleRequest(string Requested, string Locale, LocaleResolution Resolution);

internal static class LocaleRequestResolver
{
    /// <summary>Resolves a requested tag against the available locales under the unsupported-locale policy.</summary>
    /// <param name="requestedLocale">The requested tag.</param>
    /// <param name="available">The declared (and overlay-added) canonical locales in ordinal order.</param>
    /// <param name="policy">The catalog's unsupported-locale policy.</param>
    /// <param name="defaultLocale">The catalog's default locale.</param>
    /// <param name="catalog">The catalog ID, for the error message.</param>
    /// <exception cref="ArgumentException">The tag is not a structurally valid BCP 47 tag.</exception>
    /// <exception cref="TranslationNotFoundException">The policy is <see cref="UnsupportedLocalePolicy.Exact"/> and the locale is not declared.</exception>
    internal static LocaleRequest Resolve(string requestedLocale, string[] available, UnsupportedLocalePolicy policy,
        string defaultLocale, string catalog)
    {
        ArgumentNullException.ThrowIfNull(requestedLocale);
        string canonical = Canonicalize(requestedLocale, nameof(requestedLocale));
        if (Array.BinarySearch(available, canonical, StringComparer.Ordinal) >= 0)
            return new LocaleRequest(canonical, canonical, LocaleResolution.Exact);

        switch (policy)
        {
            case UnsupportedLocalePolicy.Default:
                return new LocaleRequest(canonical, defaultLocale, LocaleResolution.Default);
            case UnsupportedLocalePolicy.ParentsThenDefault:
                // CLDR likely-subtag matching (zh-TW to zh-Hant, de-CH to de-DE), else the nearest truncation with the same
                // likely script, exactly as LocaleNegotiation and the ESM runtime's negotiateLocale choose.
                return canonical.Length != 0 && LocaleNegotiation.Match(canonical, available) is { } match
                    ? new LocaleRequest(canonical, match, Classify(canonical, match))
                    : new LocaleRequest(canonical, defaultLocale, LocaleResolution.Default);
            case UnsupportedLocalePolicy.Exact:
            default:
                throw new TranslationNotFoundException($"Locale '{canonical}' is not declared by catalog '{catalog}'.");
        }
    }

    /// <summary>
    /// <see cref="LocaleResolution.Parent"/> when <paramref name="match"/> is an ancestor of <paramref name="canonical"/>
    /// (by removing subtags or along the CLDR parent chain of its data ID), else <see cref="LocaleResolution.Related"/>.
    /// </summary>
    internal static LocaleResolution Classify(string canonical, string match)
    {
        if (canonical.Length > match.Length && canonical[match.Length] == '-' && canonical.StartsWith(match, StringComparison.Ordinal))
            return LocaleResolution.Parent;
        return CldrLocales.Chain(CldrLocales.DataId(canonical)).IndexOf(CldrLocales.DataId(match), 1) > 0
            ? LocaleResolution.Parent
            : LocaleResolution.Related;
    }

    /// <summary>The nearest parent of a canonical tag among <paramref name="candidates"/>; see <see cref="CldrLocales.NearestParent"/>.</summary>
    internal static string? NearestParent(string canonicalLocale, string[] candidates) => CldrLocales.NearestParent(canonicalLocale, candidates);

    /// <summary>Canonicalizes a requested tag; an empty tag, the name of the invariant culture, stays empty.</summary>
    internal static string Canonicalize(string requestedLocale, string parameterName)
    {
        // An empty tag, the name of the invariant culture (for example under LANG=C), names no locale.
        // It is an unsupported locale without subtags, so the policy chooses the default locale or rejects it.
        if (requestedLocale.Length == 0) return string.Empty;
        if (!LocaleTag.TryCanonicalize(requestedLocale, out string canonical))
            throw new ArgumentException("The requested locale is not a structurally valid BCP 47 tag.", parameterName);
        return canonical;
    }
}
