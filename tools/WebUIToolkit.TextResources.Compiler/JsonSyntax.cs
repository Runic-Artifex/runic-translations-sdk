using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace WebUIToolkit.TextResources.Compiler;

internal enum JsonKind { Object, Array, String, Number, True, False, Null }

internal readonly struct ByteSpan
{
    internal ByteSpan(int start, int length) { Start = start; Length = length; }
    internal int Start { get; }
    internal int Length { get; }
}

internal sealed class JsonProperty
{
    internal JsonProperty(string name, ByteSpan nameSpan, JsonValue value)
    {
        Name = name;
        NameSpan = nameSpan;
        Value = value;
    }

    internal string Name { get; }
    internal ByteSpan NameSpan { get; }
    internal JsonValue Value { get; }
}

internal sealed class JsonValue
{
    internal JsonValue(JsonKind kind, ByteSpan span, string? text = null,
        IReadOnlyList<JsonProperty>? properties = null, IReadOnlyList<JsonValue>? items = null)
    {
        Kind = kind;
        Span = span;
        Text = text;
        Properties = properties ?? EmptyProperties;
        Items = items ?? EmptyItems;
    }

    private static readonly JsonProperty[] EmptyProperties = Array.Empty<JsonProperty>();
    private static readonly JsonValue[] EmptyItems = Array.Empty<JsonValue>();
    internal JsonKind Kind { get; }
    internal ByteSpan Span { get; }
    internal string? Text { get; }
    internal IReadOnlyList<JsonProperty> Properties { get; }
    internal IReadOnlyList<JsonValue> Items { get; }

    internal JsonProperty? Property(string name)
    {
        for (int i = 0; i < Properties.Count; i++)
            if (string.Equals(Properties[i].Name, name, StringComparison.Ordinal)) return Properties[i];
        return null;
    }
}

internal sealed class ParsedJson
{
    internal ParsedJson(TextResourceSource source, JsonValue? root) { Source = source; Root = root; }
    internal TextResourceSource Source { get; }
    internal JsonValue? Root { get; }
}

internal sealed class DiagnosticBag
{
    private readonly List<TextResourceDiagnostic> _items = new List<TextResourceDiagnostic>();

    internal IReadOnlyList<TextResourceDiagnostic> Items => _items;
    internal int Count => _items.Count;

    internal void Add(string id, TextResourceDiagnosticSeverity severity, string message, TextResourceSource source, ByteSpan span)
    {
        _items.Add(new TextResourceDiagnostic(id, severity, message, Location(source, span)));
    }

    internal void Add(string id, TextResourceDiagnosticSeverity severity, string message, TextSourceLocation location)
        => _items.Add(new TextResourceDiagnostic(id, severity, message, location));

    internal TextResourceDiagnostic[] ToSortedArray()
    {
        _items.Sort((left, right) =>
        {
            int comparison = StringComparer.Ordinal.Compare(left.Location.Path, right.Location.Path);
            if (comparison != 0) return comparison;
            comparison = left.Location.StartByte.CompareTo(right.Location.StartByte);
            if (comparison != 0) return comparison;
            comparison = StringComparer.Ordinal.Compare(left.Id, right.Id);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left.Message, right.Message);
        });
        return _items.ToArray();
    }

    internal static TextSourceLocation Location(TextResourceSource source, ByteSpan span)
    {
        byte[] bytes = source.Bytes;
        int start = Math.Max(0, Math.Min(span.Start, bytes.Length));
        int end = Math.Max(start, Math.Min(start + span.Length, bytes.Length));
        GetLineColumn(bytes, start, out int line, out int column);
        GetLineColumn(bytes, end, out int endLine, out int endColumn);
        return new TextSourceLocation(source.Path, start, end - start, line, column, endLine, endColumn);
    }

    private static void GetLineColumn(byte[] bytes, int offset, out int line, out int column)
    {
        line = 1;
        int lineStart = 0;
        for (int i = 0; i < offset; i++)
        {
            if (bytes[i] == (byte)'\n') { line++; lineStart = i + 1; }
        }

        int count = offset - lineStart;
        if (count > 0 && bytes[offset - 1] == (byte)'\r') count--;
        try { column = StrictJsonParser.StrictUtf8.GetCharCount(bytes, lineStart, count) + 1; }
        catch (DecoderFallbackException) { column = count + 1; }
    }
}

internal sealed class StrictJsonParser
{
    internal static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private readonly TextResourceSource _source;
    private readonly byte[] _bytes;
    private readonly DiagnosticBag _diagnostics;
    private readonly int _maximumDepth;
    private readonly CancellationToken _cancellationToken;
    private int _position;
    private bool _failed;

    private StrictJsonParser(TextResourceSource source, DiagnosticBag diagnostics, int maximumDepth, CancellationToken cancellationToken)
    {
        _source = source;
        _bytes = source.Bytes;
        _diagnostics = diagnostics;
        _maximumDepth = maximumDepth;
        _cancellationToken = cancellationToken;
    }

    internal static ParsedJson Parse(TextResourceSource source, DiagnosticBag diagnostics, TextResourceCompilerOptions options, CancellationToken cancellationToken)
    {
        if (source.Bytes.Length > options.MaximumDocumentBytes)
        {
            diagnostics.Add("WUTTEXT0022", TextResourceDiagnosticSeverity.Error,
                "Document exceeds the configured byte limit.", source, new ByteSpan(0, source.Bytes.Length));
            return new ParsedJson(source, null);
        }

        try { StrictUtf8.GetCharCount(source.Bytes); }
        catch (DecoderFallbackException)
        {
            diagnostics.Add("WUTTEXT0001", TextResourceDiagnosticSeverity.Error,
                "Source is not valid UTF-8.", source, InvalidUtf8Span(source.Bytes));
            return new ParsedJson(source, null);
        }

        // The semantic resource-tree limit excludes the document envelope. Keep a
        // small fixed allowance here so the semantic diagnostic can name the exact
        // offending resource segment.
        int parserDepth = options.MaximumDepth > int.MaxValue - 8 ? int.MaxValue : options.MaximumDepth + 8;
        cancellationToken.ThrowIfCancellationRequested();
        var parser = new StrictJsonParser(source, diagnostics, parserDepth, cancellationToken);
        if (parser.HasBom()) parser._position = 3;
        parser.SkipWhiteSpace();
        JsonValue? root = parser.ParseValue(1);
        parser.SkipWhiteSpace();
        if (!parser._failed && parser._position != parser._bytes.Length)
            parser.Fail("Unexpected content after the JSON value.", new ByteSpan(parser._position, 1));
        return new ParsedJson(source, parser._failed ? null : root);
    }

    private bool HasBom() => _bytes.Length >= 3 && _bytes[0] == 0xef && _bytes[1] == 0xbb && _bytes[2] == 0xbf;

    private JsonValue? ParseValue(int depth)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (depth > _maximumDepth)
        {
            _diagnostics.Add("WUTTEXT0022", TextResourceDiagnosticSeverity.Error,
                "JSON nesting exceeds the configured depth limit.", _source, new ByteSpan(_position, 1));
            _failed = true;
            return null;
        }
        if (_position >= _bytes.Length) { Fail("Expected a JSON value.", new ByteSpan(_position, 0)); return null; }
        byte value = _bytes[_position];
        if (value == (byte)'{') return ParseObject(depth);
        if (value == (byte)'[') return ParseArray(depth);
        if (value == (byte)'"')
        {
            int start = _position;
            string? text = ParseString();
            return text is null ? null : new JsonValue(JsonKind.String, new ByteSpan(start, _position - start), text);
        }
        if (value == (byte)'-' || (value >= (byte)'0' && value <= (byte)'9')) return ParseNumber();
        if (Match("true")) return new JsonValue(JsonKind.True, new ByteSpan(_position - 4, 4));
        if (Match("false")) return new JsonValue(JsonKind.False, new ByteSpan(_position - 5, 5));
        if (Match("null")) return new JsonValue(JsonKind.Null, new ByteSpan(_position - 4, 4));
        Fail("Expected a JSON value.", new ByteSpan(_position, 1));
        return null;
    }

    private JsonValue? ParseObject(int depth)
    {
        int start = _position++;
        var properties = new List<JsonProperty>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        SkipWhiteSpace();
        if (Take((byte)'}')) return new JsonValue(JsonKind.Object, new ByteSpan(start, _position - start), properties: properties.ToArray());
        while (!_failed)
        {
            if (_position >= _bytes.Length || _bytes[_position] != (byte)'"')
            {
                Fail("Expected a JSON property name.", new ByteSpan(_position, _position < _bytes.Length ? 1 : 0));
                return null;
            }
            int nameStart = _position;
            string? name = ParseString();
            if (name is null) return null;
            var nameSpan = new ByteSpan(nameStart, _position - nameStart);
            if (!names.Add(name))
            {
                _diagnostics.Add("WUTTEXT0001", TextResourceDiagnosticSeverity.Error,
                    "Duplicate JSON property '" + name + "'.", _source, nameSpan);
                _failed = true;
                return null;
            }
            SkipWhiteSpace();
            if (!Take((byte)':')) { Fail("Expected ':' after the property name.", new ByteSpan(_position, _position < _bytes.Length ? 1 : 0)); return null; }
            SkipWhiteSpace();
            JsonValue? child = ParseValue(depth + 1);
            if (child is null) return null;
            properties.Add(new JsonProperty(name, nameSpan, child));
            SkipWhiteSpace();
            if (Take((byte)'}')) return new JsonValue(JsonKind.Object, new ByteSpan(start, _position - start), properties: properties.ToArray());
            int commaPosition = _position;
            if (!Take((byte)',')) { Fail("Expected ',' or '}' in object.", new ByteSpan(_position, _position < _bytes.Length ? 1 : 0)); return null; }
            SkipWhiteSpace();
            if (_position < _bytes.Length && _bytes[_position] == (byte)'}')
            {
                Fail("Trailing commas are not allowed.", new ByteSpan(commaPosition, 1));
                return null;
            }
        }
        return null;
    }

    private JsonValue? ParseArray(int depth)
    {
        int start = _position++;
        var items = new List<JsonValue>();
        SkipWhiteSpace();
        if (Take((byte)']')) return new JsonValue(JsonKind.Array, new ByteSpan(start, _position - start), items: items.ToArray());
        while (!_failed)
        {
            JsonValue? child = ParseValue(depth + 1);
            if (child is null) return null;
            items.Add(child);
            SkipWhiteSpace();
            if (Take((byte)']')) return new JsonValue(JsonKind.Array, new ByteSpan(start, _position - start), items: items.ToArray());
            int commaPosition = _position;
            if (!Take((byte)',')) { Fail("Expected ',' or ']' in array.", new ByteSpan(_position, _position < _bytes.Length ? 1 : 0)); return null; }
            SkipWhiteSpace();
            if (_position < _bytes.Length && _bytes[_position] == (byte)']')
            {
                Fail("Trailing commas are not allowed.", new ByteSpan(commaPosition, 1));
                return null;
            }
        }
        return null;
    }

    private JsonValue? ParseNumber()
    {
        int start = _position;
        Take((byte)'-');
        if (_position >= _bytes.Length) { Fail("Invalid JSON number.", new ByteSpan(start, _position - start)); return null; }
        if (Take((byte)'0'))
        {
            if (_position < _bytes.Length && IsDigit(_bytes[_position])) { Fail("Leading zeros are not allowed in JSON numbers.", new ByteSpan(start, _position - start + 1)); return null; }
        }
        else
        {
            if (!IsOneToNine(_bytes[_position])) { Fail("Invalid JSON number.", new ByteSpan(start, 1)); return null; }
            while (_position < _bytes.Length && IsDigit(_bytes[_position])) _position++;
        }
        if (Take((byte)'.'))
        {
            if (_position >= _bytes.Length || !IsDigit(_bytes[_position])) { Fail("Expected digits after decimal point.", new ByteSpan(start, _position - start)); return null; }
            while (_position < _bytes.Length && IsDigit(_bytes[_position])) _position++;
        }
        if (_position < _bytes.Length && (_bytes[_position] == (byte)'e' || _bytes[_position] == (byte)'E'))
        {
            _position++;
            if (_position < _bytes.Length && (_bytes[_position] == (byte)'+' || _bytes[_position] == (byte)'-')) _position++;
            if (_position >= _bytes.Length || !IsDigit(_bytes[_position])) { Fail("Expected exponent digits.", new ByteSpan(start, _position - start)); return null; }
            while (_position < _bytes.Length && IsDigit(_bytes[_position])) _position++;
        }
        string text = Encoding.ASCII.GetString(_bytes, start, _position - start);
        return new JsonValue(JsonKind.Number, new ByteSpan(start, _position - start), text);
    }

    private string? ParseString()
    {
        int opening = _position++;
        int segment = _position;
        StringBuilder? builder = null;
        while (_position < _bytes.Length)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            byte current = _bytes[_position];
            if (current == (byte)'"')
            {
                string tail = StrictUtf8.GetString(_bytes, segment, _position - segment);
                _position++;
                return builder is null ? tail : builder.Append(tail).ToString();
            }
            if (current < 0x20) { Fail("Unescaped control character in JSON string.", new ByteSpan(_position, 1)); return null; }
            if (current != (byte)'\\') { _position++; continue; }
            builder ??= new StringBuilder();
            builder.Append(StrictUtf8.GetString(_bytes, segment, _position - segment));
            _position++;
            if (_position >= _bytes.Length) { Fail("Unterminated JSON escape.", new ByteSpan(opening, _position - opening)); return null; }
            byte escape = _bytes[_position++];
            switch (escape)
            {
                case (byte)'"': builder.Append('"'); break;
                case (byte)'\\': builder.Append('\\'); break;
                case (byte)'/': builder.Append('/'); break;
                case (byte)'b': builder.Append('\b'); break;
                case (byte)'f': builder.Append('\f'); break;
                case (byte)'n': builder.Append('\n'); break;
                case (byte)'r': builder.Append('\r'); break;
                case (byte)'t': builder.Append('\t'); break;
                case (byte)'u':
                    if (!ParseUnicodeEscape(builder)) return null;
                    break;
                default: Fail("Invalid JSON escape sequence.", new ByteSpan(_position - 2, 2)); return null;
            }
            segment = _position;
        }
        Fail("Unterminated JSON string.", new ByteSpan(opening, _position - opening));
        return null;
    }

    private bool ParseUnicodeEscape(StringBuilder builder)
    {
        int escapeStart = _position - 2;
        if (_position + 4 > _bytes.Length || !TryHex4(_position, out int code))
        {
            Fail("Invalid Unicode escape sequence.", new ByteSpan(escapeStart, Math.Min(6, _bytes.Length - escapeStart)));
            return false;
        }
        _position += 4;
        char first = (char)code;
        if (char.IsHighSurrogate(first))
        {
            if (_position + 6 > _bytes.Length || _bytes[_position] != (byte)'\\' || _bytes[_position + 1] != (byte)'u' ||
                !TryHex4(_position + 2, out int lowCode) || !char.IsLowSurrogate((char)lowCode))
            {
                Fail("A high surrogate must be followed by a low surrogate.", new ByteSpan(escapeStart, _position - escapeStart));
                return false;
            }
            builder.Append(first).Append((char)lowCode);
            _position += 6;
            return true;
        }
        if (char.IsLowSurrogate(first))
        {
            Fail("A low surrogate must follow a high surrogate.", new ByteSpan(escapeStart, 6));
            return false;
        }
        builder.Append(first);
        return true;
    }

    private bool TryHex4(int offset, out int value)
    {
        value = 0;
        if (offset + 4 > _bytes.Length) return false;
        for (int i = 0; i < 4; i++)
        {
            int digit;
            byte b = _bytes[offset + i];
            if (b >= (byte)'0' && b <= (byte)'9') digit = b - (byte)'0';
            else if (b >= (byte)'a' && b <= (byte)'f') digit = b - (byte)'a' + 10;
            else if (b >= (byte)'A' && b <= (byte)'F') digit = b - (byte)'A' + 10;
            else return false;
            value = (value << 4) | digit;
        }
        return true;
    }

    private void SkipWhiteSpace()
    {
        while (_position < _bytes.Length)
        {
            byte b = _bytes[_position];
            if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r' && b != (byte)'\n') break;
            _position++;
        }
    }

    private bool Take(byte expected)
    {
        if (_position >= _bytes.Length || _bytes[_position] != expected) return false;
        _position++;
        return true;
    }

    private bool Match(string ascii)
    {
        if (_position + ascii.Length > _bytes.Length) return false;
        for (int i = 0; i < ascii.Length; i++) if (_bytes[_position + i] != (byte)ascii[i]) return false;
        _position += ascii.Length;
        return true;
    }

    private void Fail(string message, ByteSpan span)
    {
        if (!_failed) _diagnostics.Add("WUTTEXT0001", TextResourceDiagnosticSeverity.Error, message, _source, span);
        _failed = true;
    }

    private static bool IsDigit(byte value) => value >= (byte)'0' && value <= (byte)'9';
    private static bool IsOneToNine(byte value) => value >= (byte)'1' && value <= (byte)'9';

    private static ByteSpan InvalidUtf8Span(byte[] bytes)
    {
        for (int length = 1; length <= bytes.Length; length++)
        {
            try { StrictUtf8.GetCharCount(bytes, 0, length); }
            catch (DecoderFallbackException) { return new ByteSpan(length - 1, 1); }
        }
        return new ByteSpan(0, bytes.Length == 0 ? 0 : 1);
    }
}
