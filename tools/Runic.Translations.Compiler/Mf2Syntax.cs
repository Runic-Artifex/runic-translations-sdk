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
public sealed record Mf2VariantSyntax(IReadOnlyList<string> Keys, TextSourceLocation Location)
{
    public TextSourceLocation PatternLocation { get; init; } = Location;
    public IReadOnlyList<TextSourceLocation> KeyLocations { get; init; } = Array.Empty<TextSourceLocation>();
}
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
        foreach (var token in Tokens)
            if (token.Kind == Mf2SyntaxTokenKind.Variable && token.Value == name) found[token.Location.StartByte] = token.Location;
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
    /// <summary>Checks declaration and matcher constraints independently of syntax and target support.</summary>
    public static IReadOnlyList<TranslationDiagnostic> ValidateDataModel(Mf2SyntaxDocument syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        var diagnostics = new DiagnosticBag();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var locals = syntax.Declarations.Where(d => d.Kind == "local").Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var declaration in syntax.Declarations)
        {
            if (seen.Contains(declaration.Name)) Error("Duplicate declaration '" + declaration.Name + "'.", declaration.NameLocation);
            foreach (var token in syntax.Tokens.Where(t => t.Kind == Mf2SyntaxTokenKind.Variable && t.Location.StartByte >= declaration.Expression.Location.StartByte && t.Location.StartByte < declaration.Expression.Location.StartByte + declaration.Expression.Location.LengthBytes))
                if (locals.Contains(token.Value) && !seen.Contains(token.Value)) Error("Local '" + token.Value + "' is referenced before its declaration.", token.Location);
            seen.Add(declaration.Name);
        }
        if (syntax.Match is { } match)
        {
            var variants = new HashSet<string>(StringComparer.Ordinal); bool fallback = false;
            foreach (var variant in syntax.Variants)
            {
                if (variant.Keys.Count != match.Selectors.Count) Error("Variant key count must match selector count.", variant.Location);
                string[] keys = variant.Keys.Select(key => key == "*" ? "wildcard" : "literal:" + DecodeVariantKey(key)).ToArray();
                if (!variants.Add(string.Join("", keys.Select(key => key.Length + ":" + key)))) Error("Duplicate variant keys.", variant.Location);
                fallback |= variant.Keys.Count == match.Selectors.Count && variant.Keys.All(key => key == "*");
            }
            if (!fallback) Error("A matcher requires a catch-all variant.", match.Location);
        }
        return Array.AsReadOnly(diagnostics.ToSortedArray());
        void Error(string message, TextSourceLocation location) => diagnostics.Add("RTR0067", TranslationDiagnosticSeverity.Error, message, location);
    }
    private static string DecodeVariantKey(string raw)
    {
        if (!raw.StartsWith('|')) return raw.Normalize(NormalizationForm.FormC);
        var value = new StringBuilder();
        for (int i = 1; i < raw.Length - 1; i++)
        {
            if (raw[i] == '\\') i++;
            value.Append(raw[i]);
        }
        return value.ToString().Normalize(NormalizationForm.FormC);
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
            Space();
            bool complex = Starts(".") || Starts("{{");
            while (Starts(".input") || Starts(".local")) { ReadDeclaration(); Space(); }
            if (Starts(".match")) ReadMatcher();
            else if (Starts("{{")) ReadPattern(true);
            else if (complex) { Error("A complex message requires a quoted pattern or matcher.", _at, _at); Recover(); }
            else ReadPattern(false);
            if (complex) { Space(); if (_at < _text.Length) { Error("Unexpected content after the complex message.", _at, _at + 1); Recover(); } }
            return Result();
        }
        private void ReadDeclaration()
        {
            int start = _at; bool input = Starts(".input"); _at += 6;
            Token(Mf2SyntaxTokenKind.Text, start); bool separated = Space();
            Mf2OperandSyntax? symbol = null;
            if (!input)
            {
                if (!separated) Error("A local declaration requires whitespace before its variable.", start, _at);
                symbol = Operand();
                if (symbol.Kind != Mf2OperandKind.Variable) Error("A local declaration requires a variable.", start, _at);
                Space(); int equals = _at;
                if (Starts("=")) { _at++; Token(Mf2SyntaxTokenKind.Equals, equals); }
                else Error("A local declaration requires '='.", _at, _at);
                Space();
            }
            if (!Starts("{") || Starts("{{")) { Error("A declaration requires an expression.", _at, _at); Recover(); return; }
            ReadExpression(); var expression = _expressions[^1];
            if (input) symbol = expression.Operand;
            if (symbol?.Kind != Mf2OperandKind.Variable || expression.MarkupKind != Mf2MarkupKind.None)
            { Error("Invalid declaration expression.", start, _at); return; }
            _declarations.Add(new Mf2DeclarationSyntax(input ? "input" : "local", symbol.Value, symbol.Location, expression, Location(start, _at)));
        }
        private void ReadMatcher()
        {
            int start = _at; _at += 6; Token(Mf2SyntaxTokenKind.Text, start);
            var selectors = new List<string>();
            while (true)
            {
                bool separated = Space();
                if (!Starts("$")) { if (!separated) Error("A matcher requires whitespace before its variants.", _at, _at); break; }
                if (!separated) Error("Selectors require whitespace.", _at, _at);
                selectors.Add(Operand().Value);
            }
            if (selectors.Count == 0) Error("A matcher requires variable selectors.", start, _at);
            _match = new Mf2MatchSyntax(selectors.AsReadOnly(), Location(start, _at));
            while (_at < _text.Length)
            {
                _cancellation.ThrowIfCancellationRequested(); int variantStart = _at;
                var keys = new List<string>(); var locations = new List<TextSourceLocation>();
                while (_at < _text.Length && !Starts("{{"))
                {
                    int keyStart = _at;
                    if (Starts("*")) { _at++; Token(Mf2SyntaxTokenKind.Literal, keyStart); }
                    else { var key = Operand(); if (key.Kind == Mf2OperandKind.Variable) Error("Variant keys must be literals.", keyStart, _at); }
                    if (_at == keyStart) { Recover(); break; }
                    keys.Add(_text.Substring(keyStart, _at - keyStart)); locations.Add(Location(keyStart, _at));
                    bool separated = Space();
                    if (!separated && !Starts("{{")) { Error("Variant keys require whitespace.", _at, _at); Recover(); break; }
                }
                if (keys.Count == 0) Error("A variant requires keys.", variantStart, _at);
                if (!Starts("{{")) { Error("A variant requires a quoted pattern.", _at, _at); break; }
                int patternStart = _at; ReadPattern(true);
                _variants.Add(new Mf2VariantSyntax(keys.AsReadOnly(), Location(variantStart, _at)) { PatternLocation = Location(patternStart, _at), KeyLocations = locations.AsReadOnly() });
                Space();
            }
            if (_variants.Count == 0) Error("A matcher requires variants.", start, _at);
        }
        private void ReadPattern(bool quoted)
        {
            int opening = _at;
            if (quoted) { _at += 2; Token(Mf2SyntaxTokenKind.PatternStart, opening); }
            while (_at < _text.Length)
            {
                _cancellation.ThrowIfCancellationRequested(); int start = _at;
                if (quoted && Starts("}}")) { _at += 2; Token(Mf2SyntaxTokenKind.PatternEnd, start); return; }
                if (Starts("{")) { ReadExpression(); continue; }
                do
                {
                    char ch = _text[_at++];
                    if (ch == '\\')
                    {
                        if (_at >= _text.Length || _text[_at] is not ('\\' or '{' or '}' or '|')) Error("Invalid MF2 escape.", _at - 1, _at);
                        if (_at < _text.Length) _at++;
                    }
                    else if (ch is '\0' or '}') Error("This pattern character must be escaped or removed.", _at - 1, _at);
                } while (_at < _text.Length && !Starts("{") && !(quoted && Starts("}}")));
                Token(Mf2SyntaxTokenKind.Text, start);
            }
            if (quoted) Error("Unterminated quoted pattern.", opening, Math.Min(opening + 2, _text.Length));
        }
        private void Recover() { int start = _at; _at = _text.Length; if (_at > start) Token(Mf2SyntaxTokenKind.Invalid, start); }
        private void ReadExpression()
        {
            int start = _at++; Token(Mf2SyntaxTokenKind.ExpressionStart, start); Space();
            Mf2OperandSyntax? operand = null; string? function = null, markup = null; var kind = Mf2MarkupKind.None;
            var options = new List<Mf2PropertySyntax>(); var attributes = new List<Mf2PropertySyntax>();
            if (_at < _text.Length && _text[_at] is '#' or '/')
            { int mark = _at; kind = _text[_at++] == '#' ? Mf2MarkupKind.Open : Mf2MarkupKind.Close; markup = Name(true); Token(kind == Mf2MarkupKind.Open ? Mf2SyntaxTokenKind.MarkupOpen : Mf2SyntaxTokenKind.MarkupClose, mark, markup); }
            else if (_at < _text.Length && _text[_at] is not (':' or '}' or '@')) operand = Operand();
            bool separated = Space();
            if (kind == Mf2MarkupKind.None && _at < _text.Length && _text[_at] == ':') { int from = _at++; function = Name(true); if (operand is not null && !separated) Error("A function requires whitespace after its operand.", from, _at); Token(Mf2SyntaxTokenKind.Function, from, function); separated = false; }
            while (_at < _text.Length && _text[_at] != '}')
            {
                _cancellation.ThrowIfCancellationRequested(); separated = Space() || separated; if (_at == _text.Length || _text[_at] == '}') break;
                int from = _at;
                if (_text[_at] == '/' && kind == Mf2MarkupKind.Open) { _at++; kind = Mf2MarkupKind.Standalone; Token(Mf2SyntaxTokenKind.Standalone, from); Space(); if (_at < _text.Length && _text[_at] != '}') Error("Standalone marker must end the expression.", from, _at); continue; }
                if (!separated) Error("Options and attributes require whitespace.", from, from);
                separated = false;
                bool attribute = _text[_at] == '@'; if (attribute) _at++;
                string name = Name(true);
                if (_at == from || (attribute && _at == from + 1)) { _at = Math.Max(_at, from + 1); Token(Mf2SyntaxTokenKind.Invalid, from); Error("Expected an option or attribute name.", from, _at); continue; }
                Token(attribute ? Mf2SyntaxTokenKind.Attribute : Mf2SyntaxTokenKind.Name, from, name); separated = Space();
                Mf2OperandSyntax? value = null;
                if (_at < _text.Length && _text[_at] == '=') { int equals = _at++; Token(Mf2SyntaxTokenKind.Equals, equals); Space(); value = Operand(); separated = false; if (attribute && value.Kind == Mf2OperandKind.Variable) Error("Attribute values must be literals.", from, _at); }
                else if (!attribute) Error("Options require a value.", from, _at);
                if (!attribute && (attributes.Count != 0 || (kind == Mf2MarkupKind.None && function is null))) Error("Options must follow a function or markup name and precede attributes.", from, _at);
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
                    if (ch == '\\') { if (_at >= _text.Length || _text[_at] is not ('\\' or '{' or '}' or '|')) Error("Invalid MF2 escape.", _at - 1, _at); if (_at < _text.Length) ch = _text[_at++]; } if (ch == '\0') Error("NULL is not allowed in a literal.", _at - 1, _at); value.Append(ch);
                }
                if (!closed) Error("Unterminated quoted literal.", start, _at);
            }
            else if (_at < _text.Length && _text[_at] == '$') { _at++; value.Append(Name()); kind = Mf2OperandKind.Variable; }
            else
            {
                while (_at < _text.Length && NameChar(Rune.GetRuneAt(_text, _at).Value)) { Rune rune = Rune.GetRuneAt(_text, _at); value.Append(rune.ToString()); _at += rune.Utf16SequenceLength; }
                if (Regex.IsMatch(value.ToString(), @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$")) kind = Mf2OperandKind.Number;
            }
            if (start == _at || (kind == Mf2OperandKind.Variable && value.Length == 0)) Error("Expected a literal or variable operand.", start, _at);
            Token(kind == Mf2OperandKind.Variable ? Mf2SyntaxTokenKind.Variable : Mf2SyntaxTokenKind.Literal, start, value.ToString());
            return new Mf2OperandSyntax(kind, value.ToString(), quoted, Location(start, _at));
        }
        private string Name(bool qualified = false)
        {
            int start = _at;
            if (_at < _text.Length && Bidi(_text[_at])) _at++;
            int nameStart = _at;
            if (_at < _text.Length && NameStart(Rune.GetRuneAt(_text, _at).Value)) _at += Rune.GetRuneAt(_text, _at).Utf16SequenceLength;
            else Error("Expected an MF2 name.", start, _at);
            while (_at < _text.Length && NameChar(Rune.GetRuneAt(_text, _at).Value)) _at += Rune.GetRuneAt(_text, _at).Utf16SequenceLength;
            string result = _text.Substring(nameStart, _at - nameStart);
            if (_at < _text.Length && Bidi(_text[_at])) _at++;
            if (qualified && Starts(":")) { _at++; result += ":" + Name(); }
            return result.Normalize(NormalizationForm.FormC);
        }
        private static bool NameStart(int c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '+' or '_' or >= 0xA1 and <= 0x61B or >= 0x61D and <= 0x167F or >= 0x1681 and <= 0x1FFF or >= 0x200B and <= 0x200D or >= 0x2010 and <= 0x2027 or >= 0x2030 and <= 0x205E or >= 0x2060 and <= 0x2065 or >= 0x206A and <= 0x2FFF or >= 0x3001 and <= 0xD7FF or >= 0xE000 and <= 0xFDCF or >= 0xFDF0 and <= 0xFFFD || (c >= 0x10000 && c <= 0x10FFFD && (c & 0xFFFF) <= 0xFFFD);
        private static bool NameChar(int c) => NameStart(c) || c is >= '0' and <= '9' or '-' or '.';
        private static bool Bidi(char c) => c is '\u061c' or '\u200e' or '\u200f' or >= '\u2066' and <= '\u2069';
        private bool Space()
        {
            int start = _at; bool required = false;
            while (_at < _text.Length && (_text[_at] is ' ' or '\t' or '\r' or '\n' or '\u3000' || Bidi(_text[_at]))) { required |= !Bidi(_text[_at]); _at++; }
            if (_at > start) Token(Mf2SyntaxTokenKind.Whitespace, start);
            return required;
        }
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
