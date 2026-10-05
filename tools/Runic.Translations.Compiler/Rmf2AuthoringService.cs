using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Runic.Translations.Compiler;

// Editor projections stay separate from execution/artifact contracts. Their byte
// spans address extracted MF2; the resource writer owns physical indentation.
internal sealed record Rmf2AuthoringInput(string Name, string Type, string? Function, bool Declared);
internal sealed record Rmf2AuthoringVariant(string Id, IReadOnlyList<string> Keys, string Pattern, int StartByte, int LengthBytes);
internal sealed record Rmf2AuthoringProjection(string Revision, bool Supported, string? Reason,
    IReadOnlyList<Rmf2AuthoringInput> Inputs, IReadOnlyList<string> Selectors, IReadOnlyList<string> SelectorFunctions, IReadOnlyList<Rmf2AuthoringVariant> Variants,
    IReadOnlyList<string> PluralCategories, IReadOnlyList<IReadOnlyList<string>> SelectorPluralCategories, string CldrVersion);
internal sealed record Rmf2AuthoringOperation(string Kind, string? VariantId = null, string? Pattern = null,
    string? Name = null, string? NewName = null, string? Function = null,
    IReadOnlyList<string>? Keys = null, IReadOnlyList<string>? Selectors = null);
internal sealed record Rmf2AuthoringSemantic(IReadOnlyList<string> Text, IReadOnlyList<string> Placeholders,
    IReadOnlyList<string> Slots, bool Supported, bool HasBoundaryWhitespace);

internal static class Rmf2AuthoringService
{
    private static readonly string[] CategoryOrder = ["zero", "one", "two", "few", "many", "other"];
    internal static string Revision(TranslationSource source) => Convert.ToHexStringLower(SHA256.HashData(source.Bytes));

    internal static Rmf2AuthoringProjection Project(TranslationSource source, string locale, Rmf2MessageV5? linked = null)
    {
        var syntax = Mf2SyntaxReader.Read(source);
        var semantic = linked ?? Rmf2SemanticCompilerV5.Compile(source).Message;
        var variants = new List<Rmf2AuthoringVariant>();
        if (syntax.Match is not null)
        {
            for (int i = 0; i < syntax.Variants.Count; i++)
            {
                var variant = syntax.Variants[i];
                Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture), variant.Keys,
                    variant.PatternLocation.StartByte + 2, Math.Max(0, variant.PatternLocation.LengthBytes - 4));
            }
        }
        else
        {
            var open = syntax.Tokens.FirstOrDefault(t => t.Kind == Mf2SyntaxTokenKind.PatternStart);
            var close = syntax.Tokens.LastOrDefault(t => t.Kind == Mf2SyntaxTokenKind.PatternEnd);
            int from = open is null ? 0 : open.Location.StartByte + 2;
            int to = close?.Location.StartByte ?? source.Bytes.Length;
            // One terminal newline in a direct plain file is storage framing.
            if (open is null) to = PlainEnd(source.Bytes);
            Add("0", Array.Empty<string>(), from, Math.Max(0, to - from));
        }
        var inputs = semantic?.Inputs.Select(input => {
            var declaration = syntax.Declarations.FirstOrDefault(d => d.Kind == "input" && d.Name == input.Name);
            return new Rmf2AuthoringInput(input.Name, input.Type, declaration?.Expression.Function, declaration is not null);
        }).ToArray() ?? Array.Empty<Rmf2AuthoringInput>();
        bool supported = syntax.Success && semantic is not null;
        return new(Revision(source), supported, supported ? null : "The compiler cannot safely project this syntax. Edit the MF2 source.",
            inputs, syntax.Match?.Selectors ?? Array.Empty<string>(), semantic?.Selectors.Select(selector => selector.Function).ToArray() ?? [], variants, PluralCategories(locale,
                semantic is { Selectors.Count: > 0 } && semantic.Selectors[0].Function == "ordinal"),
            semantic?.Selectors.Select(selector => (IReadOnlyList<string>)PluralCategories(locale, selector.Function == "ordinal")).ToArray() ?? [], TranslationCapabilityRegistry.CldrVersion);
        void Add(string id, IReadOnlyList<string> keys, int start, int length) => variants.Add(new(id, keys,
            Encoding.UTF8.GetString(source.Bytes, start, length), start, length));
    }

    internal static Rmf2AuthoringSemantic Semantic(TranslationSource source, Rmf2MessageV5? linked = null)
    {
        var message = linked ?? Rmf2SemanticCompilerV5.Compile(source).Message;
        if (message is null) return new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), false, false);
        bool plain = message.Syntax.Match is null && !message.Syntax.Tokens.Any(t => t.Kind == Mf2SyntaxTokenKind.PatternStart);
        bool boundary = false;
        var texts = new List<string>();
        foreach (var variant in message.Variants)
        {
            var nodes = variant.Nodes.ToArray();
            if (plain && nodes.LastOrDefault() is Rmf2TextV5 terminal)
            {
                string value = terminal.Value;
                if (value.EndsWith('\n')) value = value[..^1];
                if (value.EndsWith('\r')) value = value[..^1];
                nodes[^1] = new Rmf2TextV5(value);
                if (value.Length == 0) nodes = nodes[..^1];
            }
            boundary |= nodes.FirstOrDefault() is Rmf2TextV5 first && first.Value.Length > 0 && char.IsWhiteSpace(first.Value[0]);
            boundary |= nodes.LastOrDefault() is Rmf2TextV5 last && last.Value.Length > 0 && char.IsWhiteSpace(last.Value[^1]);
            texts.Add(string.Concat(nodes.OfType<Rmf2TextV5>().Select(t => t.Value)));
        }
        return new(texts, message.Inputs.Select(i => i.Name + ":" + i.Type).Order(StringComparer.Ordinal).ToArray(),
            message.Variants.SelectMany(v => v.Nodes).OfType<Rmf2MarkupV5>().Select(m => m.MarkupKind + ":" + m.Name + (m.Options.FirstOrDefault(o => o.Name == "ref") is { } reference ? ":" + reference.Value.Value : ""))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), true, boundary);
    }

    internal static TranslationSource Apply(TranslationSource source, string expectedRevision, Rmf2AuthoringOperation operation)
    {
        if (Revision(source) != expectedRevision) throw new ArgumentException("The message changed after this edit was prepared. Refresh the composer and try again.");
        var projection = Project(source, "en");
        if (!projection.Supported) throw new ArgumentException(projection.Reason);
        var syntax = Mf2SyntaxReader.Read(source);
        var edits = new List<(int Start, int Length, string Text)>();
        var declaration = syntax.Declarations.FirstOrDefault(d => d.Kind == "input" && d.Name == operation.Name);
        var variant = projection.Variants.FirstOrDefault(v => v.Id == operation.VariantId);
        switch (operation.Kind)
        {
            case "set-pattern":
                if (variant is null || operation.Pattern is null) throw new ArgumentException("Choose an existing variant and pattern.");
                Edit(variant.StartByte, variant.LengthBytes, operation.Pattern);
                break;
            case "rename-input":
                Name(operation.NewName);
                if (!projection.Inputs.Any(i => i.Name == operation.Name)) throw new ArgumentException("Choose an existing input.");
                if (operation.Name != operation.NewName && (projection.Inputs.Any(i => i.Name == operation.NewName) || syntax.Declarations.Any(d => d.Name == operation.NewName)))
                    throw new ArgumentException("That variable name is already used.");
                foreach (var reference in syntax.VariableReferences(operation.Name!)) Edit(reference.StartByte, reference.LengthBytes, "$" + operation.NewName);
                break;
            case "add-input":
            case "set-input":
                Name(operation.Name); Function(operation.Function);
                if (operation.Kind == "add-input" && projection.Inputs.Any(i => i.Name == operation.Name)) throw new ArgumentException("That input already exists.");
                if (declaration is not null)
                {
                    var token = syntax.Tokens.FirstOrDefault(t => t.Kind == Mf2SyntaxTokenKind.Function && t.Location.StartByte >= declaration.Expression.Location.StartByte && t.Location.StartByte < End(declaration.Expression.Location));
                    if (token is not null) Edit(token.Location.StartByte, token.Location.LengthBytes, ":" + operation.Function);
                    else Edit(End(declaration.Expression.Operand!.Location), 0, " :" + operation.Function);
                }
                else
                {
                    if (syntax.Declarations.Any(d => d.Name == operation.Name)) throw new ArgumentException("That name is bound by a local declaration.");
                    Prefix(".input {$" + operation.Name + " :" + operation.Function + "}\n");
                }
                break;
            case "remove-input":
                if (declaration is null) throw new ArgumentException("Only unused explicit declarations can be removed.");
                if (syntax.VariableReferences(declaration.Name).Any(r => r.StartByte < declaration.Location.StartByte || r.StartByte >= End(declaration.Location)))
                    throw new ArgumentException("Remove references to this input before removing its declaration.");
                Edit(declaration.Location.StartByte, declaration.Location.LengthBytes, "");
                break;
            case "set-selectors":
                var selectors = operation.Selectors ?? throw new ArgumentException("Supply selectors.");
                foreach (string selector in selectors) { Name(selector); if (!projection.Inputs.Any(i => i.Name == selector) && !syntax.Declarations.Any(d => d.Name == selector)) throw new ArgumentException("The selector must name an existing variable."); }
                if (syntax.Match is { } match)
                {
                    if (selectors.Count == 0)
                    {
                        if (syntax.Variants.Count != 1) throw new ArgumentException("Removing every selector would discard branches. Keep a single branch first.");
                        var only = syntax.Variants.Single();
                        string pattern = projection.Variants.Single().Pattern;
                        Edit(match.Location.StartByte, End(only.Location) - match.Location.StartByte, "{{" + pattern + "}}");
                    }
                    else
                    {
                        // Replacing selector names at the same arity retains each
                        // existing column. Arity changes preserve surviving names.
                        int[] columns = selectors.Select((selector, index) => selectors.Count == match.Selectors.Count
                            ? index : match.Selectors.ToList().IndexOf(selector)).ToArray();
                        var vectors = syntax.Variants.Select(branch => columns.Select(column => column < 0 ? "*" : branch.Keys[column]).ToArray()).ToArray();
                        if (vectors.Select(keys => string.Join("\u001f", keys)).Distinct(StringComparer.Ordinal).Count() != vectors.Length)
                            throw new ArgumentException("Removing this selector would merge distinct branches. Edit their keys or remove duplicates first.");
                        // Preserve whitespace between the matcher and first row.
                        var refs = syntax.Tokens.Where(t => t.Kind == Mf2SyntaxTokenKind.Variable && t.Location.StartByte >= match.Location.StartByte && t.Location.StartByte < End(match.Location)).ToArray();
                        Edit(refs[0].Location.StartByte, End(refs[^1].Location) - refs[0].Location.StartByte, string.Join(" ", selectors.Select(selector => "$" + selector)));
                        for (int i = 0; i < syntax.Variants.Count; i++)
                        {
                            var branch = syntax.Variants[i];
                            Edit(branch.KeyLocations[0].StartByte, End(branch.KeyLocations[^1]) - branch.KeyLocations[0].StartByte, string.Join(" ", vectors[i]));
                        }
                    }
                }
                else
                {
                    if (selectors.Count == 0) break;
                    var only = projection.Variants.Single();
                    var open = syntax.Tokens.FirstOrDefault(t => t.Kind == Mf2SyntaxTokenKind.PatternStart);
                    int start = open?.Location.StartByte ?? only.StartByte;
                    int length = open is null ? only.LengthBytes : only.LengthBytes + 4;
                    Edit(start, length, ".match " + string.Join(" ", selectors.Select(s => "$" + s)) + "\n" + string.Join(" ", selectors.Select(_ => "*")) + " {{" + only.Pattern + "}}");
                }
                break;
            case "add-variant":
                if (syntax.Match is null || operation.Keys is null || operation.Keys.Count != syntax.Match.Selectors.Count || operation.Pattern is null) throw new ArgumentException("Supply one key per selector and a pattern.");
                var final = syntax.Variants[^1];
                Edit(End(final.Location), 0, "\n" + string.Join(" ", operation.Keys) + " {{" + operation.Pattern + "}}");
                break;
            case "remove-variant":
                if (variant is null || syntax.Match is null) throw new ArgumentException("Choose an existing matcher branch.");
                if (variant.Keys.All(k => k == "*")) throw new ArgumentException("The catch-all branch is required.");
                var selected = syntax.Variants[int.Parse(variant.Id, System.Globalization.CultureInfo.InvariantCulture)];
                Edit(selected.Location.StartByte, selected.Location.LengthBytes, "");
                break;
            default: throw new ArgumentException("Unknown authoring operation.");
        }
        byte[] bytes = source.Bytes;
        foreach (var edit in edits.OrderByDescending(e => e.Start))
        {
            byte[] replacement = Encoding.UTF8.GetBytes(edit.Text);
            bytes = bytes[..edit.Start].Concat(replacement).Concat(bytes[(edit.Start + edit.Length)..]).ToArray();
        }
        var result = new TranslationSource(source.Path, bytes);
        var validation = Rmf2SemanticCompilerV5.Compile(result);
        if (!validation.Success) throw new ArgumentException(string.Join(" ", validation.Diagnostics.Select(d => d.Message)));
        return result;
        void Edit(int start, int length, string text) => edits.Add((start, length, text));
        void Prefix(string text)
        {
            if (!syntax.Tokens.Any(t => t.Kind == Mf2SyntaxTokenKind.PatternStart) && syntax.Match is null)
            {
                var only = projection.Variants.Single();
                Edit(0, only.LengthBytes, text + "{{" + only.Pattern + "}}");
            }
            else Edit(0, 0, text);
        }
    }

    private static int PlainEnd(byte[] bytes)
    {
        int to = bytes.Length;
        if (to > 0 && bytes[to - 1] == 10) { to--; if (to > 0 && bytes[to - 1] == 13) to--; }
        return to;
    }
    private static int End(TextSourceLocation location) => location.StartByte + location.LengthBytes;
    private static void Name(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, @"^[\p{L}_][\p{L}\p{N}_-]*$", RegexOptions.CultureInvariant)) throw new ArgumentException("Use a valid MF2 variable name.");
    }
    private static void Function(string? function)
    {
        if (function is null || Rmf2FunctionRegistryV2.Find(function) is null) throw new ArgumentException("Choose a compiler-supported input formatter.");
    }
    // Extract category names from the same generated CLDR rules shipped to ESM.
    // This avoids a host Intl evaluator or a second hand-maintained locale table.
    private static string[] PluralCategories(string locale, bool ordinal)
    {
        string language = locale.Split('-', '_')[0].ToLowerInvariant();
        string rules = TranslationCapabilityRegistry.EsmPluralRulesSource;
        var mapping = Regex.Match(rules, "\\\"" + Regex.Escape(language) + "\\\": Object.freeze\\(\\[pluralRule(\\d+), pluralRule(\\d+)\\]\\)");
        if (!mapping.Success) return Array.Empty<string>();
        string index = mapping.Groups[ordinal ? 2 : 1].Value;
        var body = Regex.Match(rules, "const pluralRule" + index + " = \\(o\\) => \\{(.*?)\\};", RegexOptions.Singleline);
        var categories = Regex.Matches(body.Value, "return \"(.*?)\"").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        return CategoryOrder.Where(categories.Contains).ToArray();
    }
}
