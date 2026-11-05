using System;
using System.ComponentModel;

namespace Runic.Translations;

/// <summary>The closed relative-time registry shared by generated schema version 2 messages.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class TextRelativeTimeFormatter
{
    private static readonly string[] Units = ["second", "minute", "hour", "day", "week", "month", "year"];
    private static readonly string[] Categories = ["zero", "one", "two", "few", "many", "other"];

    /// <summary>Formats a relative duration with the pinned CLDR patterns of a locale.</summary>
    /// <remarks>
    /// The number is formatted as <c>:number</c> formats it by default (the locale's pinned digits, separators and grouping,
    /// at most six fraction digits), and the pattern is chosen by the plural category of that displayed number.
    /// <c>numeric=auto</c> uses CLDR's relative-type text ("yesterday", "now", "next month") for whole values from
    /// -2 to 2 where the locale has one.
    /// </remarks>
    public static string Format(decimal value, string unit, string numeric, string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(numeric);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        int unitIndex = Array.IndexOf(Units, unit);
        if (unitIndex < 0) throw new ArgumentOutOfRangeException(nameof(unit));
        if (numeric is not ("always" or "auto")) throw new ArgumentOutOfRangeException(nameof(numeric));
        string?[] data = GeneratedLocaleData.FindRelativeTime(locale) ?? throw Unsupported(locale);
        int offset = unitIndex * 17;
        if (numeric == "auto" && value is >= -2 and <= 2 && decimal.Truncate(value) == value &&
            data[offset + 14 + (int)value] is { } relative)
        {
            return relative;
        }

        decimal absolute = Math.Abs(value);
        VisibleDecimal visible = VisibleDecimal.FromNumber(absolute, false, 0, 6);
        string category = TextMessageSelector.Select(visible, locale, false);
        int patterns = offset + (value < 0 ? 0 : 6);
        string pattern = data[patterns + Array.IndexOf(Categories, category)] ?? data[patterns + 5]!;
        // As :number with its default options: grouping auto, zero to six fraction digits, halfExpand.
        return pattern.Replace("{0}", Rmf2NumberFormats.Decimal(visible, "auto", locale), StringComparison.Ordinal);
    }

    private static TranslationFormatException Unsupported(string locale) =>
        new("Relative-time formatting is not supported for locale '" + locale + "'.");
}
