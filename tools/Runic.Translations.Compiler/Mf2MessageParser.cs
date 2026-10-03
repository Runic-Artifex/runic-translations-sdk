using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Runic.Translations.Compiler;

internal sealed class Mf2ParsedMessage
{
    internal Mf2ParsedMessage(string pattern, CompiledMessagePattern message, PlaceholderModel[] placeholders)
    {
        Pattern = pattern;
        Message = message;
        Placeholders = placeholders;
    }

    internal string Pattern { get; }
    internal CompiledMessagePattern Message { get; }
    internal PlaceholderModel[] Placeholders { get; }
}

internal static partial class Mf2MessageParser
{
    private static readonly Regex Variable = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex Option = new Regex("([A-Za-z_][A-Za-z0-9_-]*)=([^\\s]+)", RegexOptions.CultureInvariant);

    internal static Mf2ParsedMessage? Parse(
        TranslationSource source,
        DiagnosticBag diagnostics,
        TranslationCompilerOptions options,
        CancellationToken cancellationToken, Mf2SyntaxDocument? sourceSyntax = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Bytes.Length > options.MaximumDocumentBytes)
        {
            Error(diagnostics, source, "RTR0022", "MF2 message exceeds the configured document-size limit.");
            return null;
        }

        string text;
        try
        {
            text = StrictJsonParser.StrictUtf8.GetString(source.Bytes);
        }
        catch (DecoderFallbackException)
        {
            Error(diagnostics, source, "RTR0019", "MF2 message is not valid UTF-8.");
            return null;
        }
        if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (StrictJsonParser.StrictUtf8.GetByteCount(text) > options.MaximumValueBytes)
            Error(diagnostics, source, "RTR0022", "MF2 message value exceeds the configured byte limit.");

        var syntax = sourceSyntax ?? Mf2SyntaxReader.Read(source, options, cancellationToken);
        foreach (var diagnostic in syntax.Diagnostics) diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, diagnostic.Location);
        if (!syntax.Success) return null;
        var model = Mf2SyntaxReader.ValidateDataModel(syntax);
        foreach (var diagnostic in model) diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, diagnostic.Location);
        if (model.Count > 0) return null;
        var profile = Mf2SyntaxReader.ValidateInlineProfile(syntax);
        foreach (var diagnostic in profile) diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, diagnostic.Location);
        if (profile.Count > 0) return null;
        ValidateRmf2Capabilities(syntax, source, diagnostics);
        return LowerRmf2(syntax, diagnostics, options);
    }

    // RMF2 has its own explicit execution profile. Never accept an option and silently
    // ignore it or clamp it into another meaning in the existing compact backends.
    private static void ValidateRmf2Capabilities(Mf2SyntaxDocument syntax, TranslationSource source, DiagnosticBag diagnostics)
    {
        foreach (var token in syntax.Tokens.Where(t => t.Kind == Mf2SyntaxTokenKind.Variable && !Variable.IsMatch(t.Value)))
            diagnostics.Add("RTR0065", TranslationDiagnosticSeverity.Error, "This backend requires ASCII caller and local identifiers.", token.Location);
        if (syntax.Match is { } match && match.Selectors.Distinct(StringComparer.Ordinal).Count() != match.Selectors.Count)
            diagnostics.Add("RTR0065", TranslationDiagnosticSeverity.Error, "Repeated selector identities are not executable in this profile.", match.Location);
        foreach (var variant in syntax.Variants.Where(v => v.Keys.Any(key => key == "|*|")))
            diagnostics.Add("RTR0065", TranslationDiagnosticSeverity.Error, "A quoted wildcard key is not executable in this profile.", variant.Location);
        foreach (var expression in syntax.Expressions)
        {
            if (expression.MarkupKind != Mf2MarkupKind.None)
            {
                if (expression.MarkupKind == Mf2MarkupKind.Close && (expression.Options.Count != 0 || expression.Attributes.Count != 0)) Unsupported("Closing-tag properties are not supported by the inline backend.");
                continue;
            }
            if (expression.Attributes.Count != 0) Unsupported("Expression annotations are not executable in this profile.");
            if (expression.Function is not string name) continue;
            if (expression.Operand?.Kind != Mf2OperandKind.Variable) Unsupported("Formatted literal and function-only expressions are not executable in this profile.");
            string allowed = FunctionOptions(name);
            if (allowed.Length == 0) { Unsupported("Unsupported execution function ':" + name + "'."); continue; }
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var option in expression.Options)
            {
                if (Array.IndexOf(allowed.Split(' '), option.Name) < 0 || option.Value is null || option.Value.Kind == Mf2OperandKind.Variable)
                    Unsupported("Unknown or dynamic function option '" + option.Name + "'.");
                else values.TryAdd(option.Name, option.Value.Value);
            }
            foreach (var option in values)
            {
                bool valid = option.Key switch {
                    "select" => option.Value is "plural" or "ordinal" or "exact",
                    "useGrouping" => option.Value is "always" or "never",
                    "style" => name switch {
                        "number" => option.Value is "decimal" or "percent",
                        "runic:uuid" => option.Value is "d" or "n" or "b" or "p",
                        _ => option.Value is "iso" or "short" or "long",
                    },
                    "minimumFractionDigits" or "maximumFractionDigits" => int.TryParse(option.Value, out int digits) && digits >= 0 && digits <= (values.GetValueOrDefault("style") == "percent" ? 4 : 6),
                    "unit" => option.Value is "second" or "minute" or "hour" or "day" or "week" or "month" or "year",
                    "numeric" => option.Value is "always" or "auto", _ => false,
                };
                if (!valid) Unsupported("Unsupported value for option '" + option.Key + "'.");
            }
            if (values.ContainsKey("minimumFractionDigits") || values.ContainsKey("maximumFractionDigits"))
                if (!values.TryGetValue("minimumFractionDigits", out string? min) || !values.TryGetValue("maximumFractionDigits", out string? max) || min != max)
                    Unsupported("This backend supports fixed precision only; minimumFractionDigits and maximumFractionDigits must both be present and equal.");
            void Unsupported(string message) => diagnostics.Add("RTR0065", TranslationDiagnosticSeverity.Error, message, expression.Location);
        }
    }
    internal static string FunctionOptions(string name) => name switch {
        "string" => "select", "integer" => "select useGrouping", "number" => "select style minimumFractionDigits maximumFractionDigits",
        "date" or "time" or "datetime" or "runic:uuid" => "style", "runic:boolean" => "select", "runic:relative-time" => "unit numeric", _ => "",
    };

    private static CompiledMessageNode? ParseExpression(
        string expression,
        Dictionary<string, Declaration> declarations,
        HashSet<string> usedInputs,
        TranslationSource source,
        DiagnosticBag diagnostics)
    {
        if (!expression.StartsWith('$'))
        {
            if ((expression.StartsWith('|') && expression.EndsWith('|')) ||
                decimal.TryParse(expression, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                return new CompiledMessageText(UnquoteLiteral(expression));
            Error(diagnostics, source, "RTR0065", "The selected execution backend does not support this MF2 operand/function combination.");
            return null;
        }
        int end = 1;
        while (end < expression.Length && (char.IsAsciiLetterOrDigit(expression[end]) || expression[end] == '_')) end++;
        string name = expression.Substring(1, end - 1);
        if (!Variable.IsMatch(name))
        {
            Error(diagnostics, source, "RTR0041", "MF2 expression contains an invalid variable name.");
            return null;
        }
        string tail = expression.Substring(end).Trim();
        Declaration declaration;
        if (declarations.TryGetValue(name, out var constant) && constant.Constant is not null)
        {
            if (tail.Length != 0) { Error(diagnostics, source, "RTR0065", "Formatting literal locals is not executable in this backend profile."); return null; }
            return new CompiledMessageText(constant.Constant);
        }
        if (tail.StartsWith(':'))
        {
            declaration = ParseFunction(name, ResolveDeclaration(name, declarations).Input, tail, source, diagnostics);
            if (declarations.TryGetValue(name, out Declaration? existing) &&
                (existing.Type != declaration.Type || existing.Format != declaration.Format))
                Error(diagnostics, source, "RTR0041", "MF2 variable '" + name + "' has conflicting format declarations.");
            else declarations[name] = declaration;
        }
        else declaration = ResolveDeclaration(name, declarations);
        usedInputs.Add(declaration.Input);
        if (declaration.Function == "string" && declaration.Format == "none")
            return new CompiledMessageInput(declaration.Input);
        return new CompiledMessageFormat(declaration.Input, declaration.Function, declaration.Format, declaration.Unit, declaration.Numeric);
    }

    private static Declaration ParseFunction(string name, string input, string tail, TranslationSource source, DiagnosticBag diagnostics)
    {
        int end = 1;
        while (end < tail.Length && !char.IsWhiteSpace(tail[end])) end++;
        string function = tail.Substring(1, end - 1).ToLowerInvariant();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Option.Matches(tail.Substring(end)))
            options[match.Groups[1].Value] = UnquoteLiteral(match.Groups[2].Value);

        TranslationArgumentType type;
        string format;
        string normalizedFunction = function;
        string? unit = null;
        string? numeric = null;
        switch (function)
        {
            case "string": type = TranslationArgumentType.String; format = "none"; break;
            case "integer": type = TranslationArgumentType.Int; format = OptionValue(options, "useGrouping") == "always" ? "grouped" : "plain"; break;
            case "number":
                type = TranslationArgumentType.Number;
                string? style = OptionValue(options, "style");
                string? digits = OptionValue(options, "maximumFractionDigits") ?? OptionValue(options, "minimumFractionDigits");
                format = style == "percent" ? "percent" + ClampDigits(digits, 4) : digits is null ? "plain" : "fixed" + ClampDigits(digits, 6);
                break;
            case "date": type = TranslationArgumentType.Date; format = OptionValue(options, "style") ?? "iso"; break;
            case "time": type = TranslationArgumentType.Time; format = OptionValue(options, "style") ?? "iso"; break;
            case "datetime": type = TranslationArgumentType.DateTime; format = OptionValue(options, "style") ?? "iso"; break;
            case "runic:uuid": type = TranslationArgumentType.Guid; format = OptionValue(options, "style") ?? "d"; normalizedFunction = "uuid"; break;
            case "runic:boolean": type = TranslationArgumentType.Boolean; format = "lower"; normalizedFunction = "boolean"; break;
            case "runic:relative-time":
                type = TranslationArgumentType.Number;
                format = "plain";
                normalizedFunction = "relativeTime";
                unit = OptionValue(options, "unit") ?? "day";
                numeric = OptionValue(options, "numeric") ?? "always";
                break;
            default:
                Error(diagnostics, source, "RTR0041", "Unsupported MF2 function ':" + function + "'.");
                type = TranslationArgumentType.String;
                format = "none";
                normalizedFunction = "string";
                break;
        }
        string? selector = OptionValue(options, "select") switch
        {
            "plural" => "plural",
            "ordinal" => "ordinal",
            "exact" => "exact",
            _ => null,
        };
        return new Declaration(name, input, type, format, normalizedFunction, selector, unit, numeric);
    }

    private static Declaration ResolveDeclaration(string name, Dictionary<string, Declaration> declarations) =>
        declarations.TryGetValue(name, out Declaration? declaration)
            ? declaration
            : Declaration.CreateInput(name, name, TranslationArgumentType.String, "none", null);

    private static bool HasInputDeclaration(Dictionary<string, Declaration> declarations, string input)
    {
        foreach (Declaration declaration in declarations.Values)
            if (string.Equals(declaration.Input, input, StringComparison.Ordinal)) return true;
        return false;
    }

    private static string? OptionValue(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out string? value) ? value : null;

    private static string ClampDigits(string? value, int maximum) =>
        int.TryParse(value, out int parsed) ? Math.Max(0, Math.Min(parsed, maximum)).ToString(System.Globalization.CultureInfo.InvariantCulture) : "0";

    private static string UnquoteLiteral(string value)
    {
        if (value.Length >= 2 && value[0] == '|' && value[value.Length - 1] == '|')
            return value.Substring(1, value.Length - 2).Replace("\\|", "|", StringComparison.Ordinal);
        return value;
    }

    private static void Error(DiagnosticBag diagnostics, TranslationSource source, string id, string message) =>
        diagnostics.Add(id, TranslationDiagnosticSeverity.Error, message, source, new ByteSpan(0, source.Bytes.Length));

    private sealed class Declaration
    {
        internal Declaration(string name, string input, TranslationArgumentType type, string format, string function,
            string? selector, string? unit, string? numeric)
        {
            Name = name;
            Input = input;
            Type = type;
            Format = format;
            Function = function;
            Selector = selector;
            Unit = unit;
            Numeric = numeric;
        }

        internal string? Constant { get; init; }
        internal string Name { get; }
        internal string Input { get; }
        internal TranslationArgumentType Type { get; }
        internal string Format { get; }
        internal string Function { get; }
        internal string? Selector { get; set; }
        internal string? Unit { get; }
        internal string? Numeric { get; }

        internal static Declaration CreateInput(string name, string input, TranslationArgumentType type, string format, string? selector) =>
            new Declaration(name, input, type, format, type == TranslationArgumentType.String ? "string" : "number", selector, null, null);
    }
}
