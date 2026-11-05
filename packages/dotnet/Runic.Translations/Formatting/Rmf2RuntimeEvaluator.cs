using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Runic.Translations.Generated;

namespace Runic.Translations;

internal sealed class Rmf2ResolvedFormat
{
    private readonly Dictionary<string, TextArgument> _options;
    internal Rmf2ResolvedFormat(string function, Dictionary<string, TextArgument> options)
    {
        Function = function; _options = options;
        foreach (var option in options) ValidateOption(function, option.Key, option.Value);
        if (function == "number" && (MinimumDigits > MaximumDigits || MaximumDigits > (String("style", "decimal") == "percent" ? 4 : 6)))
            throw new ArgumentException("Number precision must satisfy 0 <= minimum <= maximum <= 6 (decimal) or 4 (percent).", nameof(options));
    }
    internal string Function { get; }
    /// <summary>The <c>u:dir</c> option (<c>ltr</c>, <c>rtl</c>, <c>auto</c> or <c>inherit</c>), or null.</summary>
    internal string? Direction => _options.TryGetValue("u:dir", out var value) ? Get<string>(value) : null;
    private int MinimumDigits => Integer("minimumFractionDigits", 0);
    private int MaximumDigits => Integer("maximumFractionDigits", String("style", "decimal") == "percent" ? 4 : 6);
    private string String(string name, string fallback) => _options.TryGetValue(name, out var value) ? Get<string>(value) : fallback;
    private int Integer(string name, int fallback) => _options.TryGetValue(name, out var value) ? checked((int)Get<long>(value)) : fallback;
    internal static void ValidateOption(string function, string name, TextArgument value)
    {
        if (value.Type != Rmf2RuntimeValidation.OptionType(function, name)) throw new ArgumentException("Option has the wrong carrier.", nameof(value));
        if (name is "minimumFractionDigits" or "maximumFractionDigits")
        {
            if (Get<long>(value) is < 0 or > 6) throw new ArgumentException("Fraction digit option is outside 0..6.", nameof(value));
            return;
        }
        string text = Get<string>(value);
        bool valid = (function, name) switch
        {
            (_, "u:dir") => text is "ltr" or "rtl" or "auto" or "inherit",
            ("string" or "runic:boolean", "select") => text == "exact",
            ("integer" or "number", "select") => text is "exact" or "plural" or "ordinal",
            ("integer" or "number", "useGrouping") => text is "auto" or "always" or "never" or "min2",
            ("number", "style") => text is "decimal" or "percent",
            ("date" or "time" or "datetime", "style") => text is "iso" or "short" or "medium" or "long" or "full",
            ("datetime", "timeZone") => Rmf2LocalizedFormats.IsTimeZone(text),
            ("runic:uuid", "style") => text is "d" or "n" or "b" or "p",
            ("runic:relative-time", "unit") => text is "second" or "minute" or "hour" or "day" or "week" or "month" or "year",
            ("runic:relative-time", "numeric") => text is "always" or "auto",
            ("runic:list", "type") => text is "conjunction" or "disjunction" or "unit",
            ("runic:list", "style") => text is "long" or "short" or "narrow",
            _ => false,
        };
        if (!valid) throw new ArgumentException("Invalid option '" + name + "' for '" + function + "'.", nameof(value));
    }
    /// <summary>
    /// Formats in <paramref name="locale"/>, the formatting locale for numbers, percentages, dates and times; lists and
    /// relative time use the plural and list data of <paramref name="contentLocale"/>.
    /// </summary>
    internal string Format(TextArgument value, string locale, string contentLocale)
    {
        string style = String("style", "iso");
        switch (Function)
        {
            case "string": return Get<string>(value);
            case "runic:boolean": return Get<bool>(value) ? "true" : "false";
            case "integer":
            {
                long integer = Get<long>(value);
                string grouping = String("useGrouping", "auto");
                // Ungrouped integers stay invariant; the grouping strategies use the locale's sign, separator and group sizes.
                if (grouping == "never") return integer.ToString(CultureInfo.InvariantCulture);
                CultureInfo culture = Rmf2LocalizedFormats.Culture(locale);
                int digits = integer == long.MinValue ? 19 : Math.Abs(integer).ToString(CultureInfo.InvariantCulture).Length;
                return integer.ToString(Groups(grouping, digits, locale, culture.NumberFormat) ? "#,0" : "0", culture);
            }
            case "number":
            {
                bool percent = String("style", "decimal") == "percent";
                int minimum = MinimumDigits, maximum = MaximumDigits;
                // Round the unscaled decimal explicitly. The custom percent formatter
                // scales its decimal digit buffer, so decimal.MaxValue cannot overflow.
                decimal rounded = decimal.Round(Number(value), maximum + (percent ? 2 : 0), MidpointRounding.AwayFromZero);
                CultureInfo culture = Rmf2LocalizedFormats.Culture(locale);
                VisibleDecimal visible = VisibleDecimal.FromNumber(Number(value), percent, minimum, maximum);
                int digits = Math.Max(1, visible.Coefficient.ToString(CultureInfo.InvariantCulture).Length - visible.Scale);
                string pattern = (Groups(String("useGrouping", "auto"), digits, locale, culture.NumberFormat) ? "#,0" : "0")
                    + (maximum == 0 ? "" : "." + new string('0', minimum) + new string('#', maximum - minimum)) + (percent ? "%" : "");
                if (!percent) return rounded.ToString(pattern, culture);
                // The scaled digits without a sign or percent sign, then the locale's percent pattern around them.
                var unsigned = (NumberFormatInfo)culture.NumberFormat.Clone();
                unsigned.PercentSymbol = string.Empty;
                return Rmf2LocalizedFormats.Percent(Math.Abs(rounded).ToString(pattern, unsigned), visible.Negative, culture.NumberFormat);
            }
            case "date": return style == "iso" ? Get<DateOnly>(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : Rmf2LocalizedFormats.Date(Get<DateOnly>(value), style, Rmf2LocalizedFormats.Culture(locale));
            case "time": return style == "iso" ? Get<TimeOnly>(value).ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture)
                : Rmf2LocalizedFormats.Time(Get<TimeOnly>(value), style, Rmf2LocalizedFormats.Culture(locale));
            case "datetime":
                // iso is always UTC; the localized styles use the timeZone option, the host's local zone by default.
                return style == "iso" ? Get<DateTimeOffset>(value).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture)
                    : Rmf2LocalizedFormats.DateTime(Get<DateTimeOffset>(value), style, String("timeZone", "local"), Rmf2LocalizedFormats.Culture(locale));
            case "runic:uuid": return Get<Guid>(value).ToString(String("style", "d"), CultureInfo.InvariantCulture);
            case "runic:list": return TextListFormatter.Format(Get<IReadOnlyList<string>>(value), String("type", "conjunction"), String("style", "long"), contentLocale);
            case "runic:relative-time": return TextRelativeTimeFormatter.Format(Rmf2RuntimeDecimal.Parse(Rmf2RuntimeDecimal.Canonical(Number(value))), String("unit", "day"), String("numeric", "always"), contentLocale);
            default: throw new TranslationFormatException("Unknown v5 formatter.");
        }
    }
    /// <summary>
    /// Whether <c>useGrouping</c> separates an integer part of <paramref name="digits"/> digits, as ICU and
    /// <c>Intl.NumberFormat</c> do: <c>auto</c> groups once the digits above the primary group reach the
    /// pinned CLDR <c>minimumGroupingDigits</c> of the content locale, <c>min2</c> requires at least two,
    /// <c>always</c> one. Group sizes and the separator come from platform globalization.
    /// </summary>
    internal static bool Groups(string grouping, int digits, string locale, NumberFormatInfo format)
    {
        if (grouping == "never") return false;
        int[] sizes = format.NumberGroupSizes;
        if (sizes.Length == 0 || sizes[0] <= 0 || format.NumberGroupSeparator.Length == 0) return false;
        int pinned = GeneratedLocaleData.MinimumGroupingDigits(locale);
        int minimum = grouping switch { "always" => 1, "min2" => Math.Max(2, pinned), _ => pinned };
        return digits - sizes[0] >= minimum;
    }
    /// <summary>
    /// The number that plural, ordinal and exact selection compare. <c>:number</c>
    /// selects on its displayed digits; <c>:integer</c> and relative time display
    /// the canonical typed value. The typed value itself is never changed.
    /// </summary>
    internal VisibleDecimal SelectionOperand(TextArgument value) => Function == "number"
        ? VisibleDecimal.FromNumber(Number(value), String("style", "decimal") == "percent", MinimumDigits, MaximumDigits)
        : value.Type == TextArgumentType.Int ? VisibleDecimal.FromInteger(Get<long>(value)) : VisibleDecimal.Canonical(Get<decimal>(value));
    internal static T Get<T>(TextArgument value) => value.TryGetValue(out T? result) ? result! : throw new TranslationFormatException("Argument does not contain its declared carrier.");
    internal static decimal Number(TextArgument value) => value.Type == TextArgumentType.Int ? Get<long>(value) : Get<decimal>(value);
    internal static string Canonical(TextArgument value) => value.Type switch
    {
        TextArgumentType.String => Get<string>(value), TextArgumentType.Int => Get<long>(value).ToString(CultureInfo.InvariantCulture),
        TextArgumentType.Number => Rmf2RuntimeDecimal.Canonical(Get<decimal>(value)), TextArgumentType.Bool => Get<bool>(value) ? "true" : "false",
        TextArgumentType.Date => Get<DateOnly>(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TextArgumentType.Time => Get<TimeOnly>(value).ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        TextArgumentType.DateTime => Get<DateTimeOffset>(value).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture),
        TextArgumentType.Guid => Get<Guid>(value).ToString("D", CultureInfo.InvariantCulture),
        // Lists never select or become option values; this only proves the carrier.
        TextArgumentType.List => string.Join(", ", Get<IReadOnlyList<string>>(value)), _ => throw new TranslationFormatException("Invalid v5 carrier."),
    };
}

internal static class Rmf2RuntimeEvaluator
{
    private sealed record Value(TextArgument Carrier, Rmf2ResolvedFormat Format);
    private sealed class Context
    {
        private readonly Dictionary<string, Value> _inputs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Value> _locals = new(StringComparer.Ordinal);
        internal Context(CompiledRmf2Message message, ReadOnlySpan<TextArgument> arguments)
        {
            if (arguments.Length != message.InputArray.Length) throw new TranslationFormatException("V5 caller argument count does not match its input contract.");
            foreach (var argument in arguments)
            {
                CompiledRmf2Input? expected = null;
                foreach (var input in message.InputArray) if (input.Name == argument.Name) { expected = input; break; }
                if (expected is null || expected.Type != argument.Type || _inputs.ContainsKey(expected.Name)) throw new TranslationFormatException("Unknown, duplicate or wrongly typed v5 caller argument.");
                _ = Rmf2ResolvedFormat.Canonical(argument); // Reject default/invalid carriers before declarations run.
                _inputs.Add(expected.Name, new(argument, new(Rmf2RuntimeValidation.DefaultFunction(argument.Type), new(StringComparer.Ordinal))));
            }
            foreach (var declaration in message.DeclarationArray)
            {
                Value value = Expression(declaration.Expression);
                if (declaration.Kind == "input") _inputs[declaration.Name] = value; else _locals.Add(declaration.Name, value);
            }
        }
        internal Value Resolve(CompiledRmf2Value value, TextArgumentType literalType) => value.Kind switch
        {
            "input" => _inputs[value.Value], "local" => _locals[value.Value],
            _ => new(Rmf2RuntimeValidation.Literal(value, literalType), new(Rmf2RuntimeValidation.DefaultFunction(literalType), new(StringComparer.Ordinal))),
        };
        internal Value Expression(CompiledRmf2Expression expression)
        {
            Value underlying = Resolve(expression.Operand, expression.ValueType);
            if (expression.Function is null) return underlying;
            var options = new Dictionary<string, TextArgument>(StringComparer.Ordinal);
            foreach (var option in expression.OptionArray) options.Add(option.Name, Resolve(option.Value, Rmf2RuntimeValidation.OptionType(expression.Function, option.Name)).Carrier);
            return new(underlying.Carrier, new(expression.Function, options));
        }
        internal string MarkupOption(CompiledRmf2Option option) => Rmf2ResolvedFormat.Canonical(Resolve(option.Value, option.Value.Kind == "number-literal" ? TextArgumentType.Number : TextArgumentType.String).Carrier);
    }
    /// <summary>
    /// Formats with <paramref name="locale"/> as the content locale unless the message has its own, and with
    /// <paramref name="requestedLocale"/> (by default <paramref name="locale"/>) as the requested locale that numbers and
    /// dates format in when it shares the content locale's language.
    /// </summary>
    internal static string Format(CompiledRmf2Message message, ReadOnlySpan<TextArgument> arguments, string locale, int maximum, string? requestedLocale = null,
        bool bidiIsolation = false)
    {
        if (message.HasMarkup) throw new TranslationFormatException("Structured localized content must be requested through FormatContent.");
        LocalizedTextContent content = FormatContent(message, arguments, locale, maximum, requestedLocale, bidiIsolation);
        var builder = new StringBuilder();
        foreach (var node in content.Nodes.Span) builder.Append(node.Value);
        return builder.ToString();
    }
    /// <remarks>
    /// With <paramref name="bidiIsolation"/>, every placeholder is isolated with MF2's default bidi strategy; without it, only
    /// placeholders with an explicit <c>u:dir</c> of <c>ltr</c>, <c>rtl</c> or <c>auto</c> are.
    /// </remarks>
    internal static LocalizedTextContent FormatContent(CompiledRmf2Message message, ReadOnlySpan<TextArgument> arguments, string locale, int maximum, string? requestedLocale = null,
        bool bidiIsolation = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        string requested = requestedLocale ?? locale;
        locale = message.ContentLocale ?? locale;
        string formatting = Rmf2LocalizedFormats.FormattingLocale(requested, locale);
        try
        {
            var context = new Context(message, arguments);
            CompiledRmf2Variant variant = Select(message, context, locale);
            var nodes = new List<LocalizedTextContentNode>(); int length = 0;
            foreach (var node in variant.NodeArray)
            {
                if (node.Kind == "markup")
                {
                    var options = new CompiledTextMarkupProperty[node.OptionArray.Length];
                    for (int index = 0; index < options.Length; index++)
                    {
                        string value = context.MarkupOption(node.OptionArray[index]);
                        AddLength(value.Length);
                        options[index] = new(node.OptionArray[index].Name, value);
                    }
                    nodes.Add(new(node.MarkupKind == "open" ? LocalizedTextContentNodeKind.ElementStart : node.MarkupKind == "close" ? LocalizedTextContentNodeKind.ElementEnd : LocalizedTextContentNodeKind.ElementStandalone,
                        node.Value, options, node.AnnotationArray));
                }
                else
                {
                    Value? expression = node.Expression is null ? null : context.Expression(node.Expression);
                    string text = expression is null ? node.Value
                        : Rmf2Bidi.Isolate(expression.Format.Format(expression.Carrier, formatting, locale), expression.Format, locale, formatting, bidiIsolation);
                    AddLength(text.Length);
                    nodes.Add(new(LocalizedTextContentNodeKind.Text, text, null, node.Expression?.AnnotationArray));
                }
            }
            return new(nodes, locale, formatting);
            void AddLength(int count)
            { if (count > maximum - length) throw new TranslationFormatException("Formatted text exceeds the configured output limit."); length += count; }
        }
        catch (TranslationFormatException) { throw; }
        catch (ArgumentException exception) { throw new TranslationFormatException("Invalid resolved v5 formatter option or locale.", exception); }
        catch (OverflowException exception) { throw new TranslationFormatException("V5 numeric value cannot be formatted.", exception); }
        catch (FormatException exception) { throw new TranslationFormatException("V5 value cannot be formatted.", exception); }
        catch (TimeZoneNotFoundException exception) { throw new TranslationFormatException(exception.Message, exception); }
        catch (InvalidTimeZoneException exception) { throw new TranslationFormatException("The time zone of a v5 date-time is invalid.", exception); }
    }
    private static CompiledRmf2Variant Select(CompiledRmf2Message message, Context context, string locale)
    {
        var values = new TextArgument[message.SelectorArray.Length];
        // Numeric selectors compare exact keys and CLDR categories with the same
        // visible decimal; string and boolean selectors compare the canonical value.
        var canonical = new string[values.Length];
        var categories = new string?[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            var selector = message.SelectorArray[index]; Value value = context.Resolve(selector.Value, selector.Type); values[index] = value.Carrier;
            if (values[index].Type is not (TextArgumentType.Int or TextArgumentType.Number)) { canonical[index] = Rmf2ResolvedFormat.Canonical(values[index]).Normalize(NormalizationForm.FormC); continue; }
            VisibleDecimal operand = value.Format.SelectionOperand(value.Carrier);
            canonical[index] = operand.ToCanonicalString();
            if (selector.Function != "exact")
            {
                PluralOperands operands = operand.Operands;
                categories[index] = GeneratedLocaleData.SelectPlural(locale, selector.Function == "ordinal", in operands)
                    ?? throw new TranslationFormatException("V5 plural selection is not supported for locale '" + locale + "'.");
            }
        }
        CompiledRmf2Variant? best = null; int[]? bestRanks = null;
        foreach (var variant in message.VariantArray)
        {
            var ranks = new int[values.Length]; bool matches = true;
            for (int index = 0; index < ranks.Length; index++)
            {
                var key = variant.KeyArray[index];
                if (key.Value is null) ranks[index] = 0;
                else if (values[index].Type is TextArgumentType.Int or TextArgumentType.Number)
                    ranks[index] = key.Canonical is not null ? key.Canonical == canonical[index] ? 2 : -1 : key.Value == categories[index] ? 1 : -1;
                else ranks[index] = string.Equals(key.Value.Normalize(NormalizationForm.FormC), canonical[index], StringComparison.Ordinal) ? 1 : -1;
                if (ranks[index] < 0) { matches = false; break; }
            }
            if (matches && (bestRanks is null || Compare(ranks, bestRanks) > 0)) { best = variant; bestRanks = ranks; }
        }
        return best ?? throw new TranslationFormatException("V5 selection has no matching variant.");
    }
    private static int Compare(int[] left, int[] right)
    { for (int index = 0; index < left.Length; index++) if (left[index] != right[index]) return left[index].CompareTo(right[index]); return 0; }
}

/// <summary>
/// Unicode MessageFormat 2's default bidi strategy (LDML 48 "Handling Bidirectional Text"): a placeholder whose direction is
/// left to right stays bare in a left-to-right message unless its <c>u:dir</c> asks for isolation, and is otherwise wrapped
/// in LRI…PDI; a right-to-left one in RLI…PDI; one of unknown direction in FSI…PDI. The ESM runtime implements the same.
/// </summary>
internal static class Rmf2Bidi
{
    internal const char LeftToRightIsolate = '\u2066', RightToLeftIsolate = '\u2067', FirstStrongIsolate = '\u2068', PopDirectionalIsolate = '\u2069';
    private enum Direction { Unknown, LeftToRight, RightToLeft }

    /// <param name="text">The formatted placeholder.</param>
    /// <param name="format">Its resolved format, with its function and <c>u:dir</c> option.</param>
    /// <param name="contentLocale">The locale the message is written in, which gives the message's direction.</param>
    /// <param name="formattingLocale">The locale numbers and dates format in.</param>
    /// <param name="isolation">The catalog's bidi isolation: without it only an explicit <c>u:dir</c> isolates.</param>
    internal static string Isolate(string text, Rmf2ResolvedFormat format, string contentLocale, string formattingLocale, bool isolation)
    {
        string? option = format.Direction;
        if (!isolation && option is null or "inherit") return text;
        bool messageRightToLeft = CldrLocales.IsRightToLeft(contentLocale);
        Direction direction = option switch
        {
            "ltr" => Direction.LeftToRight,
            "rtl" => Direction.RightToLeft,
            "auto" => Direction.Unknown,
            "inherit" => messageRightToLeft ? Direction.RightToLeft : Direction.LeftToRight,
            _ => Of(format.Function, contentLocale, formattingLocale),
        };
        bool explicitIsolation = option is "ltr" or "rtl" or "auto";
        return direction switch
        {
            Direction.LeftToRight when !messageRightToLeft && !explicitIsolation => text,
            Direction.LeftToRight => LeftToRightIsolate + text + PopDirectionalIsolate,
            Direction.RightToLeft => RightToLeftIsolate + text + PopDirectionalIsolate,
            _ => FirstStrongIsolate + text + PopDirectionalIsolate,
        };
    }

    // The direction a function's output has without u:dir: numbers, dates and times are written in their formatting locale
    // and relative times in the content locale; UUIDs and booleans are ASCII; text and lists of text come from the caller
    // and have no known direction.
    private static Direction Of(string function, string contentLocale, string formattingLocale) => function switch
    {
        "integer" or "number" or "date" or "time" or "datetime" => CldrLocales.IsRightToLeft(formattingLocale) ? Direction.RightToLeft : Direction.LeftToRight,
        "runic:relative-time" => CldrLocales.IsRightToLeft(contentLocale) ? Direction.RightToLeft : Direction.LeftToRight,
        "runic:uuid" or "runic:boolean" => Direction.LeftToRight,
        _ => Direction.Unknown,
    };
}
