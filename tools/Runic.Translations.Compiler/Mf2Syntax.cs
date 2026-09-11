using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Runic.Translations.Compiler;

/// <summary>Lossless lexical categories; text and trivia are never discarded.</summary>
public enum Mf2SyntaxTokenKind { Text, Whitespace, PatternStart, PatternEnd, ExpressionStart, ExpressionEnd, Variable, Literal, Function, Attribute, MarkupOpen, MarkupClose, Standalone, Equals, Name, Invalid }
/// <summary>One exact source slice, including its original spelling.</summary>
public sealed record Mf2SyntaxToken(Mf2SyntaxTokenKind Kind, string Raw, string Value, TextSourceLocation Location);
/// <summary>MF2 operands retain whether a value is a variable, number or string literal.</summary>
public enum Mf2OperandKind { Variable, Number, String }
public sealed record Mf2OperandSyntax(Mf2OperandKind Kind, string Value, bool Quoted, TextSourceLocation Location);
/// <summary>Attributes are distinct from function/markup options and may be valueless.</summary>
public sealed record Mf2PropertySyntax(string Name, Mf2OperandSyntax? Value, bool IsAttribute, TextSourceLocation Location);
public enum Mf2MarkupKind { None, Open, Close, Standalone }
/// <summary>A syntax expression before function resolution or renderer-profile validation.</summary>
public sealed record Mf2ExpressionSyntax(Mf2OperandSyntax? Operand, string? Function, string? MarkupName,
    Mf2MarkupKind MarkupKind, IReadOnlyList<Mf2PropertySyntax> Options, IReadOnlyList<Mf2PropertySyntax> Attributes,
    TextSourceLocation Location);
/// <summary>A declaration retains its expression and exact source symbol.</summary>
public sealed record Mf2VariantSyntax(IReadOnlyList<string> Keys, TextSourceLocation Location);
public sealed record Mf2MatchSyntax(IReadOnlyList<string> Selectors, TextSourceLocation Location);
public sealed record Mf2DeclarationSyntax(string Kind, string Name, TextSourceLocation NameLocation,
    Mf2ExpressionSyntax Expression, TextSourceLocation Location);
/// <summary>Full original bytes, a contiguous token stream and parsed expressions for authoring tools.</summary>
public sealed class Mf2SyntaxDocument
{
    internal Mf2SyntaxDocument(TranslationSource source, List<Mf2SyntaxToken> tokens, List<Mf2ExpressionSyntax> expressions,
        List<Mf2DeclarationSyntax> declarations, List<Mf2VariantSyntax> variants, Mf2MatchSyntax? match, TranslationDiagnostic[] diagnostics)
    { Source = source; Tokens = tokens.AsReadOnly(); Expressions = expressions.AsReadOnly(); Declarations = declarations.AsReadOnly(); Variants = variants.AsReadOnly(); Match = match; Diagnostics = Array.AsReadOnly(diagnostics); }
    public TranslationSource Source { get; }
    public IReadOnlyList<Mf2SyntaxToken> Tokens { get; }
    public IReadOnlyList<Mf2ExpressionSyntax> Expressions { get; }
    public IReadOnlyList<Mf2DeclarationSyntax> Declarations { get; }
    public IReadOnlyList<Mf2VariantSyntax> Variants { get; }
    public Mf2MatchSyntax? Match { get; }
    public IReadOnlyList<TranslationDiagnostic> Diagnostics { get; }
    /// <summary>Finds semantic variable occurrences, excluding quoted literals and pattern text.</summary>
    public IReadOnlyList<TextSourceLocation> VariableReferences(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var found = new Dictionary<int, TextSourceLocation>();
        foreach (var declaration in Declarations)
            if (declaration.Name == name) found[declaration.NameLocation.StartByte] = declaration.NameLocation;
        foreach (var expression in Expressions)
        {
            Add(expression.Operand);
            foreach (var property in expression.Options.Concat(expression.Attributes)) Add(property.Value);
        }
        if (Match is not null)
        {
            string text = StrictJsonParser.StrictUtf8.GetString(Source.Bytes, Match.Location.StartByte, Match.Location.LengthBytes);
            foreach (Match variable in Regex.Matches(text, @"\$[^\s]+"))
                if (variable.Value.Substring(1) == name)
                {
                    int from = Match.Location.StartByte + StrictJsonParser.StrictUtf8.GetByteCount(text.AsSpan(0, variable.Index));
                    found[from] = DiagnosticBag.Location(Source, new ByteSpan(from, StrictJsonParser.StrictUtf8.GetByteCount(variable.Value)));
                }
        }
        return Array.AsReadOnly(found.Values.OrderBy(location => location.StartByte).ToArray());
        void Add(Mf2OperandSyntax? operand) { if (operand?.Kind == Mf2OperandKind.Variable && operand.Value == name) found[operand.Location.StartByte] = operand.Location; }
    }
    public bool Success => !Diagnostics.Any(d => d.Severity == TranslationDiagnosticSeverity.Error);
}

/// <summary>Reads MF2 source independently of executable functions and balanced-inline markup rules.</summary>
public static class Mf2SyntaxReader
{
    public static Mf2SyntaxDocument Read(TranslationSource source, TranslationCompilerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); options ??= new TranslationCompilerOptions();
        return new Reader(source, options, cancellationToken).Read();
    }
    /// <summary>Validates Runic's balanced inline profile without changing the MF2 syntax tree.</summary>
    public static IReadOnlyList<TranslationDiagnostic> ValidateInlineProfile(Mf2SyntaxDocument syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        var diagnostics = new DiagnosticBag(); var stack = new Stack<Mf2ExpressionSyntax>();
        int expressionIndex = 0;
        foreach (var token in syntax.Tokens)
        {
            if (token.Kind == Mf2SyntaxTokenKind.PatternEnd) Finish();
            if (token.Kind != Mf2SyntaxTokenKind.ExpressionStart || expressionIndex >= syntax.Expressions.Count) continue;
            var expression = syntax.Expressions[expressionIndex++];
            if (expression.MarkupKind == Mf2MarkupKind.Open) stack.Push(expression);
            else if (expression.MarkupKind == Mf2MarkupKind.Close)
            {
                if (stack.Count == 0 || stack.Peek().MarkupName != expression.MarkupName)
                    Error("The inline renderer requires balanced, non-overlapping markup.", expression.Location);
                else stack.Pop();
            }
        }
        Finish(); return Array.AsReadOnly(diagnostics.ToSortedArray());
        void Finish() { while (stack.TryPop(out var opening)) Error("The inline renderer requires a closing tag for '" + opening.MarkupName + "'.", opening.Location); }
        void Error(string message, TextSourceLocation location) => diagnostics.Add("RTR0061", TranslationDiagnosticSeverity.Error, message, location);
    }

    private sealed class Reader
    {
        private readonly TranslationSource _source;
        private readonly TranslationCompilerOptions _options;
        private readonly CancellationToken _cancellation;
        private readonly DiagnosticBag _diagnostics = new();
        private readonly List<Mf2SyntaxToken> _tokens = new();
        private readonly List<Mf2ExpressionSyntax> _expressions = new();
        private readonly List<Mf2DeclarationSyntax> _declarations = new();
        private readonly List<Mf2VariantSyntax> _variants = new();
        private readonly List<int> _lineStarts = new() { 0 };
        private Mf2MatchSyntax? _match;
        private string _text = "";
        private int[] _bytes = Array.Empty<int>();
        private int _at;
        internal Reader(TranslationSource source, TranslationCompilerOptions options, CancellationToken cancellation)
        { _source = source; _options = options; _cancellation = cancellation; }
        internal Mf2SyntaxDocument Read()
        {
            _cancellation.ThrowIfCancellationRequested();
            if (_source.Bytes.Length > _options.MaximumValueBytes) { _diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "MF2 source exceeds the configured byte limit.", _source, new ByteSpan(0, 0)); return Result(); }
            try { _text = StrictJsonParser.StrictUtf8.GetString(_source.Bytes); }
            catch (DecoderFallbackException) { _diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "MF2 requires valid UTF-8.", _source, new ByteSpan(0, 0)); return Result(); }
            _bytes = new int[_text.Length + 1];
            for (int i = 0, b = 0; i < _text.Length;) { Rune rune = Rune.GetRuneAt(_text, i); for (int j = 0; j < rune.Utf16SequenceLength; j++) _bytes[i + j] = b; i += rune.Utf16SequenceLength; b += rune.Utf8SequenceLength; _bytes[i] = b; }
            for (int i = 0; i < _text.Length; i++) if (_text[i] == '\n') _lineStarts.Add(i + 1);
            while (_at < _text.Length)
            {
                _cancellation.ThrowIfCancellationRequested(); int start = _at;
                if (Starts("{{")) { _at += 2; Token(Mf2SyntaxTokenKind.PatternStart, start); }
                else if (Starts("}}")) { _at += 2; Token(Mf2SyntaxTokenKind.PatternEnd, start); }
                else if (_text[_at] == '{') ReadExpression();
                else
                {
                    do { if (_text[_at] == '\\' && _at + 1 < _text.Length) _at++; _at++; }
                    while (_at < _text.Length && _text[_at] != '{' && !Starts("}}"));
                    Token(Mf2SyntaxTokenKind.Text, start);
                }
            }
            // Only declaration prefixes directly preceding an expression qualify. Text in
            // quoted patterns is preserved as text, even when it resembles a directive.
            int quotedDepth = 0;
            var expressionsByByte = _expressions.ToDictionary(e => e.Location.StartByte);
            foreach (var token in _tokens)
            {
                if (quotedDepth == 0 && token.Kind == Mf2SyntaxTokenKind.Text)
                {
                    var matchLine = Regex.Match(token.Raw, @"(?m)^[ \t]*\.match[ \t]+([^\r\n]*)");
                    if (matchLine.Success)
                    {
                        var selectors = matchLine.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        int tokenStart = Array.BinarySearch(_bytes, token.Location.StartByte);
                        _match = new Mf2MatchSyntax(Array.AsReadOnly(selectors.Select(v => v.TrimStart('$')).ToArray()), Location(tokenStart + matchLine.Index, tokenStart + matchLine.Index + matchLine.Length));
                    }
                }
                if (token.Kind == Mf2SyntaxTokenKind.PatternStart)
                {
                    if (quotedDepth == 0 && _match is not null && token.Location.StartByte > _match.Location.StartByte)
                    {
                        int patternPosition = Array.BinarySearch(_bytes, token.Location.StartByte);
                        int patternLineStart = _text.LastIndexOf('\n', Math.Max(0, patternPosition - 1)) + 1;
                        string keys = _text.Substring(patternLineStart, patternPosition - patternLineStart).Trim();
                        var values = Regex.Matches(keys, @"\|(?:\\.|[^|])*\||[^\s]+").Select(m => m.Value).ToArray();
                        _variants.Add(new Mf2VariantSyntax(Array.AsReadOnly(values), Location(patternLineStart, patternPosition)));
                    }
                    quotedDepth++;
                }
                if (token.Kind == Mf2SyntaxTokenKind.PatternEnd) quotedDepth--;
                if (quotedDepth != 0 || token.Kind != Mf2SyntaxTokenKind.ExpressionStart) continue;
                var expression = expressionsByByte[token.Location.StartByte];
                int position = Array.BinarySearch(_bytes, token.Location.StartByte), lineStart = _text.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
                string prefix = _text.Substring(lineStart, position - lineStart);
                Match match = Regex.Match(prefix, @"^\s*\.(input|local)\b\s*(?:\$([^\s=]+)\s*=\s*)?$");
                if (!match.Success) continue;
                string kind = match.Groups[1].Value;
                string? name = kind == "input" ? expression.Operand?.Kind == Mf2OperandKind.Variable ? expression.Operand.Value : null : match.Groups[2].Value;
                if (string.IsNullOrEmpty(name)) { Error("Declaration requires a variable name.", lineStart, position); continue; }
                TextSourceLocation nameLocation = kind == "input" ? expression.Operand!.Location : Location(lineStart + match.Groups[2].Index - 1, lineStart + match.Groups[2].Index + match.Groups[2].Length);
                _declarations.Add(new Mf2DeclarationSyntax(kind, name, nameLocation, expression, DiagnosticBag.Location(_source, new ByteSpan(_bytes[lineStart], expression.Location.StartByte + expression.Location.LengthBytes - _bytes[lineStart]))));
            }
            return Result();
        }
        private void ReadExpression()
        {
            int start = _at++; Token(Mf2SyntaxTokenKind.ExpressionStart, start); Space();
            Mf2OperandSyntax? operand = null; string? function = null, markup = null; var kind = Mf2MarkupKind.None;
            var options = new List<Mf2PropertySyntax>(); var attributes = new List<Mf2PropertySyntax>();
            if (_at < _text.Length && _text[_at] is '#' or '/')
            { int mark = _at; kind = _text[_at++] == '#' ? Mf2MarkupKind.Open : Mf2MarkupKind.Close; markup = Name(); Token(kind == Mf2MarkupKind.Open ? Mf2SyntaxTokenKind.MarkupOpen : Mf2SyntaxTokenKind.MarkupClose, mark, markup); }
            else if (_at < _text.Length && _text[_at] is not (':' or '}' or '@')) operand = Operand();
            Space();
            if (kind == Mf2MarkupKind.None && _at < _text.Length && _text[_at] == ':') { int from = _at++; function = Name(); Token(Mf2SyntaxTokenKind.Function, from, function); }
            while (_at < _text.Length && _text[_at] != '}')
            {
                _cancellation.ThrowIfCancellationRequested(); Space(); if (_at == _text.Length || _text[_at] == '}') break;
                int from = _at;
                if (_text[_at] == '/' && kind == Mf2MarkupKind.Open) { _at++; kind = Mf2MarkupKind.Standalone; Token(Mf2SyntaxTokenKind.Standalone, from); Space(); if (_at < _text.Length && _text[_at] != '}') Error("Standalone marker must end the expression.", from, _at); continue; }
                bool attribute = _text[_at] == '@'; if (attribute) _at++;
                string name = Name();
                if (_at == from || (attribute && _at == from + 1)) { _at = Math.Max(_at, from + 1); Token(Mf2SyntaxTokenKind.Invalid, from); Error("Expected an option or attribute name.", from, _at); continue; }
                Token(attribute ? Mf2SyntaxTokenKind.Attribute : Mf2SyntaxTokenKind.Name, from, name); Space();
                Mf2OperandSyntax? value = null;
                if (_at < _text.Length && _text[_at] == '=') { int equals = _at++; Token(Mf2SyntaxTokenKind.Equals, equals); Space(); value = Operand(); }
                else if (!attribute) Error("Options require a value.", from, _at);
                var target = attribute ? attributes : options;
                if (target.Any(p => p.Name == name)) Error("Duplicate " + (attribute ? "attribute" : "option") + " '" + name + "'.", from, _at);
                target.Add(new Mf2PropertySyntax(name, value, attribute, Location(from, _at)));
            }
            if (_at < _text.Length) { int close = _at++; Token(Mf2SyntaxTokenKind.ExpressionEnd, close); }
            else Error("Unterminated MF2 expression.", start, Math.Min(start + 1, _text.Length));
            if (kind == Mf2MarkupKind.None && operand is null && function is null) Error("An expression requires an operand or function.", start, _at);
            _expressions.Add(new Mf2ExpressionSyntax(operand, function, markup, kind, options.AsReadOnly(), attributes.AsReadOnly(), Location(start, _at)));
        }
        private Mf2OperandSyntax Operand()
        {
            int start = _at; var value = new StringBuilder(); bool quoted = _at < _text.Length && _text[_at] == '|';
            Mf2OperandKind kind = Mf2OperandKind.String;
            if (quoted)
            {
                _at++; bool closed = false;
                while (_at < _text.Length)
                {
                    char ch = _text[_at++]; if (ch == '|') { closed = true; break; }
                    if (ch == '\\' && _at < _text.Length) ch = _text[_at++]; value.Append(ch);
                }
                if (!closed) Error("Unterminated quoted literal.", start, _at);
            }
            else if (_at < _text.Length && _text[_at] == '$') { _at++; value.Append(Name()); kind = Mf2OperandKind.Variable; }
            else
            {
                while (_at < _text.Length && !char.IsWhiteSpace(_text[_at]) && _text[_at] is not ('}' or '{' or '@' or '=' or '/')) value.Append(_text[_at++]);
                if (Regex.IsMatch(value.ToString(), @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$")) kind = Mf2OperandKind.Number;
            }
            if (start == _at || (kind == Mf2OperandKind.Variable && value.Length == 0)) Error("Expected a literal or variable operand.", start, _at);
            Token(kind == Mf2OperandKind.Variable ? Mf2SyntaxTokenKind.Variable : Mf2SyntaxTokenKind.Literal, start, value.ToString());
            return new Mf2OperandSyntax(kind, value.ToString(), quoted, Location(start, _at));
        }
        private string Name()
        {
            int start = _at;
            while (_at < _text.Length && (char.IsLetterOrDigit(_text[_at]) || _text[_at] is '_' or '-' or ':' || char.GetUnicodeCategory(_text[_at]) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)) _at++;
            return _text.Substring(start, _at - start);
        }
        private void Space() { int start = _at; while (_at < _text.Length && char.IsWhiteSpace(_text[_at])) _at++; if (_at > start) Token(Mf2SyntaxTokenKind.Whitespace, start); }
        private bool Starts(string value) => _text.AsSpan(_at).StartsWith(value, StringComparison.Ordinal);
        private void Token(Mf2SyntaxTokenKind kind, int from, string? value = null) { string raw = _text.Substring(from, _at - from); _tokens.Add(new Mf2SyntaxToken(kind, raw, value ?? raw, Location(from, _at))); }
        private TextSourceLocation Location(int from, int to)
        {
            int line = _lineStarts.BinarySearch(from); if (line < 0) line = ~line - 1;
            int endLine = _lineStarts.BinarySearch(to); if (endLine < 0) endLine = ~endLine - 1;
            return new TextSourceLocation(_source.Path, _bytes[from], _bytes[to] - _bytes[from], line + 1, from - _lineStarts[line] + 1, endLine + 1, to - _lineStarts[endLine] + 1);
        }
        private void Error(string message, int from, int to) => _diagnostics.Add("RTR0066", TranslationDiagnosticSeverity.Error, message, Location(from, to));
        private Mf2SyntaxDocument Result() => new(_source, _tokens, _expressions, _declarations, _variants, _match, _diagnostics.ToSortedArray());
    }
}
