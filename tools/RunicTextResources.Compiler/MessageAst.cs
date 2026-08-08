using System;
using System.Collections.Generic;
using System.Text;

namespace RunicTextResources.Compiler;

internal abstract class CompiledMessageNode
{
    private protected CompiledMessageNode()
    {
    }
}

internal sealed class CompiledMessageText : CompiledMessageNode
{
    internal CompiledMessageText(string value) => Value = value;

    internal string Value { get; }
}

internal sealed class CompiledMessageInput : CompiledMessageNode
{
    internal CompiledMessageInput(string name) => Name = name;

    internal string Name { get; }
}

internal sealed class CompiledMessagePattern
{
    internal CompiledMessagePattern(IReadOnlyList<CompiledMessageNode> nodes)
        : this(nodes, Array.Empty<CompiledMessageSelector>(), Array.Empty<CompiledMessageVariant>())
    {
    }

    internal CompiledMessagePattern(
        IReadOnlyList<CompiledMessageNode> nodes,
        IReadOnlyList<CompiledMessageSelector> selectors,
        IReadOnlyList<CompiledMessageVariant> variants)
    {
        Nodes = nodes;
        Selectors = selectors;
        Variants = variants;
    }

    internal IReadOnlyList<CompiledMessageNode> Nodes { get; }
    internal IReadOnlyList<CompiledMessageSelector> Selectors { get; }
    internal IReadOnlyList<CompiledMessageVariant> Variants { get; }
    internal bool IsVariant => Variants.Count != 0;
}

internal sealed class CompiledMessageSelector
{
    internal CompiledMessageSelector(string name, string input, string function)
    { Name = name; Input = input; Function = function; }
    internal string Name { get; }
    internal string Input { get; }
    internal string Function { get; }
}

internal sealed class CompiledMessageVariant
{
    internal CompiledMessageVariant(IReadOnlyDictionary<string, string> matches, CompiledMessagePattern pattern)
    { Matches = matches; Pattern = pattern; }
    internal IReadOnlyDictionary<string, string> Matches { get; }
    internal CompiledMessagePattern Pattern { get; }
}

internal static class MessagePatternCompiler
{
    internal static CompiledMessagePattern? Compile(
        string pattern,
        TextResourceSource source,
        ByteSpan span,
        DiagnosticBag diagnostics,
        out HashSet<string> names)
    {
        names = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new List<CompiledMessageNode>();
        var text = new StringBuilder();

        for (int index = 0; index < pattern.Length; index++)
        {
            char character = pattern[index];
            if (character == '{')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == '{')
                {
                    text.Append('{');
                    index++;
                    continue;
                }

                int close = pattern.IndexOf('}', index + 1);
                if (close < 0)
                {
                    diagnostics.Add(
                        "RTR0014",
                        TextResourceDiagnosticSeverity.Error,
                        "Message pattern contains an unmatched '{'.",
                        source,
                        span);
                    return null;
                }

                string name = pattern.Substring(index + 1, close - index - 1);
                if (!IsIdentifier(name))
                {
                    diagnostics.Add(
                        "RTR0014",
                        TextResourceDiagnosticSeverity.Error,
                        "Message pattern contains an invalid placeholder.",
                        source,
                        span);
                    return null;
                }

                FlushText(nodes, text);
                nodes.Add(new CompiledMessageInput(name));
                names.Add(name);
                index = close;
                continue;
            }

            if (character == '}')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == '}')
                {
                    text.Append('}');
                    index++;
                    continue;
                }

                diagnostics.Add(
                    "RTR0014",
                    TextResourceDiagnosticSeverity.Error,
                    "Message pattern contains an unmatched '}'.",
                    source,
                    span);
                return null;
            }

            text.Append(character);
        }

        FlushText(nodes, text);
        return new CompiledMessagePattern(nodes.ToArray());
    }

    private static void FlushText(List<CompiledMessageNode> nodes, StringBuilder text)
    {
        if (text.Length == 0)
        {
            return;
        }

        nodes.Add(new CompiledMessageText(text.ToString()));
        text.Clear();
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || (!IsAsciiLetter(value[0]) && value[0] != '_'))
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if (!IsAsciiLetter(character) && (character < '0' || character > '9') && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char value) =>
        (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
}
