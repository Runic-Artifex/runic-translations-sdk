using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// Markup contract version 3 (D077.16): the compiler writes v2 without its default-shaped message entries, and both
// runtimes accept v2 and v3 and compare them as one contract. specs/translations/corpus/rmf2-markup-contract-v3.json is
// the shared oracle: the compiler must compact each v2 to exactly its v3, and .NET and ESM must give every comparison the
// same verdict.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2MarkupContractV3Tests
{
    private static JsonNode Corpus() => JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-markup-contract-v3.json")))!;
    private static string[] Strings(JsonNode node) => node.AsArray().Select(static item => item!.GetValue<string>()).ToArray();

    [Test, DisplayName("the compiler compacts each corpus contract to its v3 and expands it back to its v2")]
    public void CompilerCompactsAndExpands()
    {
        foreach (JsonNode item in Corpus()["compaction"]!.AsArray().Select(static item => item!))
        {
            string id = item["id"]!.GetValue<string>(), v2 = item["v2"]!.ToJsonString(), v3 = item["v3"]!.ToJsonString();
            Assert.Equal(v3, Rmf2MarkupContractV3.Compact(v2, Strings(item["locales"]!)), id + ": compacted");
            Assert.Equal(v2, Rmf2MarkupContractV3.Expand(v3, Strings(item["keys"]!)), id + ": expanded");
            Assert.Equal(v2, Rmf2MarkupContractV3.Expand(v2, Strings(item["keys"]!)), id + ": a v2 contract expands to itself");
        }
    }

    [Test, DisplayName(".NET compares the corpus contracts as the oracle says")]
    public void DotNetComparisons()
    {
        foreach ((string id, string left, string right, string[] keys, bool same) in Comparisons())
        {
            Assert.Equal(same, Rmf2MarkupContract.SameContract(left, right, keys), id);
            Assert.Equal(same, Rmf2MarkupContract.SameContract(right, left, keys), id + " (reversed)");
        }
        // An untrusted contract that cannot be expanded is not the same contract; it never throws.
        string valid = Corpus()["compaction"]![1]!["v3"]!.ToJsonString();
        foreach (string broken in new[] { valid.Replace("\"locales\":[\"de\",\"en\",\"fr\"]", "\"locales\":7", StringComparison.Ordinal),
            valid.Replace("\"locales\":[\"de\",\"en\",\"fr\"]", "\"locales\":[1,2]", StringComparison.Ordinal), valid.Replace(",\"locales\":[\"de\",\"en\",\"fr\"]", "", StringComparison.Ordinal) })
        {
            Assert.True(broken != valid, "the broken contract differs");
            Assert.True(!Rmf2MarkupContract.SameContract(broken, valid, ["a"]), "a broken contract is the same");
        }
    }

    private static (string Id, string Left, string Right, string[] Keys, bool Same)[] Comparisons()
    {
        JsonNode corpus = Corpus();
        return
        [
            .. corpus["compaction"]!.AsArray().Select(static item => (item!["id"]!.GetValue<string>(), item["v2"]!.ToJsonString(), item["v3"]!.ToJsonString(), Strings(item["keys"]!), true)),
            .. corpus["comparisons"]!.AsArray().Select(static item => (item!["id"]!.GetValue<string>(), item["left"]!.ToJsonString(), item["right"]!.ToJsonString(), Strings(item["keys"]!), item["same"]!.GetValue<bool>())),
        ];
    }

    [Test, NotInParallel, DisplayName("ESM compares the corpus contracts as the oracle says, in Bun and Node")]
    public void EsmComparisons()
    {
        string directory = Directory.CreateTempSubdirectory("runic-markup-contract-v3-").FullName;
        try
        {
            string runtime = File.ReadAllText(RepositoryPaths.Resolve("tools", "Runic.Translations.Compiler", "Generation", "Rmf2EsmRuntimeV5.js"));
            int start = runtime.IndexOf("// @runic-markup-contract-start", StringComparison.Ordinal), end = runtime.IndexOf("// @runic-markup-contract-end", StringComparison.Ordinal);
            Assert.True(start > 0 && end > start, "the runtime's markup contract rules");
            File.WriteAllText(Path.Combine(directory, "rules.mjs"), "function isRecord(value) { return typeof value === \"object\" && value !== null && !Array.isArray(value); }\n" +
                runtime[start..end] + "\nexport { sameMarkupContract, contractEntry };\n", new UTF8Encoding(false));
            File.Copy(RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-markup-contract-v3.json"), Path.Combine(directory, "corpus.json"));
            string script = Path.Combine(directory, "compare.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { sameMarkupContract, contractEntry } from "./rules.mjs";
                const corpus = JSON.parse(await readFile(new URL("corpus.json", import.meta.url), "utf8"));
                const failures = [];
                const cases = [...corpus.compaction.map(item => ({ id: item.id, left: item.v2, right: item.v3, keys: item.keys, same: true })), ...corpus.comparisons];
                for (const item of cases)
                  for (const [left, right, label] of [[item.left, item.right, ""], [item.right, item.left, " (reversed)"]])
                    if (sameMarkupContract(left, right, item.keys) !== item.same) failures.push(item.id + label);
                // Each key's entry in v3 is its v2 entry: the listed one or the default.
                for (const item of corpus.compaction)
                  for (const key of item.keys)
                    if (JSON.stringify(contractEntry(item.v3, key)) !== JSON.stringify(contractEntry(item.v2, key))) failures.push(item.id + "/" + key);
                if (failures.length) throw new Error("ESM disagrees with the corpus: " + failures.join(", "));
                """, new UTF8Encoding(false));
            foreach (string host in Rmf2DatePercentConformanceTests.EsmHosts()) Rmf2EsmV5Tests.Run(host, [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    // en, de and fr: one plain message (the default shape), one with markup, one de takes from en, and an extra of fr.
    private static Rmf2ProjectV5 Project()
    {
        const string project = """
            {"schemaVersion":1,"catalog":"contracts","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","locales":["en","de","fr"],
             "validation":{"translationCompleteness":"allow","extraLocaleKeys":"allow"}}
            """;
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(project)),
        [
            new("translations/en.rmf2", Encoding.UTF8.GetBytes("plain = Plain\nhelp = Read the {#strong}guide{/strong}.\npartial = Only English and French\n")),
            new("translations/de.rmf2", Encoding.UTF8.GetBytes("plain = Schlicht\nhelp = Lies die {#strong}Anleitung{/strong}.\n")),
            new("translations/fr.rmf2", Encoding.UTF8.GetBytes("plain = Simple\nhelp = Lisez le {#strong}guide{/strong}.\npartial = Seulement anglais et français\nextra = Only French\n")),
        ]);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(static item => item.Id + " " + item.Message)));
        Rmf2GeneratedLocaleBinaryTests.AssertBinaryMatchesJson(result.Project!);
        return result.Project!;
    }

    [Test, DisplayName("a compiled catalog's v3 contract leaves out its default entries, links in .NET with the same entries as its v2, and both outputs embed it")]
    public void CompiledCatalog()
    {
        Rmf2ProjectV5 project = Project();
        string v2 = project.MarkupContract, v3 = project.CompactMarkupContract;
        using (JsonDocument compact = JsonDocument.Parse(v3))
        {
            Assert.Equal(3, compact.RootElement.GetProperty("version").GetInt32(), "version");
            Assert.Equal("de,en,fr", string.Join(",", compact.RootElement.GetProperty("locales").EnumerateArray().Select(static item => item.GetString())), "locales");
            // plain has the default shape; help is structured, partial falls back in de, extra is French only.
            Assert.Equal("extra,help,partial", string.Join(",", compact.RootElement.GetProperty("messages").EnumerateObject().Select(static item => item.Name)), "listed messages");
        }
        string[] keys = [.. project.CanonicalMessages.Select(static item => item.Key)];
        Assert.True(Rmf2MarkupContract.SameContract(v2, v3, keys), "v2 and v3 are not the same contract");
        Assert.Equal(v2, Rmf2MarkupContractV3.Expand(v3, keys), "expanded v3");
        Rmf2MarkupContract linked2 = Rmf2MarkupContract.Link(v2), linked3 = Rmf2MarkupContract.Link(v3);
        foreach (string key in keys.Append("extra"))
        {
            Assert.True(linked2.TryGetMessage(key, out Rmf2MarkupContract.Message? left), key + " in v2");
            Assert.True(linked3.TryGetMessage(key, out Rmf2MarkupContract.Message? right), key + " in v3");
            Assert.Equal(string.Join(",", left!.ContentLocales.OrderBy(static pair => pair.Key, StringComparer.Ordinal)), string.Join(",", right!.ContentLocales.OrderBy(static pair => pair.Key, StringComparer.Ordinal)), key + ": content locales");
            Assert.Equal((left.Structured, left.Document, left.Slots.Count, left.Skeletons.Count), (right.Structured, right.Document, right.Slots.Count, right.Skeletons.Count), key + ": shape");
        }
        // A v3 contract that lists a default entry has two forms, so it is malformed.
        string listed = v3.Replace("\"messages\":{", "\"messages\":{\"plain\":{\"slots\":{},\"structured\":false,\"contentLocales\":{\"de\":\"de\",\"en\":\"en\",\"fr\":\"fr\"},\"content\":\"inline\",\"skeletons\":[]},", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Rmf2MarkupContract.Link(listed), "a listed default entry");
        Assert.Throws<ArgumentException>(() => Rmf2MarkupContract.Link(v3.Replace("\"locales\":[\"de\",\"en\",\"fr\"]", "\"locales\":[\"en\",\"de\",\"fr\"]", StringComparison.Ordinal)), "unordered locales");

        // .NET (RMF2 ABI 5) and ESM embed the same v3 bytes; C# for an earlier runtime keeps v2.
        string catalogData = TranslationOutputRenderer.RenderRmf2V5CSharpCatalogData(project).Text;
        Assert.Equal(v3, GeneratedMarkupContract(catalogData), "C# contract");
        Assert.Equal(v2, GeneratedMarkupContract(TranslationOutputRenderer.RenderRmf2V5CSharpCatalogData(project, 4).Text), "C# contract for RMF2 ABI 4");
        string dynamic = TranslationOutputRenderer.RenderRmf2V5EsmModules(project).Single(static item => item.RelativePath.EndsWith("/dynamic.js", StringComparison.Ordinal)).Text;
        Assert.True(dynamic.Contains("export const rmf2Contract = freezeTrusted(JSON.parse(" + GenerationSupport.JsonString(v3) + "));", StringComparison.Ordinal), "ESM contract");
        using JsonDocument artifact = JsonDocument.Parse(Rmf2LocaleArtifactV5.Render(project, "de").Text);
        Assert.Equal(v3, artifact.RootElement.GetProperty("markupContract").GetRawText(), "pack contract");
    }

    private static string GeneratedMarkupContract(string catalogData)
    {
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(catalogData,
            @"internal static int MarkupContractLength => (\d+);\s*internal static global::System\.ReadOnlySpan<byte> MarkupContractData => new byte\[\]\s*\{([^}]*)\};");
        Assert.True(match.Success, "MarkupContractData");
        byte[] deflated = System.Text.RegularExpressions.Regex.Matches(match.Groups[2].Value, "0x([0-9A-F]{2})").Select(static item => Convert.ToByte(item.Groups[1].Value, 16)).ToArray();
        return GeneratedCatalogSupportText(deflated, int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string GeneratedCatalogSupportText(byte[] deflated, int length) => Generated.GeneratedCatalogSupport.ReadDeflatedText(deflated, length);

    [Test, DisplayName("a v3 catalog loads the v2 and v3 packs of its release as exact packs and overlays in .NET, and a v2 catalog the v3 ones")]
    public void DotNetLoadsBothVersions()
    {
        Rmf2ProjectV5 project = Project();
        foreach (bool compactPack in new[] { true, false })
        foreach (bool compactCatalog in new[] { true, false })
        {
            string label = "pack v" + (compactPack ? 3 : 2) + ", catalog v" + (compactCatalog ? 3 : 2);
            byte[] pack = Rmf2LocaleArtifactV5.Render(project, "de", compactPack).GetUtf8Bytes();
            TranslationPackContract contract = Rmf2V1CorpusTests.PackContract(project, "de");
            contract = Generated.GeneratedCatalogSupport.CreatePackContract(contract.Catalog, contract.Locale, contract.ContractFingerprint, contract.Messages,
                compactCatalog ? project.CompactMarkupContract : project.MarkupContract);
            VerifiedExternalTranslationPack verified = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(pack), contract).AsTask().GetAwaiter().GetResult();
            Assert.Equal(3, verified.Messages.Count, label + ": messages");
            // The same pack with another contract is still refused.
            JsonObject changed = JsonNode.Parse(pack)!.AsObject();
            changed["markupContract"]!["contracts"]!["runic:strong"]!["interactive"] = true;
            Assert.Throws<TranslationPackException>(() => TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(Encoding.UTF8.GetBytes(changed.ToJsonString())), contract).AsTask().GetAwaiter().GetResult(), "differs");
        }
        foreach (bool compactPack in new[] { true, false })
        {
            OverlayPack overlay = TranslationPackV5Loader.ParseOverlay(Rmf2LocaleArtifactV5.Render(project, "fr", compactPack).GetUtf8Bytes(), "contracts", "fr", new TranslationPackLimits(), default);
            Assert.Equal("extra,help,partial,plain", string.Join(",", overlay.Messages.Select(static item => item.Key).Order(StringComparer.Ordinal)), "overlay v" + (compactPack ? 3 : 2));
        }
    }

    [Test, NotInParallel, DisplayName("a v3 ESM catalog decodes and installs the v2 and v3 packs of its release, and refuses another contract")]
    public void EsmLoadsBothVersions()
    {
        Rmf2ProjectV5 project = Project();
        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "v3.json"), Rmf2LocaleArtifactV5.Render(project, "de").GetUtf8Bytes());
            File.WriteAllBytes(Path.Combine(directory, "v2.json"), Rmf2LocaleArtifactV5.Render(project, "de", compactContract: false).GetUtf8Bytes());
            File.WriteAllBytes(Path.Combine(directory, "fr-v2.json"), Rmf2LocaleArtifactV5.Render(project, "fr", compactContract: false).GetUtf8Bytes());
            string script = Path.Combine(directory, "load.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { decodeLocaleArtifact, installOverlay } from "./contracts.esm-v5/dynamic.js";
                const read = async name => JSON.parse(await readFile(new URL(name, import.meta.url), "utf8"));
                const failures = [];
                for (const name of ["v3.json", "v2.json"]) {
                  const artifact = await read(name), decoded = decodeLocaleArtifact(structuredClone(artifact));
                  if (!decoded.ok) failures.push(name + ": " + decoded.reason);
                  artifact.markupContract.contracts["runic:strong"].interactive = true;
                  const changed = decodeLocaleArtifact(artifact);
                  if (changed.ok || changed.reason !== "RTR0023/argument-contract-mismatch") failures.push(name + " changed: " + (changed.ok ? "accepted" : changed.reason));
                }
                const overlay = installOverlay(await read("fr-v2.json"));
                if (!overlay.ok) failures.push("overlay: " + overlay.reason); else overlay.value.remove();
                if (failures.length) throw new Error(failures.join("\n"));
                """, new UTF8Encoding(false));
            foreach (string host in Rmf2DatePercentConformanceTests.EsmHosts()) Rmf2EsmV5Tests.Run(host, [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }
}
