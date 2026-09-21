using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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
            ("string" or "runic:boolean", "select") => text == "exact",
            ("integer" or "number", "select") => text is "exact" or "plural" or "ordinal",
            ("integer", "useGrouping") => text is "always" or "never",
            ("number", "style") => text is "decimal" or "percent",
            ("date" or "time" or "datetime", "style") => text is "iso" or "short" or "long",
            ("runic:uuid", "style") => text is "d" or "n" or "b" or "p",
            ("runic:relative-time", "unit") => text is "second" or "minute" or "hour" or "day" or "week" or "month" or "year",
            ("runic:relative-time", "numeric") => text is "always" or "auto",
            _ => false,
        };
        if (!valid) throw new ArgumentException("Invalid option '" + name + "' for '" + function + "'.", nameof(value));
    }
    internal string Format(TextArgument value, string locale)
    {
        string style = String("style", "iso");
        switch (Function)
        {
            case "string": return Get<string>(value);
            case "runic:boolean": return Get<bool>(value) ? "true" : "false";
            case "integer": return String("useGrouping", "never") == "always"
                ? Get<long>(value).ToString("N0", CultureInfo.GetCultureInfo(locale))
                : Get<long>(value).ToString(CultureInfo.InvariantCulture);
            case "number":
                bool percent = String("style", "decimal") == "percent";
                int minimum = MinimumDigits, maximum = MaximumDigits;
                // Round the unscaled decimal explicitly. The custom percent formatter
                // scales its decimal digit buffer, so decimal.MaxValue cannot overflow.
                decimal rounded = decimal.Round(Number(value), maximum + (percent ? 2 : 0), MidpointRounding.AwayFromZero);
                string pattern = "0" + (maximum == 0 ? "" : "." + new string('0', minimum) + new string('#', maximum - minimum)) + (percent ? "%" : "");
                return rounded.ToString(pattern, CultureInfo.GetCultureInfo(locale));
            case "date": return Get<DateOnly>(value).ToString(style == "iso" ? "yyyy-MM-dd" : style == "short" ? "d" : "D", style == "iso" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(locale));
            case "time": return Get<TimeOnly>(value).ToString(style == "iso" ? "HH:mm:ss.FFFFFFF" : style == "short" ? "t" : "T", style == "iso" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(locale));
            case "datetime":
                DateTimeOffset instant = Get<DateTimeOffset>(value).ToUniversalTime();
                return instant.ToString(style == "iso" ? "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" : style == "short" ? "g" : "F", style == "iso" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(locale));
            case "runic:uuid": return Get<Guid>(value).ToString(String("style", "d"), CultureInfo.InvariantCulture);
            case "runic:relative-time": return TextRelativeTimeFormatter.Format(Rmf2RuntimeDecimal.Parse(Rmf2RuntimeDecimal.Canonical(Number(value))), String("unit", "day"), String("numeric", "always"), locale);
            default: throw new TranslationFormatException("Unknown v5 formatter.");
        }
    }
    internal static T Get<T>(TextArgument value) => value.TryGetValue(out T? result) ? result! : throw new TranslationFormatException("Argument does not contain its declared carrier.");
    internal static decimal Number(TextArgument value) => value.Type == TextArgumentType.Int ? Get<long>(value) : Get<decimal>(value);
    internal static string Canonical(TextArgument value) => value.Type switch
    {
        TextArgumentType.String => Get<string>(value), TextArgumentType.Int => Get<long>(value).ToString(CultureInfo.InvariantCulture),
        TextArgumentType.Number => Rmf2RuntimeDecimal.Canonical(Get<decimal>(value)), TextArgumentType.Bool => Get<bool>(value) ? "true" : "false",
        TextArgumentType.Date => Get<DateOnly>(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TextArgumentType.Time => Get<TimeOnly>(value).ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        TextArgumentType.DateTime => Get<DateTimeOffset>(value).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture),
        TextArgumentType.Guid => Get<Guid>(value).ToString("D", CultureInfo.InvariantCulture), _ => throw new TranslationFormatException("Invalid v5 carrier."),
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
    internal static string Format(CompiledRmf2Message message, ReadOnlySpan<TextArgument> arguments, string locale, int maximum)
    {
        if (message.HasMarkup) throw new TranslationFormatException("Structured localized content must be requested through FormatContent.");
        LocalizedTextContent content = FormatContent(message, arguments, locale, maximum);
        var builder = new StringBuilder();
        foreach (var node in content.Nodes.Span) builder.Append(node.Value);
        return builder.ToString();
    }
    internal static LocalizedTextContent FormatContent(CompiledRmf2Message message, ReadOnlySpan<TextArgument> arguments, string locale, int maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        locale = message.ContentLocale ?? locale;
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
                    string text = expression is null ? node.Value : expression.Format.Format(expression.Carrier, locale);
                    AddLength(text.Length);
                    nodes.Add(new(LocalizedTextContentNodeKind.Text, text, null, node.Expression?.AnnotationArray));
                }
            }
            return new(nodes, locale);
            void AddLength(int count)
            { if (count > maximum - length) throw new TranslationFormatException("Formatted text exceeds the configured output limit."); length += count; }
        }
        catch (TranslationFormatException) { throw; }
        catch (ArgumentException exception) { throw new TranslationFormatException("Invalid resolved v5 formatter option or locale.", exception); }
        catch (OverflowException exception) { throw new TranslationFormatException("V5 numeric value cannot be formatted.", exception); }
        catch (FormatException exception) { throw new TranslationFormatException("V5 value cannot be formatted.", exception); }
    }
    private static CompiledRmf2Variant Select(CompiledRmf2Message message, Context context, string locale)
    {
        var values = new TextArgument[message.SelectorArray.Length];
        var categories = new string?[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            var selector = message.SelectorArray[index]; values[index] = context.Resolve(selector.Value, selector.Type).Carrier;
            if (selector.Function != "exact")
            {
                if (GeneratedLocaleData.FindPlural(locale.Split('-')[0].ToLowerInvariant()) is null) throw new TranslationFormatException("V5 plural selection is not supported for locale '" + locale + "'.");
                categories[index] = TextMessageSelector.SelectPlural(Rmf2ResolvedFormat.Number(values[index]), locale, selector.Function == "ordinal");
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
                    ranks[index] = key.Canonical is not null ? key.Canonical == Rmf2ResolvedFormat.Canonical(values[index]) ? 2 : -1 : key.Value == categories[index] ? 1 : -1;
                else ranks[index] = string.Equals(key.Value.Normalize(NormalizationForm.FormC), Rmf2ResolvedFormat.Canonical(values[index]).Normalize(NormalizationForm.FormC), StringComparison.Ordinal) ? 1 : -1;
                if (ranks[index] < 0) { matches = false; break; }
            }
            if (matches && (bestRanks is null || Compare(ranks, bestRanks) > 0)) { best = variant; bestRanks = ranks; }
        }
        return best ?? throw new TranslationFormatException("V5 selection has no matching variant.");
    }
    private static int Compare(int[] left, int[] right)
    { for (int index = 0; index < left.Length; index++) if (left[index] != right[index]) return left[index].CompareTo(right[index]); return 0; }
}
