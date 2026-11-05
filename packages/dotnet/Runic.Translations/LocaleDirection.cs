using System;

namespace Runic.Translations;

/// <summary>The writing direction of a locale.</summary>
public enum TextDirection
{
    /// <summary>Left to right, such as English or German (HTML <c>dir="ltr"</c>).</summary>
    LeftToRight = 0,
    /// <summary>Right to left, such as Arabic or Hebrew (HTML <c>dir="rtl"</c>).</summary>
    RightToLeft = 1,
}

/// <summary>
/// The writing direction of a locale from pinned CLDR data, the same in .NET and in the ESM runtime's
/// <c>localeDirection</c>, and independent of the operating system's culture data.
/// </summary>
/// <remarks>
/// A locale is right to left when its explicit script subtag, else its likely script (CLDR likelySubtags), is one of
/// CLDR's right-to-left scripts: <c>ar</c>, <c>he</c>, <c>fa</c>, <c>pa-PK</c> and <c>ku-Arab</c> are; <c>ku</c>,
/// <c>he-Latn</c>, <c>sd-Deva</c> are not. A language without likely-subtag data is left to right. Extensions and
/// private-use subtags are ignored.
/// </remarks>
public static class LocaleDirection
{
    /// <summary>Gets the writing direction of <paramref name="locale"/>.</summary>
    /// <param name="locale">A BCP 47 tag, such as <c>ar-EG</c>; <c>_</c> separators are accepted. The invariant culture's empty name is left to right.</param>
    /// <exception cref="ArgumentException">The tag is not a structurally valid BCP 47 tag.</exception>
    public static TextDirection Get(string locale) => IsRightToLeft(locale) ? TextDirection.RightToLeft : TextDirection.LeftToRight;

    /// <summary>Whether <paramref name="locale"/> is written right to left.</summary>
    /// <inheritdoc cref="Get" path="/param"/>
    /// <inheritdoc cref="Get" path="/exception"/>
    public static bool IsRightToLeft(string locale)
    {
        ArgumentNullException.ThrowIfNull(locale);
        if (locale.Length == 0) return false;
        if (!LocaleTag.TryCanonicalize(locale.Replace('_', '-'), out string canonical))
            throw new ArgumentException("The locale is not a structurally valid BCP 47 tag.", nameof(locale));
        return CldrLocales.IsRightToLeft(canonical);
    }

    /// <summary>The HTML <c>dir</c> value of <paramref name="direction"/>: <c>ltr</c> or <c>rtl</c>.</summary>
    public static string ToHtml(TextDirection direction) => direction == TextDirection.RightToLeft ? "rtl" : "ltr";
}
