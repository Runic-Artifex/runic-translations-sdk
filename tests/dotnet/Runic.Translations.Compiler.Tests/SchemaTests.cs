using System;
using System.IO;
using System.Text.Json;
using Json.Schema;

namespace Runic.Translations.Compiler.Tests;

internal static class SchemaTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("schemas contain only resolvable local references", LocalReferencesResolve);
        runner.Add("published schema identifiers match their bundled file names", CanonicalIdentifiersMatchFiles);
        runner.Add("semantic schemas publish closed v5 profile boundaries", V3SchemaBoundaries);
        runner.Add("project schema rejects retired RMF2 selectors", ProjectExecutionProfileBoundary);
        runner.Add("valid corpus sources are strict JSON", ValidCorpusSourcesAreStrictJson);
    }

    private static void ProjectExecutionProfileBoundary()
    {
        JsonSchema schema = JsonSchema.FromFile(ReadSchemaPath("project-v1.schema.json"),
            new BuildOptions { Dialect = Dialect.Draft202012 });
        const string Prefix = "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Example\",\"className\":\"AppText\"},\"baseLocale\":\"en\"";
        AssertProject(Prefix + "}", true, "canonical project");
        AssertProject(Prefix + ",\"executionProfile\":\"rmf2-execution-v2\"}", false, "retired execution selector");
        AssertProject(Prefix + ",\"sourceLayout\":\"rmf2-v1\"}", false, "retired grouped layout selector");
        AssertProject(Prefix + ",\"sourceLayout\":\"mf2-v1\"}", false, "retired direct layout selector");

        void AssertProject(string json, bool expected, string context)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            EvaluationResults result = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            Assert.Equal(expected, result.IsValid, context + ": " + result);
        }
    }

    private static void LocalReferencesResolve()
    {
        AssertReferencesResolve(ReadSchemaPath("message-ast-v5.schema.json"));
        AssertReferencesResolve(ReadSchemaPath("web-module-manifest-v3.schema.json"));
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
        using JsonDocument ast = ReadSchema("message-ast-v5.schema.json");
        using JsonDocument artifact = ReadSchema("locale-artifact-v5.schema.json");
        using JsonDocument manifest = ReadSchema("web-module-manifest-v3.schema.json");
        Assert.Equal(5, ast.RootElement.GetProperty("properties").GetProperty("astVersion").GetProperty("const").GetInt32());
        Assert.Equal("rmf2-execution-v2", ast.RootElement.GetProperty("properties").GetProperty("profile").GetProperty("const").GetString());
        Assert.Equal(5, artifact.RootElement.GetProperty("properties").GetProperty("artifactVersion").GetProperty("const").GetInt32());
        Assert.Equal(4, manifest.RootElement.GetProperty("properties").GetProperty("esmAbiVersion").GetProperty("const").GetInt32());
    }

    private static void CanonicalIdentifiersMatchFiles()
    {
        string directory = RepositoryPaths.Resolve("specs", "translations", "schemas");
        string[] paths = Directory.GetFiles(directory, "*.schema.json", SearchOption.TopDirectoryOnly);
        Array.Sort(paths, StringComparer.Ordinal);
        Assert.True(paths.Length != 0, "No published schemas were found.");
        Assert.Equal(
            "asset-manifest-v1.schema.json,capabilities-v1.schema.json,editor-state-v1.schema.json,external-pack-v5.schema.json,locale-artifact-v5.schema.json,message-ast-v5.schema.json,project-v1.schema.json,web-module-manifest-v3.schema.json",
            string.Join(',', Array.ConvertAll(paths, Path.GetFileName)),
            "The published schema set must match the selected translation contract");
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
