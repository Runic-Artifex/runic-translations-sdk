using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tooling;

/// <summary>One part of a plain message: literal text, or one MF2 placeholder expression kept verbatim.</summary>
/// <param name="Text">The unescaped text, or <see langword="null"/> for a placeholder.</param>
/// <param name="Expression">The exact MF2 expression source, such as <c>{$name}</c> or <c>{$count :integer}</c>.</param>
/// <param name="Display">The placeholder's short display name: its variable or literal.</param>
internal sealed record TranslationInterchangePiece(string? Text, string? Expression, string? Display)
{
    internal bool IsText => Text is not null;
    internal static TranslationInterchangePiece FromText(string text) => new(text, null, null);
    internal static TranslationInterchangePiece FromExpression(string expression, string display) => new(null, expression, display);
}

/// <summary>
/// The profile v3 view of a message: a plain message (one pattern of text and placeholder expressions, without
/// declarations, selectors or markup) is a sequence of pieces. XLIFF carries the text as text and every expression
/// as one <c>&lt;ph&gt;</c> whose <c>equiv</c> is the exact expression, so the conversion is lossless both ways.
/// </summary>
internal static class TranslationInterchangePattern
{
    private const string Path = "interchange/message.mf2";

    /// <summary>The pieces of a plain message, or <see langword="null"/> for a structured or invalid one.</summary>
    internal static IReadOnlyList<TranslationInterchangePiece>? Decompose(string pattern)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(pattern);
        var source = new TranslationSource(Path, bytes);
        Mf2SyntaxDocument syntax = Mf2SyntaxReader.Read(source);
        if (!syntax.Success || syntax.Declarations.Count != 0 || syntax.Match is not null) return null;
        Rmf2MessageV5? message = Rmf2SemanticCompilerV5.Compile(source).Message;
        if (message is null || message.Declarations.Count != 0 || message.Selectors.Count != 0 || message.Variants.Count != 1)
            return null;
        IReadOnlyList<Rmf2NodeV5> nodes = message.Variants[0].Nodes;
        if (nodes.Any(static node => node is not Rmf2TextV5 and not Rmf2ExpressionNodeV5)) return null;
        Mf2ExpressionSyntax[] expressions = syntax.Expressions.Where(static expression => expression.MarkupKind == Mf2MarkupKind.None).ToArray();
        if (expressions.Length != nodes.Count(static node => node is Rmf2ExpressionNodeV5)) return null;
        var pieces = new List<TranslationInterchangePiece>();
        int next = 0;
        foreach (Rmf2NodeV5 node in nodes)
        {
            if (node is Rmf2TextV5 text)
            {
                if (text.Value.Length == 0) continue;
                if (pieces.Count != 0 && pieces[^1].IsText) pieces[^1] = TranslationInterchangePiece.FromText(pieces[^1].Text + text.Value);
                else pieces.Add(TranslationInterchangePiece.FromText(text.Value));
                continue;
            }
            Mf2ExpressionSyntax expression = expressions[next++];
            TextSourceLocation location = expression.Location;
            if (location.StartByte < 0 || location.LengthBytes <= 0 || location.StartByte + location.LengthBytes > bytes.Length) return null;
            string raw = Encoding.UTF8.GetString(bytes, location.StartByte, location.LengthBytes);
            if (!raw.StartsWith('{') || !raw.EndsWith('}')) return null;
            pieces.Add(TranslationInterchangePiece.FromExpression(raw, Display(expression, raw)));
        }
        return pieces;
    }

    /// <summary>Writes pieces as an MF2 pattern: text escaped, placeholders verbatim, quoted when framing would change it.</summary>
    internal static string Compose(IReadOnlyList<TranslationInterchangePiece> pieces)
    {
        var body = new StringBuilder();
        foreach (TranslationInterchangePiece piece in pieces)
        {
            if (!piece.IsText) { body.Append(piece.Expression); continue; }
            foreach (char character in piece.Text!)
            {
                if (character is '\\' or '{' or '}') body.Append('\\');
                body.Append(character);
            }
        }
        string text = body.ToString();
        return NeedsQuoting(pieces) ? "{{" + text + "}}" : text;
    }

    /// <summary>The text of the pieces with every placeholder shown as its expression; for comparisons and fingerprints.</summary>
    internal static string Canonical(string pattern) =>
        Decompose(pattern) is { } pieces ? Compose(pieces) : pattern;

    /// <summary>Whether the pieces are empty, or only empty text.</summary>
    internal static bool IsEmpty(IReadOnlyList<TranslationInterchangePiece> pieces) =>
        pieces.All(static piece => piece.IsText && piece.Text!.Length == 0);

    /// <summary>The variable names that the placeholders read.</summary>
    internal static IReadOnlyList<string> Variables(IReadOnlyList<TranslationInterchangePiece> pieces)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (TranslationInterchangePiece piece in pieces)
        {
            if (piece.IsText) continue;
            Mf2SyntaxDocument syntax = Mf2SyntaxReader.Read(new TranslationSource(Path, Encoding.UTF8.GetBytes(piece.Expression!)));
            foreach (Mf2SyntaxToken token in syntax.Tokens)
                if (token.Kind == Mf2SyntaxTokenKind.Variable) names.Add(token.Value.TrimStart('$'));
        }
        return names.ToArray();
    }

    /// <summary>Whether a string is exactly one MF2 placeholder expression without markup.</summary>
    internal static bool IsPlaceholderExpression(string value)
    {
        if (value.Length < 3 || value[0] != '{' || value[^1] != '}' || value.StartsWith("{{", StringComparison.Ordinal)) return false;
        IReadOnlyList<TranslationInterchangePiece>? pieces = Decompose(value);
        return pieces is { Count: 1 } && !pieces[0].IsText && pieces[0].Expression == value;
    }

    // A simple MF2 message cannot start with '.' (a keyword) and its framing trims leading and
    // trailing whitespace; a quoted pattern keeps both. An empty pattern is written {{}}.
    private static bool NeedsQuoting(IReadOnlyList<TranslationInterchangePiece> pieces)
    {
        if (pieces.Count == 0 || IsEmpty(pieces)) return true;
        TranslationInterchangePiece first = pieces[0], last = pieces[^1];
        if (first.IsText && first.Text!.Length != 0 && (first.Text[0] == '.' || char.IsWhiteSpace(first.Text[0]))) return true;
        if (last.IsText && last.Text!.Length != 0 && char.IsWhiteSpace(last.Text[^1])) return true;
        return pieces.Any(static piece => piece.IsText && piece.Text!.Contains('\n'));
    }

    private static string Display(Mf2ExpressionSyntax expression, string raw) =>
        expression.Operand is { } operand && operand.Value.Length != 0
            ? operand.Value.TrimStart('$')
            : raw.Trim('{', '}').Trim();
}
