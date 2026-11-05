using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// The pinned CLDR data and locale algorithm exist twice: CldrLocales.cs and the formatters in .NET, and the
// generated ESM runtime. These cases check both against the shared oracles in specs/translations/corpus
// (relative time, locale negotiation) and against each other for every CLDR locale.
[Category("rmf2-semantic-v5")]
internal sealed class CldrLocaleConformanceTests
{
    private static readonly string[] Units = ["second", "minute", "hour", "day", "week", "month", "year"];
    private static readonly string[] ListTypes = ["conjunction", "disjunction", "unit"];
    private static readonly string[] ListStyles = ["long", "short", "narrow"];
    private static readonly string[] PluralSamples = ["0", "1", "2", "3", "4", "5", "6", "7", "11", "12", "21", "22", "101", "111", "1000000", "0.5", "1.0", "1.5", "2.10", "0.01"];
    private static readonly string[] RelativeValues = ["-2", "-1", "0", "1", "2", "3", "5", "11", "21", "-100"];
    // Inputs that both canonicalizers must treat alike: case, '_', POSIX code sets and modifiers, extensions, private use,
    // grandfathered and malformed tags, legacy and undetermined codes, script and region forms.
    private static readonly string[] RawPreferences =
    [
        "DE_ch", "zh_hant_tw", "sr-latn-me@latin", "en-US-u-ca-gregory-x-a", "en--US", "i-klingon", "zh-min-nan", "C.UTF-8", "C", "POSIX",
        "en-a-bbb-a-ccc", "EN-gb", "de_DE.UTF-8@euro", "de_DE@euro", "x-private", "en-x", "", " en", "en ", "123", "en-US-", "-de", "de-",
        "und", "UND-tw", "und-Latn-DE", "sh", "SH-rs", "tl_PH", "mo", "jw", "cnr", "drw", "iw-il", "in_ID", "ji", "nb_NO", "nn-no", "no-bok",
        "zh-Hans-CN-u-nu-hanidec", "sr-Cyrl-RS-x-foo", "es-419", "ES-419", "pt-BR-abl1943", "de-1996", "de-DE-1901-1901", "ar-u-nu-arab",
        "hi-Latn", "pa-PK", "pa-Arab", "az-Arab", "AZ-arab-IR", "ku-IQ", "ms-Arab", "zh-TW", "zh-hk", "yue", "yue-Hans", "cmn-Hant-TW",
        "zh-cmn-Hans-CN", "art-lojban", "sgn-BE-FR", "en-GB-oed", "en-001", "en-150", "es-MX", "pt-AO", "de-CH-1996", "fil", "tlh-x-klingon",
        "qaa", "aaa-bbb-ccc-ddd", "abcdefghi", "en-Latn-US-posix", "en_US_POSIX", "sr-ME", "uz-AF", "uz-Cyrl", "kk-Arab", "mn-Mong",
    ];

    private sealed record RelativeCase(string Id, string Locale, string Value, string Unit, string Numeric, string Expected);
    private sealed record NegotiationGroup(string Id, string[] Locales, string Base, (string[] Preferences, string Expected)[] Cases);

    private static List<RelativeCase> RelativeCases()
    {
        var corpus = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-relative-time-conformance.json")))!;
        Assert.Equal("48.2", corpus["cldrVersion"]!.GetValue<string>(), "Relative-time corpus CLDR pin");
        return corpus["cases"]!.AsArray().Select(item => new RelativeCase(item!["id"]!.GetValue<string>(), item["locale"]!.GetValue<string>(),
            item["value"]!.GetValue<string>(), item["unit"]!.GetValue<string>(), item["numeric"]!.GetValue<string>(), item["expected"]!.GetValue<string>())).ToList();
    }

    private static List<NegotiationGroup> NegotiationGroups()
    {
        var corpus = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "locale-negotiation.json")))!;
        Assert.Equal("48.2", corpus["cldrVersion"]!.GetValue<string>(), "Negotiation corpus CLDR pin");
        return corpus["groups"]!.AsArray().Select(group => new NegotiationGroup(group!["id"]!.GetValue<string>(),
            group["locales"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray(), group["base"]!.GetValue<string>(),
            group["cases"]!.AsArray().Select(item => (item!["preferences"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray(), item["expected"]!.GetValue<string>())).ToArray())).ToList();
    }

    [Test, DisplayName("relative-time vectors format as CLDR gives them in .NET")]
    public void RelativeTimeDotNet()
    {
        foreach (RelativeCase item in RelativeCases())
            Assert.Equal(item.Expected, TextRelativeTimeFormatter.Format(decimal.Parse(item.Value, CultureInfo.InvariantCulture), item.Unit, item.Numeric, item.Locale), item.Id);
    }

    [Test, DisplayName("locale negotiation vectors resolve as expected in .NET")]
    public void NegotiationDotNet()
    {
        foreach (NegotiationGroup group in NegotiationGroups())
            foreach ((string[] preferences, string expected) in group.Cases)
                Assert.Equal(expected, LocaleNegotiation.Negotiate(preferences, group.Locales, group.Base), group.Id + ": " + string.Join(",", preferences));
    }

    /// <summary>
    /// Every CLDR ID and the raw inputs, plus case and POSIX forms of every third ID, so that both canonicalizers and
    /// the POSIX stripping are compared on more than canonical tags.
    /// </summary>
    private static string[] PreferencePool(string[] all)
    {
        var pool = new List<string>(all);
        pool.AddRange(RawPreferences);
        for (int index = 0; index < all.Length; index += 3)
        {
            pool.Add(all[index].ToUpperInvariant());
            pool.Add(all[index].ToLowerInvariant().Replace('-', '_') + (index % 2 == 0 ? ".UTF-8" : "@latin"));
        }

        return pool.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Deterministic two- to four-entry preference lists drawn from <paramref name="pool"/> (a fixed linear congruential sequence).</summary>
    private static string[][] PreferenceLists(string[] pool, int count)
    {
        ulong state = 0x5DEECE66DUL;
        int Next(int bound) { state = state * 6364136223846793005UL + 1442695040888963407UL; return (int)((state >> 33) % (ulong)bound); }
        var lists = new string[count][];
        for (int index = 0; index < count; index++)
        {
            lists[index] = new string[2 + Next(3)];
            for (int entry = 0; entry < lists[index].Length; entry++) lists[index][entry] = pool[Next(pool.Length)];
        }

        return lists;
    }

    [Test, NotInParallel, DisplayName("the ESM runtime matches the shared vectors and .NET for every CLDR locale")]
    public void EsmAgreesWithDotNet()
    {
        string[] all = [.. Runic.Translations.Compiler.GeneratedLocaleTables.LikelyKeys, .. Runic.Translations.Compiler.GeneratedLocaleTables.CardinalKeys,
            .. Runic.Translations.Compiler.GeneratedLocaleTables.ListIds, .. Runic.Translations.Compiler.GeneratedLocaleTables.RelativeTimeIds];
        all = all.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        // .NET results for every locale with data.
        var expected = new JsonObject();
        var lists = new JsonObject();
        foreach (string id in Runic.Translations.Compiler.GeneratedLocaleTables.ListIds)
        {
            var values = new JsonArray();
            foreach (string type in ListTypes)
                foreach (string style in ListStyles)
                    foreach (int count in new[] { 2, 3, 4 })
                        values.Add(TextListFormatter.Format(Enumerable.Range(0, count).Select(index => ((char)('A' + index)).ToString()).ToArray(), type, style, id));
            lists[id] = values;
        }
        expected["list"] = lists;
        var relative = new JsonObject();
        foreach (string id in Runic.Translations.Compiler.GeneratedLocaleTables.RelativeTimeIds)
        {
            if (GeneratedLocaleData.FindRelativeTime(id) is null) continue;
            var values = new JsonArray();
            foreach (string unit in Units)
                foreach (string numeric in new[] { "always", "auto" })
                    foreach (string value in RelativeValues)
                        values.Add(TextRelativeTimeFormatter.Format(decimal.Parse(value, CultureInfo.InvariantCulture), unit, numeric, id));
            relative[id] = values;
        }
        expected["relativeTime"] = relative;
        var plural = new JsonObject();
        foreach (string id in all)
        {
            var values = new JsonArray();
            foreach (bool ordinal in new[] { false, true })
                foreach (string sample in PluralSamples)
                {
                    PluralOperands operands = Operands(sample);
                    values.Add(GeneratedLocaleData.SelectPlural(id, ordinal, in operands));
                }
            plural[id] = values;
        }
        expected["plural"] = plural;
        var negotiation = new JsonObject();
        string[] preferences = [.. PreferencePool(all), "zh-HK", "de-CH", "en-AU", "nb-NO", "no-NO", "iw", "cmn-TW"];
        preferences = preferences.Distinct(StringComparer.Ordinal).ToArray();
        string[][] preferenceLists = PreferenceLists(preferences, 1500);
        // One preference at a time, then the generated lists, so ordering across preferences is compared too.
        foreach (NegotiationGroup group in NegotiationGroups())
            negotiation[group.Id] = new JsonArray(preferences.Select(preference => (JsonNode?)LocaleNegotiation.Negotiate([preference], group.Locales, group.Base))
                .Concat(preferenceLists.Select(list => (JsonNode?)LocaleNegotiation.Negotiate(list, group.Locales, group.Base))).ToArray());
        expected["negotiation"] = negotiation;
        // Both canonicalizers on every input, as the single-tag resolveLocale and ResolveRequestedLocale see it.
        expected["canonical"] = new JsonObject
        {
            ["all"] = new JsonArray(preferences.Select(preference => (JsonNode?)(Runic.Translations.LocaleTag.TryCanonicalize(preference, out string canonical) ? canonical : null)).ToArray()),
        };

        Rmf2ProjectV5 project = Rmf2EsmV5Tests.CompileProject("cldr", "en", ["en"], false, ("en", "x = {{x}}\n"));
        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            string runtime = File.ReadAllText(Path.Combine(directory, "cldr.esm-v5", "runtime.js"));
            string ownData = TranslationCapabilityRegistry.EsmLocaleSource(["en"]);
            Assert.True(runtime.Contains(ownData, StringComparison.Ordinal), "The runtime does not embed its locale data as expected.");
            Assert.True(!runtime.Contains("\"ru\":", StringComparison.Ordinal), "An English catalog embeds Russian locale data.");
            // One module per negotiation group, with every CLDR language's data so all locales can be compared.
            string harness(string[] locales, string baseLocale) => runtime
                .Replace(ownData, TranslationCapabilityRegistry.EsmLocaleSource(all), StringComparison.Ordinal)
                .Replace("export const baseLocale = \"en\";", "export const baseLocale = " + JsonSerializer.Serialize(baseLocale) + ";", StringComparison.Ordinal)
                .Replace("export const locales = Object.freeze([\"en\"]);", "export const locales = Object.freeze(" + JsonSerializer.Serialize(locales.Order(StringComparer.Ordinal)) + ");", StringComparison.Ordinal)
                + "\nexport { formatList as conformanceFormatList, formatRelative as conformanceFormatRelative, selectPlural as conformanceSelectPlural, canonicalLocale as conformanceCanonicalLocale };\n";
            File.WriteAllText(Path.Combine(directory, "cldr.esm-v5", "harness-all.js"), harness(["en"], "en"), new UTF8Encoding(false));
            var groups = new JsonArray();
            foreach (NegotiationGroup group in NegotiationGroups())
            {
                File.WriteAllText(Path.Combine(directory, "cldr.esm-v5", "harness-" + group.Id + ".js"), harness(group.Locales, group.Base), new UTF8Encoding(false));
                groups.Add(new JsonObject
                {
                    ["id"] = group.Id,
                    ["cases"] = new JsonArray(group.Cases.Select(item => (JsonNode)new JsonObject { ["preferences"] = new JsonArray(item.Preferences.Select(value => (JsonNode)value).ToArray()), ["expected"] = item.Expected }).ToArray()),
                });
            }
            var relativeCases = new JsonArray(RelativeCases().Select(item => (JsonNode)new JsonObject { ["id"] = item.Id, ["locale"] = item.Locale, ["value"] = item.Value, ["unit"] = item.Unit, ["numeric"] = item.Numeric, ["expected"] = item.Expected }).ToArray());
            File.WriteAllText(Path.Combine(directory, "input.json"), new JsonObject
            {
                ["groups"] = groups, ["relative"] = relativeCases, ["preferences"] = new JsonArray(preferences.Select(value => (JsonNode)value).ToArray()),
                ["preferenceLists"] = new JsonArray(preferenceLists.Select(list => (JsonNode)new JsonArray(list.Select(value => (JsonNode)value).ToArray())).ToArray()),
                ["units"] = new JsonArray(Units.Select(value => (JsonNode)value).ToArray()), ["relativeValues"] = new JsonArray(RelativeValues.Select(value => (JsonNode)value).ToArray()),
                ["pluralSamples"] = new JsonArray(PluralSamples.Select(value => (JsonNode)value).ToArray()), ["all"] = new JsonArray(all.Select(value => (JsonNode)value).ToArray()),
                ["listIds"] = new JsonArray(lists.Select(item => (JsonNode)item.Key).ToArray()), ["relativeIds"] = new JsonArray(relative.Select(item => (JsonNode)item.Key).ToArray()),
            }.ToJsonString(), new UTF8Encoding(false));
            string script = Path.Combine(directory, "conformance.mjs");
            File.WriteAllText(script, """
                import { readFile, writeFile } from "node:fs/promises";
                import * as runtime from "./cldr.esm-v5/harness-all.js";
                const input = JSON.parse(await readFile(new URL("./input.json", import.meta.url), "utf8"));
                const failures = [];
                for (const item of input.relative) {
                  const actual = runtime.conformanceFormatRelative(runtime.decimal(item.value), item.unit, item.numeric, item.locale);
                  if (actual !== item.expected) failures.push(`${item.id}: expected ${JSON.stringify(item.expected)}, got ${JSON.stringify(actual)}`);
                }
                const negotiation = {};
                for (const group of input.groups) {
                  const module = await import(`./cldr.esm-v5/harness-${group.id}.js`);
                  for (const item of group.cases) {
                    const actual = module.negotiateLocale(item.preferences);
                    if (actual !== item.expected) failures.push(`${group.id} ${JSON.stringify(item.preferences)}: expected ${item.expected}, got ${actual}`);
                  }
                  negotiation[group.id] = [...input.preferences.map(preference => module.negotiateLocale([preference])), ...input.preferenceLists.map(list => module.negotiateLocale(list))];
                }
                const list = {};
                for (const id of input.listIds) {
                  const values = [];
                  for (const type of ["conjunction", "disjunction", "unit"]) for (const style of ["long", "short", "narrow"]) for (const count of [2, 3, 4])
                    values.push(runtime.conformanceFormatList(Array.from({ length: count }, (_, index) => String.fromCharCode(65 + index)), type, style, id));
                  list[id] = values;
                }
                const relativeTime = {};
                for (const id of input.relativeIds) {
                  const values = [];
                  for (const unit of input.units) for (const numeric of ["always", "auto"]) for (const value of input.relativeValues)
                    values.push(runtime.conformanceFormatRelative(runtime.decimal(value), unit, numeric, id));
                  relativeTime[id] = values;
                }
                const plural = {};
                for (const id of input.all) {
                  const values = [];
                  for (const ordinal of [false, true]) for (const sample of input.pluralSamples) {
                    // A visible decimal keeps its trailing zeros (1.0 has v = 1).
                    const [whole, fraction = ""] = sample.split("."), value = Object.freeze({ coefficient: BigInt(whole + fraction), scale: fraction.length, negative: false });
                    let category = null; try { category = runtime.conformanceSelectPlural(value, id, ordinal); } catch {}
                    values.push(category);
                  }
                  plural[id] = values;
                }
                const canonical = { all: input.preferences.map(preference => runtime.conformanceCanonicalLocale(preference)) };
                await writeFile(new URL("./output.json", import.meta.url), JSON.stringify({ failures, list, relativeTime, plural, negotiation, canonical }));
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [script], directory);
            JsonNode output = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "output.json")))!;
            var failures = output["failures"]!.AsArray().Select(item => item!.GetValue<string>()).ToList();
            foreach (string section in new[] { "list", "relativeTime", "plural", "negotiation", "canonical" })
                foreach ((string id, JsonNode? values) in expected[section]!.AsObject())
                {
                    JsonArray dotnet = values!.AsArray(), esm = output[section]![id]!.AsArray();
                    for (int index = 0; index < dotnet.Count; index++)
                        if (dotnet[index]?.GetValue<string>() != esm[index]?.GetValue<string>())
                            failures.Add(section + " " + id + " #" + index + Input(section, index) + ": .NET " + JsonSerializer.Serialize(dotnet[index]?.GetValue<string>()) + ", ESM " + JsonSerializer.Serialize(esm[index]?.GetValue<string>()));
                }
            Assert.True(failures.Count == 0, failures.Count + " differences:\n" + string.Join("\n", failures.Take(40)));

            string Input(string section, int index) => section is "negotiation" or "canonical"
                ? " " + JsonSerializer.Serialize(index < preferences.Length ? [preferences[index]] : preferenceLists[index - preferences.Length])
                : "";
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("a catalog embeds only the CLDR data of its languages and their CLDR relatives")]
    public void EsmDataIsScoped()
    {
        string english = TranslationCapabilityRegistry.EsmLocaleSource(["en"]);
        Assert.True(english.Contains("\"en-GB\":\"en-001\"", StringComparison.Ordinal), "English parents are missing.");
        Assert.True(!english.Contains("\"de\":", StringComparison.Ordinal), "English embeds German data.");
        string norwegian = TranslationCapabilityRegistry.EsmLocaleSource(["nb"]);
        Assert.True(norwegian.Contains("\"no\":", StringComparison.Ordinal) && norwegian.Contains("\"nn\":", StringComparison.Ordinal), "Bokmål lacks its CLDR relatives.");
        string hebrew = TranslationCapabilityRegistry.EsmLocaleSource(["he"]);
        Assert.True(hebrew.Contains("\"iw\":\"he\"", StringComparison.Ordinal), "The iw alias is missing.");
        Assert.True(TranslationCapabilityRegistry.EsmLocaleSource(["fil"]).Contains("\"tl\":\"fil\"", StringComparison.Ordinal), "The tl alias is missing.");
        string serbian = TranslationCapabilityRegistry.EsmLocaleSource(["sr-Latn"]);
        Assert.True(serbian.Contains("\"sh\":\"sr-Latn\"", StringComparison.Ordinal) && serbian.Contains("\"cnr\":\"sr-ME\"", StringComparison.Ordinal), "The sh and cnr aliases are missing.");
        Assert.True(english.Length < 20_000, "English locale data is " + english.Length + " characters.");
    }

    [Test, DisplayName("the compiler supports every CLDR plural, ordinal and list locale")]
    public void CompilerCapabilities()
    {
        foreach (string locale in new[] { "pl", "ru", "uk", "pt", "pt-PT", "pt-AO", "ar", "he", "zh", "zh-Hant", "zh-TW", "ko", "tr", "fi", "sr-Latn", "kok-Latn", "en_US" })
        {
            Assert.True(TranslationCapabilityRegistry.SupportsCardinal(locale), "cardinal " + locale);
            Assert.True(TranslationCapabilityRegistry.SupportsOrdinal(locale), "ordinal " + locale);
            Assert.True(TranslationCapabilityRegistry.SupportsList(locale), "list " + locale);
            Assert.True(TranslationCapabilityRegistry.SupportsRelativeTime(locale), "relative time " + locale);
        }
        // CLDR data without plural rules of its own: the root rule (other only), so no RTR0031.
        foreach (string locale in new[] { "agq", "bas", "rw", "mi", "tt", "yav", "zgh" })
        {
            Assert.True(TranslationCapabilityRegistry.SupportsCardinal(locale) && TranslationCapabilityRegistry.SupportsOrdinal(locale), "plurals " + locale);
            Assert.True(TranslationCapabilityRegistry.SupportsRelativeTime(locale), "relative time " + locale);
            Assert.Equal("other", string.Join("|", TranslationCapabilityRegistry.PluralCategories(locale, false)), "categories " + locale);
        }
        foreach (string locale in new[] { "tlh", "qaa", "x-private" })
            Assert.True(!TranslationCapabilityRegistry.SupportsCardinal(locale) && !TranslationCapabilityRegistry.SupportsList(locale), "unexpected support for " + locale);
        Assert.Equal("one|many|other", string.Join("|", TranslationCapabilityRegistry.PluralCategories("pt-PT", false)));
        Assert.Equal("one|few|many|other", string.Join("|", TranslationCapabilityRegistry.PluralCategories("pl", false)));
        Assert.Equal("zero|one|two|few|many|other", string.Join("|", TranslationCapabilityRegistry.PluralCategories("ar", false)));
        Assert.Equal("other", string.Join("|", TranslationCapabilityRegistry.PluralCategories("nn", true)));
    }

    private static PluralOperands Operands(string sample)
    {
        int point = sample.IndexOf('.', StringComparison.Ordinal);
        int scale = point < 0 ? 0 : sample.Length - point - 1;
        return PluralOperands.FromVisible(UInt128.Parse(sample.Replace(".", "", StringComparison.Ordinal), CultureInfo.InvariantCulture), scale);
    }
}
