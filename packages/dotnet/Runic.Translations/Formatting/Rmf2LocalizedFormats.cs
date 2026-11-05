using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Runic.Translations;

/// <summary>
/// Localized dates and times from platform globalization, written by the same rules as the ESM runtime writes them with
/// <c>Intl</c> and in the digits of the pinned CLDR numbering system, and the choice of the locale numbers and dates
/// format in. Numbers and percentages come from pinned CLDR data (<see cref="Rmf2NumberFormats"/>). The shared rules are in
/// <c>specs/translations/rmf2-execution-v2.md</c> ("Localized formatting") and their conformance corpus is
/// <c>specs/translations/corpus/rmf2-date-percent-conformance.json</c>.
/// </summary>
internal static partial class Rmf2LocalizedFormats
{
    /// <summary>
    /// The platform's data for a locale, without its extensions. Without platform locale data, as under
    /// <c>InvariantGlobalization</c>, dates and times use the invariant culture instead of failing.
    /// </summary>
    internal static CultureInfo Culture(string locale)
    {
        try { return CultureInfo.GetCultureInfo(WithoutExtensions(locale)); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    /// <summary>
    /// The locale that numbers, percentages, dates and times format in: the requested locale, without its extensions and
    /// private-use subtags, when it has the content locale's language and the same implied script; otherwise the
    /// content locale. So a <c>de-CH</c> user reading <c>de</c> text sees Swiss separators and dates, while an
    /// <c>sr-Latn</c> user reading Cyrillic <c>sr</c> text, or a <c>zh-TW</c> (Traditional) user reading <c>zh</c>
    /// (Simplified) text, sees the content locale's. The script of a language CLDR writes in one script is dropped when
    /// it is the likely one (<c>de-Latn-CH</c> formats as <c>de-CH</c>), and <c>-u-nu-latn</c> is kept where the
    /// locale's CLDR default numbering system is not Latin (<c>ar-EG-u-nu-latn</c>). Plural, ordinal, list and
    /// relative-time data stay on the content locale.
    /// </summary>
    internal static string FormattingLocale(string? requested, string content)
    {
        if (requested is null || string.Equals(requested, content, StringComparison.OrdinalIgnoreCase) ||
            !LocaleTag.TryCanonicalize(requested, out string canonical) || canonical.Length == 0) return content;
        bool latin = LatinDigits(canonical);
        string tag = WithoutExtensions(canonical);
        CldrLocales.Parts left = CldrLocales.Parse(tag), right = CldrLocales.Parse(content);
        if (!left.Language.Equals(right.Language, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ImpliedScript(left), ImpliedScript(right), StringComparison.OrdinalIgnoreCase)) return content;
        string result = string.Equals(tag, content, StringComparison.OrdinalIgnoreCase) ? content : WithoutDefaultScript(tag, left);
        return latin && !Rmf2NumberFormat.For(result).IsLatin ? result + "-u-nu-latn" : result;
    }

    // The explicit script, else the CLDR likely script of the language and region (sr → Cyrl, sr-ME → Latn, zh-TW →
    // Hant), else null for a language without CLDR likely subtags.
    private static string? ImpliedScript(CldrLocales.Parts tag) => tag.Script ?? CldrLocales.Maximize(tag).Script;

    // The tag without its script when the script adds nothing: it is the likely script of the language and region, and
    // CLDR writes the language in one script only. Platform data may not know such a tag (de-Latn-CH), while the script
    // of a language written in several scripts stays (sr-Cyrl-BA, zh-Hans-HK).
    private static string WithoutDefaultScript(string tag, CldrLocales.Parts parts) =>
        parts.Script is { } script && !MultiScript(parts.Language) &&
        string.Equals(script, CldrLocales.Maximize(parts with { Script = null }).Script, StringComparison.OrdinalIgnoreCase)
            ? (parts with { Script = null }).ToString()
            : tag;

    // The languages whose CLDR likely subtags for the language and its regions name more than one script (sr, zh, pa,
    // uz, …): the values of the language-only and language-region keys. A language-script key names its own script
    // (en-Shaw → en-Shaw-GB), so it does not make the language multi-script, and en-Latn-US formats as en-US.
    private static readonly Lazy<System.Collections.Generic.HashSet<string>> s_multiScript = new(() =>
    {
        var scripts = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        var result = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < GeneratedLocaleTables.LikelyKeys.Length; index++)
        {
            CldrLocales.Parts key = CldrLocales.Parse(GeneratedLocaleTables.LikelyKeys[index]), parts = CldrLocales.Parse(GeneratedLocaleTables.LikelyValues[index]);
            if (key.Script is not null || key.Language != parts.Language) continue;
            if (scripts.TryGetValue(parts.Language, out string? known)) { if (known != parts.Script) result.Add(parts.Language); }
            else scripts.Add(parts.Language, parts.Script ?? "");
        }
        return result;
    });

    private static bool MultiScript(string language) => s_multiScript.Value.Contains(language.ToLowerInvariant());

    /// <summary>Whether a canonical tag's Unicode extension asks for Latin digits (<c>-u-nu-latn</c>).</summary>
    internal static bool LatinDigits(string canonical)
    {
        if (!canonical.Contains("-u-", StringComparison.OrdinalIgnoreCase)) return false;
        string[] parts = canonical.Split('-');
        int index = 1;
        // The u singleton, unless private use (x) starts first.
        while (index < parts.Length && !(parts[index].Length == 1 && (parts[index] is "u" or "U" or "x" or "X"))) index++;
        if (index >= parts.Length || parts[index] is "x" or "X") return false;
        for (index++; index + 1 < parts.Length && parts[index].Length > 1; index++)
            if (parts[index].Equals("nu", StringComparison.OrdinalIgnoreCase)) return parts[index + 1].Equals("latn", StringComparison.OrdinalIgnoreCase);
        return false;
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

    /// <summary>A date in a CLDR length: <c>short</c>, <c>medium</c>, <c>long</c> or <c>full</c>, in the locale's digits.</summary>
    internal static string Date(DateOnly value, string style, string locale, Rmf2FormatData? data = null)
    {
        CultureInfo culture = Culture(locale);
        string pattern = DatePattern(culture.DateTimeFormat, style);
        // .NET's dddd writes CLDR's stand-alone weekday; a pattern with CLDR's format-context EEEE needs those names.
        if (GeneratedLocaleData.FindWeekdays(locale) is { } weekdays) pattern = ReplaceField(pattern, 'd', 4, weekdays[(int)value.DayOfWeek]!);
        return Rmf2NumberFormats.LocalizeDigits(Spaces(value.ToString(pattern, culture)), locale, data);
    }

    /// <summary>
    /// A wall-clock time: <c>short</c> is the CLDR short time; <c>medium</c>, <c>long</c> and <c>full</c> are the
    /// CLDR medium time, because the longer CLDR times add a zone name that a time without a date and zone has not.
    /// </summary>
    internal static string Time(TimeOnly value, string style, string locale, Rmf2FormatData? data = null)
    {
        CultureInfo culture = Culture(locale);
        string pattern = style == "short" ? culture.DateTimeFormat.ShortTimePattern : culture.DateTimeFormat.LongTimePattern;
        // .NET writes CLDR's flexible day period (B, zh-Hant's 凌晨, 中午, 晚上) as the AM/PM designator (tt).
        if (GeneratedLocaleData.FindDayPeriods(locale) is { } periods)
        {
            int minute = value.Hour * 60 + value.Minute, index = 0;
            while (index + 2 < periods.Length && int.Parse(periods[index + 2]!, NumberStyles.None, CultureInfo.InvariantCulture) <= minute) index += 2;
            pattern = ReplaceField(pattern, 't', 1, periods[index + 1]!);
        }
        return Rmf2NumberFormats.LocalizeDigits(Spaces(value.ToString(pattern, culture)), locale, data);
    }

    // Replaces each unquoted run of at least minimum letters field in a .NET date pattern with literal text.
    private static string ReplaceField(string pattern, char field, int minimum, string text)
    {
        var builder = new System.Text.StringBuilder(pattern.Length + text.Length);
        string literal = "'" + text.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        for (int index = 0; index < pattern.Length;)
        {
            char item = pattern[index];
            if (item is '\'' or '"')
            {
                int end = pattern.IndexOf(item, index + 1);
                end = end < 0 ? pattern.Length : end + 1;
                builder.Append(pattern, index, end - index); index = end; continue;
            }
            if (item == '\\' && index + 1 < pattern.Length) { builder.Append(pattern, index, 2); index += 2; continue; }
            int run = index;
            while (run < pattern.Length && pattern[run] == item) run++;
            if (item == field && run - index >= minimum) builder.Append(literal); else builder.Append(pattern, index, run - index);
            index = run;
        }
        return builder.ToString();
    }

    /// <summary>The instant's date and time in the zone, each in the style, joined by a space.</summary>
    internal static string DateTime(DateTimeOffset instant, string style, string timeZone, string locale, Rmf2FormatData? data = null)
    {
        DateTime wall = TimeZoneInfo.ConvertTime(instant, Zone(timeZone)).DateTime;
        return Date(DateOnly.FromDateTime(wall), style, locale, data) + " " + Time(TimeOnly.FromDateTime(wall), style, locale, data);
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
    /// medium pattern equalled the short one. A numeric pattern in another field order than the short one is such a
    /// <c>yMd</c> pattern (Swedish <c>yyyy-MM-dd</c> after the Åland short and medium <c>d.M.yyyy</c>). Such a <c>yMd</c> pattern is the short pattern with fewer padded days or
    /// months (Japanese <c>yyyy/M/d</c> after <c>yyyy/MM/dd</c>), so a numeric second pattern that pads more (Arabic
    /// <c>dd/MM/yyyy</c> after <c>d/M/yyyy</c>) or has other separators (Apple's Czech <c>d. M. yyyy</c> after
    /// <c>dd.MM.yyyy</c>) is the medium one.
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
        if (!replaced && Numeric(second) && Numeric(first) && Padded(second) <= Padded(first) &&
            string.Equals(Unpadded(second), Unpadded(first), StringComparison.Ordinal)) return first;
        // A numeric yMd pattern in another field order is inherited from the language (sv-AX's yyyy-MM-dd after d.M.yyyy).
        if (!replaced && Numeric(second) && Numeric(first) && !string.Equals(FieldOrder(second), FieldOrder(first), StringComparison.Ordinal)) return first;
        return second;
    }

    // The order of the day, month and year fields of a pattern, such as "dMy".
    private static string FieldOrder(string pattern)
    {
        var order = new System.Text.StringBuilder(3);
        foreach (char item in pattern)
            if (item is 'd' or 'M' or 'y' && (order.Length == 0 || order[^1] != item)) order.Append(item);
        return order.ToString();
    }

    // The pattern with its two-letter day and month fields written with one letter.
    private static string Unpadded(string pattern) => pattern.Replace("dd", "d", StringComparison.Ordinal).Replace("MM", "M", StringComparison.Ordinal);

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

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_+-]*(?:/[A-Za-z0-9_+-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeZoneSyntax();
}
