using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runic.Translations;

/// <summary>Native-AOT-safe JSON metadata for the versioned text-reference wire contract.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TranslationReference))]
[SuppressMessage("ApiDesign", "RS0041:Public members should not use oblivious types", Justification = "The System.Text.Json source generator emits nullable-oblivious JsonTypeInfo properties.")]
public partial class TranslationReferenceJsonContext : JsonSerializerContext
{
}

// Version 1: {"version":1,"catalog","contractFingerprint","key","arguments":{name: string | boolean},"fallbackText"?}.
// Version 2: {"version":2,"catalog","contractFingerprint","key","arguments":{name: {"type","value"}},"fallbackText"?}, where a
// "message" value is {"key","arguments","fallbackText"?} of the same catalog. Writers emit members in this order and arguments
// sorted by ordinal name; readers accept any member order but reject unknown and duplicate members.
internal sealed class TranslationReferenceJsonConverter : JsonConverter<TranslationReference>
{
    public override TranslationReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A text reference must be an object.");
        // Measure the encoded object by scanning its tokens before building anything from it.
        Utf8JsonReader scan = reader;
        long start = reader.TokenStartIndex;
        if (!scan.TrySkip()) throw new JsonException("The text reference is incomplete.");
        if (scan.BytesConsumed - start > TranslationTransport.MaximumEncodedLength)
            throw new JsonException("The text reference is larger than the transport limit.");
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        try
        {
            JsonElement root = document.RootElement;
            var members = Members(root, ["version", "catalog", "contractFingerprint", "key", "arguments", "fallbackText"]);
            // An integer token only: 2.0 and 2e0 are rejected, as the ESM reader rejects them.
            if (!members.TryGetValue("version", out JsonElement versionElement) || versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out int version) || versionElement.GetRawText() != version.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                version is < TranslationTransport.MinimumReadVersion or > TranslationTransport.Version)
                throw new JsonException("Unsupported text-reference version.");
            if (!members.TryGetValue("catalog", out JsonElement catalog) || !members.TryGetValue("contractFingerprint", out JsonElement fingerprint))
                throw new JsonException("Text reference is incomplete.");
            return Reference(version, String(catalog), String(fingerprint), members, depth: 1);
        }
        catch (ArgumentException exception) { throw new JsonException("Text reference is invalid.", exception); }
        // GetString and JsonProperty.Name throw InvalidOperationException for a lone surrogate escape.
        catch (InvalidOperationException exception) { throw new JsonException("Text reference is invalid.", exception); }
    }

    private static TranslationReference Reference(int version, string catalog, string fingerprint, Dictionary<string, JsonElement> members, int depth)
    {
        if (!members.TryGetValue("key", out JsonElement key) || !members.TryGetValue("arguments", out JsonElement argumentsElement))
            throw new JsonException("Text reference is incomplete.");
        string? fallback = null;
        // Version 1 readers accepted a null fallback; version 2 omits the member instead.
        if (members.TryGetValue("fallbackText", out JsonElement fallbackElement) && (version != 1 || fallbackElement.ValueKind != JsonValueKind.Null))
            fallback = String(fallbackElement);
        if (argumentsElement.ValueKind != JsonValueKind.Object) throw new JsonException("Text-reference arguments must be an object.");
        var arguments = new Dictionary<string, TranslationReferenceArgument>(StringComparer.Ordinal);
        foreach (JsonProperty property in argumentsElement.EnumerateObject())
        {
            if (arguments.Count == TranslationTransport.MaximumArguments) throw new JsonException("Too many text-reference arguments.");
            TranslationReferenceArgument argument = version == 1
                ? property.Value.ValueKind switch
                {
                    JsonValueKind.String => new TranslationReferenceArgument(TextArgumentType.String, property.Value.GetString()!),
                    JsonValueKind.True => new TranslationReferenceArgument(TextArgumentType.Bool, "true"),
                    JsonValueKind.False => new TranslationReferenceArgument(TextArgumentType.Bool, "false"),
                    _ => throw new JsonException("Wire arguments must be strings or booleans."),
                }
                : Argument(property.Value, catalog, fingerprint, depth);
            if (!arguments.TryAdd(property.Name, argument)) throw new JsonException("Duplicate text-reference argument.");
        }
        return new TranslationReference(version, catalog, fingerprint, String(key), arguments, fallback);
    }

    private static TranslationReferenceArgument Argument(JsonElement element, string catalog, string fingerprint, int depth)
    {
        var members = Members(element, ["type", "value"]);
        if (!members.TryGetValue("type", out JsonElement typeElement) || !members.TryGetValue("value", out JsonElement value))
            throw new JsonException("A version 2 argument needs a type and a value.");
        string type = String(typeElement);
        if (type == "message")
        {
            if (depth + 1 > TranslationTransport.MaximumDepth) throw new JsonException("Message arguments are nested too deeply.");
            return new TranslationReferenceArgument(Reference(TranslationTransport.Version, catalog, fingerprint,
                Members(value, ["key", "arguments", "fallbackText"]), depth + 1));
        }
        if (!TranslationTransport.TryParseWireType(type, out TextArgumentType argumentType)) throw new JsonException("Unknown argument type '" + type + "'.");
        switch (argumentType)
        {
            case TextArgumentType.Bool:
                return value.ValueKind switch
                {
                    JsonValueKind.True => new TranslationReferenceArgument(TextArgumentType.Bool, "true"),
                    JsonValueKind.False => new TranslationReferenceArgument(TextArgumentType.Bool, "false"),
                    _ => throw new JsonException("A boolean argument needs a JSON boolean."),
                };
            case TextArgumentType.List:
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > TranslationTransport.MaximumListItems)
                    throw new JsonException("A list argument needs an array of at most " + TranslationTransport.MaximumListItems + " strings.");
                var items = new List<string>(value.GetArrayLength());
                foreach (JsonElement item in value.EnumerateArray()) items.Add(String(item));
                return new TranslationReferenceArgument(items);
            default:
                return new TranslationReferenceArgument(argumentType, String(value));
        }
    }

    private static Dictionary<string, JsonElement> Members(JsonElement element, string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException("A text-reference object was expected.");
        var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (Array.IndexOf(allowed, property.Name) < 0) throw new JsonException("Unknown text-reference member '" + property.Name + "'.");
            if (!members.TryAdd(property.Name, property.Value)) throw new JsonException("Duplicate text-reference member.");
        }
        return members;
    }

    public override void Write(Utf8JsonWriter writer, TranslationReference value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        // The transport's own escaping, identical to JSON.stringify, independent of the serializer's encoder.
        writer.WriteRawValue(TranslationReferenceWire.Write(value), skipInputValidation: true);
    }

    private static string String(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : throw new JsonException("A JSON string was expected.");
}
