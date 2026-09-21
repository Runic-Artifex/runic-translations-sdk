using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Runic.Translations;

// This parser is intentionally independent of the v2/v4 carrier. Accepting a
// v5 envelope never implies that an older reader can safely approximate it.
internal static class TranslationPackV5Loader
{
    internal static VerifiedExternalTranslationPack Parse(ReadOnlyMemory<byte> content,
        TranslationPackContract contract, TranslationPackLimits limits, CancellationToken cancellationToken)
    {
        try
        {
            if (!WithinMaximumDepth(content.Span, limits.MaximumDepth))
                throw Limit("The external pack exceeds the configured depth limit.");
            using JsonDocument document = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = limits.MaximumDepth,
            });
            Dictionary<string, JsonElement> root = Members(document.RootElement,
                ["artifactVersion", "messageGrammarVersion", "profile", "catalog", "locale", "contractFingerprint", "messages", "markupContract"]);
            if (Integer(root["artifactVersion"]) != 5)
                throw Error("The external pack artifact version is unsupported.", TranslationPackFailureReason.ArtifactVersionMismatch);
            if (Integer(root["messageGrammarVersion"]) != 5 || contract.MessageGrammarVersion != 5)
                throw Error("The external pack message grammar version is unsupported.", TranslationPackFailureReason.MessageGrammarVersionMismatch);
            if (String(root["profile"]) != contract.Profile || contract.Profile != "rmf2-execution-v2")
                throw Error("The external pack execution profile is unsupported.", TranslationPackFailureReason.MessageGrammarVersionMismatch);
            string catalog = String(root["catalog"]), locale = String(root["locale"]), fingerprint = String(root["contractFingerprint"]);
            if (!TranslationPackValidation.IsCatalog(catalog)) throw Error("The external pack catalog identifier is invalid.");
            if (!TranslationPackValidation.IsCanonicalLocale(locale)) throw Error("The external pack locale is not canonical.");
            if (!TranslationPackValidation.IsFingerprint(fingerprint)) throw Error("The external pack fingerprint is invalid.");
            if (!string.Equals(catalog, contract.Catalog, StringComparison.Ordinal)) throw Error("The external pack catalog does not match the generated contract.", TranslationPackFailureReason.CatalogMismatch);
            if (!string.Equals(locale, contract.Locale, StringComparison.Ordinal)) throw Error("The external pack locale does not match the generated contract.", TranslationPackFailureReason.LocaleMismatch);
            // The fingerprint proves compatibility with generated code. Trust in
            // the bytes remains exclusively the caller's integrity policy.
            if (!string.Equals(fingerprint, contract.ContractFingerprint, StringComparison.Ordinal)) throw Error("The external pack fingerprint does not match the generated contract.", TranslationPackFailureReason.ContractFingerprintMismatch);
            if (root["markupContract"].GetRawText() != contract.Rmf2MarkupContract)
                throw Error("The RMF2 markup contract differs from the trusted catalog.", TranslationPackFailureReason.ArgumentContractMismatch);
            var markup = new Rmf2InlineRenderer(contract.Rmf2MarkupContract!);
            if (root["messages"].ValueKind != JsonValueKind.Object) throw Error("The external pack messages value must be an object.");
            var messages = new List<VerifiedTranslationPackMessage>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root["messages"].EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!keys.Add(property.Name)) throw Error("The external pack contains duplicate message key '" + property.Name + "'.");
                if (messages.Count >= limits.MaximumMessages) throw Limit("The external pack exceeds the configured message limit.");
                if (!TranslationPackValidation.IsResourceKey(property.Name)) throw Error("The external pack contains an invalid message key.");
                if (!contract.TryGetMessage(property.Name, out TranslationPackMessageContract messageContract))
                    throw Error("The external pack contains unknown message key '" + property.Name + "'.", TranslationPackFailureReason.UnknownKey);
                messages.Add(ReadMessage(property.Value, messageContract, limits, markup, locale));
            }
            messages.Sort(static (left, right) => string.CompareOrdinal(left.Key.Name, right.Key.Name));
            return new VerifiedExternalTranslationPack(catalog, locale, fingerprint, messages.ToArray());
        }
        catch (TranslationPackException) { throw; }
        catch (JsonException exception) { throw Error("The external pack is incomplete, contains invalid UTF-8, or malformed JSON near byte " + exception.BytePositionInLine + "."); }
        catch (DecoderFallbackException) { throw Error("The external pack contains invalid UTF-8 text."); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        { throw Error("The external pack contains an invalid normalized v5 AST.", TranslationPackFailureReason.MalformedPattern); }
    }

    private static VerifiedTranslationPackMessage ReadMessage(JsonElement value, TranslationPackMessageContract contract,
        TranslationPackLimits limits, Rmf2InlineRenderer markup, string locale)
    {
        Dictionary<string, JsonElement> wrapper = Members(value, ["contentLocale", "ast"]);
        string contentLocale = String(wrapper["contentLocale"]);
        if (!TranslationPackValidation.IsCanonicalLocale(contentLocale) || contentLocale != markup.ExpectedLocale(contract.Key.Name, locale))
            throw Error("RMF2 effective content locale mismatch.", TranslationPackFailureReason.ArgumentContractMismatch);
        CompiledRmf2Message message = ReadAst(wrapper["ast"], contract, limits, contentLocale);
        try { markup.ValidatePlanV5(contract, message); }
        catch (TranslationFormatException exception) { throw Error(exception.Message, TranslationPackFailureReason.ArgumentContractMismatch); }
        return new VerifiedTranslationPackMessage(contract.Key, string.Empty, CompiledTextMessage.FromRmf2(message));
    }

    private static CompiledRmf2Message ReadAst(JsonElement value, TranslationPackMessageContract contract,
        TranslationPackLimits limits, string contentLocale)
    {
        Dictionary<string, JsonElement> ast = Members(value, ["astVersion", "profile", "inputs", "declarations", "selectors", "variants"]);
        if (Integer(ast["astVersion"]) != 5) throw Error("A message has an unsupported AST version.", TranslationPackFailureReason.ArtifactVersionMismatch);
        if (String(ast["profile"]) != "rmf2-execution-v2") throw Error("A message has an unsupported execution profile.", TranslationPackFailureReason.MessageGrammarVersionMismatch);
        CompiledRmf2Input[] inputs = ReadInputs(ast["inputs"], contract, limits);
        CompiledRmf2Declaration[] declarations = ReadArray(ast["declarations"], 256, ReadDeclaration, "declaration");
        CompiledRmf2Selector[] selectors = ReadArray(ast["selectors"], 16, ReadSelector, "selector");
        if (ast["variants"].ValueKind != JsonValueKind.Array || ast["variants"].GetArrayLength() is < 1 or > 256)
            throw Limit("A message has an invalid or excessive variant list.");
        var variants = new List<CompiledRmf2Variant>();
        foreach (JsonElement item in ast["variants"].EnumerateArray()) variants.Add(ReadVariant(item, limits));
        return new CompiledRmf2Message(inputs, declarations, selectors, variants, contentLocale);
    }

    private static CompiledRmf2Input[] ReadInputs(JsonElement value, TranslationPackMessageContract contract, TranslationPackLimits limits)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Error("A message input contract must be an array.");
        if (value.GetArrayLength() > limits.MaximumArgumentsPerMessage) throw Limit("A message exceeds the configured argument limit.");
        if (value.GetArrayLength() != contract.Arguments.Count) throw ContractMismatch(contract.Key.Name);
        var result = new CompiledRmf2Input[contract.Arguments.Count];
        int index = 0;
        foreach (JsonElement item in value.EnumerateArray())
        {
            Dictionary<string, JsonElement> fields = Members(item, ["name", "type"]);
            string name = String(fields["name"]);
            TextArgumentType type = Type(String(fields["type"]));
            TranslationPackArgumentContract expected = contract.Arguments[index];
            if (name != expected.Name || type != expected.Type || !TranslationPackValidation.IsRmf2Name(name)) throw ContractMismatch(contract.Key.Name);
            result[index++] = new CompiledRmf2Input(name, type);
        }
        return result;
    }

    private static CompiledRmf2Declaration ReadDeclaration(JsonElement value)
    {
        Dictionary<string, JsonElement> fields = Members(value, ["kind", "name", "expression"]);
        return new CompiledRmf2Declaration(String(fields["kind"]), String(fields["name"]), ReadExpression(fields["expression"]));
    }

    private static CompiledRmf2Selector ReadSelector(JsonElement value)
    {
        Dictionary<string, JsonElement> fields = Members(value, ["value", "type", "function"]);
        return new CompiledRmf2Selector(ReadValue(fields["value"]), Type(String(fields["type"])), String(fields["function"]));
    }

    private static CompiledRmf2Variant ReadVariant(JsonElement value, TranslationPackLimits limits)
    {
        Dictionary<string, JsonElement> fields = Members(value, ["keys", "nodes"]);
        CompiledRmf2Key[] keys = ReadArray(fields["keys"], 16, ReadKey, "key");
        if (fields["nodes"].ValueKind != JsonValueKind.Array || fields["nodes"].GetArrayLength() > 4096)
            throw Limit("A message exceeds the normalized node limit.");
        var nodes = new List<CompiledRmf2Node>();
        int depth = 0, textBytes = 0;
        foreach (JsonElement item in fields["nodes"].EnumerateArray())
        {
            CompiledRmf2Node node = ReadNode(item, ref textBytes, limits);
            if (node.Kind == "markup" && node.MarkupKind == "open" && ++depth > 16) throw Limit("A message exceeds the normalized markup depth limit.");
            if (node.Kind == "markup" && node.MarkupKind == "close") depth--;
            nodes.Add(node);
        }
        return new CompiledRmf2Variant(keys, nodes);
    }

    private static CompiledRmf2Key ReadKey(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("A selector key is malformed.");
        string kind = value.TryGetProperty("kind", out JsonElement kindValue) ? String(kindValue) : throw Error("A selector key is missing its kind.");
        if (kind == "wildcard") { _ = Members(value, ["kind"]); return new CompiledRmf2Key(); }
        Dictionary<string, JsonElement> fields = MembersSubset(value, new HashSet<string>(["kind", "value", "canonical"], StringComparer.Ordinal), ["kind", "value"]);
        if (kind != "literal") throw Error("A selector key kind is unsupported.");
        return new CompiledRmf2Key(String(fields["value"]), fields.TryGetValue("canonical", out JsonElement canonical) ? String(canonical) : null);
    }

    private static CompiledRmf2Node ReadNode(JsonElement value, ref int textBytes, TranslationPackLimits limits)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("kind", out JsonElement kindValue)) throw Error("A message node is malformed.");
        string kind = String(kindValue);
        if (kind == "text")
        {
            Dictionary<string, JsonElement> fields = Members(value, ["kind", "value"]);
            string text = String(fields["value"]); textBytes += Encoding.UTF8.GetByteCount(text);
            if (textBytes > limits.MaximumPatternBytes) throw Limit("A message exceeds the configured pattern limit.");
            return new CompiledRmf2Node(text);
        }
        if (kind == "expression")
        {
            Dictionary<string, JsonElement> fields = Members(value, ["kind", "expression"]);
            return new CompiledRmf2Node(ReadExpression(fields["expression"]));
        }
        if (kind == "markup")
        {
            Dictionary<string, JsonElement> fields = Members(value, ["kind", "name", "markupKind", "options", "annotations"]);
            return new CompiledRmf2Node(String(fields["name"]), String(fields["markupKind"]),
                ReadArray(fields["options"], 256, ReadOption, "option"), ReadArray(fields["annotations"], 256, ReadAnnotation, "annotation"));
        }
        throw Error("A message node kind is unsupported.");
    }

    private static CompiledRmf2Expression ReadExpression(JsonElement value)
    {
        Dictionary<string, JsonElement> fields = MembersSubset(value,
            new HashSet<string>(["operand", "valueType", "function", "options", "annotations"], StringComparer.Ordinal),
            ["operand", "valueType", "options", "annotations"]);
        return new CompiledRmf2Expression(ReadValue(fields["operand"]), Type(String(fields["valueType"])),
            fields.TryGetValue("function", out JsonElement function) ? String(function) : null,
            ReadArray(fields["options"], 256, ReadOption, "option"), ReadArray(fields["annotations"], 256, ReadAnnotation, "annotation"));
    }

    private static CompiledRmf2Option ReadOption(JsonElement value)
    {
        Dictionary<string, JsonElement> fields = Members(value, ["name", "value"]);
        return new CompiledRmf2Option(String(fields["name"]), ReadValue(fields["value"]));
    }

    private static CompiledRmf2Annotation ReadAnnotation(JsonElement value)
    {
        Dictionary<string, JsonElement> fields = MembersSubset(value,
            new HashSet<string>(["name", "value"], StringComparer.Ordinal), ["name"]);
        return new CompiledRmf2Annotation(String(fields["name"]), fields.TryGetValue("value", out JsonElement item) ? ReadLiteral(item) : null);
    }

    private static CompiledRmf2Value ReadLiteral(JsonElement value)
    {
        CompiledRmf2Value result = ReadValue(value);
        if (result.Kind is "input" or "local") throw Error("An annotation value must be literal.");
        return result;
    }

    private static CompiledRmf2Value ReadValue(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("A normalized value is malformed.");
        string kind = value.TryGetProperty("kind", out JsonElement kindValue) ? String(kindValue) : throw Error("A normalized value is missing its kind.");
        bool numeric = kind == "number-literal";
        Dictionary<string, JsonElement> fields = Members(value, numeric ? ["kind", "value", "canonical"] : ["kind", "value"]);
        if (kind is not ("input" or "local" or "string-literal" or "number-literal")) throw Error("A normalized value kind is unsupported.");
        return new CompiledRmf2Value(kind, String(fields["value"]), numeric ? String(fields["canonical"]) : null);
    }

    private static T[] ReadArray<T>(JsonElement value, int maximum, Func<JsonElement, T> read, string name)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Error("A message " + name + " list is invalid.");
        if (value.GetArrayLength() > maximum) throw Limit("A message exceeds the normalized " + name + " limit.");
        var result = new T[value.GetArrayLength()]; int index = 0;
        foreach (JsonElement item in value.EnumerateArray()) result[index++] = read(item);
        return result;
    }

    private static Dictionary<string, JsonElement> Members(JsonElement value, string[] expected) =>
        MembersSubset(value, new HashSet<string>(expected, StringComparer.Ordinal), expected);

    private static Dictionary<string, JsonElement> MembersSubset(JsonElement value, HashSet<string> allowed, string[] required)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("An external pack object is malformed.");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)) throw Error("The external pack contains unknown property '" + property.Name + "'.", TranslationPackFailureReason.UnknownMember);
            if (!result.TryAdd(property.Name, property.Value)) throw Error("The external pack contains duplicate property '" + property.Name + "'.");
        }
        foreach (string name in required) if (!result.ContainsKey(name)) throw Error("The external pack is missing required property '" + name + "'.");
        return result;
    }

    private static TextArgumentType Type(string value) => value switch
    {
        "string" => TextArgumentType.String, "int64" => TextArgumentType.Int, "decimal" => TextArgumentType.Number,
        "boolean" => TextArgumentType.Bool, "date" => TextArgumentType.Date, "time" => TextArgumentType.Time,
        "datetime" => TextArgumentType.DateTime, "guid" => TextArgumentType.Guid,
        _ => throw Error("A v5 carrier type is unsupported."),
    };

    private static bool WithinMaximumDepth(ReadOnlySpan<byte> content, int maximumDepth)
    {
        int depth = 0; bool quoted = false, escaped = false;
        foreach (byte item in content)
        {
            if (quoted) { if (escaped) escaped = false; else if (item == (byte)'\\') escaped = true; else if (item == (byte)'\"') quoted = false; }
            else if (item == (byte)'\"') quoted = true;
            else if (item is (byte)'{' or (byte)'[') { if (++depth > maximumDepth) return false; }
            else if (item is (byte)'}' or (byte)']') depth--;
        }
        return true;
    }

    private static int Integer(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : throw Error("An external pack integer is invalid.");
    private static string String(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Error("An external pack string is invalid.");
    private static TranslationPackException ContractMismatch(string key) => Error("Message '" + key + "' does not match its generated argument contract.", TranslationPackFailureReason.ArgumentContractMismatch);
    private static TranslationPackException Limit(string message) => Error(message, TranslationPackFailureReason.LimitExceeded);
    private static TranslationPackException Error(string message, TranslationPackFailureReason reason = TranslationPackFailureReason.Malformed) => TranslationPackFailure.Create(message, reason);
}
