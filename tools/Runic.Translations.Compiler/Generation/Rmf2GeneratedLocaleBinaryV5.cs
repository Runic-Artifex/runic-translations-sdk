using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Runic.Translations.Compiler.Generation;

/// <summary>
/// Writes a generated catalog's locale data in the binary encoding of RMF2 runtime ABI 5
/// (<c>specs/translations/rmf2-execution-v2.md</c>, "Generated locale data, binary encoding"), which the runtime's
/// <c>GeneratedLocaleBinary</c> reads into the same model as the locale JSON of ABI 4.
/// </summary>
/// <remarks>
/// Every string is stored once in a pool, most frequent first (then in ordinal order), so the common ones take a one-byte
/// index. A message whose only content is one text, without inputs, declarations or selectors, takes its flags and that
/// text's index. The output depends only on the values.
/// </remarks>
internal static class Rmf2GeneratedLocaleBinaryV5
{
    internal const byte FormatVersion = 1;
    private const byte PlainFlag = 1, LocaleFlag = 2;
    private const byte TextNode = 0, ExpressionNode = 1, OpenNode = 2, CloseNode = 3, StandaloneNode = 4;
    private const byte InputValue = 0, LocalValue = 1, StringLiteral = 2, NumberLiteral = 3;
    private const byte WildcardKey = 0, LiteralKey = 1, CanonicalLiteralKey = 2;

    /// <summary>One value of a locale: its message ID, effective content locale and message with the caller inputs.</summary>
    internal readonly record struct Value(int Id, string ContentLocale, Rmf2MessageV5 Message);

    /// <summary>Encodes the values of one locale, in ascending ID order.</summary>
    internal static byte[] Encode(IReadOnlyList<Value> values)
    {
        for (int index = 1; index < values.Count; index++)
            if (values[index].Id <= values[index - 1].Id) throw new ArgumentException("The values must be in ascending ID order.", nameof(values));

        // The content locale most messages have is the default; the others name theirs.
        string defaultLocale = values.Count == 0 ? "" : values.GroupBy(value => value.ContentLocale, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).First().Key;

        var counter = new Writer(null, defaultLocale);
        counter.Add(defaultLocale);
        foreach (Value value in values) counter.Message(value);
        string[] pool = counter.Counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key).ToArray();
        var indexes = new Dictionary<string, int>(pool.Length, StringComparer.Ordinal);
        for (int index = 0; index < pool.Length; index++) indexes.Add(pool[index], index);

        using var stream = new MemoryStream();
        stream.WriteByte(FormatVersion);
        WriteVarint(stream, pool.Length);
        foreach (string text in pool)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            WriteVarint(stream, utf8.Length);
            stream.Write(utf8, 0, utf8.Length);
        }
        var writer = new Writer(new Encoder(stream, indexes), defaultLocale);
        writer.String(defaultLocale);
        WriteVarint(stream, values.Count);
        int previous = -1;
        foreach (Value value in values)
        {
            WriteVarint(stream, value.Id - previous - 1);
            previous = value.Id;
            writer.Message(value);
        }
        return stream.ToArray();
    }

    internal static void WriteVarint(Stream stream, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        uint remaining = (uint)value;
        while (remaining >= 0x80)
        {
            stream.WriteByte((byte)(remaining | 0x80));
            remaining >>= 7;
        }
        stream.WriteByte((byte)remaining);
    }

    private sealed class Encoder(Stream stream, Dictionary<string, int> indexes)
    {
        internal void Byte(byte value) => stream.WriteByte(value);
        internal void Varint(int value) => WriteVarint(stream, value);
        internal void String(string value) => WriteVarint(stream, indexes[value]);
        internal int Index(string value) => indexes[value];
    }

    // Walks the values in the encoding's order. Without an encoder it only counts the strings for the pool, so both passes
    // visit exactly the same strings.
    private sealed class Writer(Encoder? encoder, string defaultLocale)
    {
        internal Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

        internal void Add(string value) => Counts[value] = Counts.TryGetValue(value, out int count) ? count + 1 : 1;

        private void Byte(byte value) => encoder?.Byte(value);
        private void Count(int value) => encoder?.Varint(value);

        internal void String(string value)
        {
            if (encoder is null) Add(value);
            else encoder.String(value);
        }

        internal void Message(Value value)
        {
            Rmf2MessageV5 message = value.Message;
            bool plain = message.Inputs.Count == 0 && message.Declarations.Count == 0 && message.Selectors.Count == 0 &&
                message.Variants.Count == 1 && message.Variants[0].Keys.Count == 0 && message.Variants[0].Nodes is [Rmf2TextV5];
            bool ownLocale = !string.Equals(value.ContentLocale, defaultLocale, StringComparison.Ordinal);
            Byte((byte)((plain ? PlainFlag : 0) | (ownLocale ? LocaleFlag : 0)));
            if (ownLocale) String(value.ContentLocale);
            if (plain)
            {
                String(((Rmf2TextV5)message.Variants[0].Nodes[0]).Value);
                return;
            }
            Count(message.Inputs.Count);
            foreach (Rmf2InputV5 input in message.Inputs) { String(input.Name); Byte(Type(input.Type)); }
            Count(message.Declarations.Count);
            foreach (Rmf2DeclarationV5 declaration in message.Declarations)
            {
                Byte(declaration.Kind switch { "input" => 0, "local" => 1, _ => throw Unsupported("declaration kind", declaration.Kind) });
                String(declaration.Name);
                Expression(declaration.Expression);
            }
            Count(message.Selectors.Count);
            foreach (Rmf2SelectorV5 selector in message.Selectors)
            {
                Value(selector.Value);
                Byte(Type(selector.Type));
                Byte(selector.Function switch { "exact" => 0, "plural" => 1, "ordinal" => 2, _ => throw Unsupported("selector function", selector.Function) });
            }
            Count(message.Variants.Count);
            foreach (Rmf2VariantV5 variant in message.Variants)
            {
                Count(variant.Keys.Count);
                foreach (Rmf2KeyV5 key in variant.Keys)
                {
                    if (key.Kind == "wildcard") { Byte(WildcardKey); continue; }
                    if (key.Kind != "literal" || key.Value is null) throw Unsupported("selector key", key.Kind);
                    Byte(key.Canonical is null ? LiteralKey : CanonicalLiteralKey);
                    String(key.Value);
                    if (key.Canonical is not null) String(key.Canonical);
                }
                Count(variant.Nodes.Count);
                foreach (Rmf2NodeV5 node in variant.Nodes)
                {
                    switch (node)
                    {
                        case Rmf2TextV5 text: Byte(TextNode); String(text.Value); break;
                        case Rmf2ExpressionNodeV5 expression: Byte(ExpressionNode); Expression(expression.Expression); break;
                        case Rmf2MarkupV5 markup:
                            Byte(markup.MarkupKind switch { "open" => OpenNode, "close" => CloseNode, "standalone" => StandaloneNode, _ => throw Unsupported("markup kind", markup.MarkupKind) });
                            String(markup.Name);
                            Properties(markup.Options, markup.Annotations);
                            break;
                        default: throw new InvalidOperationException("Unknown v5 message node.");
                    }
                }
            }
        }

        private void Expression(Rmf2ExpressionV5 expression)
        {
            Value(expression.Operand);
            Byte(Type(expression.ValueType));
            if (expression.Function is null) Count(0);
            else if (encoder is null) Add(expression.Function);
            // The function's pool index plus one; 0 is "no function".
            else encoder.Varint(encoder.Index(expression.Function) + 1);
            Properties(expression.Options, expression.Annotations);
        }

        private void Properties(IReadOnlyList<Rmf2OptionV5> options, IReadOnlyList<Rmf2AnnotationV5> annotations)
        {
            Count(options.Count);
            foreach (Rmf2OptionV5 option in options) { String(option.Name); Value(option.Value); }
            Count(annotations.Count);
            foreach (Rmf2AnnotationV5 annotation in annotations)
            {
                String(annotation.Name);
                Byte(annotation.Value is null ? (byte)0 : (byte)1);
                if (annotation.Value is not null) Value(annotation.Value);
            }
        }

        private void Value(Rmf2ValueV5 value)
        {
            Byte(value.Kind switch
            {
                "input" => InputValue,
                "local" => LocalValue,
                "string-literal" => StringLiteral,
                "number-literal" => NumberLiteral,
                _ => throw Unsupported("value kind", value.Kind),
            });
            String(value.Value);
            if (value.Kind == "number-literal") String(value.Canonical ?? throw new InvalidOperationException("A number literal has no canonical form."));
        }

        private static byte Type(string type) => type switch
        {
            "string" => 0, "int64" => 1, "decimal" => 2, "boolean" => 3, "date" => 4, "time" => 5, "datetime" => 6, "guid" => 7, "list" => 8,
            _ => throw Unsupported("carrier", type),
        };

        private static InvalidOperationException Unsupported(string what, string? value) => new("The binary locale data has no " + what + " '" + value + "'.");
    }
}
