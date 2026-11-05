using System;
using System.Globalization;
using System.Text;

namespace Runic.Translations;

/// <summary>
/// <c>:currency</c>, <c>:unit</c> and compact notation from the embedded pinned CLDR data, with the number formats of
/// <see cref="Rmf2NumberFormats"/>. The ESM runtime's <c>formatCurrency</c>, <c>formatUnit</c> and <c>formatCompact</c>
/// apply the same steps, which follow ICU (and so <c>Intl.NumberFormat</c>) except where the spec notes say otherwise.
/// </summary>
internal static class Rmf2CldrFormats
{
    private const string NoBreakSpace = " ";
    private static readonly string[] Slots = ["other", "zero", "one", "two", "few", "many"];

    /// <summary>A number pattern's subpatterns: affixes (with <c>¤</c> kept) and group sizes; an implicit negative puts <c>-</c> first.</summary>
    internal sealed record Pattern(Affixes Positive, Affixes Negative);
    internal sealed record Affixes(string Prefix, string Suffix, int Primary, int Secondary);

    // Rmf2FormatData caches the result per instance; its reader has checked the pattern (Rmf2FormatData.CurrencyPattern).
    internal static Pattern ParsePattern(string text)
    {
        int split = Unquoted(text, ';');
        Affixes positive = Subpattern(split < 0 ? text : text[..split]);
        Affixes negative = split < 0 ? positive with { Prefix = "-" + positive.Prefix } : Subpattern(text[(split + 1)..]);
        return new(positive, negative);
    }

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

    // The message's currency, unit and compact data: its own (an artifact's formatData), else the catalog's, which a pack
    // or overlay without any borrows when it is bound to the catalog.
    private static Rmf2FormatData Data(Rmf2FormatData? data) =>
        data is { HasFormatters: true } ? data : throw new TranslationFormatException("The message has no embedded CLDR currency, unit or compact data; build it with the Runic compiler.");

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
        Rmf2NumberFormat format = Rmf2NumberFormat.For(locale, embedded);
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
        Pattern pattern = data.Pattern(sign == "accounting" ? formats[1]! : At(currency, 8) ?? formats[0]!);
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
        if (unit == "percent" && display != "long") return Rmf2NumberFormats.Percent(visible, grouping, locale, embedded);
        string number = Rmf2NumberFormats.Decimal(visible, grouping, locale, embedded), id = DataId(locale), category = Category(visible, locale);
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
    /// by default to whole numbers with at least two significant digits (1.2K, 12K, 123K), to the fraction digit options
    /// when given (with only <paramref name="minimumFractionDigits"/>, to at most max(3, minimum) digits, as
    /// Intl.NumberFormat rounds); a value that rounds up to the next magnitude takes that magnitude's pattern (999999 →
    /// 1M). The pattern is chosen by the plural category of the scaled number; <c>useGrouping=auto</c> groups as <c>min2</c>.
    /// </summary>
    internal static string Compact(decimal value, string display, string grouping, int? minimumFractionDigits, int? maximumFractionDigits, string locale, Rmf2FormatData? embedded)
    {
        (VisibleDecimal rounded, int exponent, int shown, string?[][] table, Rmf2NumberFormat format) = CompactRound(value, display, minimumFractionDigits, maximumFractionDigits, locale, embedded);
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

    /// <summary>
    /// The number a compact <c>:number</c> selects on: the displayed (compact-rounded) value at full scale, 1.2K as 1200,
    /// with the compact exponent as CLDR's <c>e</c> (<c>c</c>) operand, as TR35 and ICU select a compact number.
    /// </summary>
    internal static VisibleDecimal CompactOperand(decimal value, string display, int? minimumFractionDigits, int? maximumFractionDigits, string locale, Rmf2FormatData? embedded, out int exponent)
    {
        (VisibleDecimal rounded, exponent, _, _, _) = CompactRound(value, display, minimumFractionDigits, maximumFractionDigits, locale, embedded);
        return exponent >= rounded.Scale
            ? VisibleDecimal.Create(rounded.Negative, rounded.Coefficient * VisibleDecimal.Pow10(exponent - rounded.Scale), 0)
            : VisibleDecimal.Create(rounded.Negative, rounded.Coefficient, rounded.Scale - exponent);
    }

    // The scaled and rounded value, the exponent it was divided by, the magnitude it displays and the compact table.
    private static (VisibleDecimal Rounded, int Exponent, int Shown, string?[][] Table, Rmf2NumberFormat Format) CompactRound(decimal value, string display,
        int? minimumFractionDigits, int? maximumFractionDigits, string locale, Rmf2FormatData? embedded)
    {
        Rmf2FormatData data = Data(embedded);
        Rmf2NumberFormat format = Rmf2NumberFormat.For(locale, embedded);
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
        return (rounded, exponent, shown, table, format);
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

    // coefficient × 10^-scale rounded halfExpand: to the fraction digit options when given (only a minimum: at most
    // max(3, minimum) digits, Intl.NumberFormat's maximum for decimals), else to whole numbers with at least two
    // significant digits, without trailing zeros (ICU's compact default).
    private static VisibleDecimal Round(UInt128 coefficient, int scale, bool negative, int? minimumFractionDigits, int? maximumFractionDigits)
    {
        if (scale < 0) { coefficient *= VisibleDecimal.Pow10(-scale); scale = 0; }
        int whole = coefficient == 0 ? 1 : coefficient.ToString(CultureInfo.InvariantCulture).Length - scale;
        int minimum = minimumFractionDigits ?? 0;
        int maximum = maximumFractionDigits ?? (minimumFractionDigits is { } given ? Math.Max(3, given) : Math.Max(0, whole >= 2 ? 0 : 2 - whole));
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
