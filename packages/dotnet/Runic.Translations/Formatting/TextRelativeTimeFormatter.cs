using System;
using System.Globalization;

namespace Runic.Translations;

/// <summary>The closed relative-time registry shared by generated schema version 2 messages.</summary>
public static class TextRelativeTimeFormatter
{
    /// <summary>Formats a relative duration for a compiler-supported locale family.</summary>
    public static string Format(decimal value, string unit, string numeric, string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(numeric);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        if (unit is not ("second" or "minute" or "hour" or "day" or "week" or "month" or "year"))
            throw new ArgumentOutOfRangeException(nameof(unit));
        if (numeric is not ("always" or "auto")) throw new ArgumentOutOfRangeException(nameof(numeric));
        string language = locale.Split('-')[0].ToLowerInvariant();
        GeneratedRelativeTimeLocale data = GeneratedLocaleData.FindRelativeTime(language) ?? throw Unsupported(locale);
        if (numeric == "auto" && unit == "day" && value is >= -1 and <= 1 && decimal.Truncate(value) == value)
        {
            return data.AutoDay[(int)value + 1];
        }
        decimal absolute = Math.Abs(value);
        string number = absolute.ToString(CultureInfo.InvariantCulture);
        bool one = TextMessageSelector.SelectPlural(absolute, locale, false) == "one";
        GeneratedRelativeTimeUnit? unitData = null;
        for (int index = 0; index < data.Units.Length; index++)
        {
            if (data.Units[index].Name == unit) { unitData = data.Units[index]; break; }
        }
        if (unitData is null) throw new ArgumentOutOfRangeException(nameof(unit));
        string noun = one ? unitData.One : unitData.Other;
        return (value < 0 ? data.Past : data.Future)
            .Replace("{0}", number, StringComparison.Ordinal)
            .Replace("{unit}", noun, StringComparison.Ordinal);
    }

    private static TranslationFormatException Unsupported(string locale) =>
        new("Relative-time formatting is not supported for locale '" + locale + "'.");
}
