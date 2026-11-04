using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Runic.Translations;

/// <summary>
/// Localized dates, times and percentages from platform globalization, written by the same rules as the ESM runtime
/// writes them with <c>Intl</c>, and the choice of the locale numbers and dates format in. The shared rules are in
/// <c>specs/translations/rmf2-execution-v2.md</c> ("Localized formatting") and their conformance corpus is
/// <c>specs/translations/corpus/rmf2-date-percent-conformance.json</c>.
/// </summary>
internal static partial class Rmf2LocalizedFormats
{
    /// <summary>
    /// The platform's data for a locale. Without platform locale data, as under <c>InvariantGlobalization</c>,
    /// numbers, dates and times use the invariant culture instead of failing.
    /// </summary>
    internal static CultureInfo Culture(string locale)
    {
        try { return CultureInfo.GetCultureInfo(locale); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    /// <summary>
    /// The locale that numbers, percentages, dates and times format in: the requested locale when its language subtag
    /// equals the content locale's (and their scripts match when both name one), so a <c>de-CH</c> user reading
    /// <c>de</c> text sees Swiss separators and dates; otherwise the content locale. Plural, ordinal, list and
    /// relative-time data stay on the content locale.
    /// </summary>
    internal static string FormattingLocale(string? requested, string content)
    {
        if (requested is null || string.Equals(requested, content, StringComparison.OrdinalIgnoreCase) ||
            !LocaleTag.TryCanonicalize(requested, out string canonical) || canonical.Length == 0) return content;
        if (!Subtag(canonical, 0).Equals(Subtag(content, 0), StringComparison.OrdinalIgnoreCase)) return content;
        ReadOnlySpan<char> requestedScript = Script(canonical), contentScript = Script(content);
        return !requestedScript.IsEmpty && !contentScript.IsEmpty && !requestedScript.Equals(contentScript, StringComparison.OrdinalIgnoreCase) ? content : canonical;
    }

    private static ReadOnlySpan<char> Subtag(string tag, int index)
    {
        ReadOnlySpan<char> rest = tag;
        for (int current = 0; ; current++)
        {
            int end = rest.IndexOfAny('-', '_');
            ReadOnlySpan<char> part = end < 0 ? rest : rest[..end];
            if (current == index) return part;
            if (end < 0) return ReadOnlySpan<char>.Empty;
            rest = rest[(end + 1)..];
        }
    }

    private static ReadOnlySpan<char> Script(string tag)
    {
        ReadOnlySpan<char> second = Subtag(tag, 1);
        foreach (char item in second) if (!char.IsAsciiLetter(item)) return ReadOnlySpan<char>.Empty;
        return second.Length == 4 ? second : ReadOnlySpan<char>.Empty;
    }

    /// <summary>A date in a CLDR length: <c>short</c>, <c>medium</c>, <c>long</c> or <c>full</c>.</summary>
    internal static string Date(DateOnly value, string style, CultureInfo culture) =>
        Spaces(value.ToString(DatePattern(culture.DateTimeFormat, style), culture));

    /// <summary>
    /// A wall-clock time: <c>short</c> is the CLDR short time; <c>medium</c>, <c>long</c> and <c>full</c> are the
    /// CLDR medium time, because the longer CLDR times add a zone name that a time without a date and zone has not.
    /// </summary>
    internal static string Time(TimeOnly value, string style, CultureInfo culture) =>
        Spaces(value.ToString(style == "short" ? culture.DateTimeFormat.ShortTimePattern : culture.DateTimeFormat.LongTimePattern, culture));

    /// <summary>The instant's date and time in the zone, each in the style, joined by a space.</summary>
    internal static string DateTime(DateTimeOffset instant, string style, string timeZone, CultureInfo culture)
    {
        DateTime wall = TimeZoneInfo.ConvertTime(instant, Zone(timeZone)).DateTime;
        return Date(DateOnly.FromDateTime(wall), style, culture) + " " + Time(TimeOnly.FromDateTime(wall), style, culture);
    }

    /// <summary>
    /// <c>local</c> is the host's zone, <c>utc</c> UTC, and anything else an IANA ID, which .NET resolves on every
    /// platform with ICU. An unknown ID throws <see cref="TimeZoneNotFoundException"/>.
    /// </summary>
    internal static TimeZoneInfo Zone(string timeZone) => timeZone switch
    {
        "local" => TimeZoneInfo.Local,
        "utc" => TimeZoneInfo.Utc,
        _ => TimeZoneInfo.FindSystemTimeZoneById(timeZone),
    };

    /// <summary>The syntax of a <c>timeZone</c> value besides <c>local</c> and <c>utc</c>, as the compiler checks it.</summary>
    internal static bool IsTimeZone(string value) => value.Length is > 0 and <= 64 && TimeZoneSyntax().IsMatch(value);

    // The ESM runtime writes U+202F as a space, as V8 does, so both runtimes agree whatever their ICU version.
    private static string Spaces(string value) => value.Replace('\u202F', ' ');

    private static string DatePattern(DateTimeFormatInfo format, string style)
    {
        switch (style)
        {
            case "short": return format.ShortDatePattern;
            case "full": return format.LongDatePattern;
            case "long":
            {
                // .NET reads ICU's full and then its long date pattern; "D" is the full one, with the weekday.
                string[] patterns = format.GetAllDateTimePatterns('D');
                return patterns.Length > 1 ? patterns[1] : format.LongDatePattern;
            }
            default: return MediumDatePattern(format);
        }
    }

    /// <summary>
    /// ICU's medium date pattern, which .NET has no property for. .NET lists ICU's short, medium and <c>yMd</c> date
    /// patterns without duplicates, writes every year as <c>yyyy</c>, and then puts the short pattern with a four-digit
    /// year first; the original two-digit short pattern takes the place of the first later pattern equal to it, or is
    /// appended. So the second pattern is the medium one unless it is that two-digit original (then medium equals the
    /// short pattern), or, when no two-digit year was replaced, a numeric <c>yMd</c> pattern that follows because the
    /// medium pattern equalled the short one.
    /// </summary>
    private static string MediumDatePattern(DateTimeFormatInfo format)
    {
        string first = format.ShortDatePattern;
        string[] patterns = format.GetAllDateTimePatterns('d');
        if (patterns.Length < 2 || !string.Equals(patterns[0], first, StringComparison.Ordinal)) return first;
        string second = patterns[1];
        string twoDigit = first.Replace("yyyy", "yy", StringComparison.Ordinal);
        bool replaced = !string.Equals(twoDigit, first, StringComparison.Ordinal) && Array.IndexOf(patterns, twoDigit, 1) >= 0;
        if (string.Equals(second, twoDigit, StringComparison.Ordinal)) return first;
        if (!replaced && Numeric(second) && Numeric(first)) return first;
        return second;
    }

    // A pattern of numeric day, month and year fields and punctuation only, like ICU's yMd skeleton patterns.
    private static bool Numeric(string pattern)
    {
        int months = 0;
        foreach (char item in pattern)
        {
            // Quoted text is a word, such as Spanish 'de'.
            if (item == '\'') return false;
            months = item == 'M' ? months + 1 : 0;
            if (months > 2 || item is not ('d' or 'y' or 'M') && char.IsLetter(item)) return false;
        }
        return true;
    }

    /// <summary>
    /// A percentage in the locale's percent pattern: the visible digits (already scaled and grouped) between the
    /// pattern's prefix and suffix, with the locale's percent and minus signs. A space in the pattern is U+00A0, as
    /// in CLDR and <c>Intl.NumberFormat</c>.
    /// </summary>
    internal static string Percent(string digits, bool negative, NumberFormatInfo format)
    {
        string pattern = negative
            ? format.PercentNegativePattern switch
            {
                0 => "-n %", 1 => "-n%", 2 => "-%n", 3 => "%-n", 4 => "%n-", 5 => "n-%", 6 => "n%-", 7 => "-% n", 8 => "n %-", 9 => "% n-", 10 => "% -n", 11 => "n- %", _ => "-n%",
            }
            : format.PercentPositivePattern switch { 0 => "n %", 1 => "n%", 2 => "%n", 3 => "% n", _ => "n%" };
        var builder = new System.Text.StringBuilder(digits.Length + 8);
        foreach (char item in pattern)
        {
            switch (item)
            {
                case 'n': builder.Append(digits); break;
                case '%': builder.Append(format.PercentSymbol); break;
                case '-': builder.Append(format.NegativeSign); break;
                default: builder.Append('\u00A0'); break;
            }
        }
        return builder.ToString();
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_+-]*(?:/[A-Za-z0-9_+-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeZoneSyntax();
}
