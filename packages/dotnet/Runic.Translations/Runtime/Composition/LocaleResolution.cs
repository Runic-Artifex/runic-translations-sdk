using System;

namespace Runic.Translations;

/// <summary>How a catalog resolved the requested locale to the locale of its snapshot.</summary>
/// <remarks>The set is closed: no value will be added in a minor release.</remarks>
public enum LocaleResolution
{
    /// <summary>The catalog declares the requested locale (or an overlay adds it).</summary>
    Exact = 0,
    /// <summary>The catalog lacks the requested locale and uses a parent, such as <c>fr</c> for <c>fr-CA</c>.</summary>
    Parent = 1,
    /// <summary>The catalog lacks the requested locale and its parents and uses its default locale.</summary>
    Default = 2,
}

/// <summary>A requested locale, the declared locale it resolved to and how.</summary>
internal readonly record struct LocaleRequest(string Requested, string Locale, LocaleResolution Resolution);

internal static class LocaleRequestResolver
{
    /// <summary>Resolves a requested tag against the declared locales under the unsupported-locale policy.</summary>
    /// <exception cref="ArgumentException">The tag is not a structurally valid BCP 47 tag.</exception>
    /// <exception cref="TranslationNotFoundException">The policy is <see cref="UnsupportedLocalePolicy.Exact"/> and the locale is not declared.</exception>
    internal static LocaleRequest Resolve(string requestedLocale, Predicate<string> declares, UnsupportedLocalePolicy policy,
        string defaultLocale, string catalog)
    {
        ArgumentNullException.ThrowIfNull(requestedLocale);
        string canonical = Canonicalize(requestedLocale, nameof(requestedLocale));
        if (declares(canonical)) return new LocaleRequest(canonical, canonical, LocaleResolution.Exact);

        switch (policy)
        {
            case UnsupportedLocalePolicy.Default:
                return new LocaleRequest(canonical, defaultLocale, LocaleResolution.Default);
            case UnsupportedLocalePolicy.ParentsThenDefault:
                string candidate = canonical;
                int separator = candidate.LastIndexOf('-');
                while (separator > 0)
                {
                    candidate = candidate[..separator];
                    if (declares(candidate)) return new LocaleRequest(canonical, candidate, LocaleResolution.Parent);
                    separator = candidate.LastIndexOf('-');
                }

                return new LocaleRequest(canonical, defaultLocale, LocaleResolution.Default);
            case UnsupportedLocalePolicy.Exact:
            default:
                throw new TranslationNotFoundException($"Locale '{canonical}' is not declared by catalog '{catalog}'.");
        }
    }

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
