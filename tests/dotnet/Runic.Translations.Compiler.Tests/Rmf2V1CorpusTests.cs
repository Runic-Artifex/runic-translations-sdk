using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Compiler.Tests;

internal static class Rmf2V1CorpusTests
{
    private static string Root => RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-v1");

    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 v1 corpus freezes the linked contract layouts fingerprints and artifacts", Contract);
        runner.Add("RMF2 v1 corpus agrees across linked .NET loaded packs generated ESM and dynamic ESM packs", Execution);
        runner.Add("RMF2 v1 corpus pack rejection taxonomy agrees across .NET and ESM", InvalidPacks);
    }

    private static void Contract()
    {
        using JsonDocument index = Index();
        Rmf2ProjectV5 project = Compile();
        JsonElement contracts = index.RootElement.GetProperty("contracts");
        Assert.Equal(1, index.RootElement.GetProperty("corpusVersion").GetInt32());
        Assert.Equal("runic-rmf2-v1-conformance", index.RootElement.GetProperty("identity").GetString());
        Assert.Equal("rmf2-execution-v2", Rmf2ProjectV5.Profile);
        Assert.Equal(contracts.GetProperty("messageAstVersion").GetInt32(), Rmf2ProjectV5.MessageGrammarVersion);
        Assert.Equal(contracts.GetProperty("localeArtifactVersion").GetInt32(), Rmf2LocaleArtifactV5.ArtifactVersion);
        Assert.Equal(contracts.GetProperty("runtimeAbiVersion").GetInt32(), Rmf2ProjectV5.RuntimeAbiVersion);
        Assert.Equal(contracts.GetProperty("generatedNameVersion").GetInt32(), Rmf2GeneratedNamesV1.Version);
        using (JsonDocument markup = JsonDocument.Parse(project.MarkupContract))
            Assert.Equal(contracts.GetProperty("markupContractVersion").GetInt32(), markup.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(string.Join('|', index.RootElement.GetProperty("canonicalKeys").EnumerateArray().Select(item => item.GetString())),
            string.Join('|', project.CanonicalMessages.Select(item => item.Key)));
        Assert.Equal(string.Join('|', index.RootElement.GetProperty("localeOnlyKeys").EnumerateArray().Select(item => item.GetString())),
            string.Join('|', project.ExtraMessages.Select(item => item.Key)));
        Assert.True(project.CanonicalMessages.All(message => message.Key != "locale_extra"), "Locale extra leaked into the canonical API.");
        Assert.Equal(string.Join('|', index.RootElement.GetProperty("locales").EnumerateArray().Select(item => item.GetProperty("requested").GetString() + ":" + (item.GetProperty("fallback").GetString() ?? "")).Order(StringComparer.Ordinal)),
            string.Join('|', project.Locales.Select(item => item.Tag + ":" + (item.FallbackTag ?? "")).Order(StringComparer.Ordinal)), "Locale fallback contract");
        Assert.Equal("single-read-getter|null-prototype-inputs|own-undefined-optionals|nonadditive-rejection-taxonomy",
            string.Join('|', index.RootElement.GetProperty("esmObjectCases").EnumerateArray().Select(item => item.GetString())), "ESM hostile-object contract");
        JsonElement deterministic = index.RootElement.GetProperty("determinism");
        Assert.Equal(deterministic.GetProperty("callerFingerprint").GetString(), project.CallerFingerprint, "Caller fingerprint golden");
        Assert.Equal(deterministic.GetProperty("sourceHash").GetString(), project.SourceHash, "Source hash golden");

        Rmf2ProjectV5[] layouts = Layouts(index).ToArray();
        Assert.Equal("crlf", index.RootElement.GetProperty("layouts").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "flat").GetProperty("newline").GetString(),
            "The composition corpus no longer exercises CRLF framing.");
        Assert.Equal(1, layouts.Select(layout => layout.CallerFingerprint).Distinct(StringComparer.Ordinal).Count(),
            "Equivalent logical layouts changed the caller contract.");
        Assert.Equal("checkout.cart.title", string.Join('.', layouts[^1].CanonicalMessages.Single().Path));

        Rmf2ProjectV5 reversed = Compile(reverse: true);
        Assert.Equal(project.CallerFingerprint, reversed.CallerFingerprint, "Input enumeration changed caller fingerprint.");
        Assert.Equal(project.SourceHash, reversed.SourceHash, "Input enumeration changed source hash.");
        foreach (Rmf2LocaleV5 locale in project.Locales)
        {
            TranslationGeneratedOutput first = Rmf2LocaleArtifactV5.Render(project, locale.Tag);
            TranslationGeneratedOutput second = Rmf2LocaleArtifactV5.Render(reversed, locale.Tag);
            Assert.Equal(first.Text, second.Text, "Locale artifact bytes changed with input enumeration for " + locale.Tag);
            Assert.Equal(first.Sha256, second.Sha256, "Locale artifact digest changed with input enumeration for " + locale.Tag);
            Assert.Equal(deterministic.GetProperty("localeArtifactSha256").GetProperty(locale.Tag).GetString(), first.Sha256,
                "Locale artifact digest golden for " + locale.Tag);
        }
        TranslationGeneratedOutput[] artifacts = project.Locales.Select(locale => Rmf2LocaleArtifactV5.Render(project, locale.Tag)).ToArray();
        TranslationGeneratedOutput manifest = TranslationOutputRenderer.RenderRmf2V5AssetManifestJson(project, artifacts);
        Assert.Equal(deterministic.GetProperty("assetManifestSha256").GetString(), manifest.Sha256, "Asset manifest digest golden");
        Assert.Equal(manifest.Text, TranslationOutputRenderer.RenderRmf2V5AssetManifestJson(project, artifacts.Reverse()).Text,
            "Asset manifest was not deterministic.");

        JsonElement references = index.RootElement.GetProperty("referencedEvidence");
        foreach (System.Text.Json.JsonProperty reference in references.EnumerateObject())
        {
            if (reference.Value.ValueKind == JsonValueKind.String)
                Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Root, reference.Value.GetString()!))), "Missing referenced evidence: " + reference.Value.GetString());
            else foreach (JsonElement path in reference.Value.EnumerateArray())
                Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Root, path.GetString()!))), "Missing referenced evidence: " + path.GetString());
        }
    }

    private static void Execution()
    {
        using JsonDocument index = Index();
        Rmf2ProjectV5 project = Compile();
        var renderer = new Rmf2InlineRenderer(project.MarkupContract);
        var verified = new Dictionary<string, VerifiedExternalTranslationPack>(StringComparer.Ordinal);
        foreach (Rmf2LocaleV5 locale in project.Locales)
        {
            TranslationGeneratedOutput artifact = Rmf2LocaleArtifactV5.Render(project, locale.Tag);
            verified.Add(locale.Tag, TranslationPackLoader.VerifyAsync(
                new ExternalTranslationPack(artifact.GetUtf8Bytes()), PackContract(project, locale.Tag)).AsTask().GetAwaiter().GetResult());
        }

        foreach (JsonElement test in index.RootElement.GetProperty("executions").EnumerateArray())
        {
            string id = test.GetProperty("id").GetString()!;
            string locale = test.GetProperty("locale").GetString()!;
            string key = test.GetProperty("key").GetString()!;
            JsonElement expected = test.GetProperty("expected");
            Rmf2LocaleV5 linkedLocale = project.Locales.Single(item => item.Tag == locale);
            Rmf2TranslationV5 linked = linkedLocale.ResolvedResources.Single(item => item.Key == key);
            Rmf2MessageContractV5 messageContract = project.CanonicalMessages.Concat(project.ExtraMessages).Single(item => item.Key == key);
            TextArgument[] arguments = Arguments(test.GetProperty("arguments"));
            CompiledRmf2Message direct = Lower(linked.Message with { Inputs = messageContract.Inputs }, linked.ContentLocale);
            Check(id + "/linked", key, direct, arguments, locale, expected, renderer, test);
            Assert.Equal(expected.GetProperty("contentLocale").GetString(), linked.ContentLocale, id + "/linked content locale");

            CompiledRmf2Message loaded = verified[locale].Messages.Single(item => item.Key.Name == key).Message!.Rmf2V5!;
            Check(id + "/pack", key, loaded, arguments, locale, expected, renderer, test);
            Assert.Equal(expected.GetProperty("contentLocale").GetString(), loaded.ContentLocale, id + "/pack content locale");
        }

        RunEsm(project, index.RootElement);
    }

    private static void InvalidPacks()
    {
        using JsonDocument index = Index();
        Rmf2ProjectV5 project = Compile();
        TranslationGeneratedOutput artifact = Rmf2LocaleArtifactV5.Render(project, "de");
        TranslationPackContract contract = PackContract(project, "de");
        foreach (JsonElement test in index.RootElement.GetProperty("invalidPacks").EnumerateArray())
        {
            string expected = test.GetProperty("rejection").GetString()!;
            byte[] bytes = Mutate(artifact.Text, test);
            try
            {
                _ = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(bytes), contract).AsTask().GetAwaiter().GetResult();
                throw new InvalidOperationException("Invalid pack was accepted: " + test.GetProperty("id").GetString());
            }
            catch (TranslationPackException exception)
            {
                Assert.Equal(expected, TranslationPackFailure.GetRejectionId(exception), test.GetProperty("id").GetString());
            }
        }
        RunEsmInvalid(project, index.RootElement, artifact);
    }

    private static IEnumerable<Rmf2ProjectV5> Layouts(JsonDocument index)
    {
        foreach (JsonElement layout in index.RootElement.GetProperty("layouts").EnumerateArray())
        {
            string config = layout.TryGetProperty("mountPath", out JsonElement mount)
                ? ",\"sourceRoots\":[{\"path\":" + JsonSerializer.Serialize(mount.GetString()) + ",\"namespace\":" + layout.GetProperty("namespace").GetRawText() + "}]"
                : string.Empty;
            string manifest = "{\"schemaVersion\":1,\"catalog\":\"layout\",\"code\":{\"namespace\":\"Corpus\",\"className\":\"LayoutText\"},\"baseLocale\":\"en\",\"locales\":[\"en\"]" + config + "}";
            TranslationSource project = new("translations/runic.json", Encoding.UTF8.GetBytes(manifest));
            string text = File.ReadAllText(Path.Combine(Root, layout.GetProperty("source").GetString()!));
            if (layout.TryGetProperty("newline", out JsonElement newline) && newline.GetString() == "crlf")
                text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
            TranslationSource source = new(layout.GetProperty("logicalPath").GetString()!, Encoding.UTF8.GetBytes(text));
            Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileRmf2ProjectV5(project, [source]);
            Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Id + ": " + item.Message)));
            yield return result.Project!;
        }
    }

    private static Rmf2ProjectV5 Compile(bool reverse = false)
    {
        using JsonDocument index = Index();
        JsonElement project = index.RootElement.GetProperty("project");
        TranslationSource manifest = new("translations/runic.json", File.ReadAllBytes(Path.Combine(Root, project.GetProperty("manifest").GetString()!)));
        TranslationSource[] sources = project.GetProperty("sources").EnumerateArray()
            .Select(path => new TranslationSource("translations/" + path.GetString(), File.ReadAllBytes(Path.Combine(Root, path.GetString()!)))).ToArray();
        Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileRmf2ProjectV5(manifest, reverse ? sources.Reverse() : sources);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Id + ": " + item.Message)));
        return result.Project!;
    }

    private static JsonDocument Index() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "index.json")));

    private static TranslationPackContract PackContract(Rmf2ProjectV5 project, string locale)
    {
        HashSet<string> resolved = project.Locales.Single(item => item.Tag == locale).ResolvedResources.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        Rmf2MessageContractV5[] canonical = project.CanonicalMessages.OrderBy(item => item.Id).ToArray();
        Rmf2MessageContractV5[] extras = project.ExtraMessages.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var definitions = canonical.Select(item => (Contract: item, Id: item.Id, Canonical: true))
            .Concat(extras.Select((item, index) => (Contract: item, Id: canonical.Length + index, Canonical: false)))
            .Where(item => item.Canonical || resolved.Contains(item.Contract.Key));
        return TranslationPackContract.CreateRmf2V5(project.Id, locale, project.CallerFingerprint,
            definitions.Select(item => TranslationPackMessageContract.FromRmf2Inputs(
                new TranslationKey(project.Id, item.Id, item.Contract.Key), Inputs(item.Contract.Inputs))).ToArray(), project.MarkupContract);
    }

    private static CompiledRmf2Input[] Inputs(IReadOnlyList<Rmf2InputV5> inputs) =>
        inputs.Select(item => new CompiledRmf2Input(item.Name, Type(item.Type))).ToArray();

    private static TextArgumentType Type(string type) => type switch
    {
        "string" => TextArgumentType.String, "int64" => TextArgumentType.Int, "decimal" => TextArgumentType.Number,
        "boolean" => TextArgumentType.Bool, "date" => TextArgumentType.Date, "time" => TextArgumentType.Time,
        "datetime" => TextArgumentType.DateTime, "guid" => TextArgumentType.Guid,
        _ => throw new InvalidOperationException("Unknown corpus carrier " + type),
    };

    private static TextArgument[] Arguments(JsonElement values) => values.EnumerateObject().Select(item =>
    {
        string type = item.Value.GetProperty("type").GetString()!;
        string value = item.Value.GetProperty("value").GetString()!;
        TextArgument carrier = type switch
        {
            "string" => new TextArgument("_", value),
            "int64" => new TextArgument("_", long.Parse(value, CultureInfo.InvariantCulture)),
            "decimal" => new TextArgument("_", decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)),
            _ => throw new InvalidOperationException("Unsupported corpus argument carrier " + type),
        };
        return TextArgument.CreateRmf2(item.Name, carrier);
    }).ToArray();

    private static CompiledRmf2Message Lower(Rmf2MessageV5 message, string contentLocale)
    {
        CompiledRmf2Message lowered = Rmf2RuntimeV5Tests.Lower(message);
        return new CompiledRmf2Message(lowered.Inputs.ToArray(), lowered.Declarations.ToArray(), lowered.Selectors.ToArray(), lowered.Variants.ToArray(), contentLocale);
    }

    private static void Check(string id, string key, CompiledRmf2Message message, TextArgument[] arguments, string locale,
        JsonElement expected, Rmf2InlineRenderer renderer, JsonElement test)
    {
        if (expected.GetProperty("kind").GetString() == "text")
            Assert.Equal(expected.GetProperty("value").GetString(), message.Format(arguments, locale), id);
        else
        {
            LocalizedTextContent content = message.FormatContent(arguments, locale);
            Assert.Equal(string.Join('|', expected.GetProperty("markup").EnumerateArray().Select(item => item.GetString())),
                string.Join('|', content.Nodes.Span.ToArray().Where(item => item.Kind is LocalizedTextContentNodeKind.ElementStart or LocalizedTextContentNodeKind.ElementStandalone).Select(item => item.Value)),
                id + " structured markup");
            var slots = new Dictionary<string, InlineMarkupBinding>(StringComparer.Ordinal);
            foreach (System.Text.Json.JsonProperty slot in test.GetProperty("slots").EnumerateObject())
                slots.Add(slot.Name, new InlineLinkBinding(new Uri(slot.Value.GetProperty("href").GetString()!, UriKind.RelativeOrAbsolute)));
            Assert.Equal(expected.GetProperty("plainText").GetString(), renderer.ToPlainText(key, content, slots), id);
            Assert.Equal(expected.GetProperty("contentLocale").GetString(), content.Locale, id + " structured locale");
        }
    }

    private static byte[] Mutate(string artifact, JsonElement test)
    {
        string mutation = test.GetProperty("mutation").GetString()!;
        if (mutation == "duplicateRootMember")
            return Encoding.UTF8.GetBytes(artifact.Replace("{\"artifactVersion\":5", "{\"artifactVersion\":5,\"artifactVersion\":5", StringComparison.Ordinal));
        JsonObject root = JsonNode.Parse(artifact)!.AsObject();
        JsonNode? value = JsonNode.Parse(test.GetProperty("value").GetRawText());
        switch (mutation)
        {
            case "artifactVersion": case "messageGrammarVersion": case "profile": case "catalog": case "locale": case "contractFingerprint": root[mutation] = value; break;
            case "addRootMember": root["future"] = value; break;
            case "addMessage": root["messages"]!["future"] = root["messages"]!["core_direct"]!.DeepClone(); break;
            case "contentLocale": root["messages"]!["core_total"]!["contentLocale"] = value; break;
            case "markupContract": root["markupContract"] = value; break;
            case "addAstMember": root["messages"]!["core_total"]!["ast"]!["future"] = value; break;
            case "astVersion": root["messages"]!["core_total"]!["ast"]!["astVersion"] = value; break;
            case "addWrapperMember": root["messages"]!["core_total"]!["future"] = value; break;
            case "addInputMember": root["messages"]!["core_total"]!["ast"]!["inputs"]![0]!["future"] = value; break;
            case "addDeclarationMember": root["messages"]!["core_total"]!["ast"]!["declarations"]![0]!["future"] = value; break;
            case "addSelectorMember": root["messages"]!["core_rank"]!["ast"]!["selectors"]![0]!["future"] = value; break;
            case "addVariantMember": root["messages"]!["core_rank"]!["ast"]!["variants"]![0]!["future"] = value; break;
            case "addWildcardKeyMember":
            {
                JsonObject key = root["messages"]!["core_rank"]!["ast"]!["variants"]!.AsArray()
                    .SelectMany(item => item!["keys"]!.AsArray()).Select(item => item!.AsObject())
                    .First(item => item["kind"]!.GetValue<string>() == "wildcard");
                key["future"] = value;
                break;
            }
            case "addLiteralKeyMember":
            {
                JsonObject key = root["messages"]!["core_rank"]!["ast"]!["variants"]!.AsArray()
                    .SelectMany(item => item!["keys"]!.AsArray()).Select(item => item!.AsObject())
                    .First(item => item["kind"]!.GetValue<string>() == "literal");
                key["future"] = value;
                break;
            }
            case "addTextNodeMember": root["messages"]!["core_direct"]!["ast"]!["variants"]![0]!["nodes"]![0]!["future"] = value; break;
            case "addExpressionNodeMember":
            {
                JsonObject node = root["messages"]!["core_total"]!["ast"]!["variants"]![0]!["nodes"]!.AsArray()
                    .Select(item => item!.AsObject()).First(item => item["kind"]!.GetValue<string>() == "expression");
                node["future"] = value;
                break;
            }
            case "addMarkupNodeMember":
            {
                JsonObject node = root["messages"]!["core_rich"]!["ast"]!["variants"]![0]!["nodes"]!.AsArray()
                    .Select(item => item!.AsObject()).First(item => item["kind"]!.GetValue<string>() == "markup");
                node["future"] = value;
                break;
            }
            case "addExpressionMember": root["messages"]!["core_total"]!["ast"]!["declarations"]![0]!["expression"]!["future"] = value; break;
            case "addExpressionOptionMember":
            {
                JsonObject expression = root["messages"]!["core_total"]!["ast"]!["declarations"]!.AsArray()
                    .Select(item => item!["expression"]!.AsObject()).First(item => item["options"]!.AsArray().Count > 0);
                expression["options"]![0]!["future"] = value;
                break;
            }
            case "addMarkupOptionMember":
            {
                JsonObject node = root["messages"]!["core_rich"]!["ast"]!["variants"]![0]!["nodes"]!.AsArray()
                    .Select(item => item!.AsObject()).First(item => item["kind"]!.GetValue<string>() == "markup" && item["options"]!.AsArray().Count > 0);
                node["options"]![0]!["future"] = value;
                break;
            }
            case "addExpressionAnnotationMember":
                root["messages"]!["core_total"]!["ast"]!["declarations"]![0]!["expression"]!["annotations"]!.AsArray()
                    .Add(new JsonObject { ["name"] = "future", ["future"] = value });
                break;
            case "addMarkupAnnotationMember":
            {
                JsonObject node = root["messages"]!["core_rich"]!["ast"]!["variants"]![0]!["nodes"]!.AsArray()
                    .Select(item => item!.AsObject()).First(item => item["kind"]!.GetValue<string>() == "markup");
                node["annotations"]!.AsArray().Add(new JsonObject { ["name"] = "future", ["future"] = value });
                break;
            }
            case "addValueMember": root["messages"]!["core_total"]!["ast"]!["declarations"]![0]!["expression"]!["operand"]!["future"] = value; break;
            case "addNumericValueMember":
                root["messages"]!["core_total"]!["ast"]!["declarations"]![0]!["expression"]!["operand"] =
                    new JsonObject { ["kind"] = "number-literal", ["value"] = "1", ["canonical"] = "1", ["future"] = value };
                break;
            case "missingExpressionOptionValueKindWithUnknown":
            case "nonStringExpressionOptionValueKindWithUnknown":
            {
                JsonObject expression = root["messages"]!["core_total"]!["ast"]!["declarations"]!.AsArray()
                    .Select(item => item!["expression"]!.AsObject()).First(item => item["options"]!.AsArray().Count > 0);
                JsonObject optionValue = expression["options"]![0]!["value"]!.AsObject();
                if (mutation == "missingExpressionOptionValueKindWithUnknown") optionValue.Remove("kind");
                else optionValue["kind"] = 1;
                optionValue["future"] = value;
                break;
            }
            case "missingExpressionAnnotationValueKindWithUnknown":
                root["messages"]!["core_total"]!["ast"]!["declarations"]![0]!["expression"]!["annotations"]!.AsArray().Add(
                    new JsonObject { ["name"] = "future", ["value"] = new JsonObject { ["value"] = "x", ["future"] = value } });
                break;
            case "missingMarkupValueKindWithUnknown":
            case "nonStringMarkupValueKindWithUnknown":
            {
                JsonObject node = root["messages"]!["core_rich"]!["ast"]!["variants"]![0]!["nodes"]!.AsArray()
                    .Select(item => item!.AsObject()).First(item => item["kind"]!.GetValue<string>() == "markup" && item["options"]!.AsArray().Count > 0);
                JsonObject optionValue = node["options"]![0]!["value"]!.AsObject();
                if (mutation == "missingMarkupValueKindWithUnknown") optionValue.Remove("kind");
                else optionValue["kind"] = 1;
                optionValue["future"] = value;
                break;
            }
            case "nonStringMarkupAnnotationValueKindWithUnknown":
            {
                JsonObject node = root["messages"]!["core_rich"]!["ast"]!["variants"]![0]!["nodes"]!.AsArray()
                    .Select(item => item!.AsObject()).First(item => item["kind"]!.GetValue<string>() == "markup");
                node["annotations"]!.AsArray().Add(new JsonObject
                {
                    ["name"] = "future",
                    ["value"] = new JsonObject { ["kind"] = 1, ["value"] = "x", ["future"] = value },
                });
                break;
            }
            case "unboundLocal": root["messages"]!["core_total"]!["ast"]!["declarations"]![0]!["expression"]!["operand"] = new JsonObject { ["kind"] = "local", ["value"] = "missing" }; break;
            case "implicitDynamicDependency":
            {
                JsonArray declarations = root["messages"]!["core_total"]!["ast"]!["declarations"]!.AsArray();
                JsonNode dependency = declarations.Single(item => item!["kind"]!.GetValue<string>() == "input" && item["name"]!.GetValue<string>() == "digits")!;
                declarations.Remove(dependency);
                break;
            }
            case "selectorContract": root["messages"]!["core_rank"]!["ast"]!["selectors"]![0]!["function"] = value; break;
            case "duplicateSelector": root["messages"]!["core_rank"]!["ast"]!["selectors"]![1] = root["messages"]!["core_rank"]!["ast"]!["selectors"]![0]!.DeepClone(); break;
            case "slotIdentity":
            {
                JsonArray nodes = root["messages"]!["core_rich"]!["ast"]!["variants"]![0]!["nodes"]!.AsArray();
                JsonObject link = nodes.Select(item => item!.AsObject()).Single(item => item["kind"]!.GetValue<string>() == "markup" && item["name"]!.GetValue<string>() == "runic:link" && item["markupKind"]!.GetValue<string>() == "open");
                JsonObject reference = link["options"]!.AsArray().Select(item => item!.AsObject()).Single(item => item["name"]!.GetValue<string>() == "ref");
                reference["value"]!["value"] = value;
                break;
            }
            case "nodeLimit":
            {
                JsonArray nodes = root["messages"]!["core_direct"]!["ast"]!["variants"]![0]!["nodes"]!.AsArray();
                while (nodes.Count <= test.GetProperty("value").GetInt32()) nodes.Add(new JsonObject { ["kind"] = "text", ["value"] = string.Empty });
                break;
            }
            default: throw new InvalidOperationException("Unknown corpus mutation " + mutation);
        }
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static void RunEsm(Rmf2ProjectV5 project, JsonElement index)
    {
        string directory = Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            File.WriteAllText(Path.Combine(directory, "index.json"), index.GetRawText(), new UTF8Encoding(false));
            foreach (Rmf2LocaleV5 locale in project.Locales)
                File.WriteAllBytes(Path.Combine(directory, locale.Tag + ".json"), Rmf2LocaleArtifactV5.Render(project, locale.Tag).GetUtf8Bytes());
            File.WriteAllText(Path.Combine(directory, "corpus.mjs"), EsmExecutionScript, new UTF8Encoding(false));
            Run("bun", [Path.Combine(directory, "corpus.mjs")], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void RunEsmInvalid(Rmf2ProjectV5 project, JsonElement index, TranslationGeneratedOutput artifact)
    {
        string directory = Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            File.WriteAllText(Path.Combine(directory, "index.json"), index.GetRawText(), new UTF8Encoding(false));
            foreach (JsonElement test in index.GetProperty("invalidPacks").EnumerateArray())
                File.WriteAllBytes(Path.Combine(directory, test.GetProperty("id").GetString()! + ".json"), Mutate(artifact.Text, test));
            File.WriteAllText(Path.Combine(directory, "invalid.mjs"), EsmInvalidScript, new UTF8Encoding(false));
            Run("bun", [Path.Combine(directory, "invalid.mjs")], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Write(IReadOnlyList<TranslationGeneratedOutput> outputs)
    {
        string directory = Path.Combine(Path.GetTempPath(), "runic-rmf2-v1-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (TranslationGeneratedOutput output in outputs)
        {
            string path = Path.Combine(directory, output.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, output.GetUtf8Bytes());
        }
        return directory;
    }

    private static void Run(string file, IReadOnlyList<string> arguments, string directory)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + file);
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode, output);
    }

    private const string EsmExecutionScript = """
        import { readFile } from "node:fs/promises";
        import { m } from "./oracle.esm-v5/messages.js";
        import { bindMarkup, decimal, defineMarkup, linkBinding, toPlainText } from "./oracle.esm-v5/runtime.js";
        import { decodeLocaleArtifact, decodeLocalePack, formatDynamicMessage } from "./oracle.esm-v5/dynamic.js";
        const index=JSON.parse(await readFile(new URL("./index.json",import.meta.url),"utf8"));
        const artifacts=Object.create(null);
        for(const item of index.locales){const bytes=new Uint8Array(await readFile(new URL("./"+item.requested+".json",import.meta.url)));const decoded=await decodeLocalePack(bytes,item.requested);if(!decoded.ok)throw new Error(decoded.reason);artifacts[item.requested]=decoded.value;}
        const argsOf=(spec)=>{const args=Object.create(null);for(const [name,item] of Object.entries(spec))args[name]=item.type==="decimal"?decimal(item.value):item.type==="int64"?BigInt(item.value):item.value;return args;};
        if(Object.getPrototypeOf(argsOf(index.executions.find(item=>item.id==="hostile-nfc-and-unicode").arguments))!==null)throw new Error("null-prototype-inputs");
        const badge=defineMarkup({name:"app:badge",kind:"paired",children:"inline",interactive:false,plainText:"children",options:{amount:{type:"number",values:[],default:"100",literalOnly:false},tone:{type:"enum",values:["neutral","positive"],default:"neutral",literalOnly:false}}});
        const custom=[bindMarkup(badge,({children})=>children.join(""))];
        const markup=(value)=>{const names=[];const visit=nodes=>{for(const node of nodes)if(node.kind==="element"){names.push(node.name);visit(node.children);}};visit(value.nodes);return names;};
        const project=(value,test)=>{if(test.expected.kind!=="structured")return value;const actual=markup(value);if(JSON.stringify(actual)!==JSON.stringify(test.expected.markup))throw new Error(test.id+": markup "+JSON.stringify(actual));return toPlainText(value,{slots:Object.fromEntries(Object.entries(test.slots).map(([name,item])=>[name,linkBinding({href:item.href})])),custom});};
        for(const test of index.executions){const args=argsOf(test.arguments);const artifact=artifacts[test.locale];if(artifact.messages[test.key].contentLocale!==test.expected.contentLocale)throw new Error(test.id+": content locale");const dynamic=project(formatDynamicMessage(artifact,test.key,args),test);const expected=test.expected.value??test.expected.plainText;if(dynamic!==expected)throw new Error(test.id+": dynamic "+JSON.stringify(dynamic)+" != "+JSON.stringify(expected));if(!test.dynamicOnly){const generatedValue=Object.keys(test.arguments).length===0?m[test.key]({locale:test.locale}):m[test.key](args,{locale:test.locale});const generated=project(generatedValue,test);if(generated!==expected)throw new Error(test.id+": generated "+JSON.stringify(generated)+" != "+JSON.stringify(expected));}}
        const raw=JSON.parse(await readFile(new URL("./en.json",import.meta.url),"utf8"));const reject=(candidate,reason,id)=>{const decoded=decodeLocaleArtifact(candidate);if(decoded.ok||decoded.reason!=="RTR0023/"+reason)throw new Error(id+": "+decoded.reason);};
        const missingExpressionOption=structuredClone(raw);const expressionOption=missingExpressionOption.messages.core_total.ast.declarations.map(item=>item.expression).find(item=>item.options.length).options[0];delete expressionOption.value;reject(missingExpressionOption,"argument-contract-mismatch","missing-expression-option-member");
        const missingMarkupOption=structuredClone(raw);const markupWithOptions=missingMarkupOption.messages.core_rich.ast.variants[0].nodes.find(item=>item.kind==="markup"&&item.options.length);delete markupWithOptions.options[0].value;reject(missingMarkupOption,"argument-contract-mismatch","missing-markup-option-member");
        const missingMarkupAnnotation=structuredClone(raw);missingMarkupAnnotation.messages.core_rich.ast.variants[0].nodes.find(item=>item.kind==="markup").annotations.push({value:{kind:"string-literal",value:"x"}});reject(missingMarkupAnnotation,"argument-contract-mismatch","missing-markup-annotation-member");
        const missingMarkupNodeMember=structuredClone(raw);delete missingMarkupNodeMember.messages.core_rich.ast.variants[0].nodes.find(item=>item.kind==="markup").annotations;reject(missingMarkupNodeMember,"argument-contract-mismatch","missing-markup-node-member");
        const undefinedFunction=structuredClone(raw);undefinedFunction.messages.core_total.ast.declarations.map(item=>item.expression).find(item=>item.function===undefined).function=undefined;reject(undefinedFunction,"malformed","own-undefined-function");
        const undefinedAnnotation=structuredClone(raw);undefinedAnnotation.messages.core_rich.ast.variants[0].nodes.find(item=>item.kind==="markup").annotations.push({name:"future",value:undefined});reject(undefinedAnnotation,"malformed","own-undefined-annotation-value");
        const undefinedCanonical=structuredClone(raw);undefinedCanonical.messages.core_rank.ast.variants.flatMap(item=>item.keys).find(item=>item.kind==="literal"&&!Object.hasOwn(item,"canonical")).canonical=undefined;reject(undefinedCanonical,"malformed","own-undefined-key-canonical");
        let reads=0;const stable=raw.messages.core_direct;const wrapper={contentLocale:stable.contentLocale};Object.defineProperty(wrapper,"ast",{enumerable:true,get(){reads++;return stable.ast;}});raw.messages.core_direct=wrapper;const getter=decodeLocaleArtifact(raw);if(getter.ok||getter.reason!=="RTR0023/malformed"||reads!==0)throw new Error("accessor-boundary");
        """;

    private const string EsmInvalidScript = """
        import { readFile } from "node:fs/promises";
        import { decodeLocalePack } from "./oracle.esm-v5/dynamic.js";
        const index=JSON.parse(await readFile(new URL("./index.json",import.meta.url),"utf8"));
        for(const test of index.invalidPacks){const bytes=new Uint8Array(await readFile(new URL("./"+test.id+".json",import.meta.url)));const decoded=await decodeLocalePack(bytes,"de");if(decoded.ok||decoded.reason!==test.rejection)throw new Error(test.id+": "+decoded.reason+" != "+test.rejection);}
        """;
}
