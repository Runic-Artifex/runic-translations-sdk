using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using CompilerModel = Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Compiler.Tests;

internal static class ContractCoherenceTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("generated legacy, schema-v2, and RMF2 manifests satisfy their JSON schemas", GeneratedInstancesMatchSchemas);
        runner.Add("manifest schemas reject representative contract drift", SchemasRejectContractDrift);
    }

    private static void GeneratedInstancesMatchSchemas()
    {
        (CompilerModel.CompiledTextCatalog Catalog, string TemplateSchema, string TemplatePath)[] catalogs =
        [
            (CompileLegacyCatalog(), "template-manifest-v1.schema.json", "legacy.template-manifest-v1.json"),
            (CompileSchemaV2Catalog(), "template-manifest-v2.schema.json", "portable.template-manifest-v2.json"),
            (CompileRmf2Catalog(), "template-manifest-v2.schema.json", "app.template-manifest-v2.json"),
        ];

        foreach ((CompilerModel.CompiledTextCatalog catalog, string templateSchema, string templatePath) in catalogs)
        {
            TranslationGeneratedOutput template = TranslationOutputRenderer.RenderTemplateManifestJson(catalog);
            Assert.Equal(templatePath, template.RelativePath);
            AssertSchemaAccepts(templateSchema, template.GetUtf8Bytes(), template.RelativePath);

            TranslationGeneratedOutput web = TranslationOutputRenderer.RenderEsmModules(catalog)
                .Single(item => item.Kind == TranslationGeneratedOutputKind.WebModuleManifestJson);
            AssertSchemaAccepts("web-module-manifest-v2.schema.json", web.GetUtf8Bytes(), web.RelativePath);
        }
    }

    private static void SchemasRejectContractDrift()
    {
        CompilerModel.CompiledTextCatalog catalog = CompileSchemaV2Catalog();
        TranslationGeneratedOutput template = TranslationOutputRenderer.RenderTemplateManifestJson(catalog);
        TranslationGeneratedOutput web = TranslationOutputRenderer.RenderEsmModules(catalog)
            .Single(item => item.Kind == TranslationGeneratedOutputKind.WebModuleManifestJson);

        JsonObject manifestVersion = ParseObject(template);
        manifestVersion["manifestVersion"] = 1;
        AssertSchemaRejects("template-manifest-v2.schema.json", manifestVersion, "template manifest const");

        JsonObject grammar = ParseObject(template);
        grammar["messageGrammarVersion"] = 3;
        AssertSchemaRejects("template-manifest-v2.schema.json", grammar, "template manifest enum");

        JsonObject catalogId = ParseObject(template);
        catalogId["catalog"] = "UpperCase";
        AssertSchemaRejects("template-manifest-v2.schema.json", catalogId, "template manifest pattern");

        JsonObject messagesType = ParseObject(template);
        messagesType["messages"] = "not-an-object";
        AssertSchemaRejects("template-manifest-v2.schema.json", messagesType, "template manifest type");

        JsonObject messageBounds = ParseObject(template);
        JsonObject firstMessage = messageBounds["messages"]!.AsObject().First().Value!.AsObject();
        JsonArray arguments = firstMessage["arguments"]!.AsArray();
        for (int index = arguments.Count; index <= 32; index++)
            arguments.Add(new JsonObject { ["name"] = "extra" + index, ["type"] = "string", ["format"] = "none" });
        AssertSchemaRejects("template-manifest-v2.schema.json", messageBounds, "template manifest maxItems");

        JsonObject fingerprint = ParseObject(web);
        fingerprint["contractFingerprint"] = "not-a-fingerprint";
        AssertSchemaRejects("web-module-manifest-v2.schema.json", fingerprint, "web manifest pattern");

        JsonObject entrypoint = ParseObject(web);
        entrypoint["entrypoints"]!.AsObject()["transport"] = "other.js";
        AssertSchemaRejects("web-module-manifest-v2.schema.json", entrypoint, "web manifest entrypoint const");

        JsonObject assetType = ParseObject(web);
        assetType["assets"]!.AsArray()[0]!.AsObject()["byteLength"] = "0";
        AssertSchemaRejects("web-module-manifest-v2.schema.json", assetType, "web manifest asset type");
    }

    private static CompilerModel.CompiledTextCatalog CompileLegacyCatalog()
    {
        const string manifest = """
            { "schemaVersion": 1, "catalog": "legacy", "code": { "namespace": "Tests", "className": "LegacyText" },
              "defaultLocale": "en", "locales": [{"tag":"en"}], "layers": [{"name":"base","priority":0}] }
            """;
        const string document = """
            { "schemaVersion": 1, "catalog": "legacy", "locale": "en", "layer": "base",
              "resources": { "Greeting": { "$value": "Hello {name}", "$placeholders": { "name": { "type": "string" } } } } }
            """;
        return CompileCatalog(manifest, document);
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
        return CompileCatalog(manifest, document);
    }

    private static CompilerModel.CompiledTextCatalog CompileRmf2Catalog()
    {
        CompilerModel.TranslationCompilation compilation = CompilerModel.TranslationCompiler.CompileProject(
            Rmf2Tests.Project(),
            [new CompilerModel.TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes("welcome = Hello\n"))]);
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
        CompilerModel.CompiledTextCatalog catalog = Assert.Single(compilation.Catalogs);
        Assert.Equal(4, catalog.MessageGrammarVersion);
        Assert.True(catalog.Rmf2MarkupContract is not null, "RMF2 catalog must carry its markup contract.");
        return catalog;
    }

    private static CompilerModel.CompiledTextCatalog CompileCatalog(string manifest, string document)
    {
        CompilerModel.TranslationCompilation compilation = CompilerModel.TranslationCompiler.Compile(
            [CompilerTests.Source("manifest.json", manifest)],
            [CompilerTests.Source("en.json", document)]);
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
        return Assert.Single(compilation.Catalogs);
    }

    private static JsonObject ParseObject(TranslationGeneratedOutput output) =>
        JsonNode.Parse(output.GetUtf8Bytes())!.AsObject();

    private static void AssertSchemaAccepts(string schemaFile, byte[] instanceBytes, string context)
    {
        using JsonDocument instance = JsonDocument.Parse(instanceBytes);
        JsonSchema schema = ReadValidatorSchema(schemaFile);
        EvaluationResults result = schema.Evaluate(instance.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(result.IsValid, context + " failed JSON Schema validation: " + result);
    }

    private static void AssertSchemaRejects(string schemaFile, JsonObject instance, string context)
    {
        using JsonDocument document = JsonDocument.Parse(instance.ToJsonString());
        JsonSchema schema = ReadValidatorSchema(schemaFile);
        EvaluationResults result = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(!result.IsValid, context + " was accepted by JSON Schema validation.");
    }

    private static JsonSchema ReadValidatorSchema(string fileName)
    {
        SchemaRegistry registry = new();
        BuildOptions options = new() { Dialect = Dialect.Draft202012, SchemaRegistry = registry };
        if (fileName == "template-manifest-v1.schema.json")
        {
            string argumentSchemaPath = RepositoryPaths.Resolve("specs", "translations", "schemas", "locale-artifact-v1.schema.json");
            JsonSchema argumentSchema = JsonSchema.FromFile(argumentSchemaPath, options);
            registry.Register(argumentSchema);
        }

        string schemaPath = RepositoryPaths.Resolve("specs", "translations", "schemas", fileName);
        return JsonSchema.FromFile(schemaPath, options);
    }
}
