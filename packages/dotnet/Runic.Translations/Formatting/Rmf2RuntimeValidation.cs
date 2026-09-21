using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Runic.Translations;

internal static class Rmf2RuntimeDecimal
{
    private static readonly Regex Number = new(@"\A(?<sign>-?)(?<whole>0|[1-9][0-9]*)(?:\.(?<fraction>[0-9]+))?(?:[eE](?<exponent>[+-]?[0-9]+))?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private const string MaximumCoefficient = "79228162514264337593543950335";
    internal static bool TryCanonicalize(string value, out string canonical)
    {
        canonical = string.Empty;
        if (value.Length > 4096) return false;
        Match match = Number.Match(value);
        if (!match.Success) return false;
        int exponent = 0;
        if (match.Groups["exponent"].Success && !int.TryParse(match.Groups["exponent"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) return false;
        string coefficient = (match.Groups["whole"].Value + match.Groups["fraction"].Value).TrimStart('0');
        if (coefficient.Length == 0) { canonical = "0"; return true; }
        long scale = (long)match.Groups["fraction"].Length - exponent;
        int trailing = coefficient.Length;
        while (trailing > 0 && coefficient[trailing - 1] == '0') { trailing--; scale--; }
        coefficient = coefficient.Substring(0, trailing);
        if (scale < 0)
        {
            if (coefficient.Length - scale > MaximumCoefficient.Length) return false;
            coefficient += new string('0', (int)-scale); scale = 0;
        }
        if (scale > 28 || coefficient.Length > MaximumCoefficient.Length ||
            (coefficient.Length == MaximumCoefficient.Length && string.CompareOrdinal(coefficient, MaximumCoefficient) > 0)) return false;
        canonical = scale == 0 ? coefficient : scale >= coefficient.Length
            ? "0." + new string('0', (int)scale - coefficient.Length) + coefficient
            : coefficient.Insert(coefficient.Length - (int)scale, ".");
        if (match.Groups["sign"].Length != 0) canonical = "-" + canonical;
        return true;
    }
    internal static decimal Parse(string canonical) => decimal.Parse(canonical, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    internal static string Canonical(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
}

internal static class Rmf2RuntimeValidation
{
    internal static void Name(string name, bool qualified = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!name.IsNormalized(NormalizationForm.FormC)) throw new ArgumentException("V5 identities must be NFC names.", nameof(name));
        bool first = true, colon = false;
        foreach (Rune rune in name.EnumerateRunes())
        {
            int value = rune.Value;
            if (value == ':' && qualified && !colon && !first) { colon = true; first = true; continue; }
            if (!NameStart(value) && (first || value is not (>= '0' and <= '9' or '-' or '.')))
                throw new ArgumentException("Invalid v5 identity.", nameof(name));
            first = false;
        }
        if (first) throw new ArgumentException("Invalid qualified v5 identity.", nameof(name));
    }
    private static bool NameStart(int value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '+' or '_' or
        >= 0xA1 and <= 0x61B or >= 0x61D and <= 0x167F or >= 0x1681 and <= 0x1FFF or >= 0x200B and <= 0x200D or
        >= 0x2010 and <= 0x2027 or >= 0x2030 and <= 0x205E or >= 0x2060 and <= 0x2065 or >= 0x206A and <= 0x2FFF or
        >= 0x3001 and <= 0xD7FF or >= 0xE000 and <= 0xFDCF or >= 0xFDF0 and <= 0xFFFD ||
        (value >= 0x10000 && value <= 0x10FFFD && (value & 0xFFFF) <= 0xFFFD);
    internal static void Type(TextArgumentType type)
    { if (!Enum.IsDefined(type)) throw new ArgumentException("Unknown v5 carrier type.", nameof(type)); }
    internal static T[] Copy<T>(IReadOnlyList<T>? values, int maximum) where T : class
    {
        if (values is null) return [];
        if (values.Count > maximum) throw new ArgumentException("Collection exceeds the v5 runtime limit.", nameof(values));
        var copy = new T[values.Count];
        for (int index = 0; index < copy.Length; index++) copy[index] = values[index] ?? throw new ArgumentException("Null v5 model entry.", nameof(values));
        return copy;
    }
    internal static void UniqueOptions(CompiledRmf2Option[] options)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in options) if (!names.Add(option.Name)) throw new ArgumentException("Duplicate v5 option.", nameof(options));
    }
    internal static void UniqueAnnotations(CompiledRmf2Annotation[] annotations)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var annotation in annotations) if (!names.Add(annotation.Name)) throw new ArgumentException("Duplicate v5 annotation.", nameof(annotations));
    }
    internal static string DefaultFunction(TextArgumentType type) => type switch
    {
        TextArgumentType.String => "string", TextArgumentType.Int => "integer", TextArgumentType.Number => "number",
        TextArgumentType.Bool => "runic:boolean", TextArgumentType.Date => "date", TextArgumentType.Time => "time",
        TextArgumentType.DateTime => "datetime", TextArgumentType.Guid => "runic:uuid", _ => throw new ArgumentException("Invalid carrier type.", nameof(type)),
    };
    internal static string Selection(string function) => function is "integer" or "number" or "runic:relative-time" ? "plural" : function is "string" or "runic:boolean" ? "exact" : "none";
    internal static TextArgumentType OptionType(string function, string name) => (function, name) switch
    {
        ("string" or "runic:boolean" or "integer" or "number", "select") => TextArgumentType.String,
        ("integer", "useGrouping") => TextArgumentType.String,
        ("number" or "date" or "time" or "datetime" or "runic:uuid", "style") => TextArgumentType.String,
        ("number", "minimumFractionDigits" or "maximumFractionDigits") => TextArgumentType.Int,
        ("runic:relative-time", "unit" or "numeric") => TextArgumentType.String,
        _ => throw new ArgumentException("Unknown option '" + name + "' for '" + function + "'.", nameof(name)),
    };
    internal static void Function(CompiledRmf2Expression expression)
    {
        string? function = expression.Function;
        if (function is null && expression.OptionArray.Length != 0) throw new ArgumentException("Options require an explicit function.", nameof(expression));
        if (function is not null && function != DefaultFunction(expression.ValueType) &&
            !(expression.ValueType is TextArgumentType.Int or TextArgumentType.Number && function is "number" or "runic:relative-time"))
            throw new ArgumentException("Function is incompatible with the underlying carrier.", nameof(expression));
        if (expression.Operand.Kind is "string-literal" or "number-literal")
        {
            TextArgumentType bound = function is null ? expression.Operand.Kind == "number-literal" ? TextArgumentType.Number : TextArgumentType.String :
                function is "number" or "runic:relative-time" ? TextArgumentType.Number : expression.ValueType;
            if (bound != expression.ValueType) throw new ArgumentException("Literal binding disagrees with its carrier.", nameof(expression));
            _ = Literal(expression.Operand, bound);
        }
        var resolved = new Dictionary<string, TextArgument>(StringComparer.Ordinal);
        bool dynamic = false;
        foreach (var option in expression.OptionArray)
        {
            TextArgumentType type = OptionType(function!, option.Name);
            if (option.Value.Kind is "input" or "local")
            {
                if (option.Name == "select") throw new ArgumentException("Select is literal-only.", nameof(expression));
                dynamic = true;
            }
            else
            {
                TextArgument value = Literal(option.Value, type);
                Rmf2ResolvedFormat.ValidateOption(function!, option.Name, value);
                resolved.Add(option.Name, value);
            }
        }
        if (!dynamic && function is not null) _ = new Rmf2ResolvedFormat(function, resolved);
        if (dynamic && function == "number")
        {
            long? min = ReadDigits(resolved, "minimumFractionDigits", 0);
            long? max = ReadDigits(resolved, "maximumFractionDigits", HasOption(expression, "style") ? null : 6);
            bool percent = resolved.TryGetValue("style", out var style) && style.TryGetValue(out string? text) && text == "percent";
            if (min > max || (percent && (min > 4 || max > 4))) throw new ArgumentException("Invalid static precision constraints.", nameof(expression));
        }
    }
    private static bool HasOption(CompiledRmf2Expression expression, string name) => Array.Exists(expression.OptionArray, option => option.Name == name);
    private static long? ReadDigits(Dictionary<string, TextArgument> values, string name, long? fallback) => values.TryGetValue(name, out var value) && value.TryGetValue(out long digits) ? digits : fallback;
    internal static TextArgument Literal(CompiledRmf2Value value, TextArgumentType type)
    {
        if (value.Kind == "number-literal")
        {
            if (type == TextArgumentType.Number) return new TextArgument("_", Rmf2RuntimeDecimal.Parse(value.Canonical!));
            if (type == TextArgumentType.Int && long.TryParse(value.Canonical, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer)) return new TextArgument("_", integer);
        }
        else if (value.Kind == "string-literal")
        {
            string text = value.Value;
            if (type == TextArgumentType.String) return new TextArgument("_", text);
            if (type == TextArgumentType.Bool && text is "true" or "false") return new TextArgument("_", text == "true");
            if (type == TextArgumentType.Date && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return new TextArgument("_", date);
            if (type == TextArgumentType.Time && TimeOnly.TryParseExact(text, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return new TextArgument("_", time);
            if (type == TextArgumentType.DateTime && DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant)) return new TextArgument("_", instant);
            if (type == TextArgumentType.Guid && text.Length == 36 && Guid.TryParseExact(text, "D", out var guid)) return new TextArgument("_", guid);
        }
        throw new ArgumentException("Literal does not have its declared v5 carrier.", nameof(value));
    }

    private sealed record Symbol(TextArgumentType Type, string Selection, bool ExplicitDependencies);
    internal static void Message(CompiledRmf2Message message)
    {
        var inputs = new Dictionary<string, Symbol>(StringComparer.Ordinal);
        var locals = new Dictionary<string, Symbol>(StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var explicitInputs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in message.DeclarationArray) if (declaration.Kind == "input") explicitInputs.Add(declaration.Name);
        string? previous = null;
        foreach (var input in message.InputArray)
        {
            if (previous is not null && string.CompareOrdinal(previous, input.Name) >= 0) throw new ArgumentException("V5 inputs must be unique and ordinally sorted.", nameof(message));
            inputs.Add(input.Name, new(input.Type, Selection(DefaultFunction(input.Type)), explicitInputs.Contains(input.Name))); previous = input.Name;
        }
        // Input signatures and selection annotations have whole-message scope,
        // just as in semantic lowering. Only locals require prior declarations.
        // Do not resolve options here: their local dependencies remain ordered.
        foreach (var declaration in message.DeclarationArray)
        {
            if (declaration.Kind != "input") continue;
            if (declaration.Expression.Operand.Kind != "input" || declaration.Expression.Operand.Value != declaration.Name ||
                !inputs.TryGetValue(declaration.Name, out Symbol? input) || declaration.Expression.ValueType != input.Type)
                throw new ArgumentException("Input declaration must annotate its own typed caller input.", nameof(message));
            string selection = declaration.Expression.Function is null ? input.Selection : Selection(declaration.Expression.Function);
            foreach (var option in declaration.Expression.OptionArray) if (option.Name == "select") selection = option.Value.Value;
            inputs[declaration.Name] = input with { Selection = selection };
        }
        foreach (var declaration in message.DeclarationArray)
        {
            if (!declared.Add(declaration.Name)) throw new ArgumentException("Duplicate v5 declaration.", nameof(message));
            if (declaration.Kind == "input" && (declaration.Expression.Operand.Kind != "input" || declaration.Expression.Operand.Value != declaration.Name || !inputs.ContainsKey(declaration.Name)))
                throw new ArgumentException("Input declaration must annotate its own caller input.", nameof(message));
            if (declaration.Kind == "local" && inputs.ContainsKey(declaration.Name)) throw new ArgumentException("Local shadows caller input.", nameof(message));
            Symbol symbol = Expression(declaration.Expression);
            if (declaration.Kind == "input") inputs[declaration.Name] = symbol; else locals.Add(declaration.Name, symbol);
        }
        var selectorNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selector in message.SelectorArray)
        {
            Symbol symbol = Resolve(selector.Value);
            if (!selectorNames.Add(selector.Value.Value) || symbol.Type != selector.Type || symbol.Selection != selector.Function)
                throw new ArgumentException("Inconsistent or duplicate v5 selector.", nameof(message));
        }
        if (message.VariantArray.Length == 0 || (message.SelectorArray.Length == 0 && message.VariantArray.Length != 1)) throw new ArgumentException("Invalid v5 variant count.", nameof(message));
        bool fallback = false;
        var vectors = new List<string[]>();
        foreach (var variant in message.VariantArray)
        {
            if (variant.KeyArray.Length != message.SelectorArray.Length) throw new ArgumentException("Invalid v5 key count.", nameof(message));
            var vector = new string[variant.KeyArray.Length]; bool all = true;
            for (int index = 0; index < vector.Length; index++)
            {
                var key = variant.KeyArray[index]; var selector = message.SelectorArray[index];
                if (key.Value is null) { vector[index] = "*"; continue; }
                all = false;
                if (selector.Type is TextArgumentType.Int or TextArgumentType.Number)
                {
                    bool numeric = Rmf2RuntimeDecimal.TryCanonicalize(key.Value, out string canonical);
                    if (numeric && (canonical != key.Canonical || (selector.Type == TextArgumentType.Int && !long.TryParse(canonical, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))))
                        throw new ArgumentException("Invalid exact numeric key.", nameof(message));
                    if (!numeric && (key.Canonical is not null || selector.Function == "exact" || key.Value is not ("zero" or "one" or "two" or "few" or "many" or "other")))
                        throw new ArgumentException("Invalid numeric category key.", nameof(message));
                }
                else if (key.Canonical is not null || (selector.Type == TextArgumentType.Bool && key.Value is not ("true" or "false"))) throw new ArgumentException("Invalid nonnumeric key.", nameof(message));
                vector[index] = "=" + (key.Canonical ?? key.Value.Normalize(NormalizationForm.FormC));
            }
            foreach (string[] other in vectors) if (vector.AsSpan().SequenceEqual(other)) throw new ArgumentException("Duplicate normalized key vector.", nameof(message));
            vectors.Add(vector); fallback |= all;
            var stack = new Stack<string>();
            foreach (var node in variant.NodeArray)
            {
                if (node.Expression is not null) _ = Expression(node.Expression);
                if (node.Kind != "markup") continue;
                message.HasMarkup = true;
                if (node.MarkupKind == "open") stack.Push(node.Value);
                if (node.MarkupKind == "close" && (stack.Count == 0 || stack.Pop() != node.Value)) throw new ArgumentException("Unbalanced linked markup.", nameof(message));
                foreach (var option in node.OptionArray) if (option.Value.Kind is "input" or "local") _ = Resolve(option.Value);
            }
            if (stack.Count != 0) throw new ArgumentException("Unbalanced linked markup.", nameof(message));
        }
        if (!fallback) throw new ArgumentException("V5 matchers require all-wildcard fallback.", nameof(message));

        Symbol Resolve(CompiledRmf2Value value)
        {
            var symbols = value.Kind == "input" ? inputs : locals;
            return symbols.TryGetValue(value.Value, out var symbol) ? symbol : throw new ArgumentException("Unbound or forward v5 reference '" + value.Value + "'.", nameof(message));
        }
        Symbol Expression(CompiledRmf2Expression expression)
        {
            Symbol inherited = expression.Operand.Kind is "input" or "local" ? Resolve(expression.Operand) : new(expression.ValueType, Selection(DefaultFunction(expression.ValueType)), true);
            if (inherited.Type != expression.ValueType) throw new ArgumentException("Expression carrier disagrees with its operand.", nameof(message));
            bool explicitDependencies = inherited.ExplicitDependencies;
            string selection = expression.Function is null ? inherited.Selection : Selection(expression.Function);
            foreach (var option in expression.OptionArray)
            {
                if (option.Name == "select") selection = option.Value.Value;
                if (option.Value.Kind is not ("input" or "local")) continue;
                Symbol dependency = Resolve(option.Value);
                if (dependency.Type != OptionType(expression.Function!, option.Name) || !dependency.ExplicitDependencies)
                    throw new ArgumentException("Dynamic option requires an explicitly declared typed dependency.", nameof(message));
                explicitDependencies &= dependency.ExplicitDependencies;
            }
            return new(inherited.Type, selection, explicitDependencies);
        }
    }
}
