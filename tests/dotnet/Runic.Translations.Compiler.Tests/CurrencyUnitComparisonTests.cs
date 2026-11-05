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

// A generated comparison of :currency, :unit and compact notation in .NET and ESM for every CLDR locale (and
// -u-nu-latn where the default numbering system is not Latin), as LocalizedFormatComparisonTests compares numbers. Both
// runtimes format from the same pinned CLDR data, so they must agree exactly everywhere. Where the JavaScript engine's
// ICU carries the pinned CLDR version and has data for the locale, the output must also equal Intl.NumberFormat's,
// except for the differences listed, by locale and kind, in specs/translations/corpus/currency-unit-divergences.json.
[Category("rmf2-semantic-v5")]
internal sealed class CurrencyUnitComparisonTests
{
    private const string PinnedCldrMajor = "48";
    private const int PinnedIcuMajor = 78;

    // kind, the RMF2 message, the value, and the equivalent Intl.NumberFormat options.
    private sealed record Probe(string Kind, string Message, string Value, JsonObject Intl)
    {
        internal string Name => Message + " " + Value;
    }

    private static Probe[] Probes()
    {
        var probes = new List<Probe>();
        foreach (string code in new[] { "USD", "EUR", "JPY", "CHF", "INR", "BHD" })
            foreach (string display in new[] { "symbol", "narrowSymbol", "code", "name" })
                foreach (string value in new[] { "1234567.891", "-1234.5", "1" })
                    probes.Add(new("currency", "{$n :currency currency=" + code + " currencyDisplay=" + display + "}", value,
                        new JsonObject { ["style"] = "currency", ["currency"] = code, ["currencyDisplay"] = display }));
        foreach (string value in new[] { "-1234.5", "42" })
            probes.Add(new("accounting", "{$n :currency currency=USD currencySign=accounting}", value,
                new JsonObject { ["style"] = "currency", ["currency"] = "USD", ["currencySign"] = "accounting" }));
        foreach (string unit in new[] { "kilometer", "hour", "liter", "megabyte", "percent", "fahrenheit", "kilometer-per-hour", "megabyte-per-second" })
            foreach (string display in new[] { "short", "long", "narrow" })
                foreach (string value in new[] { "1", "2", "1234.5", "-3.25", "0.5" })
                    probes.Add(new(unit.Contains("-per-", StringComparison.Ordinal) ? "compound" : "unit", "{$n :unit unit=" + unit + " unitDisplay=" + display + "}", value,
                        new JsonObject { ["style"] = "unit", ["unit"] = unit, ["unitDisplay"] = display }));
        foreach (string display in new[] { "short", "long" })
            foreach (string value in new[] { "999", "1000", "1234", "1500", "9999", "999999", "1234567", "-45678", "123456789012", "0.123", "7000000000000000" })
                probes.Add(new("compact", "{$n :number runic:notation=compact runic:compactDisplay=" + display + "}", value,
                    new JsonObject { ["notation"] = "compact", ["compactDisplay"] = display }));
        return probes.ToArray();
    }

    private sealed record Divergence(string Locale, string Kind, string Reason);

    private static List<Divergence> Divergences()
    {
        var corpus = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "currency-unit-divergences.json")))!;
        Assert.Equal("48.2", corpus["cldrVersion"]!.GetValue<string>(), "Currency and unit divergence list CLDR pin");
        return corpus["divergences"]!.AsArray().SelectMany(item => item!["locales"]!.AsArray().Select(locale => new Divergence(locale!.GetValue<string>(),
            item["kind"]!.GetValue<string>(), item["reason"]!.GetValue<string>()))).ToList();
    }

    private static string[] Locales()
    {
        var subset = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "cldr", "runic-subset-48.2.json")))!;
        string[] available = subset["availableLocales"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
        string[] latin = available.Where(locale => !Rmf2NumberFormat.For(locale).IsLatin).Select(locale => locale + "-u-nu-latn").ToArray();
        return [.. available, .. latin];
    }

    private static string DotNet(Probe probe, string locale, Rmf2FormatData data)
    {
        decimal value = decimal.Parse(probe.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        string Option(string name, string fallback) => probe.Intl[name]?.GetValue<string>() ?? fallback;
        try
        {
            return probe.Kind switch
            {
                "currency" or "accounting" => Rmf2CldrFormats.Currency(value, Option("currency", ""), Option("currencyDisplay", "symbol"), Option("currencySign", "standard"), "auto", null, locale, data, dynamic: false),
                "unit" or "compound" => Rmf2CldrFormats.Unit(value, Option("unit", ""), Option("unitDisplay", "short"), "auto", 0, 3, locale, data, dynamic: false),
                _ => Rmf2CldrFormats.Compact(value, Option("compactDisplay", "short"), "auto", null, null, locale, data),
            };
        }
        catch (TranslationFormatException exception) { return "error: " + exception.Message; }
    }

    [Test, NotInParallel, DisplayName("currencies, units and compact numbers agree in .NET and ESM for every CLDR locale, and with Intl except for listed differences")]
    public void EsmAgreesWithDotNet()
    {
        string[] locales = Locales();
        Probe[] probes = Probes();
        // A one-locale catalog that uses every probe, so its format data holds the probes' currencies, units and compact styles.
        string source = string.Concat(probes.Select((probe, index) => "p" + index + " =\n  {{" + probe.Message + "}}\n"));
        Rmf2ProjectV5 project = Rmf2CurrencyUnitConformanceTests.Project("formats", "en", source, [], []);
        Rmf2MessageV5[] messages = project.Locales.Single().DirectResources.Select(resource => resource.Message).ToArray();
        string[] languages = locales.Select(locale => locale.Split('-')[0]).Distinct(StringComparer.Ordinal).ToArray();
        string all = project.FormatData(languages, messages)!;
        Rmf2FormatData data = Rmf2FormatData.Parse(Encoding.UTF8.GetBytes(all));
        var dotnet = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string locale in locales) dotnet[locale] = probes.Select(probe => DotNet(probe, locale, data)).ToArray();

        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            string runtime = File.ReadAllText(Path.Combine(directory, "formats.esm-v5", "runtime.js"));
            string own = TranslationCapabilityRegistry.EsmLocaleSource(["en"], project.BidiIsolation, project.FormatData(["en"], messages));
            Assert.True(runtime.Contains(own, StringComparison.Ordinal), "The harness cannot find the catalog's locale data.");
            File.WriteAllText(Path.Combine(directory, "formats.esm-v5", "harness.js"), runtime.Replace(own, TranslationCapabilityRegistry.EsmLocaleSource(languages, project.BidiIsolation, all), StringComparison.Ordinal) +
                "\nexport { formatCurrency as comparisonCurrency, formatUnit as comparisonUnit, formatCompact as comparisonCompact, parseDecimal as comparisonDecimal };\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "input.json"), new JsonObject
            {
                ["locales"] = new JsonArray(locales.Select(value => (JsonNode)value).ToArray()),
                ["probes"] = new JsonArray(probes.Select(probe => (JsonNode)new JsonObject { ["kind"] = probe.Kind, ["value"] = probe.Value, ["intl"] = probe.Intl.DeepClone() }).ToArray()),
            }.ToJsonString(), new UTF8Encoding(false));
            string script = Path.Combine(directory, "comparison.mjs");
            File.WriteAllText(script, """
                import { readFile, writeFile } from "node:fs/promises";
                import * as runtime from "./formats.esm-v5/harness.js";
                const input = JSON.parse(await readFile(new URL("./input.json", import.meta.url), "utf8"));
                const output = { icu: process.versions.icu ?? "", cldr: process.versions.cldr ?? "", locales: {} };
                const maximal = tag => { const value = new Intl.Locale(tag.replace(/-u-.*$/, "")).maximize(); return `${value.language}-${value.script}-${value.region}`; };
                for (const locale of input.locales) {
                  let covered = false;
                  try { covered = maximal(new Intl.NumberFormat(locale).resolvedOptions().locale) === maximal(locale); } catch { covered = false; }
                  const pinned = [], intl = [];
                  for (const probe of input.probes) {
                    const value = runtime.comparisonDecimal(probe.value), options = probe.intl;
                    let text;
                    try {
                      text = probe.kind === "currency" || probe.kind === "accounting" ? runtime.comparisonCurrency(value, { currency: options.currency, currencyDisplay: options.currencyDisplay, currencySign: options.currencySign }, locale, false)
                        : probe.kind === "compact" ? runtime.comparisonCompact(value, options.compactDisplay, {}, locale)
                        : runtime.comparisonUnit(value, { unit: options.unit, unitDisplay: options.unitDisplay, maximumFractionDigits: 3n }, locale, false);
                    } catch (error) { text = `error: ${error.message}`; }
                    pinned.push(text);
                    let expected = null;
                    if (covered) { try { expected = new Intl.NumberFormat(locale, options).format(probe.value); } catch { expected = null; } }
                    intl.push(expected);
                  }
                  output.locales[locale] = { pinned, intl, covered };
                }
                await writeFile(new URL("./output.json", import.meta.url), JSON.stringify(output));
                """, new UTF8Encoding(false));

            var failures = new List<string>();
            var report = new List<string>();
            List<Divergence> divergences = Divergences();
            var seen = new HashSet<Divergence>();
            bool anyStrict = false;
            var unlisted = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (string host in Rmf2DatePercentConformanceTests.EsmHosts())
            {
                Rmf2EsmV5Tests.Run(host, [script], directory);
                JsonNode output = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "output.json")))!;
                string engineIcu = output["icu"]!.GetValue<string>(), engineCldr = output["cldr"]!.GetValue<string>();
                int? esmIcu = int.TryParse(engineIcu.Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ? major : null;
                bool strictIntl = engineCldr.Length > 0 ? engineCldr.StartsWith(PinnedCldrMajor + ".", StringComparison.Ordinal) : esmIcu == PinnedIcuMajor && OperatingSystem.IsLinux();
                anyStrict |= strictIntl;
                int compared = 0;
                foreach (string locale in locales)
                {
                    JsonNode esm = output["locales"]![locale]!;
                    for (int index = 0; index < probes.Length; index++)
                    {
                        string left = dotnet[locale][index], right = esm["pinned"]![index]!.GetValue<string>();
                        if (left != right) failures.Add(host + " " + locale + " " + probes[index].Name + ": .NET " + JsonSerializer.Serialize(left) + ", ESM " + JsonSerializer.Serialize(right));
                        if (left.StartsWith("error: ", StringComparison.Ordinal)) failures.Add(locale + " " + probes[index].Name + ": " + left);
                        if (esm["intl"]![index]?.GetValue<string>() is not { } intl) continue;
                        compared++;
                        if (intl == right) continue;
                        Divergence? listed = divergences.FirstOrDefault(item => item.Locale == locale && item.Kind == probes[index].Kind);
                        if (listed is not null) { seen.Add(listed); continue; }
                        if (!strictIntl) { report.Add(host + " " + locale + " " + probes[index].Name + ": Intl " + JsonSerializer.Serialize(intl) + ", pinned " + JsonSerializer.Serialize(right)); continue; }
                        failures.Add(host + " " + locale + " " + probes[index].Name + ": Intl.NumberFormat " + JsonSerializer.Serialize(intl) + ", pinned " + JsonSerializer.Serialize(right));
                        if (!unlisted.TryGetValue(probes[index].Kind, out var set)) unlisted[probes[index].Kind] = set = new(StringComparer.Ordinal);
                        set.Add(locale);
                    }
                }
                if (strictIntl && compared < 100_000) failures.Add(host + ": only " + compared + " outputs were compared with Intl.");
                Console.WriteLine(host + ": engine ICU " + engineIcu + (strictIntl ? "; compared with Intl (" + compared + " outputs)" : "; Intl differences reported only"));
            }
            if (anyStrict)
                foreach (Divergence item in divergences.Where(item => !seen.Contains(item)))
                    failures.Add("The listed divergence " + item.Locale + " " + item.Kind + " no longer occurs.");
            if (unlisted.Count > 0) Console.WriteLine("Unlisted divergences by kind:\n" + string.Join("\n", unlisted.Select(item => item.Key + ": " + string.Join(' ', item.Value))));
            if (TestContext.Parameters.TryGetValue("comparison-report", out var reportPath) && reportPath.Count > 0)
                File.WriteAllLines(reportPath[^1], failures.Select(item => "fail " + item).Concat(report.Select(item => "report " + item)), new UTF8Encoding(false));
            if (report.Count > 0) Console.WriteLine(report.Count + " differences outside the strict comparison:\n" + string.Join("\n", report.Take(100)));
            Assert.True(failures.Count == 0, failures.Count + " differences:\n" + string.Join("\n", failures.Take(200)));
        }
        finally { Directory.Delete(directory, true); }
    }
}
