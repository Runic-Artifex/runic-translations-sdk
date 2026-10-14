using System.Collections.Generic;
using System.Text;

namespace Runic.Translations;

/// <summary>
/// Joins a list with the pinned CLDR list patterns, as <c>Intl.ListFormat</c> does.
/// The ESM runtime applies the same generated separators and the same Spanish rule.
/// </summary>
internal static class TextListFormatter
{
    internal static string Format(IReadOnlyList<string> items, string type, string style, string locale)
    {
        int typeIndex = type switch { "conjunction" => 0, "disjunction" => 1, "unit" => 2, _ => throw new TranslationFormatException("Invalid list type '" + type + "'.") };
        int styleIndex = style switch { "long" => 0, "short" => 1, "narrow" => 2, _ => throw new TranslationFormatException("Invalid list style '" + style + "'.") };
        string language = GeneratedLocaleData.Language(locale);
        GeneratedListLocale data = GeneratedLocaleData.FindList(language)
            ?? throw new TranslationFormatException("List formatting is not supported for locale '" + locale + "'.");
        int offset = (typeIndex * 3 + styleIndex) * 4;
        string start = data.Separators[offset], middle = data.Separators[offset + 1], end = data.Separators[offset + 2], two = data.Separators[offset + 3];
        switch (items.Count)
        {
            case 0: return string.Empty;
            case 1: return items[0];
            case 2: return items[0] + Contextual(language, two, end, two, items[1]) + items[1];
        }
        var builder = new StringBuilder(items[0]).Append(start).Append(items[1]);
        for (int index = 2; index < items.Count - 1; index++) builder.Append(middle).Append(items[index]);
        string last = items[items.Count - 1];
        return builder.Append(Contextual(language, two, end, end, last)).Append(last).ToString();
    }

    // ICU, and therefore Intl.ListFormat, changes Spanish "y" to "e" before an /i/
    // sound and "o" to "u" before an /o/ sound. The rule applies to the two-item and
    // end separators of a pattern set that uses " y " (or else " o "), and tests the
    // last item only.
    private static string Contextual(string language, string two, string end, string separator, string last)
    {
        if (language != "es") return separator;
        if (two == " y " || end == " y ") return separator == " y " && BeforeI(last) ? " e " : separator;
        if (two == " o " || end == " o ") return separator == " o " && BeforeO(last) ? " u " : separator;
        return separator;
    }

    private static bool BeforeI(string text) =>
        text.Length > 0 && (text[0] is 'i' or 'I' ||
            (text[0] is 'h' or 'H' && text.Length > 1 && text[1] is 'i' or 'I' && (text.Length == 2 || text[2] is not ('a' or 'A' or 'e' or 'E'))));

    private static bool BeforeO(string text) =>
        text.Length > 0 && (text[0] is 'o' or 'O' or '8' ||
            (text[0] is 'h' or 'H' && text.Length > 1 && text[1] is 'o' or 'O') ||
            (text.Length >= 2 && text[0] == '1' && text[1] == '1' && (text.Length == 2 || text[2] == ' ')));
}
