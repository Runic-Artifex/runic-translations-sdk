using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Runic.Translations.Build.Tests;

// Checks tool payloads against the published schemas (specs/translations/schemas/translations-*-v1.schema.json).
// The schemas let later releases add properties; these checks are strict, so a property the schema does not declare
// fails here and the schema is updated in the same change (the snapshot of the current shape).
internal static class PayloadSchemas
{
    internal const string Tool = "translations-tool-v1.schema.json";
    internal const string Support = "translations-support-v1.schema.json";
    internal const string Serve = "translations-serve-v1.schema.json";

    internal static void Validate(string schemaFile, JsonElement value)
    {
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "schemas", schemaFile)));
        var errors = new List<string>();
        Check(schema.RootElement, schema.RootElement, value, "$", errors);
        Assert.True(errors.Count == 0, schemaFile + ": " + string.Join("; ", errors) + "\n" + value.GetRawText());
    }

    private static void Check(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        if (schema.TryGetProperty("$ref", out JsonElement reference))
        {
            string name = reference.GetString()!["#/$defs/".Length..];
            Check(root, root.GetProperty("$defs").GetProperty(name), value, path, errors);
            return;
        }
        if (schema.TryGetProperty("oneOf", out JsonElement oneOf))
        {
            int matches = oneOf.EnumerateArray().Count(option => { var inner = new List<string>(); Check(root, option, value, path, inner); return inner.Count == 0; });
            if (matches != 1) errors.Add($"{path} matches {matches} of the oneOf options");
            return;
        }
        if (schema.TryGetProperty("const", out JsonElement constant) && !JsonElement.DeepEquals(constant, value)) errors.Add($"{path} is not {constant.GetRawText()}");
        if (schema.TryGetProperty("enum", out JsonElement values) && !values.EnumerateArray().Any(item => JsonElement.DeepEquals(item, value))) errors.Add($"{path} is not one of {values.GetRawText()}");
        if (schema.TryGetProperty("type", out JsonElement type))
        {
            string[] types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(item => item.GetString()!).ToArray() : [type.GetString()!];
            if (!types.Any(name => Is(name, value))) { errors.Add($"{path} is not {string.Join(" or ", types)}"); return; }
        }
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                JsonElement properties = schema.TryGetProperty("properties", out JsonElement declared) ? declared : default;
                if (schema.TryGetProperty("required", out JsonElement required))
                    foreach (JsonElement name in required.EnumerateArray())
                        if (!value.TryGetProperty(name.GetString()!, out _)) errors.Add($"{path}.{name.GetString()} is missing");
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out JsonElement propertySchema))
                        Check(root, propertySchema, property.Value, path + "." + property.Name, errors);
                    else errors.Add($"{path}.{property.Name} is not declared by the schema");
                }
                break;
            case JsonValueKind.Array when schema.TryGetProperty("items", out JsonElement items):
                int index = 0;
                foreach (JsonElement item in value.EnumerateArray()) Check(root, items, item, $"{path}[{index++}]", errors);
                break;
            case JsonValueKind.String:
                string text = value.GetString()!;
                if (schema.TryGetProperty("pattern", out JsonElement pattern) && !Regex.IsMatch(text, pattern.GetString()!)) errors.Add($"{path} does not match {pattern.GetString()}");
                if (schema.TryGetProperty("minLength", out JsonElement minLength) && text.Length < minLength.GetInt32()) errors.Add($"{path} is too short");
                break;
            case JsonValueKind.Number:
                if (schema.TryGetProperty("minimum", out JsonElement minimum) && value.GetDouble() < minimum.GetDouble()) errors.Add($"{path} is below {minimum.GetRawText()}");
                break;
        }
    }

    private static bool Is(string type, JsonElement value) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => throw new InvalidOperationException("Unsupported schema type " + type),
    };
}
