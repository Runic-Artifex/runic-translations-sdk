using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Runic.Translations;

/// <summary>
/// The pinned CLDR currency, unit and compact-notation data that the compiler embeds with a set of messages (a .NET
/// catalog locale or a locale artifact): only the currencies, units and compact styles they use, for the CLDR locales of
/// their languages (D055.4). The ESM runtime reads the same selection (<c>generatedLocaleData.formats</c>).
/// </summary>
/// <remarks>
/// <c>{"currencies": {code: fraction digits}, "dynamicCurrencies": [...], "dynamicUnits": [...], "locales": {data ID:
/// {"currencyFormats", "currencies", "units", "per", "compact"}}}</c>. Each locale stores what differs from its CLDR
/// parent chain, so a lookup walks the chain of the formatting locale's data ID to the first locale with the entry.
/// The record layouts are those of <c>specs/translations/cldr/runic-formats-48.2.json</c>.
/// </remarks>
internal sealed class Rmf2FormatData
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private readonly Dictionary<string, int> _currencyDigits = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dynamicCurrencies = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dynamicUnits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _locales = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        internal Dictionary<string, string?[]> CurrencyFormats { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string?[]> Currencies { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string?[]?[]> Units { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string> Per { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, Dictionary<string, string?[][]>> Compact { get; } = new(StringComparer.Ordinal);
    }

    private Rmf2FormatData() { }

    /// <summary>Reads the compiler's embedded JSON.</summary>
    /// <exception cref="TranslationPackException">The data is not in the compiler's form.</exception>
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

    /// <summary>Reads the <c>formatData</c> member of a locale artifact.</summary>
    /// <exception cref="TranslationPackException">The data is not in the compiler's form.</exception>
    internal static Rmf2FormatData Parse(JsonElement root)
    {
        try
        {
            var data = new Rmf2FormatData();
            foreach (JsonProperty member in root.EnumerateObject())
            {
                switch (member.Name)
                {
                    case "currencies":
                        foreach (JsonProperty currency in member.Value.EnumerateObject()) data._currencyDigits.Add(Code(currency.Name), currency.Value.GetInt32() is >= 0 and <= 6 and var digits ? digits : throw Malformed());
                        break;
                    case "dynamicCurrencies": foreach (JsonElement item in member.Value.EnumerateArray()) data._dynamicCurrencies.Add(Code(item.GetString() ?? throw Malformed())); break;
                    case "dynamicUnits": foreach (JsonElement item in member.Value.EnumerateArray()) data._dynamicUnits.Add(item.GetString() ?? throw Malformed()); break;
                    case "locales": foreach (JsonProperty locale in member.Value.EnumerateObject()) data._locales.Add(locale.Name, ReadEntry(locale.Value)); break;
                    default: throw Malformed();
                }
            }
            return data;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException) { throw Malformed(); }

        static string Code(string value) => value.Length == 3 && value.AsSpan().IndexOfAnyExceptInRange('A', 'Z') < 0 ? value : throw Malformed();
    }

    private static Entry ReadEntry(JsonElement value)
    {
        var entry = new Entry();
        foreach (JsonProperty section in value.EnumerateObject())
            foreach (JsonProperty member in section.Value.EnumerateObject())
                switch (section.Name)
                {
                    case "currencyFormats": entry.CurrencyFormats.Add(member.Name, Slots(member.Value)); break;
                    case "currencies": entry.Currencies.Add(member.Name, Slots(member.Value)); break;
                    case "units":
                    {
                        var widths = new string?[]?[member.Value.GetArrayLength()];
                        int index = 0;
                        foreach (JsonElement width in member.Value.EnumerateArray()) widths[index++] = width.ValueKind == JsonValueKind.Null ? null : Slots(width);
                        entry.Units.Add(member.Name, widths);
                        break;
                    }
                    case "per": entry.Per.Add(member.Name, member.Value.GetString() ?? throw Malformed()); break;
                    case "compact":
                    {
                        var styles = new Dictionary<string, string?[][]>(StringComparer.Ordinal);
                        foreach (JsonProperty style in member.Value.EnumerateObject())
                        {
                            var magnitudes = new string?[style.Value.GetArrayLength()][];
                            int index = 0;
                            foreach (JsonElement record in style.Value.EnumerateArray()) magnitudes[index++] = Slots(record);
                            styles.Add(style.Name, magnitudes);
                        }
                        entry.Compact.Add(member.Name, styles);
                        break;
                    }
                    default: throw Malformed();
                }
        return entry;
    }

    // A record of optional strings whose first value is required.
    private static string?[] Slots(JsonElement value)
    {
        var result = new string?[value.GetArrayLength()];
        int index = 0;
        foreach (JsonElement item in value.EnumerateArray()) result[index++] = item.ValueKind == JsonValueKind.Null ? null : item.GetString();
        return result.Length > 0 && result[0] is not null ? result : throw Malformed();
    }

    private static TranslationPackException Malformed() => TranslationPackFailure.Create("The embedded CLDR format data is malformed.", TranslationPackFailureReason.Malformed);

    /// <summary>The CLDR fraction digits of an embedded currency, or null when the catalog did not embed it.</summary>
    internal int? CurrencyDigits(string code) => _currencyDigits.TryGetValue(code, out int digits) ? digits : null;

    internal bool IsDynamicCurrency(string code) => _dynamicCurrencies.Contains(code);
    internal bool IsDynamicUnit(string unit) => _dynamicUnits.Contains(unit);

    internal string?[]? CurrencyFormats(string id, string system) => Find(id, entry => entry.CurrencyFormats.GetValueOrDefault(system));
    internal string?[]? Currency(string id, string code) => Find(id, entry => entry.Currencies.GetValueOrDefault(code));
    internal string?[]?[]? Unit(string id, string unit) => Find(id, entry => entry.Units.GetValueOrDefault(unit));
    internal string? Per(string id, string width) => Find(id, entry => entry.Per.GetValueOrDefault(width));
    internal string?[][]? Compact(string id, string system, string style) => Find(id, entry => entry.Compact.GetValueOrDefault(system)?.GetValueOrDefault(style));

    // The first entry along CLDR's parent chain of a data ID, as the extractor stored it.
    private T? Find<T>(string id, Func<Entry, T?> select) where T : class
    {
        for (string? current = id; current is not null; current = CldrLocales.Parent(current, truncate: false))
            if (_locales.TryGetValue(current, out Entry? entry) && select(entry) is { } value) return value;
        return null;
    }
}

/// <summary>
/// <c>:currency</c>, <c>:unit</c> and compact notation from the embedded pinned CLDR data, with the number formats of
/// <see cref="Rmf2NumberFormats"/>. The ESM runtime's <c>formatCurrency</c>, <c>formatUnit</c> and <c>formatCompact</c>
/// apply the same steps, which follow ICU (and so <c>Intl.NumberFormat</c>) except where the spec notes say otherwise.
/// </summary>
internal static class Rmf2CldrFormats
{
    private const string NoBreakSpace = " ";
    // Parsed patterns, bounded by the pinned data.
    private static readonly ConcurrentDictionary<string, Pattern> s_patterns = new(StringComparer.Ordinal);
    private static readonly string[] Slots = ["other", "zero", "one", "two", "few", "many"];

    /// <summary>A number pattern's subpatterns: affixes (with <c>¤</c> kept) and group sizes; an implicit negative puts <c>-</c> first.</summary>
    private sealed record Pattern(Affixes Positive, Affixes Negative);
    private sealed record Affixes(string Prefix, string Suffix, int Primary, int Secondary);

    private static Pattern Parse(string pattern) => s_patterns.GetOrAdd(pattern, static text =>
    {
        int split = Unquoted(text, ';');
        Affixes positive = Subpattern(split < 0 ? text : text[..split]);
        Affixes negative = split < 0 ? positive with { Prefix = "-" + positive.Prefix } : Subpattern(text[(split + 1)..]);
        return new(positive, negative);
    });

    private static Affixes Subpattern(string pattern)
    {
        int first = -1, last = -1;
        bool quoted = false;
        for (int index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] == '\'') { quoted = !quoted; continue; }
            if (!quoted && pattern[index] is '#' or '0' or ',' or '.') { if (first < 0) first = index; last = index; }
        }
        string body = pattern[first..(last + 1)];
        string[] groups = body.Split('.')[0].Split(',');
        int primary = groups.Length > 1 ? groups[^1].Length : 0, secondary = groups.Length > 2 ? groups[^2].Length : primary;
        return new(pattern[..first], pattern[(last + 1)..], primary, secondary);
    }

    private static int Unquoted(string text, char value)
    {
        bool quoted = false;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\'') quoted = !quoted;
            else if (!quoted && text[index] == value) return index;
        }
        return -1;
    }

    // An affix with its quotes removed ('' is a quote), '-' as the locale's minus sign and '%' as its percent sign; '¤'
    // becomes the currency.
    private static string Affix(string affix, string minus, string? currency)
    {
        var builder = new StringBuilder(affix.Length + 8);
        for (int index = 0; index < affix.Length; index++)
        {
            char item = affix[index];
            if (item == '\'')
            {
                int end = affix.IndexOf('\'', index + 1);
                if (end == index + 1) builder.Append('\'');
                else builder.Append(affix, index + 1, end - index - 1);
                index = end;
            }
            else if (item == '-') builder.Append(minus);
            else if (item == '¤' && currency is not null) builder.Append(currency);
            else builder.Append(item);
        }
        return builder.ToString();
    }

    private static string DataId(string locale) => CldrLocales.DataId(Rmf2LocalizedFormats.WithoutExtensions(locale));

    private static int Slot(string category) => Array.IndexOf(Slots, category);

    // The plural category of a visible number in the formatting locale, other without rules.
    private static string Category(VisibleDecimal visible, string locale)
    {
        PluralOperands operands = visible.Operands;
        return GeneratedLocaleData.SelectPlural(locale, ordinal: false, in operands) ?? "other";
    }

    // A plural slot of a record that starts with other: the category's pattern, else other's.
    private static string Plural(string?[] record, int offset, string category)
    {
        int index = offset + Slot(category);
        return (index < record.Length ? record[index] : null) ?? record[offset]!;
    }

    private static Rmf2FormatData Data(Rmf2FormatData? data) =>
        data ?? throw new TranslationFormatException("The message has no embedded CLDR currency, unit or compact data; build it with the Runic compiler.");

    /// <summary>
    /// <c>:currency</c>: the amount rounded to <paramref name="fractionDigits"/> (the currency's CLDR digits by default), in
    /// the locale's currency pattern (standard or accounting) with the symbol, narrow symbol or ISO code and CLDR's currency
    /// spacing, or with the plural display name in the locale's currency unit pattern.
    /// </summary>
    internal static string Currency(decimal value, string code, string display, string sign, string grouping, int? fractionDigits, string locale, Rmf2FormatData? embedded, bool dynamic)
    {
        Rmf2FormatData data = Data(embedded);
        if (dynamic && !data.IsDynamicCurrency(code))
            throw new TranslationFormatException("Currency '" + code + "' is not in the catalog's runic.json formats.currencies.");
        int digits = fractionDigits ?? data.CurrencyDigits(code) ?? throw new TranslationFormatException("The catalog does not embed currency '" + code + "'.");
        VisibleDecimal visible = VisibleDecimal.FromNumber(value, false, digits, digits);
        Rmf2NumberFormat format = Rmf2NumberFormat.For(locale);
        string id = DataId(locale);
        string?[] formats = data.CurrencyFormats(id, format.System) ?? throw new TranslationFormatException("No currency formats for locale '" + locale + "'.");
        string?[] currency = data.Currency(id, code) ?? throw new TranslationFormatException("No data for currency '" + code + "' in locale '" + locale + "'.");
        // [symbol, narrow, names other … many, pattern, decimal, group]; formats [standard, accounting, unit patterns other … many, decimal, group].
        string decimalSeparator = At(currency, 9) ?? At(formats, 8) ?? format.Decimal, groupSeparator = At(currency, 10) ?? At(formats, 9) ?? format.Group;
        if (display == "name")
        {
            string number = Rmf2NumberFormats.Digits(visible, grouping, locale, format, format.Primary, format.Secondary, decimalSeparator, groupSeparator);
            if (visible.Negative) number = format.Minus + number;
            string category = Category(visible, locale);
            return Substitute(Plural(formats, 2, category), number, Plural(currency, 2, category));
        }
        string symbol = display switch { "code" => code, "narrowSymbol" => currency[1] ?? currency[0]!, _ => currency[0]! };
        // A currency's own pattern (CLDR <currency><pattern>) replaces the standard pattern for that currency.
        Pattern pattern = Parse(sign == "accounting" ? formats[1]! : At(currency, 8) ?? formats[0]!);
        Affixes affixes = visible.Negative ? pattern.Negative : pattern.Positive;
        string digitsText = Rmf2NumberFormats.Digits(visible, grouping, locale, format, affixes.Primary, affixes.Secondary, decimalSeparator, groupSeparator);
        string prefix = Affix(affixes.Prefix, format.Minus, null), suffix = Affix(affixes.Suffix, format.Minus, null);
        // CLDR currency spacing (root's in every locale): a no-break space between a digit and a symbol whose adjacent
        // character is neither a symbol (S) nor a separator (Z).
        if (prefix.EndsWith('¤') && Spaced(symbol, last: true) && StartsWithDigit(digitsText, first: true)) prefix += NoBreakSpace;
        if (suffix.StartsWith('¤') && Spaced(symbol, last: false) && StartsWithDigit(digitsText, first: false)) suffix = NoBreakSpace + suffix;
        return prefix.Replace("¤", symbol, StringComparison.Ordinal) + digitsText + suffix.Replace("¤", symbol, StringComparison.Ordinal);
    }

    private static string? At(string?[] record, int index) => index < record.Length ? record[index] : null;

    private static bool Spaced(string symbol, bool last)
    {
        if (symbol.Length == 0) return false;
        Rune rune = last ? Rune.GetRuneAt(symbol, char.IsLowSurrogate(symbol[^1]) && symbol.Length > 1 ? symbol.Length - 2 : symbol.Length - 1) : Rune.GetRuneAt(symbol, 0);
        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        return category is not (UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol
            or UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);
    }

    private static bool StartsWithDigit(string text, bool first)
    {
        if (text.Length == 0) return false;
        Rune rune = first ? Rune.GetRuneAt(text, 0) : Rune.GetRuneAt(text, char.IsLowSurrogate(text[^1]) && text.Length > 1 ? text.Length - 2 : text.Length - 1);
        return Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber;
    }

    // {0} and {1} in one pass, so a value never has its own braces replaced.
    private static string Substitute(string pattern, string zero, string one)
    {
        var builder = new StringBuilder(pattern.Length + zero.Length + one.Length);
        for (int index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] == '{' && index + 2 < pattern.Length && pattern[index + 2] == '}' && pattern[index + 1] is '0' or '1')
            { builder.Append(pattern[index + 1] == '0' ? zero : one); index += 2; }
            else builder.Append(pattern[index]);
        }
        return builder.ToString();
    }

    /// <summary>
    /// <c>:unit</c>: the number as <c>:number</c> formats it, in the unit's CLDR pattern for its plural category and
    /// width (narrow falls back to short, short to long). An <c>X-per-Y</c> unit without its own CLDR entry uses Y's per
    /// pattern, else the locale's per pattern with Y's singular name. <c>percent</c> in short and narrow widths uses the
    /// locale's percent pattern without scaling, as ICU does.
    /// </summary>
    internal static string Unit(decimal value, string unit, string display, string grouping, int minimumFractionDigits, int maximumFractionDigits, string locale, Rmf2FormatData? embedded, bool dynamic)
    {
        Rmf2FormatData data = Data(embedded);
        if (dynamic && !data.IsDynamicUnit(unit))
            throw new TranslationFormatException("Unit '" + unit + "' is not in the catalog's runic.json formats.units.");
        VisibleDecimal visible = VisibleDecimal.FromNumber(value, false, minimumFractionDigits, maximumFractionDigits);
        if (unit == "percent" && display != "long") return Rmf2NumberFormats.Percent(visible, grouping, locale);
        string number = Rmf2NumberFormats.Decimal(visible, grouping, locale), id = DataId(locale), category = Category(visible, locale);
        int width = display switch { "long" => 0, "short" => 1, _ => 2 };
        if (data.Unit(id, unit) is { } own) return Substitute(Plural(Width(own, width), 0, category), number, "");
        int split = unit.IndexOf("-per-", StringComparison.Ordinal);
        string?[] numerator = Width(data.Unit(id, split > 0 ? unit[..split] : unit) ?? throw Missing(unit, locale), width);
        string?[] denominator = Width(data.Unit(id, unit[(split + 5)..]) ?? throw Missing(unit, locale), width);
        string text = Substitute(Plural(numerator, 0, category), number, "");
        if (At(denominator, 6) is { } perUnit) return Substitute(perUnit, text, "");
        string single = Substitute(Plural(denominator, 0, "one"), "", "").Trim();
        return Substitute(data.Per(id, display) ?? throw Missing(unit, locale), text, single);

        static TranslationFormatException Missing(string unit, string locale) => new("The catalog does not embed unit '" + unit + "' for locale '" + locale + "'.");
    }

    // A width of a unit record, or the nearest wider one that CLDR does not repeat.
    private static string?[] Width(string?[]?[] record, int width)
    {
        for (int index = Math.Min(width, record.Length - 1); index >= 0; index--) if (record[index] is { } value) return value;
        throw new TranslationFormatException("A unit record has no long width.");
    }

    /// <summary>
    /// <c>:number runic:notation=compact</c>: the number scaled by the CLDR compact pattern of its magnitude and rounded,
    /// by default to whole numbers with at least two significant digits (1.2K, 12K, 123K), or to the fraction digit
    /// options when given; a value that rounds up to the next magnitude takes that magnitude's pattern (999999 → 1M).
    /// The pattern is chosen by the plural category of the scaled number; <c>useGrouping=auto</c> groups as <c>min2</c>.
    /// </summary>
    internal static string Compact(decimal value, string display, string grouping, int? minimumFractionDigits, int? maximumFractionDigits, string locale, Rmf2FormatData? embedded)
    {
        Rmf2FormatData data = Data(embedded);
        Rmf2NumberFormat format = Rmf2NumberFormat.For(locale);
        string?[][] table = data.Compact(DataId(locale), format.System, display) ?? throw new TranslationFormatException("No compact patterns for locale '" + locale + "'.");
        VisibleDecimal exact = VisibleDecimal.Canonical(value);
        UInt128 coefficient = exact.Coefficient;
        int scale = exact.Scale, magnitude = Magnitude(coefficient, scale);
        int exponent = Multiplier(table, magnitude);
        VisibleDecimal rounded = Round(coefficient, scale + exponent, exact.Negative, minimumFractionDigits, maximumFractionDigits);
        if (rounded.Coefficient != 0 && Magnitude(rounded.Coefficient, rounded.Scale) + exponent != magnitude)
        {
            int next = Multiplier(table, magnitude + 1);
            if (next != exponent) { exponent = next; rounded = Round(coefficient, scale + exponent, exact.Negative, minimumFractionDigits, maximumFractionDigits); }
        }
        int shown = rounded.Coefficient == 0 ? 0 : Magnitude(rounded.Coefficient, rounded.Scale) + exponent;
        string number = Rmf2NumberFormats.Digits(rounded, grouping == "auto" ? "min2" : grouping, locale, format, format.Primary, format.Secondary, format.Decimal, format.Group);
        string sign = rounded.Negative ? format.Minus : "";
        if (exponent == 0 && shown < 3) return sign + number;
        string?[] record = table[Math.Min(shown, table.Length + 2) - 3];
        string pattern = Plural(record, 0, Category(rounded, locale));
        if (rounded.Coefficient == 1 && rounded.Scale == 0 && At(record, 6) is { } one) pattern = one;
        if (pattern == "0") return sign + number;
        int split = Unquoted(pattern, ';');
        if (rounded.Negative && split >= 0) return Zeros(pattern[(split + 1)..], number, format.Minus);
        return sign + Zeros(split < 0 ? pattern : pattern[..split], number, format.Minus);
    }

    // The pattern with its run of zeros replaced by the number; a pattern without zeros spells the number out.
    private static string Zeros(string pattern, string number, string minus)
    {
        int start = Unquoted(pattern, '0');
        if (start < 0) return Affix(pattern, minus, null);
        int end = start;
        while (end < pattern.Length && pattern[end] == '0') end++;
        return Affix(pattern[..start], minus, null) + number + Affix(pattern[end..], minus, null);
    }

    // The power of ten of the leading digit of coefficient × 10^-scale (not zero).
    private static int Magnitude(UInt128 coefficient, int scale) => coefficient == 0 ? 0 : coefficient.ToString(CultureInfo.InvariantCulture).Length - scale - 1;

    // The power of ten a magnitude's compact pattern divides by: 0 below thousands or for a "0" pattern, else the magnitude
    // less the pattern's zeros after the first; magnitudes above the table use its largest pattern.
    private static int Multiplier(string?[][] table, int magnitude)
    {
        if (magnitude < 3) return 0;
        int capped = Math.Min(magnitude, table.Length + 2);
        string pattern = table[capped - 3][0]!;
        if (pattern == "0") return 0;
        int start = Unquoted(pattern, '0');
        if (start < 0) return capped;
        int zeros = 0;
        while (start + zeros < pattern.Length && pattern[start + zeros] == '0') zeros++;
        return capped - (zeros - 1);
    }

    // coefficient × 10^-scale rounded halfExpand: to the fraction digit options when given, else to whole numbers with at
    // least two significant digits, without trailing zeros (ICU's compact default).
    private static VisibleDecimal Round(UInt128 coefficient, int scale, bool negative, int? minimumFractionDigits, int? maximumFractionDigits)
    {
        if (scale < 0) { coefficient *= VisibleDecimal.Pow10(-scale); scale = 0; }
        int whole = coefficient == 0 ? 1 : coefficient.ToString(CultureInfo.InvariantCulture).Length - scale;
        int maximum = maximumFractionDigits ?? Math.Max(0, whole >= 2 ? 0 : 2 - whole), minimum = minimumFractionDigits ?? 0;
        if (minimumFractionDigits is not null && maximumFractionDigits is null) maximum = Math.Max(maximum, minimum);
        if (scale > maximum)
        {
            UInt128 divisor = VisibleDecimal.Pow10(scale - maximum), remainder = coefficient % divisor;
            coefficient /= divisor;
            if (remainder * 2 >= divisor) coefficient++;
            scale = maximum;
        }
        while (scale > minimum && coefficient % 10 == 0) { coefficient /= 10; scale--; }
        while (scale < minimum) { coefficient *= 10; scale++; }
        return VisibleDecimal.Create(negative, coefficient, scale);
    }
}
