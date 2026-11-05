using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;

namespace Runic.Translations;

/// <summary>
/// The pinned CLDR data that the compiler embeds with a set of messages (D055.4): the currencies, units and compact styles
/// they use for the CLDR locales of their languages (a .NET or ESM catalog, a locale artifact), and, in a locale artifact
/// whose messages format numbers, the number records of its languages (<c>numbers</c>), so an overlay that adds a
/// language formats with that language's symbols and digits on a catalog without them. The ESM runtime reads the same
/// data (<c>generatedLocaleData.formats</c>, an artifact's <c>formatData</c>) and checks it with the same rules.
/// </summary>
/// <remarks>
/// <c>{"currencies": {code: digits}, "dynamicCurrencies": [...], "dynamicUnits": [...], "locales": {data ID:
/// {"currencyFormats", "currencies", "units", "per", "compact"}}, "numbers": {"locales": {data ID: record}, "grouping",
/// "likely", "parents", "aliases"}}</c>. Each locale stores what differs from its CLDR parent chain, so a lookup walks the
/// chain of the formatting locale's data ID to the first locale with the entry. Every member is checked when the data is
/// read (<see cref="Parse(JsonElement)"/>; <c>specs/translations/rmf2-execution-v2.md</c>, "Embedded format data"), so a
/// crafted pack fails as <c>RTR0023/malformed</c> and never while it formats. Parsed patterns and number formats are
/// cached per instance, so the caches are bounded by the data and go away with it.
/// </remarks>
internal sealed class Rmf2FormatData
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private const int MaximumText = 128, MaximumMembers = 256, MaximumLocales = 2048, MaximumLikely = 4096, MaximumCachedLocales = 256;
    private static readonly string[] Widths = ["long", "short", "narrow"];
    private static readonly System.Buffers.SearchValues<char> DataIdVariant = System.Buffers.SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789");
    private static readonly System.Buffers.SearchValues<char> TagCharacters = System.Buffers.SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
    private readonly Dictionary<string, int> _currencyDigits = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dynamicCurrencies = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dynamicUnits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _locales = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?[]> _numberRecords = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _grouping = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Rmf2CldrFormats.Pattern> _patterns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Rmf2NumberFormat> _numberFormats = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Rmf2NumberFormat?> _localeNumberFormats = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        internal Dictionary<string, string?[]> CurrencyFormats { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string?[]> Currencies { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string?[]?[]> Units { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string> Per { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, Dictionary<string, string?[][]>> Compact { get; } = new(StringComparer.Ordinal);
    }

    private Rmf2FormatData() { }

    // Another instance's number records with a catalog's currency, unit and compact data.
    private Rmf2FormatData(Rmf2FormatData numbers, Rmf2FormatData formatters)
    {
        _currencyDigits = formatters._currencyDigits; _dynamicCurrencies = formatters._dynamicCurrencies; _dynamicUnits = formatters._dynamicUnits;
        _locales = formatters._locales; _patterns = formatters._patterns; HasFormatters = true;
        _numberRecords = numbers._numberRecords; _grouping = numbers._grouping;
    }

    /// <summary>
    /// The data a pack or overlay message formats with: its own, with the catalog's currency, unit and compact data when it
    /// has none of its own (an artifact's <c>formatData</c> holds them only when its messages use them).
    /// </summary>
    internal static Rmf2FormatData? Combine(Rmf2FormatData? own, Rmf2FormatData? catalog) =>
        own is null ? catalog : own.HasFormatters || catalog is not { HasFormatters: true } ? own : new(own, catalog);

    /// <summary>Whether the data has currency, unit and compact data (<c>currencies</c> and <c>locales</c>).</summary>
    internal bool HasFormatters { get; private set; }

    /// <summary>Reads the compiler's embedded JSON (a generated catalog's).</summary>
    /// <exception cref="TranslationPackException">The data breaks the rules of embedded format data.</exception>
    internal static Rmf2FormatData Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > MaximumBytes) throw Malformed();
        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions { MaxDepth = 8 });
        try
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            return Parse(document.RootElement);
        }
        catch (JsonException) { throw Malformed(); }
    }

    /// <summary>Reads and checks the <c>formatData</c> member of a locale artifact.</summary>
    /// <exception cref="TranslationPackException">The data breaks the rules of embedded format data.</exception>
    internal static Rmf2FormatData Parse(JsonElement root)
    {
        try
        {
            var data = new Rmf2FormatData();
            bool currencies = false, locales = false, formatters = false;
            foreach (JsonProperty member in Object(root, 5))
            {
                switch (member.Name)
                {
                    case "currencies":
                        foreach (JsonProperty currency in Object(member.Value, MaximumMembers))
                            data._currencyDigits.Add(Code(currency.Name), currency.Value.ValueKind == JsonValueKind.Number && currency.Value.TryGetInt32(out int digits) && digits is >= 0 and <= 6 ? digits : throw Malformed());
                        currencies = formatters = true;
                        break;
                    case "dynamicCurrencies":
                        foreach (JsonElement item in Array(member.Value, 0, MaximumMembers).EnumerateArray()) if (!data._dynamicCurrencies.Add(Code(Text(item)))) throw Malformed();
                        formatters = true;
                        break;
                    case "dynamicUnits":
                        foreach (JsonElement item in Array(member.Value, 0, MaximumMembers).EnumerateArray()) if (!data._dynamicUnits.Add(Unit(Text(item)))) throw Malformed();
                        formatters = true;
                        break;
                    case "locales":
                        foreach (JsonProperty locale in Object(member.Value, MaximumLocales)) data._locales.Add(DataId(locale.Name), ReadEntry(locale.Value));
                        locales = formatters = true;
                        break;
                    case "numbers": data.ReadNumbers(member.Value); break;
                    default: throw Malformed();
                }
            }
            // The formatter members come together, and a run-time currency is one whose digits the data has.
            if (formatters && !(currencies && locales)) throw Malformed();
            foreach (string code in data._dynamicCurrencies) if (!data._currencyDigits.ContainsKey(code)) throw Malformed();
            data.HasFormatters = formatters;
            return data;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException) { throw Malformed(); }
    }

    private static Entry ReadEntry(JsonElement value)
    {
        var entry = new Entry();
        foreach (JsonProperty section in Object(value, 5))
            switch (section.Name)
            {
                case "currencyFormats":
                    // [standard, accounting, unit pattern other … many, currency decimal, currency group]
                    foreach (JsonProperty member in Object(section.Value, 16))
                    {
                        string?[] record = Record(member.Value, 3, 10);
                        CurrencyPattern(record[0]); CurrencyPattern(record[1]);
                        for (int index = 2; index < 8; index++) Placeholders(At(record, index), 1, 1, required: index == 2);
                        Separator(At(record, 8)); Separator(At(record, 9));
                        entry.CurrencyFormats.Add(NumberingSystem(member.Name), record);
                    }
                    break;
                case "currencies":
                    // [symbol, narrow, names other … many, pattern, decimal, group]
                    foreach (JsonProperty member in Object(section.Value, MaximumMembers))
                    {
                        string?[] record = Record(member.Value, 3, 11);
                        if (record[2] is null) throw Malformed();
                        if (At(record, 8) is { } pattern) CurrencyPattern(pattern);
                        Separator(At(record, 9)); Separator(At(record, 10));
                        entry.Currencies.Add(Code(member.Name), record);
                    }
                    break;
                case "units":
                    // [long, short, narrow] of [other … many, per]
                    foreach (JsonProperty member in Object(section.Value, MaximumMembers))
                    {
                        var widths = new string?[]?[Array(member.Value, 1, 3).GetArrayLength()];
                        int index = 0;
                        foreach (JsonElement width in member.Value.EnumerateArray())
                        {
                            if (width.ValueKind == JsonValueKind.Null && index > 0) { widths[index++] = null; continue; }
                            string?[] record = Record(width, 1, 7);
                            // A unit pattern has at most one {0} (CLDR names some units without the number), a per-unit pattern exactly one.
                            for (int slot = 0; slot < 6; slot++) if (At(record, slot) is { } pattern && (Count(pattern, "{0}") > 1 || Count(pattern, "{1}") > 0)) throw Malformed();
                            Placeholders(At(record, 6), 1, 0, required: false);
                            widths[index++] = record;
                        }
                        entry.Units.Add(Unit(member.Name), widths);
                    }
                    break;
                case "per":
                    foreach (JsonProperty member in Object(section.Value, 3))
                    {
                        string pattern = Text(member.Value);
                        Placeholders(pattern, 1, 1, required: true);
                        entry.Per.Add(System.Array.IndexOf(Widths, member.Name) >= 0 ? member.Name : throw Malformed(), pattern);
                    }
                    break;
                case "compact":
                    foreach (JsonProperty member in Object(section.Value, 16))
                    {
                        var styles = new Dictionary<string, string?[][]>(StringComparer.Ordinal);
                        foreach (JsonProperty style in Object(member.Value, 2))
                        {
                            var magnitudes = new string?[Array(style.Value, 1, 32).GetArrayLength()][];
                            int index = 0;
                            foreach (JsonElement item in style.Value.EnumerateArray())
                            {
                                string?[] record = Record(item, 1, 7);
                                foreach (string? pattern in record) if (pattern is not null) CompactPattern(pattern, index + 3);
                                magnitudes[index++] = record;
                            }
                            styles.Add(style.Name is "short" or "long" ? style.Name : throw Malformed(), magnitudes);
                        }
                        entry.Compact.Add(NumberingSystem(member.Name), styles);
                    }
                    break;
                default: throw Malformed();
            }
        return entry;
    }

    // numbers: the number records (Rmf2NumberFormat) and minimumGroupingDigits of the artifact's languages, and the CLDR
    // likely subtags, parents and aliases that resolve their tags, which the ESM runtime of a catalog without these
    // languages lacks. .NET resolves tags with its own pinned tables, which are the same.
    private void ReadNumbers(JsonElement value)
    {
        foreach (JsonProperty member in Object(value, 5))
            switch (member.Name)
            {
                case "locales":
                    foreach (JsonProperty locale in Object(member.Value, MaximumLocales)) _numberRecords.Add(DataId(locale.Name), NumberRecord(locale.Value));
                    break;
                case "grouping":
                    foreach (JsonProperty locale in Object(member.Value, MaximumLocales))
                        _grouping.Add(DataId(locale.Name), locale.Value.ValueKind == JsonValueKind.Number && locale.Value.TryGetInt32(out int digits) && digits is >= 1 and <= 4 ? digits : throw Malformed());
                    break;
                case "likely" or "parents" or "aliases":
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JsonProperty entry in Object(member.Value, MaximumLikely))
                    {
                        Tag(Text(entry.Value));
                        if (!seen.Add(Tag(entry.Name))) throw Malformed();
                    }
                    break;
                }
                default: throw Malformed();
            }
    }

    // [numbering system, then 12 fields for it and, when it is not latn, 12 for latn]: digits, decimal, group, minus,
    // primary, secondary, percent primary, percent secondary, percent prefix, suffix, negative prefix, negative suffix.
    private static string?[] NumberRecord(JsonElement value)
    {
        int length = Array(value, 13, 25).GetArrayLength();
        if (length is not (13 or 25)) throw Malformed();
        var record = new string?[length];
        int index = 0;
        foreach (JsonElement item in value.EnumerateArray()) record[index++] = item.ValueKind == JsonValueKind.String ? item.GetString() : throw Malformed();
        string system = NumberingSystem(record[0]!);
        if ((system == "latn") != (length == 13)) throw Malformed();
        for (int block = 1; block < length; block += 12)
        {
            string digits = record[block]!;
            bool latin = block == 13 || system == "latn";
            // Ten digits of one width: all in the Basic Multilingual Plane, or all surrogate pairs (Adlam, Chakma).
            if (latin ? digits != "0123456789" : !(digits.Length == 10 && digits.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF') < 0 || digits.Length == 20 && AllPairs(digits))) throw Malformed();
            for (int field = 1; field <= 3; field++) if (record[block + field]!.Length is < 1 or > 8) throw Malformed();
            for (int field = 4; field <= 7; field++) if (record[block + field] is not { Length: 1 } size || size[0] is < '1' or > '9') throw Malformed();
            for (int field = 8; field <= 11; field++) if (record[block + field]!.Length > 16) throw Malformed();
        }
        return record;

        static bool AllPairs(string text)
        {
            for (int index = 0; index < text.Length; index += 2) if (!char.IsSurrogatePair(text[index], text[index + 1])) return false;
            return true;
        }
    }

    private static JsonElement.ObjectEnumerator Object(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Malformed();
        int count = 0;
        foreach (JsonProperty _ in value.EnumerateObject()) if (++count > maximum) throw Malformed();
        return value.EnumerateObject();
    }

    private static JsonElement Array(JsonElement value, int minimum, int maximum) =>
        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() is var length && length >= minimum && length <= maximum ? value : throw Malformed();

    private static string Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= MaximumText } text ? text : throw Malformed();

    // A record of optional strings whose first and last values are present (the compiler leaves out trailing nulls).
    private static string?[] Record(JsonElement value, int minimum, int maximum)
    {
        var result = new string?[Array(value, minimum, maximum).GetArrayLength()];
        int index = 0;
        foreach (JsonElement item in value.EnumerateArray()) result[index++] = item.ValueKind == JsonValueKind.Null ? null : Text(item);
        return result[0] is not null && result[^1] is not null ? result : throw Malformed();
    }

    private static string? At(string?[] record, int index) => index < record.Length ? record[index] : null;

    private static string Code(string value) => value.Length == 3 && value.AsSpan().IndexOfAnyExceptInRange('A', 'Z') < 0 ? value : throw Malformed();

    // A unit identifier: lowercase letters and digits in hyphen-separated parts, at most 64 characters.
    private static string Unit(string value)
    {
        if (value.Length is < 1 or > 64 || value[0] == '-' || value[^1] == '-' || value.Contains("--", StringComparison.Ordinal)) throw Malformed();
        foreach (char item in value) if (item is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')) throw Malformed();
        return value;
    }

    private static string NumberingSystem(string value)
    {
        if (value.Length is < 4 or > 8) throw Malformed();
        foreach (char item in value) if (item is not (>= 'a' and <= 'z')) throw Malformed();
        return value;
    }

    // A CLDR data ID: language[-Script][-REGION][-variant…].
    private static string DataId(string value)
    {
        string[] parts = value.Split('-');
        if (value.Length > 64 || parts[0].Length is < 2 or > 3 || !Lower(parts[0])) throw Malformed();
        int index = 1;
        if (index < parts.Length && parts[index].Length == 4 && char.IsAsciiLetterUpper(parts[index][0]) && Lower(parts[index][1..])) index++;
        if (index < parts.Length && (parts[index].Length == 2 && char.IsAsciiLetterUpper(parts[index][0]) && char.IsAsciiLetterUpper(parts[index][1]) ||
            parts[index].Length == 3 && parts[index].AsSpan().IndexOfAnyExceptInRange('0', '9') < 0)) index++;
        for (; index < parts.Length; index++)
            if (parts[index].Length is < 4 or > 8 || parts[index].AsSpan().IndexOfAnyExcept(DataIdVariant) >= 0) throw Malformed();
        return value;

        static bool Lower(string text) => text.Length > 0 && text.AsSpan().IndexOfAnyExceptInRange('a', 'z') < 0;
    }

    // A BCP 47-like tag of the CLDR resolution tables: ASCII letters and digits in hyphen-separated parts.
    private static string Tag(string value)
    {
        if (value.Length is < 2 or > 64) throw Malformed();
        foreach (string part in value.Split('-'))
            if (part.Length is < 1 or > 8 || part.AsSpan().IndexOfAnyExcept(TagCharacters) >= 0) throw Malformed();
        return value;
    }

    private static void Separator(string? value) { if (value is { Length: > 8 }) throw Malformed(); }

    // {0} and {1} exactly as often as given; a required pattern must be present.
    private static void Placeholders(string? pattern, int zero, int one, bool required)
    {
        if (pattern is null) { if (required) throw Malformed(); return; }
        if (Count(pattern, "{0}") != zero || Count(pattern, "{1}") != one) throw Malformed();
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    // A currency number pattern: one or two subpatterns, each with balanced quotes, one unquoted ¤ and one digit run
    // (#,##0.00: groups of # and 0, the last ending in 0, at most one decimal point).
    private static void CurrencyPattern(string? pattern)
    {
        if (pattern is null) throw Malformed();
        foreach (string subpattern in Subpatterns(pattern))
        {
            int currency = 0, first = -1, last = -1;
            bool quoted = false;
            for (int index = 0; index < subpattern.Length; index++)
            {
                char item = subpattern[index];
                if (item == '\'') { quoted = !quoted; continue; }
                if (quoted) continue;
                if (item == '¤') currency++;
                else if (item is '#' or '0' or ',' or '.') { if (first < 0) first = index; else if (last != index - 1) throw Malformed(); last = index; }
            }
            if (currency != 1 || first < 0) throw Malformed();
            string[] parts = subpattern[first..(last + 1)].Split('.');
            if (parts.Length > 2) throw Malformed();
            foreach (string group in parts[0].Split(',')) if (group.Length == 0 || group.AsSpan().IndexOfAnyExcept("#0") >= 0) throw Malformed();
            if (!parts[0].EndsWith('0') || parts.Length == 2 && parts[1].AsSpan().IndexOfAnyExcept("#0") >= 0) throw Malformed();
        }
    }

    // A compact pattern of 10^magnitude: "0", or one or two subpatterns with balanced quotes and at most one unquoted run
    // of zeros, which has at most magnitude − 2 zeros so the divisor is at least 10³.
    private static void CompactPattern(string pattern, int magnitude)
    {
        if (pattern == "0") return;
        foreach (string subpattern in Subpatterns(pattern))
        {
            int runs = 0, zeros = 0;
            bool quoted = false;
            for (int index = 0; index < subpattern.Length; index++)
            {
                char item = subpattern[index];
                if (item == '\'') { quoted = !quoted; continue; }
                if (quoted || item != '0') continue;
                if (index == 0 || subpattern[index - 1] != '0') runs++;
                zeros++;
            }
            if (runs > 1 || zeros > magnitude - 2) throw Malformed();
        }
    }

    // The subpatterns at an unquoted ';' (one or two), each with balanced quotes.
    private static string[] Subpatterns(string pattern)
    {
        int quotes = 0, split = -1;
        for (int index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] == '\'') quotes++;
            else if (pattern[index] == ';' && quotes % 2 == 0) { if (split >= 0) throw Malformed(); split = index; }
        }
        if (quotes % 2 != 0) throw Malformed();
        string[] result = split < 0 ? [pattern] : [pattern[..split], pattern[(split + 1)..]];
        foreach (string item in result) if (Count(item, "'") % 2 != 0) throw Malformed();
        return result;
    }

    private static TranslationPackException Malformed() => TranslationPackFailure.Create("The embedded CLDR format data is malformed.", TranslationPackFailureReason.Malformed);

    /// <summary>The CLDR fraction digits of an embedded currency, or null when the data does not embed it.</summary>
    internal int? CurrencyDigits(string code) => _currencyDigits.TryGetValue(code, out int digits) ? digits : null;

    internal bool IsDynamicCurrency(string code) => _dynamicCurrencies.Contains(code);
    internal bool IsDynamicUnit(string unit) => _dynamicUnits.Contains(unit);

    internal string?[]? CurrencyFormats(string id, string system) => Find(id, system, static (entry, key) => entry.CurrencyFormats.GetValueOrDefault(key));
    internal string?[]? Currency(string id, string code) => Find(id, code, static (entry, key) => entry.Currencies.GetValueOrDefault(key));
    internal string?[]?[]? Unit(string id, string unit) => Find(id, unit, static (entry, key) => entry.Units.GetValueOrDefault(key));
    internal string? Per(string id, string width) => Find(id, width, static (entry, key) => entry.Per.GetValueOrDefault(key));
    internal string?[][]? Compact(string id, string system, string style) =>
        Find(id, (system, style), static (entry, key) => entry.Compact.GetValueOrDefault(key.system)?.GetValueOrDefault(key.style));

    /// <summary>A parsed number pattern of this data.</summary>
    internal Rmf2CldrFormats.Pattern Pattern(string pattern) => _patterns.GetOrAdd(pattern, Rmf2CldrFormats.ParsePattern);

    /// <summary>
    /// The number format of a formatting locale from this data's number records (its own minimumGroupingDigits included),
    /// or null when the records do not cover the locale.
    /// </summary>
    internal Rmf2NumberFormat? NumberFormat(string locale)
    {
        if (_numberRecords.Count == 0) return null;
        if (_localeNumberFormats.TryGetValue(locale, out Rmf2NumberFormat? cached)) return cached;
        Rmf2NumberFormat? format = null;
        if (GeneratedLocaleData.Canonical(locale) is { } canonical)
        {
            string id = CldrLocales.DataId(Rmf2LocalizedFormats.WithoutExtensions(canonical));
            if (FindId(id, _numberRecords) is { } numbers)
            {
                bool latin = Rmf2LocalizedFormats.LatinDigits(canonical);
                int grouping = FindId(id, _grouping) is { } groupingId ? _grouping[groupingId] : GeneratedLocaleData.MinimumGroupingDigits(locale);
                format = _numberFormats.GetOrAdd(numbers + (latin ? "|latn|" : "||") + grouping.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    static (_, state) => Rmf2NumberFormat.FromRecord(state.Record, state.Latin, state.Grouping), (Record: _numberRecords[numbers], Latin: latin, Grouping: grouping));
            }
        }
        // The requested tags seen last, emptied when full, as GeneratedLocaleData caches them.
        if (_localeNumberFormats.Count >= MaximumCachedLocales) _localeNumberFormats.Clear();
        _localeNumberFormats[locale] = format;
        return format;
    }

    private static string? FindId<T>(string id, Dictionary<string, T> table)
    {
        for (string? current = id; current is not null; current = CldrLocales.Parent(current, truncate: false))
            if (table.ContainsKey(current)) return current;
        return null;
    }

    // The first entry along CLDR's parent chain of a data ID, as the extractor stored it.
    private T? Find<T, TKey>(string id, TKey key, Func<Entry, TKey, T?> select) where T : class
    {
        for (string? current = id; current is not null; current = CldrLocales.Parent(current, truncate: false))
            if (_locales.TryGetValue(current, out Entry? entry) && select(entry, key) is { } value) return value;
        return null;
    }
}
