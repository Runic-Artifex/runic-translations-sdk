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
        CancellationToken cancellationToken, bool rmf2 = false, Mf2SyntaxDocument? sourceSyntax = null)
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

        if (rmf2)
        {
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
        var declarations = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        int offset = 0;
        while (TryReadLine(text, offset, out string line, out int next))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || (!rmf2 && trimmed.StartsWith("//", StringComparison.Ordinal)))
            {
                offset = next;
                continue;
            }
            if (trimmed.StartsWith(".input", StringComparison.Ordinal))
            {
                ReadInput(trimmed, declarations, source, diagnostics);
                offset = next;
                continue;
            }
            if (trimmed.StartsWith(".local", StringComparison.Ordinal))
            {
                ReadLocal(trimmed, declarations, source, diagnostics, rmf2);
                offset = next;
                continue;
            }
            break;
        }

        string body = text.Substring(Math.Min(offset, text.Length));
        if (!rmf2) body = body.Trim();
        if (body.Length == 0)
        {
            Error(diagnostics, source, "RTR0041", "MF2 message has no body.");
            return null;
        }

        if (rmf2)
            foreach (Declaration declaration in declarations.Values)
                if (declaration.Selector is null && declaration.Type is TranslationArgumentType.Int or TranslationArgumentType.Number)
                    declaration.Selector = "plural";
        var usedInputs = new HashSet<string>(StringComparer.Ordinal);
        CompiledMessagePattern? message;
        if (body.StartsWith(".match", StringComparison.Ordinal))
            message = ParseMatch(body, declarations, usedInputs, source, diagnostics, cancellationToken);
        else
        {
            string pattern = UnquotePattern(body, source, diagnostics);
            IReadOnlyList<CompiledMessageNode>? nodes = ParsePattern(pattern, declarations, usedInputs, source, diagnostics);
            message = nodes is null ? null : new CompiledMessagePattern(nodes);
        }
        if (message is null) return null;

        foreach (string name in usedInputs)
            if (!HasInputDeclaration(declarations, name))
                declarations.Add(name, Declaration.CreateInput(name, name, TranslationArgumentType.String, "none", null));

        var placeholders = new List<PlaceholderModel>();
        var included = new HashSet<string>(StringComparer.Ordinal);
        foreach (Declaration declaration in declarations.Values)
        {
            if (declaration.Constant is not null || (!rmf2 && !usedInputs.Contains(declaration.Input)) || !included.Add(declaration.Input)) continue;
            placeholders.Add(new PlaceholderModel(declaration.Input, declaration.Type, declaration.Format));
        }
        placeholders.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        if (placeholders.Count > options.MaximumPlaceholdersPerValue)
            Error(diagnostics, source, "RTR0022", "MF2 input count exceeds the configured limit.");

        return new Mf2ParsedMessage(text, message, placeholders.ToArray());
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

    private static CompiledMessagePattern? ParseMatch(
        string body,
        Dictionary<string, Declaration> declarations,
        HashSet<string> usedInputs,
        TranslationSource source,
        DiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        int lineEnd = body.IndexOf('\n');
        string matchLine = lineEnd < 0 ? body : body.Substring(0, lineEnd);
        string[] selectorTokens = matchLine.Substring(".match".Length).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (selectorTokens.Length == 0)
        {
            Error(diagnostics, source, "RTR0041", "MF2 .match requires at least one selector.");
            return null;
        }
        var selectors = new List<CompiledMessageSelector>(selectorTokens.Length);
        for (int index = 0; index < selectorTokens.Length; index++)
        {
            string name = selectorTokens[index].TrimStart('$');
            if (!Variable.IsMatch(name))
            {
                Error(diagnostics, source, "RTR0041", "MF2 selector names must be variables.");
                return null;
            }
            if (!selectorTokens[index].StartsWith('$') || selectors.Exists(s => s.Name == name))
            { Error(diagnostics, source, "RTR0041", "MF2 selectors must be distinct variables."); return null; }
            Declaration declaration = ResolveDeclaration(name, declarations);
            if (declaration.Constant is not null) { Error(diagnostics, source, "RTR0065", "Constant local selectors are not executable in this backend profile."); return null; }
            usedInputs.Add(declaration.Input);
            selectors.Add(new CompiledMessageSelector(name, declaration.Input, declaration.Selector ?? "exact"));
        }

        string variantsText = lineEnd < 0 ? string.Empty : body.Substring(lineEnd + 1);
        int position = 0;
        var variants = new List<CompiledMessageVariant>();
        while (position < variantsText.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SkipWhitespace(variantsText, ref position);
            if (position == variantsText.Length) break;
            int open = variantsText.IndexOf("{{", position, StringComparison.Ordinal);
            if (open < 0)
            {
                Error(diagnostics, source, "RTR0041", "MF2 variant is missing a quoted pattern.");
                return null;
            }
            string[] keys = variantsText.Substring(position, open - position).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (keys.Length != selectors.Count)
            {
                Error(diagnostics, source, "RTR0041", "MF2 variant key count must match the selector count.");
                return null;
            }
            int close = FindQuotedPatternEnd(variantsText, open + 2);
            if (close < 0)
            {
                Error(diagnostics, source, "RTR0041", "MF2 variant contains an unterminated quoted pattern.");
                return null;
            }
            string pattern = variantsText.Substring(open + 2, close - open - 2);
            IReadOnlyList<CompiledMessageNode>? nodes = ParsePattern(pattern, declarations, usedInputs, source, diagnostics);
            if (nodes is null) return null;
            var matches = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < keys.Length; index++)
                matches.Add(selectors[index].Name, UnquoteLiteral(keys[index]));
            variants.Add(new CompiledMessageVariant(matches, new CompiledMessagePattern(nodes)));
            position = close + 2;
        }
        if (variants.Count == 0)
        {
            Error(diagnostics, source, "RTR0041", "MF2 .match requires at least one variant.");
            return null;
        }
        bool hasFallback = false;
        for (int index = 0; index < variants.Count; index++)
        {
            bool fallback = true;
            foreach (string value in variants[index].Matches.Values) fallback &= value == "*";
            hasFallback |= fallback;
        }
        if (!hasFallback)
            Error(diagnostics, source, "RTR0041", "MF2 .match requires a catch-all '*' variant.");
        return new CompiledMessagePattern(Array.Empty<CompiledMessageNode>(), selectors.ToArray(), variants.ToArray());
    }

    private static CompiledMessageNode[]? ParsePattern(
        string pattern,
        Dictionary<string, Declaration> declarations,
        HashSet<string> usedInputs,
        TranslationSource source,
        DiagnosticBag diagnostics)
    {
        int position = 0;
        return ParseNodes(pattern, ref position, null, declarations, usedInputs, source, diagnostics);
    }

    private static CompiledMessageNode[]? ParseNodes(
        string pattern,
        ref int position,
        string? closingMarkup,
        Dictionary<string, Declaration> declarations,
        HashSet<string> usedInputs,
        TranslationSource source,
        DiagnosticBag diagnostics, int depth = 0)
    {
        if (depth > 64) { Error(diagnostics, source, "RTR0022", "MF2 markup depth exceeds 64 levels."); return null; }
        var nodes = new List<CompiledMessageNode>();
        var text = new StringBuilder();
        while (position < pattern.Length)
        {
            char value = pattern[position];
            if (value == '\\' && position + 1 < pattern.Length && pattern[position + 1] is '{' or '}' or '\\')
            {
                text.Append(pattern[position + 1]);
                position += 2;
                continue;
            }
            if (value != '{')
            {
                text.Append(value);
                position++;
                continue;
            }

            int close = FindExpressionEnd(pattern, position + 1);
            if (close < 0)
            {
                string original = StrictJsonParser.StrictUtf8.GetString(source.Bytes);
                int start = Math.Max(0, original.IndexOf(pattern, StringComparison.Ordinal)) + position;
                diagnostics.Add("RTR0041", TranslationDiagnosticSeverity.Error, "MF2 pattern contains an unterminated expression.", source,
                    new ByteSpan(StrictJsonParser.StrictUtf8.GetByteCount(original.AsSpan(0, start)), StrictJsonParser.StrictUtf8.GetByteCount(pattern.AsSpan(position))));
                return null;
            }
            Flush(nodes, text);
            string expression = pattern.Substring(position + 1, close - position - 1).Trim();
            position = close + 1;
            if (expression.StartsWith('/'))
            {
                string name = expression.Substring(1).Trim();
                if (!string.Equals(name, closingMarkup, StringComparison.Ordinal))
                {
                    Error(diagnostics, source, "RTR0041", "MF2 markup contains a mismatched closing tag.");
                    return null;
                }
                return nodes.ToArray();
            }
            if (expression.StartsWith('#'))
            {
                bool standalone = expression.EndsWith('/');
                string declaration = expression.Substring(1, expression.Length - 1 - (standalone ? 1 : 0)).Trim();
                int space = declaration.IndexOf(' ');
                int tab = declaration.IndexOf('\t');
                int separator = space < 0 ? tab : tab < 0 ? space : Math.Min(space, tab);
                string name = separator < 0 ? declaration : declaration.Substring(0, separator);
                if (!Rmf2MarkupRegistry.Name.IsMatch(name))
                {
                    Error(diagnostics, source, "RTR0041", "MF2 markup names must be identifiers.");
                    return null;
                }
                var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
                var annotations = new SortedDictionary<string, string>(StringComparer.Ordinal);
                var variableOptions = new HashSet<string>(StringComparer.Ordinal);
                string properties = separator < 0 ? string.Empty : declaration.Substring(separator + 1);
                if (!ReadMarkupProperties(properties, attributes, annotations, variableOptions, usedInputs, declarations, source, diagnostics)) return null;
                CompiledMessageNode[] children = Array.Empty<CompiledMessageNode>();
                if (!standalone)
                {
                    children = ParseNodes(pattern, ref position, name, declarations, usedInputs, source, diagnostics, depth + 1)!;
                    if (children is null) return null;
                }
                nodes.Add(new CompiledMessageMarkup(name, attributes, children) { Standalone = standalone, Annotations = annotations, VariableOptions = variableOptions });
                continue;
            }
            CompiledMessageNode? node = ParseExpression(expression, declarations, usedInputs, source, diagnostics);
            if (node is null) return null;
            nodes.Add(node);
        }
        if (closingMarkup is not null)
        {
            Error(diagnostics, source, "RTR0041", "MF2 markup tag '" + closingMarkup + "' is not closed.");
            return null;
        }
        Flush(nodes, text);
        return nodes.ToArray();
    }

    private static CompiledMessageNode? ParseExpression(
        string expression,
        Dictionary<string, Declaration> declarations,
        HashSet<string> usedInputs,
        TranslationSource source,
        DiagnosticBag diagnostics,
        bool resolveAliases = false)
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
            declaration = ParseFunction(name, resolveAliases ? ResolveDeclaration(name, declarations).Input : name, tail, source, diagnostics);
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

    private static void ReadInput(string line, Dictionary<string, Declaration> declarations, TranslationSource source, DiagnosticBag diagnostics)
    {
        int open = line.IndexOf('{');
        int close = line.LastIndexOf('}');
        if (open < 0 || close <= open)
        {
            Error(diagnostics, source, "RTR0041", "Invalid MF2 .input declaration.");
            return;
        }
        string expression = line.Substring(open + 1, close - open - 1).Trim();
        if (!TrySplitVariable(expression, out string name, out string tail))
        {
            Error(diagnostics, source, "RTR0041", "Invalid MF2 .input variable.");
            return;
        }
        Declaration declaration = tail.Length == 0
            ? Declaration.CreateInput(name, name, TranslationArgumentType.String, "none", null)
            : ParseFunction(name, name, tail, source, diagnostics);
        if (!declarations.TryAdd(name, declaration))
            Error(diagnostics, source, "RTR0041", "Duplicate MF2 declaration for '" + name + "'.");
    }

    private static void ReadLocal(string line, Dictionary<string, Declaration> declarations, TranslationSource source, DiagnosticBag diagnostics, bool rmf2)
    {
        int equals = line.IndexOf('=');
        int open = line.IndexOf('{', equals + 1);
        int close = line.LastIndexOf('}');
        string left = equals < 0 ? string.Empty : line.Substring(".local".Length, equals - ".local".Length).Trim().TrimStart('$');
        if (rmf2 && Variable.IsMatch(left) && open >= 0 && close > open)
        {
            var literalSyntax = Mf2SyntaxReader.Read(new TranslationSource(source.Path, Encoding.UTF8.GetBytes(line.Substring(open, close - open + 1))));
            var expression = literalSyntax.Expressions.Count == 1 ? literalSyntax.Expressions[0] : null;
            if (literalSyntax.Success && expression?.Operand is { Kind: not Mf2OperandKind.Variable } operandSyntax)
            {
                if (expression.Function is not null || expression.Attributes.Count != 0)
                { Error(diagnostics, source, "RTR0065", "Formatted or attributed literal locals require an unsupported backend capability."); return; }
                if (!declarations.TryAdd(left, new Declaration(left, left, TranslationArgumentType.String, "none", "string", null, null, null) { Constant = operandSyntax.Value }))
                    Error(diagnostics, source, "RTR0041", "Duplicate MF2 declaration for '" + left + "'.");
                return;
            }
        }
        if (!Variable.IsMatch(left) || open < 0 || close <= open ||
            !TrySplitVariable(line.Substring(open + 1, close - open - 1).Trim(), out string input, out string tail))
        {
            Error(diagnostics, source, "RTR0041", "Invalid MF2 .local declaration.");
            return;
        }
        Declaration operand = ResolveDeclaration(input, declarations);
        if (rmf2 && operand.Constant is not null && tail.Length != 0)
        {
            Error(diagnostics, source, "RTR0065", "Formatted or attributed constant aliases require an unsupported backend capability.");
            return;
        }
        Declaration declaration = tail.Length == 0
            ? new Declaration(left, operand.Input, operand.Type, operand.Format, operand.Function, operand.Selector, operand.Unit, operand.Numeric) { Constant = operand.Constant }
            : ParseFunction(left, operand.Input, tail, source, diagnostics);
        if (!declarations.TryAdd(left, declaration))
            Error(diagnostics, source, "RTR0041", "Duplicate MF2 declaration for '" + left + "'.");
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

    private static bool TrySplitVariable(string expression, out string name, out string tail)
    {
        name = string.Empty;
        tail = string.Empty;
        if (!expression.StartsWith('$')) return false;
        int end = 1;
        while (end < expression.Length && (char.IsAsciiLetterOrDigit(expression[end]) || expression[end] == '_')) end++;
        name = expression.Substring(1, end - 1);
        tail = expression.Substring(end).Trim();
        return Variable.IsMatch(name) && (tail.Length == 0 || tail.StartsWith(':'));
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

    private static string UnquotePattern(string body, TranslationSource source, DiagnosticBag diagnostics)
    {
        if (!body.StartsWith("{{", StringComparison.Ordinal)) return body;
        int close = FindQuotedPatternEnd(body, 2);
        if (close < 0 || body.Substring(close + 2).Trim().Length != 0)
        {
            Error(diagnostics, source, "RTR0041", "MF2 quoted pattern is not terminated correctly.");
            return string.Empty;
        }
        return body.Substring(2, close - 2);
    }

    private static int FindQuotedPatternEnd(string value, int start)
    {
        int expressionDepth = 0;
        bool quoted = false;
        for (int index = start; index < value.Length - 1; index++)
        {
            if (value[index] == '|') quoted = !quoted;
            if (quoted || (index > start && value[index - 1] == '\\')) continue;
            if (value[index] == '{') expressionDepth++;
            else if (value[index] == '}')
            {
                if (expressionDepth > 0) expressionDepth--;
                else if (value[index + 1] == '}') return index;
            }
        }
        return -1;
    }

    private static int FindExpressionEnd(string value, int start)
    {
        bool quoted = false;
        for (int index = start; index < value.Length; index++)
        {
            if (value[index] == '|' && (index == start || value[index - 1] != '\\')) quoted = !quoted;
            else if (!quoted && value[index] == '}') return index;
        }
        return -1;
    }

    private static bool ReadMarkupProperties(string value, SortedDictionary<string, string> options,
        SortedDictionary<string, string> annotations, HashSet<string> variableOptions, HashSet<string> inputs,
        Dictionary<string, Declaration> declarations, TranslationSource source, DiagnosticBag diagnostics)
    {
        int at = 0;
        while (at < value.Length)
        {
            SkipWhitespace(value, ref at); if (at == value.Length) break;
            bool annotation = value[at] == '@'; if (annotation) at++;
            int start = at;
            while (at < value.Length && (char.IsAsciiLetterOrDigit(value[at]) || value[at] is '_' or '-')) at++;
            string key = value.Substring(start, at - start);
            if (key.Length == 0) return Invalid();
            SkipWhitespace(value, ref at);
            string item = ""; bool variable = false;
            if (at < value.Length && value[at] == '=')
            {
                at++; SkipWhitespace(value, ref at); if (at == value.Length) return Invalid();
                if (value[at] == '|')
                {
                    at++; var literal = new StringBuilder(); bool closed = false;
                    while (at < value.Length)
                    {
                        char ch = value[at++];
                        if (ch == '|') { closed = true; break; }
                        if (ch == '\\' && at < value.Length) ch = value[at++];
                        literal.Append(ch);
                    }
                    if (!closed) return Invalid();
                    item = literal.ToString();
                }
                else
                {
                    start = at; while (at < value.Length && !char.IsWhiteSpace(value[at])) at++;
                    item = value.Substring(start, at - start);
                    variable = item.StartsWith('$');
                    if (variable) { item = item.Substring(1); if (!Variable.IsMatch(item) || annotation) return Invalid(); }
                }
            }
            else if (!annotation) return Invalid();
            if (!(annotation ? annotations : options).TryAdd(key, item)) return Invalid();
            if (variable)
            {
                Declaration input = ResolveDeclaration(item, declarations);
                if (input.Constant is not null) options[key] = input.Constant;
                else { options[key] = input.Input; variableOptions.Add(key); inputs.Add(input.Input); }
            }
        }
        return true;
        bool Invalid() { Error(diagnostics, source, "RTR0041", "Invalid or duplicate MF2 markup option/attribute."); return false; }
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

    private static bool TryReadLine(string value, int offset, out string line, out int next)
    {
        if (offset >= value.Length)
        {
            line = string.Empty;
            next = value.Length;
            return false;
        }
        int end = value.IndexOf('\n', offset);
        if (end < 0)
        {
            line = value.Substring(offset);
            next = value.Length;
        }
        else
        {
            line = value.Substring(offset, end - offset);
            next = end + 1;
        }
        return true;
    }

    private static void SkipWhitespace(string value, ref int position)
    {
        while (position < value.Length && char.IsWhiteSpace(value[position])) position++;
    }

    private static void Flush(List<CompiledMessageNode> nodes, StringBuilder text)
    {
        if (text.Length == 0) return;
        nodes.Add(new CompiledMessageText(text.ToString()));
        text.Clear();
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
