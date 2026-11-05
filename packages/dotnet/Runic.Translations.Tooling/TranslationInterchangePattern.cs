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

/// <summary>One variant of a selector message: its keys as written (<see langword="null"/> for the single pattern of a
/// message with declarations and no <c>.match</c>) and the pieces of its pattern.</summary>
internal sealed record TranslationInterchangeVariant(string? Keys, IReadOnlyList<TranslationInterchangePiece> Pieces);

/// <summary>
/// A message with declarations or a <c>.match</c> whose patterns are text and placeholders. XLIFF carries the header
/// (declarations and the <c>.match</c> statement) as one protected <c>&lt;ph&gt;</c> and every variant as a
/// <c>&lt;pc&gt;</c> whose start and end codes are its keys and pattern quotes, so translators edit only the text.
/// </summary>
/// <param name="Header">The declarations and the <c>.match</c> statement as written, without trailing whitespace.</param>
/// <param name="Display">A short display for the header: its last line.</param>
/// <param name="PluralFunction"><c>plural</c> or <c>ordinal</c> when the message has exactly one selector and it selects by plural category.</param>
internal sealed record TranslationInterchangeSelector(string Header, string Display, IReadOnlyList<TranslationInterchangeVariant> Variants, string? PluralFunction)
{
    /// <summary>Whether the plural selector's operand is an integer (<c>:integer</c>), which reaches fewer categories.</summary>
    internal bool IntegerOperand { get; init; }

    /// <summary>Every placeholder piece of every variant, in order.</summary>
    internal IEnumerable<TranslationInterchangePiece> Pieces => Variants.SelectMany(static variant => variant.Pieces);
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
        string text = Body(pieces);
        return NeedsQuoting(pieces) ? "{{" + text + "}}" : text;
    }

    // The pattern text of pieces: text escaped, placeholders verbatim, without quoting.
    private static string Body(IReadOnlyList<TranslationInterchangePiece> pieces)
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
        return body.ToString();
    }

    /// <summary>
    /// The selector view of a message with declarations or a <c>.match</c>, or <see langword="null"/> when it is plain,
    /// invalid, or has markup, or a pattern reads something a standalone pattern cannot (such as a dynamic option).
    /// </summary>
    internal static TranslationInterchangeSelector? DecomposeSelector(string message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        var source = new TranslationSource(Path, bytes);
        Mf2SyntaxDocument syntax = Mf2SyntaxReader.Read(source);
        if (!syntax.Success || (syntax.Declarations.Count == 0 && syntax.Match is null)) return null;
        Rmf2MessageV5? compiled = Rmf2SemanticCompilerV5.Compile(source).Message;
        if (compiled is null || compiled.Variants.Any(static variant => variant.Nodes.Any(static node => node is not Rmf2TextV5 and not Rmf2ExpressionNodeV5))) return null;
        var spans = new List<(string? Keys, int Start, int End)>();
        int headerEnd;
        if (syntax.Match is null)
        {
            Mf2SyntaxToken? opening = syntax.Tokens.FirstOrDefault(static token => token.Kind == Mf2SyntaxTokenKind.PatternStart);
            Mf2SyntaxToken? closing = syntax.Tokens.LastOrDefault(static token => token.Kind == Mf2SyntaxTokenKind.PatternEnd);
            if (opening is null || closing is null) return null;
            headerEnd = opening.Location.StartByte;
            spans.Add((null, opening.Location.StartByte + 2, closing.Location.StartByte));
        }
        else
        {
            if (syntax.Variants.Count == 0 || syntax.Variants.Count != compiled.Variants.Count) return null;
            headerEnd = syntax.Variants[0].KeyLocations.Count == 0 ? syntax.Variants[0].Location.StartByte : syntax.Variants[0].KeyLocations[0].StartByte;
            foreach (Mf2VariantSyntax variant in syntax.Variants)
            {
                TextSourceLocation pattern = variant.PatternLocation;
                if (pattern.LengthBytes < 4) return null;
                spans.Add((string.Join(' ', variant.Keys), pattern.StartByte + 2, pattern.StartByte + pattern.LengthBytes - 2));
            }
        }
        var variants = new List<TranslationInterchangeVariant>(spans.Count);
        foreach ((string? keys, int start, int end) in spans)
        {
            if (end < start) return null;
            string inner = Encoding.UTF8.GetString(bytes, start, end - start);
            IReadOnlyList<TranslationInterchangePiece>? pieces = inner.Length == 0 ? [] : Decompose("{{" + inner + "}}");
            if (pieces is null) return null;
            variants.Add(new(keys, pieces));
        }
        string header = Encoding.UTF8.GetString(bytes, 0, headerEnd).TrimEnd();
        if (header.Length == 0) return null;
        string display = header.Split('\n')[^1].Trim();
        string? plural = compiled.Selectors.Count == 1 && compiled.Selectors[0].Function is "plural" or "ordinal" ? compiled.Selectors[0].Function : null;
        return new(header, display, variants, plural) { IntegerOperand = plural is not null && compiled.Selectors[0].Type == "int64" };
    }

    /// <summary>Writes a selector message: the header, then each variant on its own line.</summary>
    internal static string Compose(TranslationInterchangeSelector selector) =>
        selector.Header + "\n" + string.Join("\n", selector.Variants.Select(static variant =>
            (variant.Keys is null ? "" : variant.Keys + " ") + "{{" + Body(variant.Pieces) + "}}"));

    // One MF2 key: the catch-all, a |quoted| literal, or an unquoted name or number literal.
    private static readonly System.Text.RegularExpressions.Regex VariantKeyList = new(
        @"^\s*(?:\*|\|(?:[^\\|]|\\[\\|{}])*\||[^\s{}|\\*]+)(?:\s+(?:\*|\|(?:[^\\|]|\\[\\|{}])*\||[^\s{}|\\*]+))*\s*$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Whether a string is a list of MF2 variant keys and nothing else: no braces, patterns or expressions.</summary>
    internal static bool IsVariantKeyList(string keys) => keys.Length <= 4096 && VariantKeyList.IsMatch(keys);

    /// <summary>Whether two selector views have the same header, the same keys in order and the same variant texts.</summary>
    internal static bool SameSelector(TranslationInterchangeSelector left, TranslationInterchangeSelector right) =>
        CanonicalHeader(left.Header) == CanonicalHeader(right.Header) && left.Variants.Count == right.Variants.Count &&
        left.Variants.Zip(right.Variants).All(static pair => CanonicalKeys(pair.First.Keys) == CanonicalKeys(pair.Second.Keys) &&
            Compose(Canonical(pair.First.Pieces)) == Compose(Canonical(pair.Second.Pieces)));

    /// <summary>Header spellings compare without the whitespace that only separates syntax.</summary>
    internal static string CanonicalHeader(string header) => string.Join(' ', header.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Keys compare as written, with separating whitespace collapsed.</summary>
    internal static string? CanonicalKeys(string? keys) => keys is null ? null : string.Join(' ', keys.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The text of the pieces with every placeholder shown as its expression; for comparisons and fingerprints.</summary>
    internal static string Canonical(string pattern) =>
        Decompose(pattern) is { } pieces ? Compose(pieces) : pattern;

    /// <summary>
    /// The canonical spelling of one placeholder expression: whitespace collapsed to single separators, string
    /// literals quoted, options and attributes sorted by name. <c>{ $count  :integer }</c> and <c>{$count :integer}</c>
    /// are the same; a different operand, function or option is not. An expression that does not parse is returned as is.
    /// </summary>
    internal static string CanonicalExpression(string expression)
    {
        Mf2SyntaxDocument syntax = Mf2SyntaxReader.Read(new TranslationSource(Path, Encoding.UTF8.GetBytes(expression)));
        Mf2ExpressionSyntax[] expressions = syntax.Expressions.ToArray();
        if (!syntax.Success || expressions.Length != 1 || expressions[0].MarkupKind != Mf2MarkupKind.None) return expression;
        Mf2ExpressionSyntax parsed = expressions[0];
        var parts = new List<string>();
        if (parsed.Operand is { } operand) parts.Add(Operand(operand));
        if (parsed.Function is { } function) parts.Add(":" + function);
        foreach (Mf2PropertySyntax option in parsed.Options.OrderBy(static option => option.Name, StringComparer.Ordinal))
            parts.Add(option.Name + "=" + (option.Value is { } value ? Operand(value) : string.Empty));
        foreach (Mf2PropertySyntax attribute in parsed.Attributes.OrderBy(static attribute => attribute.Name, StringComparer.Ordinal))
            parts.Add("@" + attribute.Name + (attribute.Value is { } value ? "=" + Operand(value) : string.Empty));
        return "{" + string.Join(' ', parts) + "}";
    }

    /// <summary>
    /// The canonical form of a whole message for fingerprints: a plain message is its pattern with canonical
    /// expressions; any other message is its token stream without the whitespace that only separates syntax (pattern
    /// text, including its spaces, is kept).
    /// </summary>
    internal static string CanonicalMessage(string message)
    {
        if (Decompose(message) is { } pieces) return Compose(Canonical(pieces));
        Mf2SyntaxDocument syntax = Mf2SyntaxReader.Read(new TranslationSource(Path, Encoding.UTF8.GetBytes(message)));
        if (!syntax.Success) return message;
        var canonical = new StringBuilder();
        foreach (Mf2SyntaxToken token in syntax.Tokens)
        {
            if (token.Kind == Mf2SyntaxTokenKind.Whitespace) continue;
            canonical.Append((char)('A' + (int)token.Kind)).Append(token.Kind == Mf2SyntaxTokenKind.Literal ? token.Value : token.Raw).Append('\u0000');
        }
        return canonical.ToString();
    }

    /// <summary>The pieces with every expression in its canonical spelling.</summary>
    internal static IReadOnlyList<TranslationInterchangePiece> Canonical(IReadOnlyList<TranslationInterchangePiece> pieces) =>
        pieces.Select(static piece => piece.IsText ? piece : piece with { Expression = CanonicalExpression(piece.Expression!) }).ToArray();

    // A literal is always quoted: {1}, {|1|} and {|a|}, {a} are the same values.
    private static string Operand(Mf2OperandSyntax operand) => operand.Kind == Mf2OperandKind.Variable
        ? "$" + operand.Value
        : "|" + operand.Value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal) + "|";

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
