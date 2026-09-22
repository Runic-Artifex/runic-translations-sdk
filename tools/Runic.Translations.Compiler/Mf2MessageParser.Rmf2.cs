using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Runic.Translations.Compiler;

internal static partial class Mf2MessageParser
{
    // RMF2 lowers the shared data model. Legacy MF2 retains its compatibility parser.
    private static Mf2ParsedMessage? LowerRmf2(Mf2SyntaxDocument syntax, DiagnosticBag diagnostics, TranslationCompilerOptions options)
    {
        if (diagnostics.Items.Any(d => d.Severity == TranslationDiagnosticSeverity.Error)) return null;
        var declarations = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in syntax.Declarations)
        {
            var expression = item.Expression;
            Declaration declaration;
            if (expression.Operand is { Kind: not Mf2OperandKind.Variable } literal)
                declaration = new Declaration(item.Name, item.Name, TranslationArgumentType.String, "none", "string", null, null, null) { Constant = literal.Value };
            else
            {
                string input = expression.Operand!.Value;
                var operand = ResolveDeclaration(input, declarations);
                if (operand.Constant is not null && expression.Function is not null)
                { Error(diagnostics, syntax.Source, "RTR0065", "Formatting constant locals is not executable in this profile."); return null; }
                declaration = expression.Function is null
                    ? new Declaration(item.Name, operand.Input, operand.Type, operand.Format, operand.Function, operand.Selector, operand.Unit, operand.Numeric) { Constant = operand.Constant }
                    : ParseFunction(item.Name, operand.Input, FunctionTail(expression), syntax.Source, diagnostics);
            }
            if (declaration.Selector is null && declaration.Type is TranslationArgumentType.Int or TranslationArgumentType.Number) declaration.Selector = "plural";
            declarations.Add(item.Name, declaration);
        }
        CompiledMessagePattern message;
        if (syntax.Match is { } match)
        {
            var selectors = new List<CompiledMessageSelector>();
            foreach (string name in match.Selectors)
            {
                var declaration = ResolveDeclaration(name, declarations);
                if (declaration.Constant is not null) { Error(diagnostics, syntax.Source, "RTR0065", "Constant local selectors are not executable in this profile."); return null; }
                used.Add(declaration.Input);
                selectors.Add(new CompiledMessageSelector(name, declaration.Input, declaration.Selector ?? "exact"));
            }
            var variants = syntax.Variants.Select(variant => new CompiledMessageVariant(
                selectors.Select((selector, index) => (selector.Name, Value: DecodeKey(variant.Keys[index]))).ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal),
                Pattern(variant.PatternLocation.StartByte + 2, variant.PatternLocation.StartByte + variant.PatternLocation.LengthBytes - 2))).ToArray();
            message = new CompiledMessagePattern(Array.Empty<CompiledMessageNode>(), selectors, variants);
        }
        else
        {
            var opening = syntax.Tokens.FirstOrDefault(t => t.Kind == Mf2SyntaxTokenKind.PatternStart);
            var closing = syntax.Tokens.LastOrDefault(t => t.Kind == Mf2SyntaxTokenKind.PatternEnd);
            message = opening is null ? Pattern(0, syntax.Source.Bytes.Length) : Pattern(opening.Location.StartByte + 2, closing!.Location.StartByte);
        }
        foreach (string input in used)
            if (!HasInputDeclaration(declarations, input)) declarations.Add(input, Declaration.CreateInput(input, input, TranslationArgumentType.String, "none", null));
        var placeholders = declarations.Values.Where(d => d.Constant is null).GroupBy(d => d.Input, StringComparer.Ordinal)
            .Select(group => group.First()).OrderBy(d => d.Input, StringComparer.Ordinal)
            .Select(d => new PlaceholderModel(d.Input, d.Type, d.Format)).ToArray();
        if (placeholders.Length > options.MaximumPlaceholdersPerValue) Error(diagnostics, syntax.Source, "RTR0022", "MF2 input count exceeds the configured limit.");
        return new Mf2ParsedMessage(StrictJsonParser.StrictUtf8.GetString(syntax.Source.Bytes), message, placeholders);

        CompiledMessagePattern Pattern(int from, int to)
        {
            var root = new List<CompiledMessageNode>(); var current = root;
            var parents = new Stack<List<CompiledMessageNode>>();
            var expressions = syntax.Expressions.ToDictionary(e => e.Location.StartByte);
            int consumed = from;
            foreach (var token in syntax.Tokens)
            {
                if (token.Location.StartByte < consumed || token.Location.StartByte >= to) continue;
                if (token.Kind is Mf2SyntaxTokenKind.Text or Mf2SyntaxTokenKind.Whitespace) { current.Add(new CompiledMessageText(Unescape(token.Raw))); continue; }
                if (token.Kind != Mf2SyntaxTokenKind.ExpressionStart) continue;
                var expression = expressions[token.Location.StartByte]; consumed = expression.Location.StartByte + expression.Location.LengthBytes;
                if (expression.MarkupKind == Mf2MarkupKind.Close) { current = parents.Pop(); continue; }
                if (expression.MarkupName is string tag)
                {
                    var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    var variables = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in expression.Options)
                    {
                        var operand = property.Value!;
                        if (operand.Kind == Mf2OperandKind.Variable)
                        {
                            var declaration = ResolveDeclaration(operand.Value, declarations);
                            properties[property.Name] = declaration.Constant ?? declaration.Input;
                            if (declaration.Constant is null) { variables.Add(property.Name); used.Add(declaration.Input); }
                        }
                        else properties[property.Name] = operand.Value;
                    }
                    var children = new List<CompiledMessageNode>();
                    current.Add(new CompiledMessageMarkup(tag, properties, children) {
                        Standalone = expression.MarkupKind == Mf2MarkupKind.Standalone,
                        Annotations = expression.Attributes.ToDictionary(p => p.Name, p => p.Value?.Value ?? "", StringComparer.Ordinal), VariableOptions = variables,
                    });
                    if (expression.MarkupKind == Mf2MarkupKind.Open) { parents.Push(current); current = children; }
                }
                else if (expression.Operand is { Kind: not Mf2OperandKind.Variable } literal) current.Add(new CompiledMessageText(literal.Value));
                else
                {
                    var node = ParseExpression("$" + expression.Operand!.Value + (expression.Function is null ? "" : " " + FunctionTail(expression)), declarations, used, syntax.Source, diagnostics, resolveAliases: true);
                    if (node is not null) current.Add(node);
                }
            }
            return new CompiledMessagePattern(root);
        }
    }
    private static string FunctionTail(Mf2ExpressionSyntax expression) => ":" + expression.Function + string.Concat(expression.Options.Select(p => " " + p.Name + "=" + p.Value!.Value));
    private static string DecodeKey(string raw) => (raw.StartsWith('|') ? Unescape(raw.Substring(1, raw.Length - 2)) : raw).Normalize(NormalizationForm.FormC);
    private static string Unescape(string raw)
    {
        var result = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++) { if (raw[i] == '\\' && i + 1 < raw.Length) i++; result.Append(raw[i]); }
        return result.ToString();
    }
}
