using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Runic.Translations.Compiler;

namespace Runic.Translations.Compiler.Tests;

internal static class Rmf2SemanticV5SchemaTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 v5 emitted ASTs and golden envelope validate against Draft 2020-12", ValidInstances);
        runner.Add("RMF2 v5 Draft 2020-12 schemas reject malformed AST, envelope, and manifest mutations", InvalidInstances);
    }

    private static void ValidInstances()
    {
        var astSchema = ReadSchema("message-ast-v5.schema.json");
        var artifactSchema = ReadSchema("locale-artifact-v5.schema.json");
        foreach (string source in new[] {
            GoldenSource(),
            ".local $a = {$n} {{ {$a :number} }}",
            ".input {$n :integer} .local $a = {$n :number} {{ {$a :integer} }}",
            "{|2026-09-21| :date} {|true| :runic:boolean} {42 :integer}",
            ".input {$s :string} .match $s |*| {{literal}} * {{fallback}}",
        })
        {
            var result = Rmf2SemanticCompilerV5.Compile(new TranslationSource("schema-test.mf2", Encoding.UTF8.GetBytes(source)));
            Assert.True(result.Success, "Schema test source failed semantic compilation.");
            var ast = JsonNode.Parse(Rmf2MessageJsonV5.Serialize(result.Message!))!.AsObject();
            AssertValidation(astSchema, ast, true, "Emitted normalized AST");
            var artifact = GoldenArtifact();
            artifact["messages"]!["Example"]!["ast"] = ast.DeepClone();
            AssertValidation(artifactSchema, artifact, true, "Complete envelope containing emitted AST");
        }
        AssertValidation(artifactSchema, GoldenArtifact(), true, "Hand-authored golden envelope");
    }

    private static void InvalidInstances()
    {
        var astSchema = ReadSchema("message-ast-v5.schema.json");
        var artifactSchema = ReadSchema("locale-artifact-v5.schema.json");
        var manifestSchema = ReadSchema("web-module-manifest-v3.schema.json");
        var astMutations = new (string Name, Action<JsonObject> Mutate)[] {
            ("old AST version", ast => ast["astVersion"] = 4),
            ("missing underlying value type", ast => ast["declarations"]![3]!["expression"]!.AsObject().Remove("valueType")),
            ("unknown underlying value type", ast => ast["declarations"]![3]!["expression"]!["valueType"] = "float64"),
            ("caller reference in annotation", ast => ast["declarations"]![3]!["expression"]!["annotations"]![0]!["value"] = new JsonObject { ["kind"] = "input", ["value"] = "fake" }),
            ("untagged option", ast => ast["declarations"]![3]!["expression"]!["options"]![0]!["value"] = "percent"),
            ("unknown formatter", ast => ast["declarations"]![3]!["expression"]!["function"] = "vendor:unknown"),
            ("literal value on wildcard", ast => ast["variants"]![2]!["keys"]![0]!["value"] = "*"),
            ("missing numeric canonical value", ast => ast["variants"]![1]!["nodes"]![3]!["expression"]!["operand"]!.AsObject().Remove("canonical")),
            ("non-string canonical number", ast => ast["variants"]![1]!["keys"]![0]!["canonical"] = 1),
            ("unknown root property", ast => ast["extra"] = true),
        };
        foreach (var mutation in astMutations)
        {
            var artifact = GoldenArtifact();
            var ast = artifact["messages"]!["Example"]!["ast"]!.AsObject();
            mutation.Mutate(ast);
            AssertValidation(astSchema, ast, false, mutation.Name);
            AssertValidation(artifactSchema, artifact, false, "Envelope: " + mutation.Name);
        }
        var envelopeMutations = new (string Name, Action<JsonObject> Mutate)[] {
            ("old artifact version", artifact => artifact["artifactVersion"] = 4),
            ("missing content locale", artifact => artifact["messages"]!["Example"]!.AsObject().Remove("contentLocale")),
            ("invalid fingerprint", artifact => artifact["contractFingerprint"] = "sha256:bad"),
            ("wrong markup version through external reference", artifact => artifact["markupContract"]!["version"] = 2),
            ("missing required markup field through external reference", artifact => artifact["markupContract"]!.AsObject().Remove("contracts")),
            ("unknown envelope property", artifact => artifact["extra"] = true),
        };
        foreach (var mutation in envelopeMutations)
        {
            var artifact = GoldenArtifact(); mutation.Mutate(artifact);
            AssertValidation(artifactSchema, artifact, false, mutation.Name);
        }

        var collectionBounds = new (string Name, Func<JsonObject, JsonObject> Owner, string Property, JsonObject Item)[] {
            ("expression options", ast => ast["declarations"]![3]!["expression"]!.AsObject(), "options",
                new JsonObject { ["name"] = "style", ["value"] = new JsonObject { ["kind"] = "string-literal", ["value"] = "percent" } }),
            ("expression annotations", ast => ast["declarations"]![3]!["expression"]!.AsObject(), "annotations",
                new JsonObject { ["name"] = "note" }),
            ("markup options", ast => ast["variants"]![2]!["nodes"]![1]!.AsObject(), "options",
                new JsonObject { ["name"] = "ref", ["value"] = new JsonObject { ["kind"] = "string-literal", ["value"] = "icon" } }),
            ("markup annotations", ast => ast["variants"]![2]!["nodes"]![1]!.AsObject(), "annotations",
                new JsonObject { ["name"] = "note" }),
        };
        foreach (var boundary in collectionBounds)
        {
            JsonObject artifact = GoldenArtifact();
            JsonObject ast = artifact["messages"]!["Example"]!["ast"]!.AsObject();
            JsonArray values = Repeated(boundary.Item, 256);
            boundary.Owner(ast)[boundary.Property] = values;
            AssertValidation(astSchema, ast, true, boundary.Name + " at structural schema limit");
            values.Add(boundary.Item.DeepClone());
            AssertValidation(astSchema, ast, false, boundary.Name + " above reader limit");
            AssertValidation(artifactSchema, artifact, false, "Envelope: " + boundary.Name + " above reader limit");
        }

        JsonObject manifest = WebManifest();
        AssertValidation(manifestSchema, manifest, true, "Complete web module manifest at schema byte-length limit");
        for (int index = 0; index < 6; index++)
        {
            JsonObject incomplete = WebManifest();
            incomplete["assets"]![index]!["path"] = "extra-" + index + ".js";
            AssertValidation(manifestSchema, incomplete, false, "Web module manifest missing required entrypoint asset " + index);
        }
        JsonObject empty = WebManifest();
        empty["assets"] = new JsonArray();
        AssertValidation(manifestSchema, empty, false, "Web module manifest with no assets");
        JsonObject duplicate = WebManifest();
        duplicate["assets"]!.AsArray().Add(duplicate["assets"]![0]!.DeepClone());
        AssertValidation(manifestSchema, duplicate, false, "Web module manifest with a duplicate required asset");
        JsonObject unsafeLength = WebManifest();
        unsafeLength["assets"]![0]!["byteLength"] = 9_007_199_254_740_992L;
        AssertValidation(manifestSchema, unsafeLength, false, "Web module manifest byte length above JavaScript safe integer");
    }

    internal static JsonSchema ReadSchema(string fileName)
    {
        // All references resolve from the checked-in semantic schemas. No remote
        // fetch or dialect downgrade is involved.
        var registry = new SchemaRegistry();
        var options = new BuildOptions { Dialect = Dialect.Draft202012, SchemaRegistry = registry };
        foreach (string dependency in new[] { "locale-artifact-v5.schema.json", "message-ast-v5.schema.json" })
            if (dependency != fileName) registry.Register(JsonSchema.FromFile(RepositoryPaths.Resolve("specs", "translations", "schemas", dependency), options));
        return JsonSchema.FromFile(RepositoryPaths.Resolve("specs", "translations", "schemas", fileName), options);
    }
    internal static void AssertValidation(JsonSchema schema, JsonObject instance, bool expected, string context)
    {
        using var document = JsonDocument.Parse(instance.ToJsonString());
        var result = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.Equal(expected, result.IsValid, context + ": " + JsonSerializer.Serialize(result));
    }
    private static JsonArray Repeated(JsonObject item, int count)
    {
        JsonArray values = [];
        for (int index = 0; index < count; index++)
        {
            JsonObject value = item.DeepClone().AsObject();
            value["name"] = item["name"]!.GetValue<string>() + index;
            values.Add(value);
        }
        return values;
    }
    private static JsonObject WebManifest()
    {
        JsonArray assets = [];
        string[] paths = ["messages.js", "messages.d.ts", "runtime.js", "server.js", "transport.js", "dynamic.js"];
        foreach (string path in paths)
        {
            assets.Add(new JsonObject {
                ["path"] = path,
                ["sha256"] = new string('0', 64),
                ["byteLength"] = 9_007_199_254_740_991L,
                ["mediaType"] = path.EndsWith(".d.ts", StringComparison.Ordinal) ? "text/typescript" : "text/javascript",
            });
        }
        return new JsonObject {
            ["webModuleManifestVersion"] = 3,
            ["esmAbiVersion"] = 4,
            ["rmf2RuntimeAbiVersion"] = 2,
            ["messageGrammarVersion"] = 5,
            ["profile"] = "rmf2-execution-v2",
            ["generatedNameVersion"] = 1,
            ["catalog"] = "app",
            ["contractFingerprint"] = "sha256:" + new string('1', 64),
            ["sourceHash"] = "sha256:" + new string('2', 64),
            ["entrypoints"] = new JsonObject {
                ["messages"] = "messages.js",
                ["types"] = "messages.d.ts",
                ["runtime"] = "runtime.js",
                ["server"] = "server.js",
                ["transport"] = "transport.js",
                ["dynamic"] = "dynamic.js",
            },
            ["assets"] = assets,
        };
    }
    private static JsonObject GoldenArtifact() => JsonNode.Parse(File.ReadAllText(RepositoryPaths.Resolve("specs", "translations", "corpus", "semantic-v5", "locale-artifact.json")))!.AsObject();
    private static string GoldenSource() => File.ReadAllText(RepositoryPaths.Resolve("specs", "translations", "corpus", "semantic-v5", "message.mf2"));
}
