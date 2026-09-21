using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CompilerModel = Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Compiler.Tests;

internal static class ContractCoherenceTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("schema-v2 template manifests accept the compiler instance", TemplateManifestInstanceMatchesSchema);
        runner.Add("schema-v2 web module manifests accept the compiler instance", WebModuleManifestInstanceMatchesSchema);
    }

    private static void TemplateManifestInstanceMatchesSchema()
    {
        CompilerModel.CompiledTextCatalog catalog = CompileSchemaV2Catalog();
        TranslationGeneratedOutput output = TranslationOutputRenderer.RenderTemplateManifestJson(catalog);
        Assert.Equal("portable.template-manifest-v2.json", output.RelativePath);
        using JsonDocument instance = JsonDocument.Parse(output.GetUtf8Bytes());
        using JsonDocument schema = ReadSchema("template-manifest-v2.schema.json");
        AssertClosedObject(instance.RootElement, schema.RootElement, "template manifest");
        Assert.Equal(2, instance.RootElement.GetProperty("manifestVersion").GetInt32());
        Assert.Equal(2, instance.RootElement.GetProperty("messageGrammarVersion").GetInt32());
        JsonElement messageSchema = schema.RootElement.GetProperty("$defs").GetProperty("message");
        JsonElement argumentSchema = schema.RootElement.GetProperty("$defs").GetProperty("argument");
        foreach (System.Text.Json.JsonProperty message in instance.RootElement.GetProperty("messages").EnumerateObject())
        {
            AssertClosedObject(message.Value, messageSchema, "template message " + message.Name);
            foreach (JsonElement argument in message.Value.GetProperty("arguments").EnumerateArray())
                AssertClosedObject(argument, argumentSchema, "template argument " + message.Name);
        }
    }

    private static void WebModuleManifestInstanceMatchesSchema()
    {
        CompilerModel.CompiledTextCatalog catalog = CompileSchemaV2Catalog();
        IReadOnlyList<TranslationGeneratedOutput> outputs = TranslationOutputRenderer.RenderEsmModules(catalog);
        TranslationGeneratedOutput output = outputs.Single(item => item.Kind == TranslationGeneratedOutputKind.WebModuleManifestJson);
        Assert.Equal("portable.esm/web-module-manifest-v2.json", output.RelativePath);
        using JsonDocument instance = JsonDocument.Parse(output.GetUtf8Bytes());
        using JsonDocument schema = ReadSchema("web-module-manifest-v2.schema.json");
        AssertClosedObject(instance.RootElement, schema.RootElement, "web module manifest");
        JsonElement entrypoints = instance.RootElement.GetProperty("entrypoints");
        AssertClosedObject(entrypoints, schema.RootElement.GetProperty("properties").GetProperty("entrypoints"), "web entrypoints");
        Assert.Equal("transport.js", entrypoints.GetProperty("transport").GetString());
        HashSet<string> assets = new(StringComparer.Ordinal);
        foreach (JsonElement asset in instance.RootElement.GetProperty("assets").EnumerateArray())
        {
            JsonElement assetSchema = schema.RootElement.GetProperty("properties").GetProperty("assets").GetProperty("items");
            AssertClosedObject(asset, assetSchema, "web asset");
            Assert.True(assets.Add(asset.GetProperty("path").GetString() ?? string.Empty), "web asset paths must be unique");
        }
        foreach (System.Text.Json.JsonProperty entrypoint in entrypoints.EnumerateObject())
            Assert.True(assets.Contains(entrypoint.Value.GetString() ?? string.Empty), "web entrypoint is missing from assets: " + entrypoint.Name);
    }

    private static CompilerModel.CompiledTextCatalog CompileSchemaV2Catalog()
    {
        const string manifest = """
            { "schemaVersion": 2, "catalog": "portable", "code": { "namespace": "Tests", "className": "PortableText" },
              "defaultLocale": "en", "locales": [{"tag":"en"}], "layers": [{"name":"base","priority":0}] }
            """;
        const string document = """
            { "schemaVersion": 2, "catalog": "portable", "locale": "en", "layer": "base",
              "resources": { "Greeting": { "$value": "Hello {name}", "$placeholders": { "name": { "type": "string" } } } } }
            """;
        CompilerModel.TranslationCompilation compilation = CompilerModel.TranslationCompiler.Compile(
            [CompilerTests.Source("manifest.json", manifest)],
            [CompilerTests.Source("en.json", document)]);
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
        return Assert.Single(compilation.Catalogs);
    }

    private static JsonDocument ReadSchema(string fileName) => JsonDocument.Parse(
        File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "schemas", fileName)),
        new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 128 });

    private static void AssertClosedObject(JsonElement instance, JsonElement schema, string context)
    {
        Assert.Equal(System.Text.Json.JsonValueKind.Object, instance.ValueKind, context + " must be an object");
        JsonElement properties = schema.GetProperty("properties");
        HashSet<string> required = new(StringComparer.Ordinal);
        foreach (JsonElement item in schema.GetProperty("required").EnumerateArray())
            required.Add(item.GetString() ?? string.Empty);
        foreach (string name in required)
            Assert.True(instance.TryGetProperty(name, out _), context + " is missing required property " + name);
        foreach (System.Text.Json.JsonProperty property in instance.EnumerateObject())
            Assert.True(properties.TryGetProperty(property.Name, out _), context + " contains unknown property " + property.Name);
    }
}
