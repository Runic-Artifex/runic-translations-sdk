using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Json.Schema;

namespace Runic.Translations.Compiler.Tests;

internal static class SchemaTests
{
    private const string JsonSchemaDialect = "https://json-schema.org/draft/2020-12/schema";

    public static void Register(TestRunner runner)
    {
        runner.Add("schemas are strict versioned JSON Schema 2020-12 documents", SchemasAreVersionedAndClosed);
        runner.Add("schemas contain only resolvable local references", LocalReferencesResolve);
        runner.Add("published schema identifiers match their bundled file names", CanonicalIdentifiersMatchFiles);
        runner.Add("v3 source and locale-pack schemas publish closed profile boundaries", V3SchemaBoundaries);
        runner.Add("project schema constrains RMF2 execution-v2 activation", ProjectExecutionProfileBoundary);
        runner.Add("valid corpus sources are strict JSON", ValidCorpusSourcesAreStrictJson);
    }

    private static void ProjectExecutionProfileBoundary()
    {
        JsonSchema schema = JsonSchema.FromFile(ReadSchemaPath("project-v1.schema.json"),
            new BuildOptions { Dialect = Dialect.Draft202012 });
        const string Prefix = "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Example\",\"className\":\"AppText\"},\"baseLocale\":\"en\"";
        AssertProject(Prefix + "}", true, "omitted profile");
        AssertProject(Prefix + ",\"sourceLayout\":\"rmf2-v1\",\"executionProfile\":\"rmf2-execution-v2\"}", true, "selected RMF2 profile");
        AssertProject(Prefix + ",\"executionProfile\":\"rmf2-execution-v2\"}", false, "profile without layout");
        AssertProject(Prefix + ",\"sourceLayout\":\"locale-toml\",\"executionProfile\":\"rmf2-execution-v2\"}", false, "profile with wrong layout");
        AssertProject(Prefix + ",\"sourceLayout\":\"rmf2-v1\",\"executionProfile\":\"future-profile\"}", false, "unknown profile");

        void AssertProject(string json, bool expected, string context)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            EvaluationResults result = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            Assert.Equal(expected, result.IsValid, context + ": " + result);
        }
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
        AssertReferencesResolve(ReadSchemaPath("catalog-v2.schema.json"));
        AssertReferencesResolve(ReadSchemaPath("resources-v2.schema.json"));
        AssertReferencesResolve(ReadSchemaPath("message-ast-v2.schema.json"));
        AssertReferencesResolve(ReadSchemaPath("template-manifest-v2.schema.json"));
        AssertReferencesResolve(ReadSchemaPath("web-module-manifest-v2.schema.json"));
        AssertReferencesResolve(ReadSchemaPath("capabilities-v1.schema.json"));
        AssertReferencesResolve(ReadSchemaPath("project-v1.schema.json"));
    }

    private static void ValidCorpusSourcesAreStrictJson()
    {
        string validRoot = RepositoryPaths.Resolve("specs", "translations", "corpus", "valid");
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

    private static void V3SchemaBoundaries()
    {
        using JsonDocument resources = ReadSchema("resources-v3.schema.json");
        using JsonDocument ast = ReadSchema("message-ast-v3.schema.json");
        using JsonDocument pack = ReadSchema("locale-pack-v2.schema.json");
        Assert.Equal(3, resources.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());
        Assert.Equal("runic-mf2-subset/1", ast.RootElement.GetProperty("properties").GetProperty("profile").GetProperty("const").GetString());
        Assert.Equal("locale-artifact-v2.schema.json", pack.RootElement.GetProperty("$ref").GetString());
    }

    private static void CanonicalIdentifiersMatchFiles()
    {
        string directory = RepositoryPaths.Resolve("specs", "translations", "schemas");
        string[] paths = Directory.GetFiles(directory, "*.schema.json", SearchOption.TopDirectoryOnly);
        Array.Sort(paths, StringComparer.Ordinal);
        Assert.True(paths.Length != 0, "No published schemas were found.");
        foreach (string path in paths)
        {
            using JsonDocument schema = ReadSchema(Path.GetFileName(path));
            Assert.Equal(
                "https://runic-artifex.eu/schemas/translations/" + Path.GetFileName(path),
                schema.RootElement.GetProperty("$id").GetString(),
                path);
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
        RepositoryPaths.Resolve("specs", "translations", "schemas", fileName);

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
        bool hasDefinitions = schema.RootElement.TryGetProperty("$defs", out JsonElement definitions);
        Visit(schema.RootElement);

        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (System.Text.Json.JsonProperty property in element.EnumerateObject())
                {
                    if (property.NameEquals("$ref"))
                    {
                        string reference = property.Value.GetString() ?? string.Empty;
                        const string prefix = "#/$defs/";
                        Assert.True(reference.StartsWith(prefix, StringComparison.Ordinal),
                            $"Only local $defs references are allowed in {schemaPath}: {reference}");
                        Assert.True(hasDefinitions && definitions.TryGetProperty(reference.AsSpan(prefix.Length), out _),
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
