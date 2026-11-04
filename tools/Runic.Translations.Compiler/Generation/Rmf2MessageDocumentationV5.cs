using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Runic.Translations.Compiler.Generation;

// Hover text for generated members: the base locale's authored text and the entry's comment ("base text — description"),
// and @param descriptions, on one line each and bounded, so C# and TypeScript tooling shows what a key says.
internal sealed class Rmf2MessageDocumentationV5
{
    private const int MaximumTextLength = 200;
    private readonly Dictionary<string, Rmf2TranslationV5> resources;

    internal Rmf2MessageDocumentationV5(Rmf2ProjectV5 project)
    {
        Rmf2LocaleV5? locale = project.Locales.FirstOrDefault(item => item.Tag == project.DefaultLocale);
        resources = locale is null ? new(StringComparer.Ordinal)
            : locale.ResolvedResources.GroupBy(item => item.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    /// <summary>"base text — description", or null when the message has neither.</summary>
    internal string? Summary(string key)
    {
        if (!resources.TryGetValue(key, out Rmf2TranslationV5? resource)) return null;
        string? text = OneLine(Text(resource), MaximumTextLength), description = OneLine(resource.Description, int.MaxValue);
        if (text is null) return description;
        return description is null ? text : text + " — " + description;
    }

    internal string? Parameter(string key, string input) =>
        resources.TryGetValue(key, out Rmf2TranslationV5? resource) && resource.ParameterDescriptions?.TryGetValue(input, out string? description) == true
            ? OneLine(description, int.MaxValue) : null;

    // The authored pattern; a message with declarations or selectors (".input", ".match") shows its catch-all variant
    // as text and {$placeholders}, so the summary reads like the message and not like its syntax.
    private static string? Text(Rmf2TranslationV5 resource)
    {
        if (resource.SourceText is { } source && !source.TrimStart().StartsWith('.')) return source;
        Rmf2VariantV5? variant = resource.Message.Variants.LastOrDefault(item => item.Keys.All(key => key.Kind == "wildcard"));
        if (variant is null) return resource.SourceText;
        var builder = new StringBuilder();
        foreach (Rmf2NodeV5 node in variant.Nodes)
            builder.Append(node switch
            {
                Rmf2TextV5 text => text.Value,
                Rmf2ExpressionNodeV5 { Expression.Operand: { Kind: "input" or "local" } operand } => "{$" + operand.Value + "}",
                Rmf2ExpressionNodeV5 expression => expression.Expression.Operand.Value,
                Rmf2MarkupV5 { MarkupKind: "close" } markup => "{/" + markup.Name + "}",
                Rmf2MarkupV5 { MarkupKind: "standalone" } markup => "{#" + markup.Name + "/}",
                Rmf2MarkupV5 markup => "{#" + markup.Name + "}",
                _ => "",
            });
        return builder.ToString();
    }

    // C# XML documentation and JSDoc both run to the end of a line; whitespace runs become one space.
    private static string? OneLine(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var builder = new StringBuilder(value.Length);
        bool space = false;
        foreach (char character in value.Trim())
        {
            if (char.IsWhiteSpace(character)) { space = true; continue; }
            if (space) builder.Append(' ');
            space = false;
            builder.Append(character);
        }
        if (builder.Length <= maximum) return builder.ToString();
        int length = maximum;
        if (char.IsHighSurrogate(builder[length - 1])) length--;
        return builder.ToString(0, length).TrimEnd() + "…";
    }

    /// <summary>A JSDoc comment body that cannot close early.</summary>
    internal static string JsDoc(string value) => value.Replace("*/", "*\\/", StringComparison.Ordinal);
}
