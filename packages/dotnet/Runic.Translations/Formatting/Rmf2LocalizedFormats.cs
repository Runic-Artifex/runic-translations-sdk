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
    /// The locale that numbers, percentages, dates and times format in: the requested locale, without its extensions and
    /// private-use subtags, when it has the content locale's language and the same implied script; otherwise the
    /// content locale. So a <c>de-CH</c> user reading <c>de</c> text sees Swiss separators and dates, while an
    /// <c>sr-Latn</c> user reading Cyrillic <c>sr</c> text, or a <c>zh-TW</c> (Traditional) user reading <c>zh</c>
    /// (Simplified) text, sees the content locale's. Plural, ordinal, list and relative-time data stay on the content
    /// locale.
    /// </summary>
    internal static string FormattingLocale(string? requested, string content)
    {
        if (requested is null || string.Equals(requested, content, StringComparison.OrdinalIgnoreCase) ||
            !LocaleTag.TryCanonicalize(requested, out string canonical) || canonical.Length == 0) return content;
        canonical = WithoutExtensions(canonical);
        if (string.Equals(canonical, content, StringComparison.OrdinalIgnoreCase)) return content;
        Tag left = Parse(canonical), right = Parse(content);
        if (!left.Language.Equals(right.Language, StringComparison.OrdinalIgnoreCase)) return content;
        return string.Equals(ImpliedScript(left), ImpliedScript(right), StringComparison.OrdinalIgnoreCase) ? canonical : content;
    }

    // The language, script and region subtags of a canonical tag.
    private readonly record struct Tag(string Language, string? Script, string? Region);

    private static Tag Parse(string canonical)
    {
        string[] parts = canonical.Split('-');
        int index = 1;
        if (parts[0].Length <= 3)
            for (int count = 0; count < 3 && index < parts.Length && parts[index].Length == 3 && char.IsAsciiLetter(parts[index][0]); count++) index++;
        string? script = null, region = null;
        if (index < parts.Length && parts[index].Length == 4 && char.IsAsciiLetter(parts[index][0])) script = parts[index++];
        if (index < parts.Length && (parts[index].Length == 2 || (parts[index].Length == 3 && char.IsAsciiDigit(parts[index][0])))) region = parts[index];
        return new Tag(parts[0], script, region);
    }

    /// <summary>
    /// A canonical tag without its Unicode and other extensions and private-use subtags: <c>en-US-u-ca-buddhist</c>
    /// formats as <c>en-US</c>. The runtimes would apply calendar and numbering extensions differently, and requested tags
    /// can come from untrusted input.
    /// </summary>
    internal static string WithoutExtensions(string canonical)
    {
        for (int index = canonical.IndexOf('-'); index >= 0 && index + 2 <= canonical.Length; index = canonical.IndexOf('-', index + 1))
            if (index + 2 == canonical.Length || canonical[index + 2] == '-') return canonical[..index];
        return canonical;
    }

    // The explicit script, else the language's likely script for the region (CLDR likelySubtags), else null for a
    // language without an entry; two tags of such a language match only when neither names a script.
    // TODO(#131): replace the table with CldrLocales.Maximize(...).Script once the pinned likelySubtags data lands.
    private static string? ImpliedScript(Tag tag) => tag.Script ?? LikelyScript(tag.Language.ToLowerInvariant(), tag.Region?.ToUpperInvariant());

    /// <summary>
    /// The likely script of a language in a region, from CLDR 48.2 likelySubtags, for the languages that CLDR writes in
    /// more than one script. Other languages return null.
    /// </summary>
    internal static string? LikelyScript(string language, string? region) => (language, region) switch
    {
        ("az", "IQ" or "IR") => "Arab", ("az", "RU") => "Cyrl", ("az", _) => "Latn",
        ("bs", _) => "Latn",
        ("ff", _) => "Latn",
        ("ha", "CM" or "SD") => "Arab", ("ha", _) => "Latn",
        ("kk", "AF" or "CN" or "IR" or "MN") => "Arab", ("kk", _) => "Cyrl",
        ("ks", _) => "Arab",
        ("ku", "AM" or "AZ" or "GE" or "TM") => "Cyrl", ("ku", "IQ" or "IR" or "LB") => "Arab", ("ku", _) => "Latn",
        ("ky", "CN") => "Arab", ("ky", "TR") => "Latn", ("ky", _) => "Cyrl",
        ("mn", "CN") => "Mong", ("mn", _) => "Cyrl",
        ("ms", "CC") => "Arab", ("ms", _) => "Latn",
        ("pa", "PK") => "Arab", ("pa", _) => "Guru",
        ("sd", "IN") => "Deva", ("sd", _) => "Arab",
        ("shi", _) => "Tfng",
        ("sr", "ME" or "RO" or "TR") => "Latn", ("sr", _) => "Cyrl",
        ("tg", "PK") => "Arab", ("tg", _) => "Cyrl",
        ("ug", "KZ" or "MN") => "Cyrl", ("ug", _) => "Arab",
        ("uz", "AF") => "Arab", ("uz", "CN") => "Cyrl", ("uz", _) => "Latn",
        ("vai", _) => "Vaii",
        ("yue", "CN") => "Hans", ("yue", _) => "Hant",
        ("zh", "AU" or "BN" or "GB" or "GF" or "HK" or "ID" or "MO" or "PA" or "PF" or "PH" or "SR" or "TH" or "TW" or "US" or "VN") => "Hant",
        ("zh", _) => "Hans",
        _ => null,
    };

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
    /// <c>local</c> is the host's zone, <c>utc</c> UTC, and anything else an IANA ID, which .NET resolves from the
    /// platform's zone data: tzdata on Linux and macOS, and on Windows the system zones through ICU's IANA mapping. An ID
    /// the platform cannot resolve, unknown or missing from its zone data (a Linux image without tzdata, Windows in NLS
    /// or invariant mode), throws <see cref="TimeZoneNotFoundException"/> with a message naming it, which the evaluator
    /// reports as a <see cref="TranslationFormatException"/>; the ESM runtime throws a <c>RangeError</c> for the
    /// same IDs. There is no silent fallback to UTC.
    /// </summary>
    internal static TimeZoneInfo Zone(string timeZone)
    {
        switch (timeZone)
        {
            case "local": return TimeZoneInfo.Local;
            case "utc": return TimeZoneInfo.Utc;
        }
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
        catch (TimeZoneNotFoundException exception)
        {
            throw new TimeZoneNotFoundException("The time zone '" + timeZone + "' is not available. It is unknown, or the platform has no data for it: " +
                "IANA zones need tzdata on Linux and ICU globalization on Windows (not NLS or invariant mode).", exception);
        }
    }

    /// <summary>The syntax of a <c>timeZone</c> value besides <c>local</c> and <c>utc</c>, as the compiler checks it.</summary>
    internal static bool IsTimeZone(string value) => value.Length is > 0 and <= 64 && TimeZoneSyntax().IsMatch(value);

    // The ESM runtime writes U+202F as a space, as V8 does, so both runtimes agree whatever their ICU version. A run of
    // one bidirectional mark (U+200E, U+200F, U+061C) is written once: .NET doubles the marks of Arabic date patterns.
    private static string Spaces(string value)
    {
        value = value.Replace('\u202F', ' ');
        if (value.AsSpan().IndexOfAny('\u200E', '\u200F', '\u061C') < 0) return value;
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (char item in value)
            if (!(item is '\u200E' or '\u200F' or '\u061C' && builder.Length > 0 && builder[^1] == item)) builder.Append(item);
        return builder.ToString();
    }

    private static string DatePattern(DateTimeFormatInfo format, string style)
    {
        switch (style)
        {
            case "short": return format.ShortDatePattern;
            case "full": return format.LongDatePattern;
            // Windows NLS mode lists other date patterns, so ICU's medium and long patterns cannot be derived there.
            case "medium" or "long" when Nls: return style == "medium" ? format.ShortDatePattern : format.LongDatePattern;
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
    /// Whether .NET uses Windows NLS rather than ICU for culture data (<c>System.Globalization.UseNls</c>, or a Windows
    /// version without ICU). Localized formatting is specified and conformance-tested in ICU mode, the .NET default; in
    /// NLS mode <c>medium</c> dates are the short and <c>long</c> dates the full pattern, and IANA zones are unavailable.
    /// The check is the one the .NET globalization documentation gives for ICU mode.
    /// </summary>
    private static readonly bool Nls = OperatingSystem.IsWindows() && !IcuMode();

    private static bool IcuMode()
    {
        SortVersion version = CultureInfo.InvariantCulture.CompareInfo.Version;
        byte[] bytes = version.SortId.ToByteArray();
        int id = bytes[3] << 24 | bytes[2] << 16 | bytes[1] << 8 | bytes[0];
        return id != 0 && id == version.FullVersion;
    }

    /// <summary>
    /// ICU's medium date pattern, which .NET has no property for. .NET lists ICU's short, medium and <c>yMd</c> date
    /// patterns without duplicates, writes every year as <c>yyyy</c>, and then puts the short pattern with a four-digit
    /// year first; the original two-digit short pattern takes the place of the first later pattern equal to it, or is
    /// appended. So the second pattern is the medium one unless it is that two-digit original (then medium equals the
    /// short pattern), or, when no two-digit year was replaced, a numeric <c>yMd</c> pattern that follows because the
    /// medium pattern equalled the short one. A <c>yMd</c> pattern never pads a day or month that the short pattern
    /// leaves unpadded, so a numeric second pattern that does (Arabic <c>dd/MM/yyyy</c> after <c>d/M/yyyy</c>) is the
    /// medium one.
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
        if (!replaced && Numeric(second) && Numeric(first) && Padded(second) <= Padded(first)) return first;
        return second;
    }

    // The number of two-letter day and month fields (dd, MM) of a pattern.
    private static int Padded(string pattern)
    {
        int count = 0;
        for (int index = 0; index < pattern.Length;)
        {
            char item = pattern[index];
            int end = index;
            while (end < pattern.Length && pattern[end] == item) end++;
            if (item is 'd' or 'M' && end - index == 2) count++;
            index = end;
        }
        return count;
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
