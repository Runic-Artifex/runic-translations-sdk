using System;
using System.Text;
using System.Threading;

using Runic.Translations.Generated;

namespace Runic.Translations;

/// <summary>
/// Reads the locale data a generated catalog embeds for RMF2 runtime ABI 5: the binary encoding of
/// <c>specs/translations/rmf2-execution-v2.md</c> ("Generated locale data, binary encoding"), a string pool, varints and one
/// tag byte per node, straight into the executable v5 model.
/// </summary>
/// <remarks>
/// <para>
/// The data is embedded by the compiler, which validated its messages, but the reader still fails closed: it checks every
/// bound, varint, count, tag, string index and UTF-8 string, with the JSON reader's limits (inputs 32, declarations 256,
/// selectors 16, variants 1 to 256, keys 16, nodes 4,096, options and annotations 256, markup depth 16, pattern bytes),
/// before it allocates the model, and reports a violation as <see cref="TranslationPackException"/>
/// (<see cref="TranslationPackFailureReason.Malformed"/>). No array it allocates is larger than the data, as every element
/// takes at least one byte.
/// </para>
/// <para>
/// <see cref="Read(byte[], Rmf2FormatData?)"/> checks the whole locale first. It then builds each message's model on its first format
/// (<see cref="CompiledTextMessage.Rmf2V5"/>), so a message that is never formatted keeps only its bytes; that decode
/// cannot fail, and concurrent first formats may decode a message twice but publish one model. Strings of the pool are
/// decoded once, on first use, and shared.
/// </para>
/// </remarks>
internal sealed class GeneratedLocaleBinary
{
    /// <summary>The encoding version, the first byte of the data.</summary>
    internal const byte FormatVersion = 1;

    // Message flags.
    internal const byte PlainFlag = 1, LocaleFlag = 2;

    // Node tags.
    internal const byte TextNode = 0, ExpressionNode = 1, OpenNode = 2, CloseNode = 3, StandaloneNode = 4;

    // Value kinds.
    internal const byte InputValue = 0, LocalValue = 1, StringLiteral = 2, NumberLiteral = 3;

    // Key kinds.
    internal const byte WildcardKey = 0, LiteralKey = 1, CanonicalLiteralKey = 2;

    private const int MaximumInputs = TranslationPackLimits.DefaultMaximumArgumentsPerMessage, MaximumDeclarations = 256,
        MaximumSelectors = 16, MaximumVariants = 256, MaximumKeys = 16, MaximumNodes = 4096, MaximumOptions = 256,
        MaximumAnnotations = 256, MaximumMarkupDepth = 16, MaximumPatternBytes = TranslationPackLimits.DefaultMaximumPatternBytes,
        MaximumTextLength = 65536;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly byte[] _data;
    private readonly int[] _stringStarts;
    private readonly int[] _stringLengths;
    private readonly string?[] _strings;
    private readonly Rmf2FormatData? _formatData;
    // The pool index of the content locale of every message that does not name its own.
    private int _defaultLocale;

    private GeneratedLocaleBinary(byte[] data, int[] starts, int[] lengths, Rmf2FormatData? formatData)
    {
        _data = data;
        _stringStarts = starts;
        _stringLengths = lengths;
        _strings = new string?[starts.Length];
        _formatData = formatData;
    }

    /// <summary>Checks a locale's binary data and returns its values in ascending ID order.</summary>
    /// <param name="data">The inflated data; the values keep it, so the caller must not change it.</param>
    /// <param name="formatData">The CLDR currency, unit and compact data the compiler embedded for these messages, if any.</param>
    /// <exception cref="TranslationPackException">The data is not in the compiler's form.</exception>
    internal static CompiledTranslationValue[] Read(byte[] data, Rmf2FormatData? formatData = null) => Read(data, formatData, decodeOnFirstFormat: true);

    /// <summary>As <see cref="Read(byte[], Rmf2FormatData?)"/>; without <paramref name="decodeOnFirstFormat"/> every model is built now.</summary>
    internal static CompiledTranslationValue[] Read(byte[] data, Rmf2FormatData? formatData, bool decodeOnFirstFormat)
    {
        int position = 0;
        if (data.Length == 0 || data[position++] != FormatVersion) throw Malformed("The generated locale data has an unknown encoding version.");
        int stringCount = Count(data, ref position, int.MaxValue);
        var starts = new int[stringCount];
        var lengths = new int[stringCount];
        for (int index = 0; index < stringCount; index++)
        {
            int length = Varint(data, ref position);
            if (length > data.Length - position) throw Malformed("A string of the generated locale data runs past its end.");
            if (!System.Text.Unicode.Utf8.IsValid(data.AsSpan(position, length))) throw Malformed("A string of the generated locale data is not valid UTF-8.");
            starts[index] = position;
            lengths[index] = length;
            position += length;
        }
        var reader = new GeneratedLocaleBinary(data, starts, lengths, formatData);
        int defaultLocale = reader._defaultLocale = reader.StringIndex(ref position);
        // A message takes at least two bytes: its ID delta and its flags.
        int messageCount = Count(data, ref position, int.MaxValue, 2);
        var values = new CompiledTranslationValue[messageCount];
        int id = -1;
        bool lazy = decodeOnFirstFormat;
        for (int index = 0; index < messageCount; index++)
        {
            int delta = Varint(data, ref position);
            if (delta > int.MaxValue - 1 - id) throw Malformed("A message ID of the generated locale data is out of range.");
            id += delta + 1;
            int start = position;
            int contentLocale = reader.Message(ref position, build: false, defaultLocale, out _);
            values[index] = new CompiledTranslationValue(id, string.Empty, lazy
                ? CompiledTextMessage.FromGeneratedBinary(reader, start, reader.String(contentLocale))
                : CompiledTextMessage.FromRmf2(reader.Decode(start)));
        }
        if (position != data.Length) throw Malformed("The generated locale data has content after its messages.");
        return values;
    }

    /// <summary>The CLDR data the compiler embedded for these messages, which every message of the locale carries.</summary>
    internal Rmf2FormatData? FormatData => _formatData;

    /// <summary>Builds the model of the message at <paramref name="start"/>, which <see cref="Read(byte[], Rmf2FormatData?)"/> checked.</summary>
    internal CompiledRmf2Message Decode(int start)
    {
        int position = start;
        Message(ref position, build: true, _defaultLocale, out CompiledRmf2Message? message);
        return message!;
    }

    /// <summary>Whether the caller inputs of the message at <paramref name="start"/> are exactly these, without building it.</summary>
    internal bool InputsMatch(int start, TranslationPlaceholderDescriptor[] descriptors)
    {
        int position = start;
        byte flags = _data[position++];
        if ((flags & LocaleFlag) != 0) Varint(_data, ref position);
        if ((flags & PlainFlag) != 0) return descriptors.Length == 0;
        int count = Varint(_data, ref position);
        if (count != descriptors.Length) return false;
        for (int index = 0; index < count; index++)
        {
            string name = String(Varint(_data, ref position));
            TextArgumentType type = Type(_data[position++]);
            if (!string.Equals(descriptors[index].Name, name, StringComparison.Ordinal) || descriptors[index].Type != type) return false;
        }
        return true;
    }

    // Reads one message. Without build it only checks it, allocating nothing; with build it creates the model, which the
    // check guarantees to succeed. Returns the pool index of its content locale (defaultLocale unless it names its own).
    private int Message(ref int position, bool build, int defaultLocale, out CompiledRmf2Message? message)
    {
        message = null;
        byte flags = Byte(ref position);
        if ((flags & ~(PlainFlag | LocaleFlag)) != 0) throw Malformed("A message of the generated locale data has unknown flags.");
        int contentLocale = (flags & LocaleFlag) != 0 ? StringIndex(ref position) : defaultLocale;
        if ((flags & PlainFlag) != 0)
        {
            int text = StringIndex(ref position);
            CheckText(text, out int bytes);
            if (bytes > MaximumPatternBytes) throw Malformed("A message of the generated locale data exceeds the pattern limit.");
            if (build)
                message = new CompiledRmf2Message(TrustedRmf2.Compiler, [], [], [],
                    [new CompiledRmf2Variant(TrustedRmf2.Compiler, [], [new CompiledRmf2Node(String(text))])], String(contentLocale))
                { FormatData = _formatData };
            return contentLocale;
        }

        int inputCount = Count(ref position, MaximumInputs, 2);
        CompiledRmf2Input[]? inputs = build ? Array<CompiledRmf2Input>(inputCount) : null;
        for (int index = 0; index < inputCount; index++)
        {
            int name = StringIndex(ref position);
            TextArgumentType type = TypeByte(ref position);
            if (build) inputs![index] = new CompiledRmf2Input(TrustedRmf2.Compiler, String(name), type);
        }

        int declarationCount = Count(ref position, MaximumDeclarations, 3);
        CompiledRmf2Declaration[]? declarations = build ? Array<CompiledRmf2Declaration>(declarationCount) : null;
        for (int index = 0; index < declarationCount; index++)
        {
            byte kind = Byte(ref position);
            if (kind > 1) throw Malformed("A declaration kind of the generated locale data is unsupported.");
            int name = StringIndex(ref position);
            CompiledRmf2Expression? expression = Expression(ref position, build);
            if (build) declarations![index] = new CompiledRmf2Declaration(TrustedRmf2.Compiler, kind == 0 ? "input" : "local", String(name), expression!);
        }

        int selectorCount = Count(ref position, MaximumSelectors, 4);
        CompiledRmf2Selector[]? selectors = build ? Array<CompiledRmf2Selector>(selectorCount) : null;
        for (int index = 0; index < selectorCount; index++)
        {
            CompiledRmf2Value? value = Value(ref position, build, literalOnly: false);
            TextArgumentType type = TypeByte(ref position);
            byte function = Byte(ref position);
            if (function > 2) throw Malformed("A selector function of the generated locale data is unsupported.");
            if (build) selectors![index] = new CompiledRmf2Selector(TrustedRmf2.Compiler, value!, type, function switch { 0 => "exact", 1 => "plural", _ => "ordinal" });
        }

        int variantCount = Count(ref position, MaximumVariants, 2);
        if (variantCount == 0) throw Malformed("A message of the generated locale data has no variant.");
        CompiledRmf2Variant[]? variants = build ? new CompiledRmf2Variant[variantCount] : null;
        for (int index = 0; index < variantCount; index++)
        {
            // As the JSON reader: the text of each variant counts against the pattern limit.
            int textBytes = 0;
            int keyCount = Count(ref position, MaximumKeys, 1);
            CompiledRmf2Key[]? keys = build ? Array<CompiledRmf2Key>(keyCount) : null;
            for (int key = 0; key < keyCount; key++)
            {
                byte kind = Byte(ref position);
                if (kind == WildcardKey) { if (build) keys![key] = CompiledRmf2Key.Wildcard; continue; }
                if (kind is not (LiteralKey or CanonicalLiteralKey)) throw Malformed("A selector key kind of the generated locale data is unsupported.");
                int value = StringIndex(ref position);
                int canonical = kind == CanonicalLiteralKey ? StringIndex(ref position) : -1;
                if (build) keys![key] = new CompiledRmf2Key(TrustedRmf2.Compiler, String(value), canonical < 0 ? null : String(canonical));
            }

            int nodeCount = Count(ref position, MaximumNodes, 2);
            CompiledRmf2Node[]? nodes = build ? Array<CompiledRmf2Node>(nodeCount) : null;
            int depth = 0;
            for (int node = 0; node < nodeCount; node++)
            {
                byte tag = Byte(ref position);
                switch (tag)
                {
                    case TextNode:
                    {
                        int text = StringIndex(ref position);
                        CheckText(text, out int bytes);
                        textBytes += bytes;
                        if (textBytes > MaximumPatternBytes) throw Malformed("A message of the generated locale data exceeds the pattern limit.");
                        if (build) nodes![node] = new CompiledRmf2Node(String(text));
                        break;
                    }
                    case ExpressionNode:
                    {
                        CompiledRmf2Expression? expression = Expression(ref position, build);
                        if (build) nodes![node] = new CompiledRmf2Node(expression!);
                        break;
                    }
                    case OpenNode or CloseNode or StandaloneNode:
                    {
                        if (tag == OpenNode && ++depth > MaximumMarkupDepth) throw Malformed("A message of the generated locale data exceeds the markup depth limit.");
                        if (tag == CloseNode) depth--;
                        int name = StringIndex(ref position);
                        CompiledRmf2Option[]? options = Options(ref position, build);
                        CompiledRmf2Annotation[]? annotations = Annotations(ref position, build);
                        if (build) nodes![node] = new CompiledRmf2Node(TrustedRmf2.Compiler, String(name),
                            tag == OpenNode ? "open" : tag == CloseNode ? "close" : "standalone", options!, annotations!);
                        break;
                    }
                    default:
                        throw Malformed("A node kind of the generated locale data is unsupported.");
                }
            }
            if (build) variants![index] = new CompiledRmf2Variant(TrustedRmf2.Compiler, keys!, nodes!);
        }

        if (build)
            message = new CompiledRmf2Message(TrustedRmf2.Compiler, inputs!, declarations!, selectors!, variants!, String(contentLocale)) { FormatData = _formatData };
        return contentLocale;
    }

    private CompiledRmf2Expression? Expression(ref int position, bool build)
    {
        CompiledRmf2Value? operand = Value(ref position, build, literalOnly: false);
        TextArgumentType valueType = TypeByte(ref position);
        int function = Varint(_data, ref position) - 1;
        if (function >= _strings.Length) throw Malformed("A string index of the generated locale data is out of range.");
        CompiledRmf2Option[]? options = Options(ref position, build);
        CompiledRmf2Annotation[]? annotations = Annotations(ref position, build);
        return build ? new CompiledRmf2Expression(TrustedRmf2.Compiler, operand!, valueType, function < 0 ? null : String(function), options!, annotations!) : null;
    }

    private CompiledRmf2Option[]? Options(ref int position, bool build)
    {
        int count = Count(ref position, MaximumOptions, 3);
        CompiledRmf2Option[]? options = build ? Array<CompiledRmf2Option>(count) : null;
        for (int index = 0; index < count; index++)
        {
            int name = StringIndex(ref position);
            CompiledRmf2Value? value = Value(ref position, build, literalOnly: false);
            if (build) options![index] = new CompiledRmf2Option(TrustedRmf2.Compiler, String(name), value!);
        }
        return options;
    }

    private CompiledRmf2Annotation[]? Annotations(ref int position, bool build)
    {
        int count = Count(ref position, MaximumAnnotations, 2);
        CompiledRmf2Annotation[]? annotations = build ? Array<CompiledRmf2Annotation>(count) : null;
        for (int index = 0; index < count; index++)
        {
            int name = StringIndex(ref position);
            byte present = Byte(ref position);
            if (present > 1) throw Malformed("An annotation of the generated locale data is malformed.");
            CompiledRmf2Value? value = present == 1 ? Value(ref position, build, literalOnly: true) : null;
            if (build) annotations![index] = new CompiledRmf2Annotation(TrustedRmf2.Compiler, String(name), value);
        }
        return annotations;
    }

    private CompiledRmf2Value? Value(ref int position, bool build, bool literalOnly)
    {
        byte kind = Byte(ref position);
        if (kind > NumberLiteral) throw Malformed("A value kind of the generated locale data is unsupported.");
        if (literalOnly && kind < StringLiteral) throw Malformed("An annotation value of the generated locale data must be literal.");
        int value = StringIndex(ref position);
        int canonical = kind == NumberLiteral ? StringIndex(ref position) : -1;
        if (!build) return null;
        string name = kind switch { InputValue => "input", LocalValue => "local", StringLiteral => "string-literal", _ => "number-literal" };
        return new CompiledRmf2Value(TrustedRmf2.Compiler, name, String(value), canonical < 0 ? null : String(canonical));
    }

    // A text node's string: its UTF-8 bytes count against the pattern limit, and it holds at most 65,536 UTF-16 code units,
    // as CompiledRmf2Node requires.
    private void CheckText(int index, out int bytes)
    {
        bytes = _stringLengths[index];
        if (bytes > MaximumTextLength && Encoding.UTF8.GetCharCount(_data.AsSpan(_stringStarts[index], bytes)) > MaximumTextLength)
            throw Malformed("A text of the generated locale data exceeds the text limit.");
    }

    // A string of the pool, decoded on first use and shared; concurrent first uses may decode it twice, which is harmless.
    internal string String(int index)
    {
        string? value = Volatile.Read(ref _strings[index]);
        if (value is not null) return value;
        value = StrictUtf8.GetString(_data, _stringStarts[index], _stringLengths[index]);
        return Interlocked.CompareExchange(ref _strings[index], value, null) ?? value;
    }

    private int StringIndex(ref int position)
    {
        int index = Varint(_data, ref position);
        return index < _strings.Length ? index : throw Malformed("A string index of the generated locale data is out of range.");
    }

    private TextArgumentType TypeByte(ref int position) => Type(Byte(ref position));

    private static TextArgumentType Type(byte type) => type switch
    {
        0 => TextArgumentType.String,
        1 => TextArgumentType.Int,
        2 => TextArgumentType.Number,
        3 => TextArgumentType.Bool,
        4 => TextArgumentType.Date,
        5 => TextArgumentType.Time,
        6 => TextArgumentType.DateTime,
        7 => TextArgumentType.Guid,
        8 => TextArgumentType.List,
        _ => throw Malformed("A carrier type of the generated locale data is unsupported."),
    };

    private byte Byte(ref int position) =>
        position < _data.Length ? _data[position++] : throw Malformed("The generated locale data is incomplete.");

    private int Count(ref int position, int maximum, int minimumElementBytes) => Count(_data, ref position, maximum, minimumElementBytes);

    // A list length: at most maximum, and no more elements than the rest of the data can hold at minimumElementBytes each,
    // so no array is larger than the data.
    private static int Count(byte[] data, ref int position, int maximum, int minimumElementBytes = 1)
    {
        int count = Varint(data, ref position);
        if (count > maximum) throw Malformed("A list of the generated locale data exceeds its limit.");
        if ((long)count * minimumElementBytes > data.Length - position) throw Malformed("A list of the generated locale data runs past its end.");
        return count;
    }

    // An unsigned LEB128 varint of at most five bytes and at most int.MaxValue.
    private static int Varint(byte[] data, ref int position)
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            if (position >= data.Length) throw Malformed("The generated locale data is incomplete.");
            byte current = data[position++];
            if (shift == 28 && current > 0x07) throw Malformed("A varint of the generated locale data is out of range.");
            value |= (uint)(current & 0x7F) << shift;
            if ((current & 0x80) == 0) return (int)value;
        }
        throw Malformed("A varint of the generated locale data is out of range.");
    }

    private static T[] Array<T>(int count) => count == 0 ? [] : new T[count];

    private static TranslationPackException Malformed(string message) =>
        new(message, TranslationPackFailureReason.Malformed);
}
