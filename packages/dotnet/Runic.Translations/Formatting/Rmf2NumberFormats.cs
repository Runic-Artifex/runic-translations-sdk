using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Runic.Translations;

/// <summary>
/// The pinned CLDR number formats of a locale in one numbering system: its digits, symbols, group sizes and percent
/// pattern. Both runtimes format numbers, percentages and the numbers in relative time from these records and write
/// dates and times in the same digits, so .NET and ESM agree whatever the platform's ICU version. The ESM runtime reads
/// the same records (<c>numberFormat</c> in <c>Rmf2EsmRuntimeV5.js</c>).
/// </summary>
internal sealed class Rmf2NumberFormat
{
    private const int FieldCount = 12;
    // By CLDR data ID, in the default numbering system and in latn: bounded by the pinned data, whatever tags callers request.
    private static readonly ConcurrentDictionary<string, Rmf2NumberFormat> s_default = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Rmf2NumberFormat> s_latin = new(StringComparer.Ordinal);

    /// <summary>CLDR root: Latin digits, <c>.</c>, <c>,</c>, <c>-</c>, groups of three and <c>#,##0%</c>.</summary>
    internal static readonly Rmf2NumberFormat Root = new("latn", "0123456789", ".", ",", "-", 3, 3, 3, 3, "", "%", "-", "%");

    private readonly string[] _digits;

    private Rmf2NumberFormat(string system, string digits, string decimalSeparator, string groupSeparator, string minusSign, int primary, int secondary,
        int percentPrimary, int percentSecondary, string percentPrefix, string percentSuffix, string negativePercentPrefix, string negativePercentSuffix,
        int? minimumGrouping = null)
    {
        System = system; MinimumGrouping = minimumGrouping;
        // Ten digits of one width: all in the Basic Multilingual Plane, or all surrogate pairs (Adlam, Chakma).
        int width = digits.Length / 10;
        _digits = new string[10];
        for (int index = 0; index < 10; index++) _digits[index] = digits.Substring(index * width, width);
        Decimal = decimalSeparator; Group = groupSeparator; Minus = minusSign; Primary = primary; Secondary = secondary;
        PercentPrimary = percentPrimary; PercentSecondary = percentSecondary;
        PercentPrefix = percentPrefix; PercentSuffix = percentSuffix; NegativePercentPrefix = negativePercentPrefix; NegativePercentSuffix = negativePercentSuffix;
    }

    /// <summary>The CLDR numbering system, such as <c>latn</c>, <c>arab</c>, <c>arabext</c>, <c>beng</c> or <c>deva</c>.</summary>
    internal string System { get; }
    internal string Decimal { get; }
    internal string Group { get; }
    internal string Minus { get; }
    internal int Primary { get; }
    internal int Secondary { get; }
    internal int PercentPrimary { get; }
    internal int PercentSecondary { get; }
    internal string PercentPrefix { get; }
    internal string PercentSuffix { get; }
    internal string NegativePercentPrefix { get; }
    internal string NegativePercentSuffix { get; }
    internal bool IsLatin => System == "latn";
    /// <summary>CLDR <c>minimumGroupingDigits</c> when the format comes from a message's own data; null for the runtime's (per locale).</summary>
    internal int? MinimumGrouping { get; }

    /// <summary>
    /// The formats of a formatting locale: its CLDR default numbering system, or Latin digits when the tag carries
    /// <c>-u-nu-latn</c>. The number records of the message's own data (<paramref name="data"/>, a locale artifact's
    /// <c>formatData.numbers</c>) come first; a locale without CLDR number data uses the root formats.
    /// </summary>
    internal static Rmf2NumberFormat For(string locale, Rmf2FormatData? data = null)
    {
        if (data?.NumberFormat(locale) is { } own) return own;
        if (GeneratedLocaleData.NumbersId(locale) is not { } id) return Root;
        bool latin = Rmf2LocalizedFormats.LatinDigits(locale);
        return (latin ? s_latin : s_default).GetOrAdd(id, static (id, latin) => FromRecord(GeneratedLocaleData.FindNumbersById(id), latin, null), latin);
    }

    /// <summary>
    /// A format from a number record: the default system's fields and, when that is not latn, latn's after them;
    /// <paramref name="latin"/> takes latn's.
    /// </summary>
    internal static Rmf2NumberFormat FromRecord(string?[] record, bool latin, int? minimumGrouping)
    {
        int offset = latin && record.Length > 1 + FieldCount ? 1 + FieldCount : 1;
        string system = offset == 1 ? record[0]! : "latn";
        return new(system, record[offset]!, record[offset + 1]!, record[offset + 2]!, record[offset + 3]!, Int(record[offset + 4]), Int(record[offset + 5]),
            Int(record[offset + 6]), Int(record[offset + 7]), record[offset + 8]!, record[offset + 9]!, record[offset + 10]!, record[offset + 11]!, minimumGrouping);

        static int Int(string? value) => int.Parse(value!, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    /// <summary>Writes the ASCII digits of <paramref name="text"/> in this numbering system; other characters stay.</summary>
    internal string Digits(string text)
    {
        if (IsLatin || text.AsSpan().IndexOfAnyInRange('0', '9') < 0) return text;
        var builder = new StringBuilder(text.Length * _digits[0].Length);
        foreach (char item in text) builder.Append(item is >= '0' and <= '9' ? _digits[item - '0'] : item);
        return builder.ToString();
    }
}

/// <summary>
/// <c>:number</c>, <c>:integer</c> and percent output from the pinned CLDR number formats: the visible decimal's digits,
/// grouped by the pattern's group sizes and the pinned <c>minimumGroupingDigits</c>, with the locale's decimal, group
/// and minus symbols and its digits. The ESM runtime's <c>formatDecimal</c> and <c>formatInteger</c> apply the same steps.
/// </summary>
internal static class Rmf2NumberFormats
{
    /// <summary>A decimal (<c>:number</c> without a style, the number in relative time) in the locale.</summary>
    internal static string Decimal(VisibleDecimal visible, string grouping, string locale, Rmf2FormatData? data = null)
    {
        Rmf2NumberFormat format = Rmf2NumberFormat.For(locale, data);
        string digits = Digits(visible, grouping, locale, format, format.Primary, format.Secondary);
        return visible.Negative ? format.Minus + digits : digits;
    }

    /// <summary>A percentage: <paramref name="visible"/> is already multiplied by 100, as <c>:number style=percent</c> displays it.</summary>
    internal static string Percent(VisibleDecimal visible, string grouping, string locale, Rmf2FormatData? data = null)
    {
        Rmf2NumberFormat format = Rmf2NumberFormat.For(locale, data);
        string digits = Digits(visible, grouping, locale, format, format.PercentPrimary, format.PercentSecondary);
        return visible.Negative ? format.NegativePercentPrefix + digits + format.NegativePercentSuffix : format.PercentPrefix + digits + format.PercentSuffix;
    }

    /// <summary>
    /// An integer, as <c>:number</c> writes it without fraction digits: the locale's minus sign and digits for every
    /// <c>useGrouping</c> value, so <c>:integer useGrouping=never</c> and <c>:number useGrouping=never</c> agree
    /// (<c>fa</c>: <c>‎−۱۲۳۴</c>). Codes and identifiers that must stay ASCII use <c>:string</c>.
    /// </summary>
    internal static string Integer(long value, string grouping, string locale, Rmf2FormatData? data = null) => Decimal(VisibleDecimal.FromInteger(value), grouping, locale, data);

    /// <summary>The ASCII digits of a localized date or time in the locale's numbering system.</summary>
    internal static string LocalizeDigits(string text, string locale, Rmf2FormatData? data = null) => Rmf2NumberFormat.For(locale, data).Digits(text);

    // The unsigned digits with the decimal separator and, where useGrouping groups, the group separator.
    private static string Digits(VisibleDecimal visible, string grouping, string locale, Rmf2NumberFormat format, int primary, int secondary) =>
        Digits(visible, grouping, locale, format, primary, secondary, format.Decimal, format.Group);

    /// <summary>
    /// The unsigned digits in the locale's numbering system, grouped by <paramref name="primary"/> and
    /// <paramref name="secondary"/> where <paramref name="grouping"/> groups, with the given separators (a currency's own).
    /// </summary>
    internal static string Digits(VisibleDecimal visible, string grouping, string locale, Rmf2NumberFormat format, int primary, int secondary, string decimalSeparator, string groupSeparator)
    {
        string all = visible.Coefficient.ToString(CultureInfo.InvariantCulture).PadLeft(visible.Scale + 1, '0');
        string whole = all[..^visible.Scale], fraction = all[^visible.Scale..];
        var builder = new StringBuilder(all.Length * 2);
        if (Groups(grouping, whole.Length, format.MinimumGrouping ?? GeneratedLocaleData.MinimumGroupingDigits(locale), primary, groupSeparator))
        {
            // Groups from the right: the primary size, then the secondary size (Indian 12,34,567).
            int first = whole.Length - primary, start = first % secondary;
            if (start == 0) start = Math.Min(secondary, first);
            builder.Append(whole, 0, start);
            for (int index = start; index < first; index += secondary) builder.Append(groupSeparator).Append(whole, index, secondary);
            builder.Append(groupSeparator).Append(whole, first, primary);
        }
        else builder.Append(whole);
        if (fraction.Length > 0) builder.Append(decimalSeparator).Append(fraction);
        return format.Digits(builder.ToString());
    }

    /// <summary>
    /// Whether <c>useGrouping</c> separates an integer part of <paramref name="digits"/> digits, as ICU and
    /// <c>Intl.NumberFormat</c> do: <c>auto</c> groups once the digits above the primary group reach the pinned CLDR
    /// <c>minimumGroupingDigits</c> of the locale, <c>min2</c> requires at least two, <c>always</c> one.
    /// </summary>
    internal static bool Groups(string grouping, int digits, int pinned, int primary, string separator)
    {
        if (grouping == "never" || primary <= 0 || separator.Length == 0) return false;
        int minimum = grouping switch { "always" => 1, "min2" => Math.Max(2, pinned), _ => pinned };
        return digits - primary >= minimum;
    }
}
