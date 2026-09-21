using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Compiler.Tests;

internal static class Rmf2ArtifactV5Tests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 v5 locale artifact round-trips typed fallback content without v4 conversion", RoundTrip);
        runner.Add("RMF2 v5 external pack rejects hostile envelope and AST mutations", HostileMatrix);
        runner.Add("RMF2 v5 pack factories preserve legacy ASCII validation and old reader rejection", VersionIsolation);
    }

    private static void RoundTrip()
    {
        (Rmf2ProjectV5 project, TranslationGeneratedOutput artifact) = Fixture("de");
        TranslationPackContract contract = Contract(project, "de");
        VerifiedExternalTranslationPack verified = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(artifact.GetUtf8Bytes()), contract).AsTask().GetAwaiter().GetResult();
        Assert.Equal(2, verified.Messages.Count);
        CompiledRmf2Message bill = verified.Messages.Single(item => item.Key.Name == "account_bill").Message!.Rmf2V5!;
        Assert.Equal("count,rate,用户", string.Join(',', bill.Inputs.ToArray().Select(input => input.Name)));
        LocalizedTextContent content = bill.FormatContent([
            new TextArgument("count", 2L), new TextArgument("rate", .125m), TextArgument.CreateRmf2("用户", new TextArgument("unused", "Ada"))], "de");
        Assert.True(content.Nodes.Length > 0 && content.Locale == "de", "The verified linked content did not retain its effective locale.");
        CompiledRmf2Message total = verified.Messages.Single(item => item.Key.Name == "account_total").Message!.Rmf2V5!;
        Assert.Equal("12,50%", total.Format([new TextArgument("rate", .125m)], "de"));
        Assert.Equal(5, JsonDocument.Parse(artifact.Text).RootElement.GetProperty("artifactVersion").GetInt32());
        Rmf2SemanticV5SchemaTests.AssertValidation(Rmf2SemanticV5SchemaTests.ReadSchema("locale-artifact-v5.schema.json"),
            JsonNode.Parse(artifact.Text)!.AsObject(), true, "Emitted project locale artifact");
        Assert.True(!artifact.Text.Contains("\"astVersion\":4", StringComparison.Ordinal), "v5 emission passed through the v4 carrier.");

        TranslationGeneratedOutput englishArtifact = Rmf2LocaleArtifactV5.Render(project, "en");
        VerifiedExternalTranslationPack english = TranslationPackLoader.VerifyAsync(
            new ExternalTranslationPack(englishArtifact.GetUtf8Bytes()), Contract(project, "en")).AsTask().GetAwaiter().GetResult();
        CompiledTranslationDefinition[] definitions = project.CanonicalMessages.Select(message =>
            CompiledTranslationDefinition.FromRmf2Inputs(message.Key, Inputs(message.Inputs))).ToArray();
        var catalog = new CompiledTranslationCatalog(project.Id, "en", definitions,
        [
            new CompiledTranslationLocale("de", "en", []),
            new CompiledTranslationLocale("en", null, english.Messages.Select(message =>
                new CompiledTranslationValue(message.Key.Id, message.Pattern, message.Message!)).OrderBy(value => value.Id).ToArray()),
        ]);
        var factory = new ExternalTranslationSnapshotFactory(new PackSource(artifact.GetUtf8Bytes()), project.Id,
            project.CallerFingerprint, requested => Contract(project, requested));
        ITranslationSnapshot snapshot = factory.CreateSnapshotAsync(catalog, "de", new DefaultTextValueFormatter(), default).AsTask().GetAwaiter().GetResult();
        Assert.Equal("12,50%", snapshot.Format(new TranslationKey(project.Id, 1, "account_total"), [new TextArgument("rate", .125m)]));

        var fallbackCompilation = TranslationCompiler.CompileRmf2ProjectV5(
            Rmf2Tests.Project(",\"validation\":{\"translationCompleteness\":\"allow\"},\"locales\":[\"en\",\"de\",{\"tag\":\"fr\",\"fallback\":\"de\"}]"),
            [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes("x = English")), new TranslationSource("translations/de.rmf2", Encoding.UTF8.GetBytes("x = Deutsch"))]);
        Assert.True(fallbackCompilation.Success, string.Join("; ", fallbackCompilation.Diagnostics.Select(item => item.Message)));
        Rmf2ProjectV5 fallbackProject = fallbackCompilation.Project!;
        VerifiedExternalTranslationPack fallback = TranslationPackLoader.VerifyAsync(
            new ExternalTranslationPack(Rmf2LocaleArtifactV5.Render(fallbackProject, "fr").GetUtf8Bytes()), Contract(fallbackProject, "fr")).AsTask().GetAwaiter().GetResult();
        Assert.Equal("de", fallback.Messages[0].Message!.Rmf2V5!.ContentLocale);
        Assert.Equal("Deutsch", fallback.Messages[0].Message!.Rmf2V5!.Format([], "fr"));
    }

    private static void HostileMatrix()
    {
        (Rmf2ProjectV5 project, TranslationGeneratedOutput artifact) = Fixture("de");
        TranslationPackContract contract = Contract(project, "de");
        JsonObject original = JsonNode.Parse(artifact.Text)!.AsObject();
        var mutations = new List<(string Name, Func<JsonObject, string> Mutate)>
        {
            ("artifact version", root => Change(root, "artifactVersion", 4)),
            ("grammar version", root => Change(root, "messageGrammarVersion", 4)),
            ("profile", root => Change(root, "profile", "rmf2-execution-v1")),
            ("catalog", root => Change(root, "catalog", "other")),
            ("locale", root => Change(root, "locale", "fr")),
            ("fingerprint", root => Change(root, "contractFingerprint", "sha256:" + new string('f', 64))),
            ("unknown root", root => Add(root, "future", true)),
            ("content locale", root => Change(Message(root, "account_total"), "contentLocale", "en")),
            ("AST profile", root => Change(Ast(root, "account_total"), "profile", "rmf2-execution-v1")),
            ("NFC input", root => Change(Ast(root, "account_bill")["inputs"]![2]!.AsObject(), "name", "cafe\u0301")),
            ("binding order", root => { JsonArray inputs = Ast(root, "account_bill")["inputs"]!.AsArray(); JsonNode first = inputs[0]!.DeepClone(); inputs[0] = inputs[1]!.DeepClone(); inputs[1] = first; return root.ToJsonString(); }),
            ("unbound reference", root => Change(FirstExpression(Ast(root, "account_total")), "operand", JsonNode.Parse("{\"kind\":\"local\",\"value\":\"missing\"}"))),
            ("decimal canonical", root => Change(FirstNumber(Ast(root, "account_total")), "canonical", "125")),
            ("fallback variant", root => { Ast(root, "account_bill")["variants"]!.AsArray().RemoveAt(0); return root.ToJsonString(); }),
            ("unknown markup", root => Change(FirstMarkup(Ast(root, "account_bill")), "name", "runic:unknown")),
            ("slot identity", root => Change(FirstMarkup(Ast(root, "account_bill"))["options"]![0]!["value"]!.AsObject(), "value", "other")),
            ("node limit", root => { JsonArray nodes = Ast(root, "account_total")["variants"]![0]!["nodes"]!.AsArray(); JsonNode template = nodes[0]!.DeepClone(); while (nodes.Count <= 4096) nodes.Add(template.DeepClone()); return root.ToJsonString(); }),
        };
        foreach ((string name, Func<JsonObject, string> mutate) in mutations)
        {
            string json = mutate(original.DeepClone().AsObject());
            Reject(json, contract, name);
        }
        string duplicate = artifact.Text.Replace("{\"artifactVersion\":5", "{\"artifactVersion\":5,\"artifactVersion\":5", StringComparison.Ordinal);
        Reject(duplicate, contract, "duplicate member");
        byte[] invalidUtf8 = artifact.GetUtf8Bytes(); invalidUtf8[Array.IndexOf(invalidUtf8, (byte)'b')] = 0xff;
        Reject(invalidUtf8, contract, "invalid UTF-8");
    }

    private static void VersionIsolation()
    {
        (Rmf2ProjectV5 project, TranslationGeneratedOutput artifact) = Fixture("en");
        Rmf2MessageContractV5 source = project.CanonicalMessages.Single(item => item.Key == "account_bill");
        TranslationPackMessageContract broad = TranslationPackMessageContract.FromRmf2Inputs(
            new TranslationKey(project.Id, source.Id, source.Key), Inputs(source.Inputs));
        Assert.True(broad.Arguments.Any(item => item.Name == "用户"), "The RMF2 factory rejected a valid NFC caller identity.");
        try
        {
            _ = new TranslationPackMessageContract(new TranslationKey(project.Id, source.Id, source.Key),
                [new TranslationPackArgumentContract("用户", TextArgumentType.String, TextArgumentFormat.None)]);
            throw new InvalidOperationException("The legacy pack message constructor accepted an RMF2-only identity.");
        }
        catch (ArgumentException) { }
        foreach (int grammar in new[] { 1, 2, 4 })
        {
            TranslationPackContract old = grammar == 4
                ? new TranslationPackContract(project.Id, "en", project.CallerFingerprint, [broad], 4, project.MarkupContract)
                : new TranslationPackContract(project.Id, "en", project.CallerFingerprint, [broad], grammar);
            Reject(artifact.GetUtf8Bytes(), old, "old reader accepted v5");
        }
    }

    private static void Reject(string json, TranslationPackContract contract, string name) => Reject(Encoding.UTF8.GetBytes(json), contract, name);
    private static void Reject(byte[] bytes, TranslationPackContract contract, string name)
    {
        try
        {
            _ = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(bytes), contract).AsTask().GetAwaiter().GetResult();
            throw new InvalidOperationException("Hostile case was accepted: " + name);
        }
        catch (TranslationPackException) { }
    }

    private static (Rmf2ProjectV5 Project, TranslationGeneratedOutput Artifact) Fixture(string locale)
    {
        string root = RepositoryPaths.Resolve("specs", "translations", "corpus", "v5-project");
        var result = TranslationCompiler.CompileRmf2ProjectV5(
            new TranslationSource("translations/runic.json", File.ReadAllBytes(Path.Combine(root, "runic.json"))),
            [new("translations/en.rmf2", File.ReadAllBytes(Path.Combine(root, "en.rmf2"))), new("translations/de.rmf2", File.ReadAllBytes(Path.Combine(root, "de.rmf2")))]);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        return (result.Project!, Rmf2LocaleArtifactV5.Render(result.Project!, locale));
    }

    private static TranslationPackContract Contract(Rmf2ProjectV5 project, string locale) => TranslationPackContract.CreateRmf2V5(
        project.Id, locale, project.CallerFingerprint,
        project.CanonicalMessages.Select(message => TranslationPackMessageContract.FromRmf2Inputs(
            new TranslationKey(project.Id, message.Id, message.Key), Inputs(message.Inputs))).ToArray(), project.MarkupContract);

    private static CompiledRmf2Input[] Inputs(IReadOnlyList<Rmf2InputV5> inputs) =>
        inputs.Select(item => new CompiledRmf2Input(item.Name, item.Type switch
        {
            "string" => TextArgumentType.String, "int64" => TextArgumentType.Int, "decimal" => TextArgumentType.Number,
            "boolean" => TextArgumentType.Bool, "date" => TextArgumentType.Date, "time" => TextArgumentType.Time,
            "datetime" => TextArgumentType.DateTime, "guid" => TextArgumentType.Guid,
            _ => throw new InvalidOperationException("Unknown test carrier."),
        })).ToArray();

    private static JsonObject Message(JsonObject root, string key) => root["messages"]![key]!.AsObject();
    private static JsonObject Ast(JsonObject root, string key) => Message(root, key)["ast"]!.AsObject();
    private static JsonObject FirstExpression(JsonObject ast) => ast["declarations"]![0]!["expression"]!.AsObject();
    private static JsonObject FirstNumber(JsonObject ast) => ast["declarations"]![1]!["expression"]!["options"]![0]!["value"]!.AsObject();
    private static JsonObject FirstMarkup(JsonObject ast) => ast["variants"]![0]!["nodes"]!.AsArray().Select(node => node!.AsObject()).First(node => node["kind"]!.GetValue<string>() == "markup");
    private static string Change(JsonObject value, string name, JsonNode? replacement) { value[name] = replacement; return value.ToJsonString(); }
    private static string Add(JsonObject value, string name, JsonNode? replacement) { value.Add(name, replacement); return value.ToJsonString(); }

    private sealed class PackSource(byte[] bytes) : IExternalTranslationSource
    {
        public ValueTask<ExternalTranslationPack?> LoadAsync(string catalog, string locale, System.Threading.CancellationToken cancellationToken) =>
            ValueTask.FromResult<ExternalTranslationPack?>(new ExternalTranslationPack(bytes));
    }
}
