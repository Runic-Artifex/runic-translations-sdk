using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunicTranslations;

/// <summary>Native-AOT-safe JSON metadata for the versioned text-reference wire contract.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TextResourceReference))]
public partial class TextResourceReferenceJsonContext : JsonSerializerContext
{
}

internal sealed class TextResourceReferenceJsonConverter : JsonConverter<TextResourceReference>
{
    public override TextResourceReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A text reference must be an object.");
        int? version = null;
        string? catalog = null;
        string? fingerprint = null;
        string? key = null;
        string? fallback = null;
        Dictionary<string, TextResourceReferenceArgument>? arguments = null;
        var members = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            string member = reader.GetString()!;
            if (!members.Add(member) || !reader.Read()) throw new JsonException("Duplicate or incomplete text-reference member.");
            switch (member)
            {
                case "version": version = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int parsed) ? parsed : throw new JsonException(); break;
                case "catalog": catalog = ReadString(ref reader); break;
                case "contractFingerprint": fingerprint = ReadString(ref reader); break;
                case "key": key = ReadString(ref reader); break;
                case "arguments": arguments = ReadArguments(ref reader); break;
                case "fallbackText": fallback = reader.TokenType == JsonTokenType.Null ? null : ReadString(ref reader); break;
                default: throw new JsonException("Unknown text-reference member '" + member + "'.");
            }
        }
        if (version != TextResourceTransport.Version) throw new JsonException("Unsupported text-reference version.");
        if (catalog is null || fingerprint is null || key is null || arguments is null) throw new JsonException("Text reference is incomplete.");
        try { return new TextResourceReference(catalog, fingerprint, key, arguments, fallback); }
        catch (ArgumentException exception) { throw new JsonException("Text reference is invalid.", exception); }
    }

    public override void Write(Utf8JsonWriter writer, TextResourceReference value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteNumber("version", value.Version);
        writer.WriteString("catalog", value.Catalog);
        writer.WriteString("contractFingerprint", value.ContractFingerprint);
        writer.WriteString("key", value.Key);
        writer.WritePropertyName("arguments");
        writer.WriteStartObject();
        var names = new List<string>(value.Arguments.Keys);
        names.Sort(StringComparer.Ordinal);
        for (int index = 0; index < names.Count; index++)
        {
            TextResourceReferenceArgument argument = value.Arguments[names[index]];
            if (argument.Type == TextArgumentType.Bool) writer.WriteBoolean(names[index], argument.Value == "true");
            else writer.WriteString(names[index], argument.Value);
        }
        writer.WriteEndObject();
        if (value.FallbackText is not null) writer.WriteString("fallbackText", value.FallbackText);
        writer.WriteEndObject();
    }

    private static Dictionary<string, TextResourceReferenceArgument> ReadArguments(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Text-reference arguments must be an object.");
        var result = new Dictionary<string, TextResourceReferenceArgument>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            string name = reader.GetString()!;
            if (!reader.Read()) throw new JsonException();
            TextResourceReferenceArgument argument = reader.TokenType switch
            {
                JsonTokenType.String => new TextResourceReferenceArgument(TextArgumentType.String, reader.GetString()!),
                JsonTokenType.True => new TextResourceReferenceArgument(TextArgumentType.Bool, "true"),
                JsonTokenType.False => new TextResourceReferenceArgument(TextArgumentType.Bool, "false"),
                _ => throw new JsonException("Wire arguments must be strings or booleans."),
            };
            if (!result.TryAdd(name, argument)) throw new JsonException("Duplicate text-reference argument.");
            if (result.Count > TextResourceTransport.MaximumArguments) throw new JsonException("Too many text-reference arguments.");
        }
        return result;
    }

    private static string ReadString(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.String ? reader.GetString()! : throw new JsonException();
}
