using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Runic.Translations.Compiler;

// Syntax-level view of document profile v1 blocks for authoring tools (folding, outline,
// completion context, quick fixes and canonical layout). It works on unvalidated syntax, so
// every result is best effort: unbalanced or misplaced tags are skipped, never repaired.
// The block vocabulary is fixed in v1 and project aliases cannot shadow it (RTR0060).
internal static class Rmf2DocumentSyntax
{
    private static readonly SearchValues<byte> LineSpace = SearchValues.Create(" \t\r\n"u8);

    internal sealed record Block(string Name, Mf2ExpressionSyntax Open, Mf2ExpressionSyntax Close, int Depth)
    {
        internal int ContentStart => Open.Location.StartByte + Open.Location.LengthBytes;
        internal int ContentEnd => Close.Location.StartByte;
        internal int End => Close.Location.StartByte + Close.Location.LengthBytes;
        internal bool Leaf => Name is "runic:p" or "runic:h" or "runic:li";
    }

    // Canonical name of a built-in block element, or null for anything else.
    internal static string? BlockName(string? name) => name switch
    {
        "p" or "runic:p" => "runic:p",
        "h" or "runic:h" => "runic:h",
        "ul" or "runic:ul" => "runic:ul",
        "ol" or "runic:ol" => "runic:ol",
        "li" or "runic:li" => "runic:li",
        _ => null,
    };

    // Balanced block pairs in source order of their open tags.
    internal static IReadOnlyList<Block> Blocks(Mf2SyntaxDocument syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        var result = new List<Block>();
        var open = new List<(string Name, Mf2ExpressionSyntax Expression)>();
        foreach (Mf2ExpressionSyntax expression in syntax.Expressions.OrderBy(item => item.Location.StartByte))
        {
            if (BlockName(expression.MarkupName) is not { } name) continue;
            if (expression.MarkupKind == Mf2MarkupKind.Open) open.Add((name, expression));
            else if (expression.MarkupKind == Mf2MarkupKind.Close)
            {
                int index = open.FindLastIndex(item => item.Name == name);
                if (index < 0) continue;
                result.Add(new Block(name, open[index].Expression, expression, index));
                open.RemoveRange(index, open.Count - index);
            }
        }
        return result.OrderBy(item => item.Open.Location.StartByte).ToArray();
    }

    // Visible heading text: pattern text with whitespace collapsed; placeholders keep their source.
    // Variables in markup options, such as a link target, are not visible text and stay out.
    internal static string HeadingText(Mf2SyntaxDocument syntax, Block block)
    {
        var text = new StringBuilder();
        TextSourceLocation[] markup = syntax.Expressions.Where(item => item.MarkupName is not null).Select(item => item.Location).ToArray();
        foreach (Mf2SyntaxToken token in syntax.Tokens)
        {
            int start = token.Location.StartByte;
            if (start < block.ContentStart || start >= block.ContentEnd) continue;
            if (markup.Any(location => location.StartByte <= start && start < location.StartByte + location.LengthBytes)) continue;
            if (token.Kind is Mf2SyntaxTokenKind.Text or Mf2SyntaxTokenKind.Whitespace) text.Append(token.Value);
            else if (token.Kind == Mf2SyntaxTokenKind.Variable) text.Append('{').Append('$').Append(token.Value).Append('}');
        }
        return string.Join(' ', text.ToString().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    internal static string Describe(Block block)
    {
        string name = block.Name.Substring(6);
        IEnumerable<string> options = block.Open.Options.Where(option => option.Value is not null)
            .Select(option => option.Name + "=" + (option.Value!.Kind == Mf2OperandKind.Variable ? "$" : string.Empty) + option.Value.Value);
        return string.Join(' ', new[] { name }.Concat(options));
    }

    // Message byte spans [Start, End) of line-break runs inside document leaves that become a
    // space between two Thai, Lao, Khmer or Myanmar characters (RTR0078). Joining the lines
    // removes the run.
    internal static IEnumerable<(int Start, int End)> SoutheastAsianBreaks(Mf2SyntaxDocument syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        byte[] bytes = syntax.Source.GetUtf8Bytes();
        foreach (Block leaf in Blocks(syntax).Where(item => item.Leaf))
        {
            foreach (Mf2SyntaxToken token in syntax.Tokens.Where(item => item.Kind == Mf2SyntaxTokenKind.Text &&
                item.Location.StartByte >= leaf.ContentStart && item.Location.StartByte + item.Location.LengthBytes <= leaf.ContentEnd))
            {
                string value = token.Raw;
                int at = 0;
                while (at < value.Length)
                {
                    if (!IsLineSpace(value[at])) { at++; continue; }
                    int start = at;
                    bool newline = false;
                    while (at < value.Length && IsLineSpace(value[at])) { newline |= value[at] is '\r' or '\n'; at++; }
                    if (!newline || start == 0 || at == value.Length) continue;
                    int left = char.IsLowSurrogate(value[start - 1]) && start > 1 ? char.ConvertToUtf32(value[start - 2], value[start - 1]) : value[start - 1];
                    int right = char.IsHighSurrogate(value[at]) && at + 1 < value.Length ? char.ConvertToUtf32(value[at], value[at + 1]) : value[at];
                    if (!Rmf2DocumentProfileV5.SoutheastAsian(left) || !Rmf2DocumentProfileV5.SoutheastAsian(right)) continue;
                    int from = token.Location.StartByte + Encoding.UTF8.GetByteCount(value.AsSpan(0, start));
                    int to = token.Location.StartByte + Encoding.UTF8.GetByteCount(value.AsSpan(0, at));
                    if (to <= bytes.Length) yield return (from, to);
                }
            }
        }

        static bool IsLineSpace(char value) => value is ' ' or '\t' or '\r' or '\n';
    }

    // Canonical document layout (profile section 5, rule 5): one block per line and two spaces
    // of indentation per list level; leaf contents are kept byte for byte. Returns null when
    // the message is not a plain document pattern (declarations, .match, quoted patterns, text
    // between blocks or unbalanced tags), so the caller leaves it unchanged.
    internal static string? Layout(Mf2SyntaxDocument syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        string message = Encoding.UTF8.GetString(syntax.Source.GetUtf8Bytes());
        if (!syntax.Success || syntax.Declarations.Count != 0 || syntax.Match is not null || syntax.Variants.Count > 1 ||
            message.TrimStart().StartsWith("{{", StringComparison.Ordinal)) return null;
        IReadOnlyList<Block> blocks = Blocks(syntax);
        if (blocks.Count == 0) return null;
        byte[] bytes = syntax.Source.GetUtf8Bytes();
        // Every markup tag outside a leaf must be a block tag that belongs to a balanced pair.
        var paired = new HashSet<int>(blocks.SelectMany(item => new[] { item.Open.Location.StartByte, item.Close.Location.StartByte }));
        IEnumerable<Block> leaves = blocks.Where(item => item.Leaf);
        bool InLeaf(int at) => leaves.Any(leaf => leaf.ContentStart <= at && at < leaf.ContentEnd);
        foreach (Mf2ExpressionSyntax expression in syntax.Expressions)
            if (!InLeaf(expression.Location.StartByte) && !paired.Contains(expression.Location.StartByte)) return null;
        var lines = new List<string>();
        int position = 0;
        if (!Emit(blocks.Where(item => item.Depth == 0)) || !Whitespace(position, bytes.Length)) return null;
        return string.Join('\n', lines);

        bool Emit(IEnumerable<Block> siblings)
        {
            foreach (Block block in siblings)
            {
                if (!Whitespace(position, block.Open.Location.StartByte)) return false;
                string indent = new(' ', block.Depth * 2);
                if (block.Leaf)
                {
                    lines.Add(indent + Slice(block.Open.Location.StartByte, block.End));
                    position = block.End;
                    continue;
                }
                lines.Add(indent + Slice(block.Open.Location.StartByte, block.ContentStart));
                position = block.ContentStart;
                if (!Emit(blocks.Where(item => item.Depth == block.Depth + 1 &&
                    item.Open.Location.StartByte >= block.ContentStart && item.End <= block.ContentEnd))) return false;
                if (!Whitespace(position, block.ContentEnd)) return false;
                lines.Add(indent + Slice(block.ContentEnd, block.End));
                position = block.End;
            }
            return true;
        }
        string Slice(int from, int to) => Encoding.UTF8.GetString(bytes, from, to - from);
        bool Whitespace(int from, int to) => from <= to && bytes.AsSpan(from, to - from).IndexOfAnyExcept(LineSpace) < 0;
    }

    // Block names, block tags and complete leaves in order: equal signatures mean two layouts
    // differ only in whitespace between blocks, which the document profile ignores.
    internal static string Signature(Mf2SyntaxDocument syntax)
    {
        byte[] bytes = syntax.Source.GetUtf8Bytes();
        return string.Join('\u0000', Blocks(syntax).Select(block => block.Leaf
            ? Encoding.UTF8.GetString(bytes, block.Open.Location.StartByte, block.End - block.Open.Location.StartByte)
            : Encoding.UTF8.GetString(bytes, block.Open.Location.StartByte, block.Open.Location.LengthBytes) + "\u0001" +
              Encoding.UTF8.GetString(bytes, block.Close.Location.StartByte, block.Close.Location.LengthBytes)));
    }

    internal static string Level(Block block) =>
        block.Open.Options.FirstOrDefault(option => option.Name == "level")?.Value?.Value ?? "?";
}
