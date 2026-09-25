using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Runic.Translations.Compiler;

/// <summary>A transport-independent completion with a plain-text contract description.</summary>
public sealed record Rmf2Completion(string Label, string Detail);
/// <summary>Project-owned markup and message-symbol information for authoring clients.</summary>
public sealed class Rmf2LanguageService
{
    private readonly Rmf2MarkupRegistry _registry;
    private Rmf2LanguageService(Rmf2MarkupRegistry registry, IReadOnlyList<TranslationDiagnostic> diagnostics)
    { _registry = registry; Diagnostics = diagnostics; }
    public IReadOnlyList<TranslationDiagnostic> Diagnostics { get; }
    public static Rmf2LanguageService Create(TranslationSource project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var diagnostics = new DiagnosticBag();
        var parsed = StrictJsonParser.Parse(project, diagnostics, new TranslationCompilerOptions(), cancellationToken);
        var registry = Rmf2MarkupRegistry.Read(parsed.Root?.Property("markup"), project, diagnostics);
        return new Rmf2LanguageService(registry, Array.AsReadOnly(diagnostics.ToSortedArray()));
    }
    /// <summary>Offers registered tags and semantic variables; an expression adds its contract options.</summary>
    public IReadOnlyList<Rmf2Completion> Complete(Mf2SyntaxDocument? syntax, int byteOffset)
        => Complete(syntax, byteOffset, null);
    /// <summary>Adds base-message functional slots to the local syntax and registry completions.</summary>
    public IReadOnlyList<Rmf2Completion> Complete(Mf2SyntaxDocument? syntax, int byteOffset, Mf2SyntaxDocument? baseSyntax)
    {
        var items = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string function in new[] { "string", "integer", "number", "date", "time", "datetime", "runic:uuid", "runic:boolean", "runic:relative-time" })
            items[":" + function] = Rmf2ProjectV5.Profile + " · options: " + Mf2MessageParser.FunctionOptions(function);
        foreach (var pair in _registry.Contracts) Tag(pair.Key, pair.Value);
        foreach (var pair in _registry.Aliases) Tag(pair.Key, _registry.Contracts[pair.Value]);
        if (syntax is not null)
        {
            foreach (var token in syntax.Tokens.Where(t => t.Kind == Mf2SyntaxTokenKind.Variable))
                items["$" + token.Value] = DescribeVariable(syntax, token.Value);
            foreach (var declaration in syntax.Declarations)
                items["$" + declaration.Name] = DescribeVariable(syntax, declaration.Name);
            var expression = syntax.Expressions.FirstOrDefault(e => Contains(e.Location, byteOffset));
            if (expression?.Function is string functionName)
                foreach (string option in Mf2MessageParser.FunctionOptions(functionName).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (!expression.Options.Any(p => p.Name == option)) items[option + "="] = ":" + functionName + " · literal formatter option";
            if (expression?.MarkupName is string name && Resolve(name) is { } contract && expression.MarkupKind != Mf2MarkupKind.Close)
            {
                foreach (var option in contract.Options)
                {
                    if (!expression.Options.Any(p => p.Name == option.Key)) items[option.Key + "="] = DescribeOption(option.Value);
                    if (expression.Options.Any(p => p.Name == option.Key && p.Location.StartByte <= byteOffset && byteOffset <= p.Location.StartByte + p.Location.LengthBytes))
                        foreach (string value in option.Value.Values) items[value] = option.Key + " · " + DescribeOption(option.Value);
                }
                if (baseSyntax is not null && expression.Options.Any(p => p.Name == "ref" && p.Location.StartByte <= byteOffset && byteOffset <= p.Location.StartByte + p.Location.LengthBytes))
                    foreach (var original in baseSyntax.Expressions.Where(e => e.MarkupName is not null && Resolve(e.MarkupName)?.Name == contract.Name))
                        foreach (var property in original.Options.Where(p => p.Name == "ref" && p.Value is { Kind: not Mf2OperandKind.Variable }))
                            items[property.Value!.Value] = contract.Name + " · base-message functional slot";
                if (contract.Name is "runic:link" or "runic:action" or "runic:icon" && !expression.Options.Any(p => p.Name == "ref"))
                    items["ref="] = "Static functional slot ID";
            }
        }
        return Array.AsReadOnly(items.Select(p => new Rmf2Completion(p.Key, p.Value)).ToArray());
        void Tag(string name, Rmf2MarkupRegistry.Contract contract)
        {
            items["#" + name] = DescribeContract(contract);
            if (!contract.Standalone) items["/" + name] = DescribeContract(contract);
        }
    }
    /// <summary>Describes the semantic token at an extracted-message UTF-8 position.</summary>
    public string? Hover(Mf2SyntaxDocument syntax, int byteOffset)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        var token = syntax.Tokens.FirstOrDefault(t => Contains(t.Location, byteOffset));
        if (token?.Kind == Mf2SyntaxTokenKind.Variable) return DescribeVariable(syntax, token.Value);
        var declaration = syntax.Declarations.FirstOrDefault(d => Contains(d.NameLocation, byteOffset));
        if (declaration is not null) return DescribeVariable(syntax, declaration.Name);
        var expression = syntax.Expressions.FirstOrDefault(e => Contains(e.Location, byteOffset));
        if (expression?.MarkupName is not string name || Resolve(name) is not { } contract) return null;
        var property = expression.Options.FirstOrDefault(p => Contains(p.Location, byteOffset));
        if (property is not null && contract.Options.TryGetValue(property.Name, out var option)) return property.Name + ": " + DescribeOption(option);
        return DescribeContract(contract);
    }
    private Rmf2MarkupRegistry.Contract? Resolve(string name) => _registry.Contracts.GetValueOrDefault(_registry.Aliases.GetValueOrDefault(name, name));
    private static bool Contains(TextSourceLocation location, int position) => location.StartByte <= position && position < location.StartByte + location.LengthBytes;
    private static string DescribeContract(Rmf2MarkupRegistry.Contract contract) => contract.Name + " · " + (contract.Standalone ? "standalone" : "paired") + (contract.Interactive ? " · interactive" : "") + " · plain text: " + contract.PlainText;
    private static string DescribeOption(Rmf2MarkupRegistry.Option option) => option.Type + (option.Values.Length == 0 ? "" : " (" + string.Join(", ", option.Values) + ")") + (option.Default is null ? " · required" : " · default: " + option.Default) + (option.LiteralOnly ? " · literal only" : " · literal or variable");
    private static string DescribeVariable(Mf2SyntaxDocument syntax, string name)
    {
        var declaration = syntax.Declarations.FirstOrDefault(d => d.Name == name);
        return "$" + name + " · " + (declaration?.Kind == "local" ? "message local" : "caller input") + (declaration?.Expression.Function is string function ? " · :" + function : "");
    }
}
