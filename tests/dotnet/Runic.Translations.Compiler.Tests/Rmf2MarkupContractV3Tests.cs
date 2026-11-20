using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            int canonical = runtime.IndexOf("\nfunction canonicalLocale(", StringComparison.Ordinal), canonicalEnd = runtime.IndexOf("\n}\n", canonical, StringComparison.Ordinal);
            Assert.True(start > 0 && end > start && canonical > 0 && canonicalEnd > canonical, "the runtime's markup contract rules");
            File.WriteAllText(Path.Combine(directory, "rules.mjs"), "function isRecord(value) { return typeof value === \"object\" && value !== null && !Array.isArray(value); }\n" +
                runtime[canonical..(canonicalEnd + 3)] + runtime[start..end] + "\nexport { sameMarkupContract, contractEntry };\n", new UTF8Encoding(false));
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
    // plain adds that many more default-shaped messages to every locale.
    private static Rmf2ProjectV5 Project(int plain = 0)
    {
        string more = string.Concat(Enumerable.Range(0, plain).Select(static index => "plain" + index + " = Plain " + index + "\n"));
        const string project = """
            {"schemaVersion":1,"catalog":"contracts","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","locales":["en","de","fr"],
             "validation":{"translationCompleteness":"allow","extraLocaleKeys":"allow"}}
            """;
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(project)),
        [
            new("translations/en.rmf2", Encoding.UTF8.GetBytes("plain = Plain\nhelp = Read the {#strong}guide{/strong}.\npartial = Only English and French\n" + more)),
            new("translations/de.rmf2", Encoding.UTF8.GetBytes("plain = Schlicht\nhelp = Lies die {#strong}Anleitung{/strong}.\n" + more)),
            new("translations/fr.rmf2", Encoding.UTF8.GetBytes("plain = Simple\nhelp = Lisez le {#strong}guide{/strong}.\npartial = Seulement anglais et français\nextra = Only French\n" + more)),
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
    // Pack contracts whose locales would make the version 2 form huge: the same tag a million times (not ordered), and
    // 456,976 distinct canonical tags in ordinal order (aa-AA … zz-ZZ), each within the 8 MiB pack limit.
    private static IEnumerable<(string Name, string Locales)> HostileLocales()
    {
        yield return ("repeated", "[" + string.Join(",", Enumerable.Repeat("\"de\"", 1_000_000)) + "]");
        var tags = new List<string>(26 * 26 * 26 * 26);
        for (char a = 'a'; a <= 'z'; a++) for (char b = 'a'; b <= 'z'; b++) for (char c = 'A'; c <= 'Z'; c++) for (char d = 'A'; d <= 'Z'; d++)
            tags.Add("\"" + a + b + "-" + c + d + "\"");
        yield return ("distinct", "[" + string.Join(",", tags) + "]");
    }

    private static byte[] WithLocales(byte[] pack, string locales)
    {
        string text = Encoding.UTF8.GetString(pack);
        const string Locales = "\"locales\":[\"de\",\"en\",\"fr\"]";
        Assert.True(text.Contains(Locales, StringComparison.Ordinal), "the pack's v3 locales");
        return Encoding.UTF8.GetBytes(text.Replace(Locales, "\"locales\":" + locales, StringComparison.Ordinal));
    }

    // Loading must end in a TranslationPackException or a result, in bounded time and memory.
    private static TranslationPackException? Bounded(string label, Action load, bool rejects)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
        TranslationPackException? refused = null;
        try
        {
            load();
            Assert.True(!rejects, label + ": accepted");
        }
        catch (TranslationPackException exception)
        {
            Assert.True(rejects, label + ": refused with " + exception.Reason + ": " + exception.Message);
            refused = exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(label + " failed with " + exception.GetType().Name + ", not TranslationPackException: " + exception, exception);
        }
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        long used = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(elapsed < TimeSpan.FromSeconds(10), label + " took " + elapsed + ".");
        Assert.True(used < 256L * 1024 * 1024, label + " allocated " + used + " bytes.");
        return refused;
    }

    [Test, DisplayName("a pack contract with a huge locales array is refused by v3 and v2 catalogs and as an overlay, in bounded time and memory")]
    public void HostileLocalesAreBounded()
    {
        // 200 default-shaped messages: written out in version 2, each would repeat the pack's whole locales array.
        Rmf2ProjectV5 project = Project(plain: 200);
        byte[] pack = Rmf2LocaleArtifactV5.Render(project, "de").GetUtf8Bytes(), overlay = Rmf2LocaleArtifactV5.Render(project, "fr").GetUtf8Bytes();
        TranslationPackContract generated = Rmf2V1CorpusTests.PackContract(project, "de");
        foreach ((string name, string locales) in HostileLocales())
        {
            byte[] hostile = WithLocales(pack, locales);
            Assert.True(hostile.Length < 8 * 1024 * 1024, name + ": " + hostile.Length + " bytes");
            foreach (bool compactCatalog in new[] { true, false })
            {
                TranslationPackContract contract = Generated.GeneratedCatalogSupport.CreatePackContract(generated.Catalog, generated.Locale, generated.ContractFingerprint,
                    generated.Messages, compactCatalog ? project.CompactMarkupContract : project.MarkupContract);
                string label = name + " locales, catalog v" + (compactCatalog ? 3 : 2);
                TranslationPackException? refused = Bounded(label, () => TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(hostile), contract).AsTask().GetAwaiter().GetResult(), rejects: true);
                Assert.Equal(TranslationPackFailureReason.ArgumentContractMismatch, refused!.Reason, label + ": reason");
            }
            // An overlay is checked key by key against its own contract: the repeated tags are malformed, and the distinct
            // ones leave out fr, so an unlisted message has no content locale for it.
            Bounded(name + " locales, overlay", () => TranslationPackV5Loader.ParseOverlay(WithLocales(overlay, locales), "contracts", "fr", new TranslationPackLimits(), default), rejects: true);
        }
    }

    // Malformed v3 contracts in a pack: every one is refused with a TranslationPackException, never another exception.
    private static IEnumerable<(string Name, string From, string To)> MalformedContracts() =>
    [
        ("locales not an array", "\"locales\":[\"de\",\"en\",\"fr\"]", "\"locales\":{\"de\":1}"),
        ("locales of numbers", "\"locales\":[\"de\",\"en\",\"fr\"]", "\"locales\":[1,2,3]"),
        ("locales null", "\"locales\":[\"de\",\"en\",\"fr\"]", "\"locales\":null"),
        ("unordered locales", "\"locales\":[\"de\",\"en\",\"fr\"]", "\"locales\":[\"en\",\"de\",\"fr\"]"),
        ("repeated locale", "\"locales\":[\"de\",\"en\",\"fr\"]", "\"locales\":[\"de\",\"de\",\"en\"]"),
        ("non-canonical locale", "\"locales\":[\"de\",\"en\",\"fr\"]", "\"locales\":[\"DE\",\"en\",\"fr\"]"),
        ("fewer locales", "\"locales\":[\"de\",\"en\",\"fr\"]", "\"locales\":[\"de\",\"en\"]"),
        ("more locales", "\"locales\":[\"de\",\"en\",\"fr\"]", "\"locales\":[\"de\",\"en\",\"fr\",\"it\"]"),
        ("no locales", ",\"locales\":[\"de\",\"en\",\"fr\"]", ""),
        ("messages an array", "\"messages\":{\"extra\"", "\"messages\":[],\"x\":{\"extra\""),
        ("version as text", "\"version\":3", "\"version\":\"3\""),
        ("contracts null", "\"contracts\":{", "\"contracts\":null,\"y\":{"),
    ];

    [Test, DisplayName(".NET refuses malformed v3 pack contracts with a TranslationPackException")]
    public void DotNetRefusesMalformedContracts()
    {
        Rmf2ProjectV5 project = Project();
        string pack = Rmf2LocaleArtifactV5.Render(project, "de").Text;
        TranslationPackContract generated = Rmf2V1CorpusTests.PackContract(project, "de");
        TranslationPackContract contract = Generated.GeneratedCatalogSupport.CreatePackContract(generated.Catalog, generated.Locale, generated.ContractFingerprint,
            generated.Messages, project.CompactMarkupContract);
        foreach ((string name, string from, string to) in MalformedContracts())
        {
            Assert.True(pack.Contains(from, StringComparison.Ordinal), name + ": the pack contains " + from);
            byte[] malformed = Encoding.UTF8.GetBytes(ReplaceFirst(pack, from, to));
            Bounded(name, () => TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(malformed), contract).AsTask().GetAwaiter().GetResult(), rejects: true);
        }
    }

    private static string ReplaceFirst(string text, string from, string to)
    {
        int at = text.IndexOf(from, StringComparison.Ordinal);
        return text[..at] + to + text[(at + from.Length)..];
    }

    [Test, DisplayName("the contract comparison never throws on truncated, flipped, spliced or inflated contracts, in bounded time")]
    public void FuzzedContractsCompareClosed()
    {
        Rmf2ProjectV5 project = Project();
        string trusted = project.CompactMarkupContract, v2 = project.MarkupContract;
        string[] keys = [.. project.CanonicalMessages.Select(static item => item.Key)];
        byte[][] seeds = [Encoding.UTF8.GetBytes(trusted), Encoding.UTF8.GetBytes(v2)];
        var random = new Random(0x0DE5EC81);
        int same = 0, different = 0;
        long slowest = 0;
        for (int round = 0; round < 4000; round++)
        {
            byte[] mutant = MutateContract(seeds[random.Next(seeds.Length)], random);
            string text = Encoding.UTF8.GetString(mutant);
            long started = Stopwatch.GetTimestamp();
            bool verdict;
            try { verdict = Rmf2MarkupContract.SameContract(text, round % 2 == 0 ? trusted : v2, keys); }
            catch (Exception exception) { throw new InvalidOperationException("Round " + round + " threw " + exception.GetType().Name + ": " + exception, exception); }
            slowest = Math.Max(slowest, Stopwatch.GetTimestamp() - started);
            if (verdict) same++; else different++;
        }
        Assert.True(same > 0 && different > 2000, "The mutations did not cover both outcomes: " + same + " same, " + different + " different.");
        Assert.True(Stopwatch.GetElapsedTime(0, slowest) < TimeSpan.FromSeconds(2), "A single comparison took " + Stopwatch.GetElapsedTime(0, slowest) + ".");
    }

    private static byte[] MutateContract(byte[] seed, Random random)
    {
        byte[] data = (byte[])seed.Clone();
        switch (random.Next(7))
        {
            case 0: return data[..random.Next(data.Length)];                                            // truncation
            case 1:                                                                                     // bit flips
                for (int flips = 1 + random.Next(4); flips > 0; flips--) data[random.Next(data.Length)] ^= (byte)(1 << random.Next(8));
                return data;
            case 2:                                                                                     // printable bytes
                for (int index = random.Next(data.Length), end = Math.Min(data.Length, index + 1 + random.Next(8)); index < end; index++) data[index] = (byte)random.Next(32, 127);
                return data;
            case 3:                                                                                     // a removed range
            {
                int at = random.Next(data.Length), length = 1 + random.Next(Math.Min(16, data.Length - at));
                return [.. data[..at], .. data[(at + length)..]];
            }
            case 4:                                                                                     // a duplicated range
            {
                int at = random.Next(data.Length), length = 1 + random.Next(Math.Min(64, data.Length - at));
                return [.. data[..(at + length)], .. data[at..(at + length)], .. data[(at + length)..]];
            }
            case 5:                                                                                     // inflated locales
            {
                string text = Encoding.UTF8.GetString(data);
                int at = text.IndexOf("\"locales\":[", StringComparison.Ordinal);
                if (at < 0) return data;
                at += "\"locales\":[".Length;
                string tag = random.Next(2) == 0 ? "\"de\"," : "\"zz-ZZ\",";
                return Encoding.UTF8.GetBytes(text[..at] + string.Concat(Enumerable.Repeat(tag, 1 + random.Next(200_000))) + text[at..]);
            }
            default: return data;                                                                       // unchanged
        }
    }

    [Test, NotInParallel, DisplayName("ESM refuses hostile and malformed v3 pack contracts and hostile overlays, in bounded time and without throwing")]
    public void EsmRefusesHostileContracts()
    {
        Rmf2ProjectV5 project = Project(plain: 200);
        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            byte[] pack = Rmf2LocaleArtifactV5.Render(project, "de").GetUtf8Bytes(), overlay = Rmf2LocaleArtifactV5.Render(project, "fr").GetUtf8Bytes();
            var cases = new List<string>();
            foreach ((string name, string locales) in HostileLocales())
            {
                File.WriteAllBytes(Path.Combine(directory, name + ".json"), WithLocales(pack, locales));
                File.WriteAllBytes(Path.Combine(directory, name + "-overlay.json"), WithLocales(overlay, locales));
                cases.Add(name);
            }
            string text = Encoding.UTF8.GetString(pack);
            int index = 0;
            foreach ((string name, string from, string to) in MalformedContracts())
            {
                File.WriteAllText(Path.Combine(directory, "malformed-" + index + ".json"), ReplaceFirst(text, from, to), new UTF8Encoding(false));
                index++;
            }
            string script = Path.Combine(directory, "hostile.mjs");
            File.WriteAllText(script, "const hostile = " + JsonSerializer.Serialize(cases) + ", malformed = " + index + ";\n" + """
                import { readFile } from "node:fs/promises";
                import { decodeLocaleArtifact, installOverlay } from "./contracts.esm-v5/dynamic.js";
                const read = async name => JSON.parse(await readFile(new URL(name, import.meta.url), "utf8"));
                const failures = [];
                const timed = (label, run) => {
                  const started = performance.now();
                  try { return run(); } catch (error) { failures.push(label + " threw " + error); return undefined; }
                  finally { const elapsed = performance.now() - started; if (elapsed > 10000) failures.push(label + " took " + Math.round(elapsed) + " ms"); }
                };
                for (const name of hostile) {
                  const artifact = await read(name + ".json");
                  const decoded = timed(name, () => decodeLocaleArtifact(artifact));
                  if (decoded && (decoded.ok || decoded.reason !== "RTR0023/argument-contract-mismatch")) failures.push(name + ": " + (decoded.ok ? "accepted" : decoded.reason));
                  const overlay = await read(name + "-overlay.json");
                  const installed = timed(name + " overlay", () => installOverlay(overlay));
                  if (installed?.ok) { installed.value.remove(); failures.push(name + " overlay: accepted"); }
                }
                for (let index = 0; index < malformed; index++) {
                  const artifact = await read("malformed-" + index + ".json");
                  const decoded = timed("malformed " + index, () => decodeLocaleArtifact(artifact));
                  if (decoded?.ok) failures.push("malformed " + index + ": accepted");
                }
                if (failures.length) throw new Error(failures.join("\n"));
                """, new UTF8Encoding(false));
            foreach (string host in Rmf2DatePercentConformanceTests.EsmHosts()) Rmf2EsmV5Tests.Run(host, [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }
}

