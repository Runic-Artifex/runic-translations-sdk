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
        runner.Add("RMF2 v5 Draft 2020-12 schemas reject malformed AST and envelope mutations", InvalidInstances);
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
    }

    private static JsonSchema ReadSchema(string fileName)
    {
        // All references resolve from the checked-in schemas. No remote fetch or
        // dialect downgrade is involved, including the unchanged markup v1 shape.
        var registry = new SchemaRegistry();
        var options = new BuildOptions { Dialect = Dialect.Draft202012, SchemaRegistry = registry };
        foreach (string dependency in new[] { "locale-artifact-v4.schema.json", "message-ast-v5.schema.json" })
            if (dependency != fileName) registry.Register(JsonSchema.FromFile(RepositoryPaths.Resolve("specs", "translations", "schemas", dependency), options));
        return JsonSchema.FromFile(RepositoryPaths.Resolve("specs", "translations", "schemas", fileName), options);
    }
    private static void AssertValidation(JsonSchema schema, JsonObject instance, bool expected, string context)
    {
        using var document = JsonDocument.Parse(instance.ToJsonString());
        var result = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.Equal(expected, result.IsValid, context + ": " + JsonSerializer.Serialize(result));
    }
    private static JsonObject GoldenArtifact() => JsonNode.Parse(File.ReadAllText(RepositoryPaths.Resolve("specs", "translations", "corpus", "semantic-v5", "locale-artifact.json")))!.AsObject();
    private static string GoldenSource() => File.ReadAllText(RepositoryPaths.Resolve("specs", "translations", "corpus", "semantic-v5", "message.mf2"));
}
