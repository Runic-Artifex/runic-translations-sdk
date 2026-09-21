using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace Runic.Translations.Compiler;

/// <summary>The opt-in semantic foundation. The default project path still uses the v4 adapter.</summary>
internal static class Rmf2SemanticCompilerV5
{
    internal static Rmf2SemanticResultV5 Compile(TranslationSource source, TranslationCompilerOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new TranslationCompilerOptions();
        var syntax = Mf2SyntaxReader.Read(source, options, cancellationToken);
        return new Lowerer(syntax, options, cancellationToken).Lower();
    }

    internal static Rmf2SemanticResultV5 CompileWithCallerContract(TranslationSource source,
        IReadOnlyList<Rmf2InputV5> inputs, TranslationCompilerOptions options, CancellationToken cancellationToken)
        => new Lowerer(Mf2SyntaxReader.Read(source, options, cancellationToken), options, cancellationToken,
            inputs.ToDictionary(input => input.Name, input => input.Type, StringComparer.Ordinal)).Lower();

    private sealed record Symbol(string Kind, string ValueType, string Selection, IReadOnlyList<string> Inputs);
    private sealed class Lowerer(Mf2SyntaxDocument syntax, TranslationCompilerOptions options, CancellationToken cancellation,
        IReadOnlyDictionary<string, string>? callerTypes = null)
    {
        private readonly DiagnosticBag _diagnostics = new();
        private readonly Dictionary<string, Symbol> _symbols = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _inputs = new(StringComparer.Ordinal);
        private readonly HashSet<string> _explicitInputs = syntax.Declarations.Where(d => d.Kind == "input").Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        private readonly HashSet<string> _locals = syntax.Declarations.Where(d => d.Kind == "local").Select(d => d.Name).ToHashSet(StringComparer.Ordinal);

        internal Rmf2SemanticResultV5 Lower()
        {
            foreach (var diagnostic in syntax.Diagnostics) _diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, diagnostic.Location);
            if (HasErrors()) return Result(null);
            foreach (var diagnostic in Mf2SyntaxReader.ValidateDataModel(syntax).Concat(Mf2SyntaxReader.ValidateInlineProfile(syntax))) _diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, diagnostic.Location);
            if (HasErrors()) return Result(null);
            // Seed signatures only after declaration validation: prior references
            // cannot be retroactively bound by a later input declaration. Locals
            // still require declaration order and retain their expression graph.
            if (callerTypes is not null)
                foreach (var input in callerTypes)
                    if (!_locals.Contains(input.Key))
                        _symbols[input.Key] = new("input", input.Value, DefaultSelection(input.Value), new[] { input.Key });
            foreach (var declaration in syntax.Declarations.Where(d => d.Kind == "input"))
            {
                var function = declaration.Expression.Function is string name ? Rmf2FunctionRegistryV2.Find(name) : null;
                string type = function?.InputType ?? "string";
                if (callerTypes?.TryGetValue(declaration.Name, out string? callerType) == true)
                {
                    if (function is not null && type != callerType) Error("Input declaration differs from the canonical caller type for '" + declaration.Name + "'.", declaration.NameLocation);
                    type = callerType;
                }
                _inputs[declaration.Name] = type;
                _symbols[declaration.Name] = new("input", type, Selection(declaration.Expression, function?.Selection ?? DefaultSelection(type)), new[] { declaration.Name });
            }
            // Infer unconstrained input types through complete local operand chains.
            // Formatter metadata on a local does not create a new underlying value.
            // Annotated input declarations are fixed contracts; unannotated ones can
            // be constrained by uses. Option-only variables are not inferred here.
            var declaredTypes = syntax.Declarations.Where(declaration => declaration.Kind == "input" && declaration.Expression.Function is not null)
                .Select(declaration => declaration.Name).ToHashSet(StringComparer.Ordinal);
            var localOperands = syntax.Declarations.Where(declaration => declaration.Kind == "local")
                .ToDictionary(declaration => declaration.Name, declaration => declaration.Expression.Operand, StringComparer.Ordinal);
            var inferredTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var expression in syntax.Expressions)
            {
                if (expression.Function is not string name || Rmf2FunctionRegistryV2.Find(name) is not { } function) continue;
                var operand = expression.Operand;
                while (operand is { Kind: Mf2OperandKind.Variable } && localOperands.TryGetValue(operand.Value, out var underlying)) operand = underlying;
                if (operand is not { Kind: Mf2OperandKind.Variable } || declaredTypes.Contains(operand.Value)) continue;
                string type = function.InputType;
                if (callerTypes?.TryGetValue(operand.Value, out string? callerType) == true)
                {
                    if (type != callerType && !(callerType == "int64" && type == "decimal"))
                        Error("Formatter requires a different caller type for '" + operand.Value + "'.", operand.Location);
                    type = callerType;
                }
                if (inferredTypes.TryGetValue(operand.Value, out string? previous) && previous != type)
                {
                    if (previous is "int64" or "decimal" && type is "int64" or "decimal") type = "int64";
                    else Error("Conflicting formatter input types for '" + operand.Value + "'.", operand.Location);
                }
                inferredTypes[operand.Value] = type;
                _inputs[operand.Value] = type;
                _symbols[operand.Value] = new("input", type, DefaultSelection(type), new[] { operand.Value });
            }
            var declarations = new List<Rmf2DeclarationV5>();
            foreach (var declaration in syntax.Declarations)
            {
                cancellation.ThrowIfCancellationRequested();
                var (expression, symbol) = Expression(declaration.Expression);
                declarations.Add(new(declaration.Kind, declaration.Name, expression));
                _symbols[declaration.Name] = symbol with { Kind = declaration.Kind };
            }
            var selectors = new List<Rmf2SelectorV5>();
            var variants = new List<Rmf2VariantV5>();
            if (syntax.Match is { } match)
            {
                if (match.Selectors.Distinct(StringComparer.Ordinal).Count() != match.Selectors.Count) Error("Repeated selector identities are not supported.", match.Location);
                foreach (string name in match.Selectors)
                {
                    var symbol = Resolve(name, null, match.Location);
                    if (symbol.ValueType is not ("string" or "boolean" or "int64" or "decimal")) Error("This type cannot select a variant.", match.Location);
                    selectors.Add(new(new(symbol.Kind, name), symbol.ValueType, symbol.Selection));
                }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var variant in syntax.Variants)
                {
                    var keys = variant.Keys.Select((raw, index) => Key(raw, selectors[index], variant.KeyLocations[index])).ToArray();
                    string vector = string.Concat(keys.Select(key => {
                        string normalized = key.Kind == "wildcard" ? "*" : "=" + (key.Canonical ?? key.Value!.Normalize(NormalizationForm.FormC));
                        return normalized.Length.ToString(CultureInfo.InvariantCulture) + ":" + normalized;
                    }));
                    if (!seen.Add(vector)) DataError("Duplicate normalized variant key vector.", variant.Location);
                    variants.Add(new(keys, Pattern(variant.PatternLocation.StartByte + 2, variant.PatternLocation.StartByte + variant.PatternLocation.LengthBytes - 2)));
                }
            }
            else
            {
                var opening = syntax.Tokens.FirstOrDefault(t => t.Kind == Mf2SyntaxTokenKind.PatternStart);
                var closing = syntax.Tokens.LastOrDefault(t => t.Kind == Mf2SyntaxTokenKind.PatternEnd);
                variants.Add(new(Array.Empty<Rmf2KeyV5>(), Pattern(opening is null ? 0 : opening.Location.StartByte + 2, closing?.Location.StartByte ?? syntax.Source.Bytes.Length)));
            }
            if (_inputs.Count > options.MaximumPlaceholdersPerValue || _inputs.Count > 32 || selectors.Count > 16 || variants.Count > 256 || declarations.Count > 256)
                Error("Message exceeds the v5 input, selector, variant or declaration limit.", syntax.Tokens[0].Location);
            return Result(HasErrors() ? null : new(syntax, _inputs.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new Rmf2InputV5(p.Key, p.Value)).ToArray(), declarations, selectors, variants));
        }

        private (Rmf2ExpressionV5 Expression, Symbol Symbol) Expression(Mf2ExpressionSyntax expression)
        {
            var function = expression.Function is string name ? Rmf2FunctionRegistryV2.Find(name) : null;
            if (expression.Function is not null && function is null) Error("Unknown execution function ':" + expression.Function + "'.", expression.Location);
            if (expression.Operand is null) Error("Function-only expressions are outside rmf2-execution-v2.", expression.Location);
            var operand = expression.Operand is null ? new Rmf2ValueV5("string-literal", "") : Value(expression.Operand, function?.InputType);
            var inherited = operand.Kind is "input" or "local" ? _symbols.GetValueOrDefault(operand.Value)
                : new Symbol("local", LiteralType(operand, function), operand.Kind == "number-literal" ? "plural" : "exact", Array.Empty<string>());
            inherited ??= new Symbol("input", "string", "exact", Array.Empty<string>());
            if (function is not null && !AcceptsOperand(operand, inherited.ValueType, function.InputType)) Error("Function ':" + function.Name + "' requires " + function.InputType + " input.", expression.Location);
            var loweredOptions = new List<Rmf2OptionV5>();
            var dependencies = new HashSet<string>(inherited.Inputs, StringComparer.Ordinal);
            foreach (var property in expression.Options)
            {
                var rule = function?.Options.FirstOrDefault(item => item.Name == property.Name);
                var value = Value(property.Value!, null);
                loweredOptions.Add(new(property.Name, value));
                if (rule is null) { Error("Unknown option '" + property.Name + "'.", property.Location); continue; }
                if (value.Kind is "input" or "local")
                {
                    var symbol = _symbols.GetValueOrDefault(value.Value);
                    if (!rule.Dynamic || symbol is null || symbol.ValueType != rule.InputType || symbol.Inputs.Any(input => !_explicitInputs.Contains(input)))
                        Error("Dynamic option '" + property.Name + "' requires a declared " + rule.InputType + " input or a local of that type, and must permit dynamic values.", property.Location);
                    if (symbol is not null) dependencies.UnionWith(symbol.Inputs);
                }
                else if (!rule.AcceptsLiteral(value)) Error("Invalid literal for option '" + property.Name + "'.", property.Location);
            }
            if (function is not null && loweredOptions.All(option => option.Value.Kind is not ("input" or "local")))
            {
                string? error = Rmf2FunctionRegistryV2.ValidateResolvedOptions(function.Name, loweredOptions.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal));
                if (error is not null) Error(error, expression.Location);
            }
            else if (function?.Name == "number")
            {
                var supplied = loweredOptions.ToDictionary(option => option.Name, option => option.Value, StringComparer.Ordinal);
                bool percent = supplied.TryGetValue("style", out var style) && style.Kind == "string-literal" && style.Value == "percent";
                int? min = Digits("minimumFractionDigits", 0), max = Digits("maximumFractionDigits", supplied.ContainsKey("style") ? null : 6);
                if ((min.HasValue && max.HasValue && min > max) || (percent && (min > 4 || max > 4))) Error("Invalid static fraction digit constraints.", expression.Location);
                int? Digits(string name, int? fallback) => !supplied.TryGetValue(name, out var value) ? fallback : value.Kind == "number-literal" && int.TryParse(value.Canonical, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int digits) ? digits : null;
            }
            string selection = Selection(expression, function?.Selection ?? inherited.Selection);
            return (new(operand, inherited.ValueType, expression.Function, loweredOptions, Annotations(expression)), new("local", inherited.ValueType, selection, dependencies.Order(StringComparer.Ordinal).ToArray()));
        }

        private List<Rmf2NodeV5> Pattern(int from, int to)
        {
            var nodes = new List<Rmf2NodeV5>(); var text = new StringBuilder();
            var expressions = syntax.Expressions.ToDictionary(expression => expression.Location.StartByte);
            int consumed = from;
            foreach (var token in syntax.Tokens)
            {
                cancellation.ThrowIfCancellationRequested();
                if (token.Location.StartByte < consumed || token.Location.StartByte >= to) continue;
                if (token.Kind is Mf2SyntaxTokenKind.Text or Mf2SyntaxTokenKind.Whitespace) { text.Append(Unescape(token.Raw)); continue; }
                if (token.Kind != Mf2SyntaxTokenKind.ExpressionStart) continue;
                Flush();
                var expression = expressions[token.Location.StartByte]; consumed = expression.Location.StartByte + expression.Location.LengthBytes;
                if (expression.MarkupKind == Mf2MarkupKind.None) nodes.Add(new Rmf2ExpressionNodeV5(Expression(expression).Expression));
                else nodes.Add(new Rmf2MarkupV5(expression.MarkupName!, expression.MarkupKind.ToString().ToLowerInvariant(),
                    expression.Options.Select(property => new Rmf2OptionV5(property.Name, Value(property.Value!, null))).ToArray(), Annotations(expression)));
            }
            Flush();
            if (nodes.Count > 4096) Error("A pattern exceeds the v5 node limit.", syntax.Tokens[0].Location);
            return nodes;
            void Flush() { if (text.Length != 0) { nodes.Add(new Rmf2TextV5(text.ToString())); text.Clear(); } }
        }

        private Rmf2ValueV5 Value(Mf2OperandSyntax operand, string? expectedType)
        {
            if (operand.Kind == Mf2OperandKind.Variable)
            { var symbol = Resolve(operand.Value, expectedType, operand.Location); return new(symbol.Kind, operand.Value); }
            if (operand.Kind == Mf2OperandKind.String)
            {
                if (!operand.Quoted && operand.Value.Length != 0 && (char.IsAsciiDigit(operand.Value[0]) || operand.Value[0] == '-')) DataError("Malformed unquoted numeric literal.", operand.Location);
                return new("string-literal", operand.Value);
            }
            if (!Rmf2DecimalV5.TryCanonicalize(operand.Value, out string canonical)) Error("Number is outside the exact portable decimal domain.", operand.Location);
            return new("number-literal", operand.Value, canonical);
        }
        private Symbol Resolve(string name, string? expectedType, TextSourceLocation location)
        {
            if (_symbols.TryGetValue(name, out var symbol))
            {
                if (symbol.Kind == "input") _inputs[name] = symbol.ValueType;
                return symbol;
            }
            if (_locals.Contains(name)) DataError("Local '" + name + "' is referenced before its declaration.", location);
            string type = expectedType ?? "string";
            symbol = new("input", type, DefaultSelection(type), new[] { name });
            _symbols[name] = symbol; _inputs[name] = type;
            return symbol;
        }
        private Rmf2AnnotationV5[] Annotations(Mf2ExpressionSyntax expression) => expression.Attributes.Select(property =>
            new Rmf2AnnotationV5(property.Name, property.Value is null ? null : Value(property.Value, null))).ToArray();

        private Rmf2KeyV5 Key(string raw, Rmf2SelectorV5 selector, TextSourceLocation location)
        {
            if (raw == "*") return new("wildcard");
            string value = raw.StartsWith('|') ? Unescape(raw.Substring(1, raw.Length - 2)) : raw;
            string normalized = value.Normalize(NormalizationForm.FormC);
            if (!raw.StartsWith('|') && raw.Length != 0 && (char.IsAsciiDigit(raw[0]) || raw[0] == '-') && !Rmf2DecimalV5.TryCanonicalize(raw, out _))
                DataError("Malformed or out-of-domain numeric variant key.", location);
            if (selector.Type is "int64" or "decimal")
            {
                if (Rmf2DecimalV5.TryCanonicalize(normalized, out string number))
                {
                    if (selector.Type == "int64" && !long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)) DataError("An integer selector requires an int64 exact key.", location);
                    return new("literal", value, number);
                }
                if (selector.Function is not ("plural" or "ordinal") || normalized is not ("zero" or "one" or "two" or "few" or "many" or "other"))
                    DataError("A numeric selector key must be a portable exact number or an enabled plural category.", location);
            }
            else if (selector.Type == "boolean" && normalized is not ("true" or "false")) DataError("A boolean selector key must be true, false or wildcard.", location);
            return new("literal", value);
        }
        private static bool AcceptsOperand(Rmf2ValueV5 value, string actual, string expected)
        {
            if (value.Kind is "input" or "local") return actual == expected || (actual == "int64" && expected == "decimal");
            if (value.Kind == "number-literal") return expected == "decimal" || (expected == "int64" && long.TryParse(value.Canonical, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _));
            return expected switch {
                "string" => true,
                "boolean" => value.Value is "true" or "false",
                "date" => DateOnly.TryParseExact(value.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "time" => TimeOnly.TryParseExact(value.Value, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "datetime" => DateTimeOffset.TryParseExact(value.Value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _),
                "guid" => value.Value.Length == 36 && Guid.TryParseExact(value.Value, "D", out _),
                _ => false,
            };
        }
        // Literal binding introduces a typed value once. Later formatting of an
        // input/local reference must keep that value type, even when the function
        // accepts a wider domain (for example :number consuming an int64 value).
        private static string LiteralType(Rmf2ValueV5 value, Rmf2FunctionRuleV2? function) => function?.InputType ?? (value.Kind == "number-literal" ? "decimal" : "string");
        private static string Selection(Mf2ExpressionSyntax expression, string fallback) => expression.Options.FirstOrDefault(option => option.Name == "select")?.Value?.Value ?? fallback;
        private static string DefaultSelection(string type) => type is "int64" or "decimal" ? "plural" : type is "string" or "boolean" ? "exact" : "none";
        private static string Unescape(string value)
        {
            var result = new StringBuilder();
            for (int index = 0; index < value.Length; index++) { if (value[index] == '\\' && index + 1 < value.Length) index++; result.Append(value[index]); }
            return result.ToString();
        }
        private void Error(string message, TextSourceLocation location) => _diagnostics.Add("RTR0065", TranslationDiagnosticSeverity.Error, message, location);
        private void DataError(string message, TextSourceLocation location) => _diagnostics.Add("RTR0067", TranslationDiagnosticSeverity.Error, message, location);
        private bool HasErrors() => _diagnostics.Items.Any(diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error);
        private Rmf2SemanticResultV5 Result(Rmf2MessageV5? message) => new(message, Array.AsReadOnly(_diagnostics.ToSortedArray()));
    }
}
