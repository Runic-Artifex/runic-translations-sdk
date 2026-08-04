using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RunicTextResources.Compiler.Tests;

internal static class SchemaTests
{
    private const string JsonSchemaDialect = "https://json-schema.org/draft/2020-12/schema";

    public static void Register(TestRunner runner)
    {
        runner.Add("schemas are strict versioned JSON Schema 2020-12 documents", SchemasAreVersionedAndClosed);
        runner.Add("schemas contain only resolvable local references", LocalReferencesResolve);
        runner.Add("valid corpus sources are strict JSON", ValidCorpusSourcesAreStrictJson);
    }

    private static void SchemasAreVersionedAndClosed()
    {
        using JsonDocument catalog = ReadSchema("catalog-v1.schema.json");
        using JsonDocument resources = ReadSchema("resources-v1.schema.json");

        AssertSchemaRoot(catalog.RootElement, "catalog", "code", "defaultLocale", "locales", "layers");
        AssertSchemaRoot(resources.RootElement, "catalog", "locale", "layer", "resources");

        JsonElement catalogDefinitions = catalog.RootElement.GetProperty("$defs");
        Assert.True(catalogDefinitions.TryGetProperty("locale", out _), "The catalog schema must define locale declarations.");
        Assert.True(catalogDefinitions.TryGetProperty("layer", out _), "The catalog schema must define layers.");
        Assert.True(catalogDefinitions.TryGetProperty("validation", out _), "The catalog schema must define validation policies.");
        Assert.True(catalogDefinitions.TryGetProperty("runtime", out _), "The catalog schema must define runtime policies.");

        JsonElement resourceDefinitions = resources.RootElement.GetProperty("$defs");
        Assert.True(resourceDefinitions.TryGetProperty("resourceGroup", out _), "The resource schema must define recursive groups.");
        Assert.True(resourceDefinitions.TryGetProperty("metadataLeaf", out _), "The resource schema must define metadata leaves.");
        Assert.True(resourceDefinitions.TryGetProperty("placeholderDescriptor", out _), "The resource schema must define placeholder descriptors.");
        Assert.True(resourceDefinitions.TryGetProperty("guidPlaceholder", out _), "All eight version 1 placeholder types must be represented.");
    }

    private static void LocalReferencesResolve()
    {
        AssertReferencesResolve(ReadSchemaPath("catalog-v1.schema.json"));
        AssertReferencesResolve(ReadSchemaPath("resources-v1.schema.json"));
    }

    private static void ValidCorpusSourcesAreStrictJson()
    {
        string validRoot = RepositoryPaths.Resolve("spec", "corpus", "valid");
        Assert.True(Directory.Exists(validRoot), "The version 1 valid corpus directory is missing.");

        string[] paths = Directory.GetFiles(validRoot, "*.json", SearchOption.AllDirectories);
        Assert.True(paths.Length != 0, "The version 1 valid corpus is empty.");
        Array.Sort(paths, StringComparer.Ordinal);

        foreach (string path in paths)
        {
            byte[] utf8 = File.ReadAllBytes(path);
            JsonDocumentOptions options = new()
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            };
            using JsonDocument document = JsonDocument.Parse(utf8, options);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind, path);
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32(), path);
        }
    }

    private static JsonDocument ReadSchema(string fileName)
    {
        JsonDocumentOptions options = new()
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 128,
        };
        return JsonDocument.Parse(File.ReadAllBytes(ReadSchemaPath(fileName)), options);
    }

    private static string ReadSchemaPath(string fileName) =>
        RepositoryPaths.Resolve("spec", "schemas", fileName);

    private static void AssertSchemaRoot(JsonElement root, params string[] requiredMembers)
    {
        Assert.Equal(JsonSchemaDialect, root.GetProperty("$schema").GetString());
        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.True(!root.GetProperty("additionalProperties").GetBoolean(), "Version 1 schema roots must reject unknown members.");
        Assert.True(!root.GetProperty("unevaluatedProperties").GetBoolean(), "Version 1 schema roots must reject unevaluated members.");
        Assert.Equal(1, root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());

        HashSet<string> required = new(StringComparer.Ordinal);
        foreach (JsonElement item in root.GetProperty("required").EnumerateArray())
        {
            required.Add(item.GetString() ?? string.Empty);
        }

        Assert.True(required.Contains("schemaVersion"), "schemaVersion must be required.");
        foreach (string member in requiredMembers)
        {
            Assert.True(required.Contains(member), $"'{member}' must be required.");
        }
    }

    private static void AssertReferencesResolve(string schemaPath)
    {
        using JsonDocument schema = ReadSchema(Path.GetFileName(schemaPath));
        JsonElement definitions = schema.RootElement.GetProperty("$defs");
        Visit(schema.RootElement);

        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.NameEquals("$ref"))
                    {
                        string reference = property.Value.GetString() ?? string.Empty;
                        const string prefix = "#/$defs/";
                        Assert.True(reference.StartsWith(prefix, StringComparison.Ordinal),
                            $"Only local $defs references are allowed in {schemaPath}: {reference}");
                        Assert.True(definitions.TryGetProperty(reference.AsSpan(prefix.Length), out _),
                            $"Unresolved schema reference in {schemaPath}: {reference}");
                    }

                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Visit(item);
                }
            }
        }
    }
}
