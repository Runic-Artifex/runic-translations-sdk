using System;
using System.Collections.Generic;
using System.Text;

namespace Runic.Translations;

/// <summary>
/// Joins a list with the pinned CLDR list patterns, as <c>Intl.ListFormat</c> does.
/// The ESM runtime applies the same generated patterns and the same Spanish rule.
/// </summary>
internal static class TextListFormatter
{
    // isolateItems wraps each item in FSI…PDI (bidi isolation); the Spanish and Hebrew rules still look at the raw item.
    internal static string Format(IReadOnlyList<string> items, string type, string style, string locale, bool isolateItems = false)
    {
        int typeIndex = type switch { "conjunction" => 0, "disjunction" => 1, "unit" => 2, _ => throw new TranslationFormatException("Invalid list type '" + type + "'.") };
        int styleIndex = style switch { "long" => 0, "short" => 1, "narrow" => 2, _ => throw new TranslationFormatException("Invalid list style '" + style + "'.") };
        string?[] data = GeneratedLocaleData.FindList(locale)
            ?? throw new TranslationFormatException("List formatting is not supported for locale '" + locale + "'.");
        int offset = (typeIndex * 3 + styleIndex) * 4;
        string start = data[offset]!, middle = data[offset + 1]!, end = data[offset + 2]!, two = data[offset + 3]!;
        string language = GeneratedLocaleData.Canonical(locale) is { } canonical ? CldrLocales.Parse(canonical).Language : "";
        switch (items.Count)
        {
            case 0: return string.Empty;
            case 1: return Item(0);
            case 2: return Apply(Contextual(language, two, end, two, items[1]), Item(0), Item(1));
        }
        // CLDR composes from the end: end(n-2, n-1), then middle(i, rest) and start(0, rest).
        string last = items[items.Count - 1];
        string text = Apply(Contextual(language, two, end, end, last), Item(items.Count - 2), Item(items.Count - 1));
        for (int index = items.Count - 3; index >= 1; index--) text = Apply(middle, Item(index), text);
        return Apply(start, Item(0), text);

        string Item(int index) => isolateItems ? Rmf2Bidi.IsolateItem(items[index]) : items[index];
    }

    // Every pinned pattern has {0} before {1} (eng/generate-cldr.mjs checks this).
    private static string Apply(string pattern, string first, string second)
    {
        int zero = pattern.IndexOf("{0}", StringComparison.Ordinal), one = pattern.IndexOf("{1}", StringComparison.Ordinal);
        return new StringBuilder(pattern.Length + first.Length + second.Length)
            .Append(pattern, 0, zero).Append(first).Append(pattern, zero + 3, one - zero - 3).Append(second).Append(pattern, one + 3, pattern.Length - one - 3)
            .ToString();
    }

    // ICU, and therefore Intl.ListFormat, changes Spanish "y" to "e" before an /i/
    // sound and "o" to "u" before an /o/ sound. The rule applies to the two-item and
    // end patterns of a pattern set that uses "{0} y {1}" (or else "{0} o {1}"), and
    // tests the last item only. Hebrew "ו" takes a hyphen ("ו-") before an item that
    // does not start with a Hebrew character.
    private static string Contextual(string language, string two, string end, string pattern, string last)
    {
        if (language is "he" or "iw")
            return pattern == "{0} \u05D5{1}" && last.Length > 0 && !IsHebrew(last[0]) ? "{0} \u05D5-{1}" : pattern;
        if (language != "es") return pattern;
        if (two == "{0} y {1}" || end == "{0} y {1}") return pattern == "{0} y {1}" && BeforeI(last) ? "{0} e {1}" : pattern;
        if (two == "{0} o {1}" || end == "{0} o {1}") return pattern == "{0} o {1}" && BeforeO(last) ? "{0} u {1}" : pattern;
        return pattern;
    }

    // The Hebrew block's letters and points, and the Hebrew presentation forms.
    private static bool IsHebrew(char value) => value is (>= '\u0591' and <= '\u05F4') or (>= '\uFB1D' and <= '\uFB4F');

    private static bool BeforeI(string text) =>
        text.Length > 0 && (text[0] is 'i' or 'I' ||
            (text[0] is 'h' or 'H' && text.Length > 1 && text[1] is 'i' or 'I' && (text.Length == 2 || text[2] is not ('a' or 'A' or 'e' or 'E'))));

    private static bool BeforeO(string text) =>
        text.Length > 0 && (text[0] is 'o' or 'O' or '8' ||
            (text[0] is 'h' or 'H' && text.Length > 1 && text[1] is 'o' or 'O') ||
            (text.Length >= 2 && text[0] == '1' && text[1] == '1' && (text.Length == 2 || text[2] == ' ')));
}
