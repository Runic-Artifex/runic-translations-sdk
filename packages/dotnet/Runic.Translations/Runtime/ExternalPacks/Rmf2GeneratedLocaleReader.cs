using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Runic.Translations;

/// <summary>
/// Reads the locale data a generated catalog embeds, <c>[[id, {"contentLocale", "ast"}], ...]</c>, straight into the
/// executable v5 model with a forward-only <see cref="Utf8JsonReader"/>: no JSON document, no member dictionaries.
/// </summary>
/// <remarks>
/// The compiler validated these messages when it generated the catalog, so this reader checks the data's shape,
/// vocabulary and limits but does not repeat the semantic validation that hand-built models and external packs get
/// (bound references, typed options, key vectors, NFC names). The catalog still checks every message against its
/// definition's inputs. Equal short strings, such as input names, share one instance.
/// </remarks>
internal sealed class Rmf2GeneratedLocaleReader
{
    // Strings up to this many UTF-8 bytes are decoded on the stack and shared; longer text is allocated directly.
    private const int SharedStringBytes = 128;

    private readonly HashSet<string> _strings = new(StringComparer.Ordinal);
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private readonly TranslationPackLimits _limits = new();
    private readonly List<CompiledRmf2Input> _inputs = [];
    private readonly List<CompiledRmf2Declaration> _declarations = [];
    private readonly List<CompiledRmf2Selector> _selectors = [];
    private readonly List<CompiledRmf2Variant> _variants = [];
    private readonly List<CompiledRmf2Key> _keys = [];
    private readonly List<CompiledRmf2Node> _nodes = [];
    private readonly List<CompiledRmf2Option> _options = [];
    private readonly List<CompiledRmf2Annotation> _annotations = [];

    private Rmf2GeneratedLocaleReader() => _lookup = _strings.GetAlternateLookup<ReadOnlySpan<char>>();

    internal static CompiledTranslationValue[] Read(ReadOnlySpan<byte> content)
    {
        var reader = new Utf8JsonReader(content, new JsonReaderOptions { MaxDepth = TranslationPackLimits.DefaultMaximumDepth });
        try { return new Rmf2GeneratedLocaleReader().ReadValues(ref reader); }
        catch (TranslationPackException) { throw; }
        catch (JsonException exception) { throw Error("Generated locale data is malformed JSON near byte " + exception.BytePositionInLine + "."); }
        // Utf8JsonReader reports a token of another type, and CopyString invalid UTF-8, as InvalidOperationException.
        catch (InvalidOperationException) { throw Error("Generated locale data contains a value of the wrong type or invalid UTF-8."); }
        catch (ArgumentException exception) { throw Error("Generated locale data contains an invalid normalized v5 AST: " + exception.Message, TranslationPackFailureReason.MalformedPattern); }
    }

    private CompiledTranslationValue[] ReadValues(ref Utf8JsonReader reader)
    {
        Next(ref reader, JsonTokenType.StartArray, "Generated locale data must be an array.");
        var values = new List<CompiledTranslationValue>();
        while (Next(ref reader) != JsonTokenType.EndArray)
        {
            Expect(ref reader, JsonTokenType.StartArray, "A generated locale value is malformed.");
            Next(ref reader, JsonTokenType.Number, "A generated locale value is malformed.");
            int id = reader.TryGetInt32(out int number) ? number : throw Error("An external pack integer is invalid.");
            Next(ref reader, JsonTokenType.StartObject, "A generated locale value is malformed.");
            CompiledRmf2Message message = ReadWrapper(ref reader);
            Next(ref reader, JsonTokenType.EndArray, "A generated locale value is malformed.");
            values.Add(new CompiledTranslationValue(id, string.Empty, CompiledTextMessage.FromRmf2(message)));
        }
        if (reader.Read()) throw Error("Generated locale data has content after its value array.");
        return values.ToArray();
    }

    private CompiledRmf2Message ReadWrapper(ref Utf8JsonReader reader)
    {
        string? contentLocale = null;
        Ast? ast = null;
        while (Member(ref reader))
        {
            if (reader.ValueTextEquals("contentLocale"u8)) { Once(contentLocale, "contentLocale"); reader.Read(); contentLocale = String(ref reader); }
            else if (reader.ValueTextEquals("ast"u8)) { Once(ast, "ast"); reader.Read(); ast = ReadAst(ref reader); }
            else throw Unknown(ref reader);
        }
        if (contentLocale is null) throw Missing("contentLocale");
        if (ast is not { } value) throw Missing("ast");
        return new CompiledRmf2Message(TrustedRmf2.Compiler, value.Inputs, value.Declarations, value.Selectors, value.Variants, contentLocale);
    }

    private readonly record struct Ast(CompiledRmf2Input[] Inputs, CompiledRmf2Declaration[] Declarations,
        CompiledRmf2Selector[] Selectors, CompiledRmf2Variant[] Variants);

    private Ast ReadAst(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "An external pack object is malformed.");
        bool version = false, profile = false;
        CompiledRmf2Input[]? inputs = null;
        CompiledRmf2Declaration[]? declarations = null;
        CompiledRmf2Selector[]? selectors = null;
        CompiledRmf2Variant[]? variants = null;
        while (Member(ref reader))
        {
            if (reader.ValueTextEquals("astVersion"u8))
            {
                Once(version, "astVersion"); version = true; reader.Read();
                if (Integer(ref reader) != 5) throw Error("A message has an unsupported AST version.", TranslationPackFailureReason.ArtifactVersionMismatch);
            }
            else if (reader.ValueTextEquals("profile"u8))
            {
                Once(profile, "profile"); profile = true; reader.Read();
                if (reader.TokenType != JsonTokenType.String || !reader.ValueTextEquals("rmf2-execution-v2"u8))
                    throw Error("A message has an unsupported execution profile.", TranslationPackFailureReason.MessageGrammarVersionMismatch);
            }
            else if (reader.ValueTextEquals("inputs"u8)) { Once(inputs, "inputs"); reader.Read(); inputs = ReadInputs(ref reader); }
            else if (reader.ValueTextEquals("declarations"u8)) { Once(declarations, "declarations"); reader.Read(); declarations = ReadDeclarations(ref reader); }
            else if (reader.ValueTextEquals("selectors"u8)) { Once(selectors, "selectors"); reader.Read(); selectors = ReadSelectors(ref reader); }
            else if (reader.ValueTextEquals("variants"u8)) { Once(variants, "variants"); reader.Read(); variants = ReadVariants(ref reader); }
            else throw Unknown(ref reader);
        }
        if (!version) throw Missing("astVersion");
        if (!profile) throw Missing("profile");
        return new(inputs ?? throw Missing("inputs"), declarations ?? throw Missing("declarations"),
            selectors ?? throw Missing("selectors"), variants ?? throw Missing("variants"));
    }

    private CompiledRmf2Input[] ReadInputs(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartArray, "A message input contract must be an array.");
        _inputs.Clear();
        while (Next(ref reader) != JsonTokenType.EndArray)
        {
            if (_inputs.Count == _limits.MaximumArgumentsPerMessage) throw Limit("A message exceeds the configured argument limit.");
            Expect(ref reader, JsonTokenType.StartObject, "An external pack object is malformed.");
            string? name = null; TextArgumentType? type = null;
            while (Member(ref reader))
            {
                if (reader.ValueTextEquals("name"u8)) { Once(name, "name"); reader.Read(); name = String(ref reader); }
                else if (reader.ValueTextEquals("type"u8)) { Once(type, "type"); reader.Read(); type = Type(ref reader); }
                else throw Unknown(ref reader);
            }
            _inputs.Add(new CompiledRmf2Input(TrustedRmf2.Compiler, name ?? throw Missing("name"), type ?? throw Missing("type")));
        }
        return ToArray(_inputs);
    }

    private CompiledRmf2Declaration[] ReadDeclarations(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartArray, "A message declaration list is invalid.");
        _declarations.Clear();
        while (Next(ref reader) != JsonTokenType.EndArray)
        {
            if (_declarations.Count == 256) throw Limit("A message exceeds the normalized declaration limit.");
            Expect(ref reader, JsonTokenType.StartObject, "An external pack object is malformed.");
            string? kind = null, name = null; CompiledRmf2Expression? expression = null;
            while (Member(ref reader))
            {
                if (reader.ValueTextEquals("kind"u8)) { Once(kind, "kind"); reader.Read(); kind = String(ref reader); }
                else if (reader.ValueTextEquals("name"u8)) { Once(name, "name"); reader.Read(); name = String(ref reader); }
                else if (reader.ValueTextEquals("expression"u8)) { Once(expression, "expression"); reader.Read(); expression = ReadExpression(ref reader); }
                else throw Unknown(ref reader);
            }
            if (kind is not ("input" or "local") and not null) throw Error("A declaration kind is unsupported.");
            _declarations.Add(new CompiledRmf2Declaration(TrustedRmf2.Compiler, kind ?? throw Missing("kind"), name ?? throw Missing("name"), expression ?? throw Missing("expression")));
        }
        return ToArray(_declarations);
    }

    private CompiledRmf2Selector[] ReadSelectors(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartArray, "A message selector list is invalid.");
        _selectors.Clear();
        while (Next(ref reader) != JsonTokenType.EndArray)
        {
            if (_selectors.Count == 16) throw Limit("A message exceeds the normalized selector limit.");
            Expect(ref reader, JsonTokenType.StartObject, "An external pack object is malformed.");
            CompiledRmf2Value? value = null; TextArgumentType? type = null; string? function = null;
            while (Member(ref reader))
            {
                if (reader.ValueTextEquals("value"u8)) { Once(value, "value"); reader.Read(); value = ReadValue(ref reader); }
                else if (reader.ValueTextEquals("type"u8)) { Once(type, "type"); reader.Read(); type = Type(ref reader); }
                else if (reader.ValueTextEquals("function"u8)) { Once(function, "function"); reader.Read(); function = String(ref reader); }
                else throw Unknown(ref reader);
            }
            if (function is not ("exact" or "plural" or "ordinal") and not null) throw Error("A selector function is unsupported.");
            _selectors.Add(new CompiledRmf2Selector(TrustedRmf2.Compiler, value ?? throw Missing("value"), type ?? throw Missing("type"), function ?? throw Missing("function")));
        }
        return ToArray(_selectors);
    }

    private CompiledRmf2Variant[] ReadVariants(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartArray, "A message has an invalid or excessive variant list.");
        _variants.Clear();
        while (Next(ref reader) != JsonTokenType.EndArray)
        {
            if (_variants.Count == 256) throw Limit("A message has an invalid or excessive variant list.");
            Expect(ref reader, JsonTokenType.StartObject, "An external pack object is malformed.");
            CompiledRmf2Key[]? keys = null; CompiledRmf2Node[]? nodes = null;
            while (Member(ref reader))
            {
                if (reader.ValueTextEquals("keys"u8)) { Once(keys, "keys"); reader.Read(); keys = ReadKeys(ref reader); }
                else if (reader.ValueTextEquals("nodes"u8)) { Once(nodes, "nodes"); reader.Read(); nodes = ReadNodes(ref reader); }
                else throw Unknown(ref reader);
            }
            _variants.Add(new CompiledRmf2Variant(TrustedRmf2.Compiler, keys ?? throw Missing("keys"), nodes ?? throw Missing("nodes")));
        }
        if (_variants.Count == 0) throw Limit("A message has an invalid or excessive variant list.");
        return ToArray(_variants);
    }

    private CompiledRmf2Key[] ReadKeys(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartArray, "A message key list is invalid.");
        _keys.Clear();
        while (Next(ref reader) != JsonTokenType.EndArray)
        {
            if (_keys.Count == 16) throw Limit("A message exceeds the normalized key limit.");
            Expect(ref reader, JsonTokenType.StartObject, "A selector key is malformed.");
            string? kind = null, value = null, canonical = null;
            while (Member(ref reader))
            {
                if (reader.ValueTextEquals("kind"u8)) { Once(kind, "kind"); reader.Read(); kind = String(ref reader); }
                else if (reader.ValueTextEquals("value"u8)) { Once(value, "value"); reader.Read(); value = String(ref reader); }
                else if (reader.ValueTextEquals("canonical"u8)) { Once(canonical, "canonical"); reader.Read(); canonical = String(ref reader); }
                else throw Unknown(ref reader);
            }
            if (kind == "wildcard")
            {
                if (value is not null) throw Error("The external pack contains unknown property 'value'.", TranslationPackFailureReason.UnknownMember);
                if (canonical is not null) throw Error("The external pack contains unknown property 'canonical'.", TranslationPackFailureReason.UnknownMember);
                _keys.Add(CompiledRmf2Key.Wildcard);
            }
            else if (kind == "literal") _keys.Add(new CompiledRmf2Key(TrustedRmf2.Compiler, value ?? throw Missing("value"), canonical));
            else throw kind is null ? Error("A selector key is missing its kind.") : Error("A selector key kind is unsupported.");
        }
        return ToArray(_keys);
    }

    private CompiledRmf2Node[] ReadNodes(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartArray, "A message exceeds the normalized node limit.");
        _nodes.Clear();
        int depth = 0, textBytes = 0;
        while (Next(ref reader) != JsonTokenType.EndArray)
        {
            if (_nodes.Count == 4096) throw Limit("A message exceeds the normalized node limit.");
            Expect(ref reader, JsonTokenType.StartObject, "A message node is malformed.");
            string? kind = null, value = null, name = null, markupKind = null;
            CompiledRmf2Expression? expression = null;
            CompiledRmf2Option[]? options = null; CompiledRmf2Annotation[]? annotations = null;
            while (Member(ref reader))
            {
                if (reader.ValueTextEquals("kind"u8)) { Once(kind, "kind"); reader.Read(); kind = String(ref reader); }
                else if (reader.ValueTextEquals("value"u8)) { Once(value, "value"); reader.Read(); value = String(ref reader); }
                else if (reader.ValueTextEquals("expression"u8)) { Once(expression, "expression"); reader.Read(); expression = ReadExpression(ref reader); }
                else if (reader.ValueTextEquals("name"u8)) { Once(name, "name"); reader.Read(); name = String(ref reader); }
                else if (reader.ValueTextEquals("markupKind"u8)) { Once(markupKind, "markupKind"); reader.Read(); markupKind = String(ref reader); }
                else if (reader.ValueTextEquals("options"u8)) { Once(options, "options"); reader.Read(); options = ReadOptions(ref reader); }
                else if (reader.ValueTextEquals("annotations"u8)) { Once(annotations, "annotations"); reader.Read(); annotations = ReadAnnotations(ref reader); }
                else throw Unknown(ref reader);
            }
            switch (kind)
            {
                case "text":
                    Only(expression is null && name is null && markupKind is null && options is null && annotations is null);
                    string text = value ?? throw Missing("value");
                    textBytes += Encoding.UTF8.GetByteCount(text);
                    if (textBytes > _limits.MaximumPatternBytes) throw Limit("A message exceeds the configured pattern limit.");
                    _nodes.Add(new CompiledRmf2Node(text));
                    break;
                case "expression":
                    Only(value is null && name is null && markupKind is null && options is null && annotations is null);
                    _nodes.Add(new CompiledRmf2Node(expression ?? throw Missing("expression")));
                    break;
                case "markup":
                    Only(value is null && expression is null);
                    if (markupKind is not ("open" or "close" or "standalone")) throw markupKind is null ? Missing("markupKind") : Error("A markup event kind is unsupported.");
                    if (markupKind == "open" && ++depth > 16) throw Limit("A message exceeds the normalized markup depth limit.");
                    if (markupKind == "close") depth--;
                    _nodes.Add(new CompiledRmf2Node(TrustedRmf2.Compiler, name ?? throw Missing("name"), markupKind,
                        options ?? throw Missing("options"), annotations ?? throw Missing("annotations")));
                    break;
                default:
                    throw kind is null ? Error("A message node is malformed.") : Error("A message node kind is unsupported.");
            }
        }
        return ToArray(_nodes);
    }

    private CompiledRmf2Expression ReadExpression(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "An external pack object is malformed.");
        CompiledRmf2Value? operand = null; TextArgumentType? valueType = null; string? function = null;
        CompiledRmf2Option[]? options = null; CompiledRmf2Annotation[]? annotations = null;
        while (Member(ref reader))
        {
            if (reader.ValueTextEquals("operand"u8)) { Once(operand, "operand"); reader.Read(); operand = ReadValue(ref reader); }
            else if (reader.ValueTextEquals("valueType"u8)) { Once(valueType, "valueType"); reader.Read(); valueType = Type(ref reader); }
            else if (reader.ValueTextEquals("function"u8)) { Once(function, "function"); reader.Read(); function = String(ref reader); }
            else if (reader.ValueTextEquals("options"u8)) { Once(options, "options"); reader.Read(); options = ReadOptions(ref reader); }
            else if (reader.ValueTextEquals("annotations"u8)) { Once(annotations, "annotations"); reader.Read(); annotations = ReadAnnotations(ref reader); }
            else throw Unknown(ref reader);
        }
        return new CompiledRmf2Expression(TrustedRmf2.Compiler, operand ?? throw Missing("operand"), valueType ?? throw Missing("valueType"), function,
            options ?? throw Missing("options"), annotations ?? throw Missing("annotations"));
    }

    private CompiledRmf2Option[] ReadOptions(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartArray, "A message option list is invalid.");
        _options.Clear();
        while (Next(ref reader) != JsonTokenType.EndArray)
        {
            if (_options.Count == 256) throw Limit("A message exceeds the normalized option limit.");
            Expect(ref reader, JsonTokenType.StartObject, "An external pack object is malformed.");
            string? name = null; CompiledRmf2Value? value = null;
            while (Member(ref reader))
            {
                if (reader.ValueTextEquals("name"u8)) { Once(name, "name"); reader.Read(); name = String(ref reader); }
                else if (reader.ValueTextEquals("value"u8)) { Once(value, "value"); reader.Read(); value = ReadValue(ref reader); }
                else throw Unknown(ref reader);
            }
            _options.Add(new CompiledRmf2Option(TrustedRmf2.Compiler, name ?? throw Missing("name"), value ?? throw Missing("value")));
        }
        return ToArray(_options);
    }

    private CompiledRmf2Annotation[] ReadAnnotations(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartArray, "A message annotation list is invalid.");
        _annotations.Clear();
        while (Next(ref reader) != JsonTokenType.EndArray)
        {
            if (_annotations.Count == 256) throw Limit("A message exceeds the normalized annotation limit.");
            Expect(ref reader, JsonTokenType.StartObject, "An external pack object is malformed.");
            string? name = null; CompiledRmf2Value? value = null;
            while (Member(ref reader))
            {
                if (reader.ValueTextEquals("name"u8)) { Once(name, "name"); reader.Read(); name = String(ref reader); }
                else if (reader.ValueTextEquals("value"u8)) { Once(value, "value"); reader.Read(); value = ReadValue(ref reader); }
                else throw Unknown(ref reader);
            }
            if (value?.Kind is "input" or "local") throw Error("An annotation value must be literal.");
            _annotations.Add(new CompiledRmf2Annotation(TrustedRmf2.Compiler, name ?? throw Missing("name"), value));
        }
        return ToArray(_annotations);
    }

    private CompiledRmf2Value ReadValue(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "A normalized value is malformed.");
        string? kind = null, value = null, canonical = null;
        while (Member(ref reader))
        {
            if (reader.ValueTextEquals("kind"u8)) { Once(kind, "kind"); reader.Read(); kind = String(ref reader); }
            else if (reader.ValueTextEquals("value"u8)) { Once(value, "value"); reader.Read(); value = String(ref reader); }
            else if (reader.ValueTextEquals("canonical"u8)) { Once(canonical, "canonical"); reader.Read(); canonical = String(ref reader); }
            else throw Unknown(ref reader);
        }
        if (kind is null) throw Error("A normalized value is missing its kind.");
        if (kind is not ("input" or "local" or "string-literal" or "number-literal")) throw Error("A normalized value kind is unsupported.");
        if (kind == "number-literal" ? canonical is null : canonical is not null)
            throw canonical is null ? Missing("canonical") : Error("The external pack contains unknown property 'canonical'.", TranslationPackFailureReason.UnknownMember);
        return new CompiledRmf2Value(TrustedRmf2.Compiler, kind, value ?? throw Missing("value"), canonical);
    }

    // Moves from an object's start, or from the end of the previous member's value, to the next property name; false at
    // the end of the object.
    private static bool Member(ref Utf8JsonReader reader) => Next(ref reader) switch
    {
        JsonTokenType.PropertyName => true,
        JsonTokenType.EndObject => false,
        _ => throw Error("An external pack object is malformed."),
    };

    private string String(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String) throw Error("An external pack string is invalid.");
        if (reader.HasValueSequence || reader.ValueSpan.Length > SharedStringBytes) return reader.GetString()!;
        Span<char> buffer = stackalloc char[SharedStringBytes];
        ReadOnlySpan<char> text = buffer[..reader.CopyString(buffer)];
        if (_lookup.TryGetValue(text, out string? shared)) return shared;
        string created = text.ToString();
        _strings.Add(created);
        return created;
    }

    private static int Integer(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value) ? value : throw Error("An external pack integer is invalid.");

    private static TextArgumentType Type(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String) throw Error("An external pack string is invalid.");
        if (reader.ValueTextEquals("string"u8)) return TextArgumentType.String;
        if (reader.ValueTextEquals("int64"u8)) return TextArgumentType.Int;
        if (reader.ValueTextEquals("decimal"u8)) return TextArgumentType.Number;
        if (reader.ValueTextEquals("boolean"u8)) return TextArgumentType.Bool;
        if (reader.ValueTextEquals("date"u8)) return TextArgumentType.Date;
        if (reader.ValueTextEquals("time"u8)) return TextArgumentType.Time;
        if (reader.ValueTextEquals("datetime"u8)) return TextArgumentType.DateTime;
        if (reader.ValueTextEquals("guid"u8)) return TextArgumentType.Guid;
        if (reader.ValueTextEquals("list"u8)) return TextArgumentType.List;
        throw Error("A v5 carrier type is unsupported.");
    }

    private static JsonTokenType Next(ref Utf8JsonReader reader) =>
        reader.Read() ? reader.TokenType : throw Error("Generated locale data is incomplete.");

    private static void Next(ref Utf8JsonReader reader, JsonTokenType expected, string message)
    {
        if (Next(ref reader) != expected) throw Error(message);
    }

    private static void Expect(ref Utf8JsonReader reader, JsonTokenType expected, string message)
    {
        if (reader.TokenType != expected) throw Error(message);
    }

    private static T[] ToArray<T>(List<T> items) => items.Count == 0 ? [] : items.ToArray();

    private static void Once(object? current, string name)
    {
        if (current is not null) throw Error("The external pack contains duplicate property '" + name + "'.");
    }

    private static void Once(bool seen, string name) => Once(seen ? (object)true : null, name);

    private static void Only(bool valid)
    {
        if (!valid) throw Error("A message node has a property its kind does not allow.", TranslationPackFailureReason.UnknownMember);
    }

    private static TranslationPackException Unknown(ref Utf8JsonReader reader) =>
        Error("The external pack contains unknown property '" + reader.GetString() + "'.", TranslationPackFailureReason.UnknownMember);

    private static TranslationPackException Missing(string name) => Error("The external pack is missing required property '" + name + "'.");
    private static TranslationPackException Limit(string message) => Error(message, TranslationPackFailureReason.LimitExceeded);
    private static TranslationPackException Error(string message, TranslationPackFailureReason reason = TranslationPackFailureReason.Malformed) =>
        TranslationPackFailure.Create(message, reason);
}
