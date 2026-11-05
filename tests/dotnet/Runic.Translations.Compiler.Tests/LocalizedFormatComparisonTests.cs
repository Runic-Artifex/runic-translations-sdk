using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// A generated comparison of localized numbers, percentages, dates and times in .NET and ESM for every CLDR locale (and
// -u-nu-latn for the locales whose default numbering system is not Latin), as CldrLocaleConformanceTests compares
// lists, relative time and negotiation. Numbers and percentages come from pinned CLDR data, so the runtimes must agree
// exactly everywhere, and with Intl.NumberFormat where the JavaScript engine's ICU carries the pinned CLDR version.
// Dates and times come from platform data, so they are compared only when .NET and the JavaScript engine load the same
// ICU major version (the locked development shell); every difference must then be within the corpus tolerance or be
// listed, with its reason, in specs/translations/corpus/localized-format-divergences.json.
[Category("rmf2-semantic-v5")]
internal sealed class LocalizedFormatComparisonTests
{
    // The pinned CLDR major version and the ICU major version that carries it.
    private const string PinnedCldrMajor = "48";
    private const int PinnedIcuMajor = 78;
    private static readonly (string Kind, string Value, string Grouping)[] NumberProbes =
    [
        ("number", "1234567.891", "auto"), ("number", "-1234.5", "auto"), ("number", "0.5", "auto"), ("number", "12345", "min2"),
        ("percent", "0.5", "auto"), ("percent", "-0.125", "auto"), ("percent", "12345.6789", "auto"),
        ("integer", "12345678", "auto"), ("integer", "-1234", "always"), ("integer", "1234", "min2"), ("integer", "-1234", "never"),
    ];
    private static readonly string[] Dates = ["2026-11-04", "2026-02-01"];
    private static readonly string[] DateStyles = ["short", "medium", "long", "full"];
    private static readonly string[] Times = ["13:05:09", "00:00:00", "06:15:00", "12:30:00", "19:45:00"];
    private static readonly string[] TimeStyles = ["short", "medium"];

    // Engines lists the JavaScript engines that show the divergence; without it, every engine does.
    private sealed record Divergence(string Locale, string Probe, string DotNet, string Esm, string Reason, string[]? Engines);

    private static string[] Locales()
    {
        var subset = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "cldr", "runic-subset-48.2.json")))!;
        string[] available = subset["availableLocales"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
        Assert.True(available.Length > 700, "The CLDR locale list is missing.");
        // The locales whose default numbering system is not Latin, again with -u-nu-latn.
        string[] latin = available.Where(locale => !Rmf2NumberFormat.For(locale).IsLatin).Select(locale => locale + "-u-nu-latn").ToArray();
        Assert.True(latin.Length > 40, "Too few locales with native digits: " + latin.Length);
        return [.. available, .. latin];
    }

    private static List<Divergence> Divergences()
    {
        var corpus = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "localized-format-divergences.json")))!;
        Assert.Equal("48.2", corpus["cldrVersion"]!.GetValue<string>(), "Divergence list CLDR pin");
        return corpus["divergences"]!.AsArray().Select(item => new Divergence(item!["locale"]!.GetValue<string>(), item["probe"]!.GetValue<string>(),
            item["dotnet"]!.GetValue<string>(), item["esm"]!.GetValue<string>(), item["reason"]!.GetValue<string>(),
            item["engines"]?.AsArray().Select(value => value!.GetValue<string>()).ToArray())).ToList();
    }

    private static IEnumerable<string> DateProbes() =>
        Dates.SelectMany(date => DateStyles.Select(style => "date " + date + " " + style))
            .Concat(Times.SelectMany(time => TimeStyles.Select(style => "time " + time + " " + style)));

    private static string DotNetNumber((string Kind, string Value, string Grouping) probe, string locale)
    {
        decimal value = decimal.Parse(probe.Value, CultureInfo.InvariantCulture);
        return probe.Kind switch
        {
            "integer" => Rmf2NumberFormats.Integer((long)value, probe.Grouping, locale),
            "percent" => Rmf2NumberFormats.Percent(VisibleDecimal.FromNumber(value, true, 0, 4), probe.Grouping, locale),
            _ => Rmf2NumberFormats.Decimal(VisibleDecimal.FromNumber(value, false, 0, 6), probe.Grouping, locale),
        };
    }

    private static string DotNetDate(string probe, string locale)
    {
        string[] parts = probe.Split(' ');
        return parts[0] == "date"
            ? Rmf2LocalizedFormats.Date(DateOnly.ParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture), parts[2], locale)
            : Rmf2LocalizedFormats.Time(TimeOnly.ParseExact(parts[1], "HH:mm:ss", CultureInfo.InvariantCulture), parts[2], locale);
    }

    [Test, NotInParallel, DisplayName("localized numbers, percentages, dates and times agree in .NET and ESM for every CLDR locale")]
    public void EsmAgreesWithDotNet()
    {
        string[] locales = Locales();
        string[] dateProbes = DateProbes().ToArray();
        var dotnet = new JsonObject();
        foreach (string locale in locales)
            dotnet[locale] = new JsonObject
            {
                ["numbers"] = new JsonArray(NumberProbes.Select(probe => (JsonNode)DotNetNumber(probe, locale)).ToArray()),
                ["dates"] = new JsonArray(dateProbes.Select(probe => (JsonNode)DotNetDate(probe, locale)).ToArray()),
            };

        Rmf2ProjectV5 project = Rmf2EsmV5Tests.CompileProject("formats", "en", ["en"], false, ("en", "x = {{x}}\n"));
        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            string runtime = File.ReadAllText(Path.Combine(directory, "formats.esm-v5", "runtime.js"));
            string ownData = TranslationCapabilityRegistry.EsmLocaleSource(["en"]);
            string all = TranslationCapabilityRegistry.EsmLocaleSource(locales.Select(locale => locale.Split('-')[0]).Distinct(StringComparer.Ordinal));
            File.WriteAllText(Path.Combine(directory, "formats.esm-v5", "harness.js"), runtime.Replace(ownData, all, StringComparison.Ordinal) +
                "\nexport { formatDecimal as comparisonDecimal, formatInteger as comparisonInteger, formatTemporal as comparisonTemporal };\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "input.json"), new JsonObject
            {
                ["locales"] = new JsonArray(locales.Select(value => (JsonNode)value).ToArray()),
                ["numbers"] = new JsonArray(NumberProbes.Select(probe => (JsonNode)new JsonArray(probe.Kind, probe.Value, probe.Grouping)).ToArray()),
                ["dates"] = new JsonArray(dateProbes.Select(value => (JsonNode)value).ToArray()),
            }.ToJsonString(), new UTF8Encoding(false));
            string script = Path.Combine(directory, "comparison.mjs");
            File.WriteAllText(script, """
                import { readFile, writeFile } from "node:fs/promises";
                import * as runtime from "./formats.esm-v5/harness.js";
                const input = JSON.parse(await readFile(new URL("./input.json", import.meta.url), "utf8"));
                const output = { icu: process.versions.icu ?? "", cldr: process.versions.cldr ?? "", locales: {} };
                for (const locale of input.locales) {
                  const numbers = [], intl = [];
                  for (const [kind, value, grouping] of input.numbers) {
                    numbers.push(kind === "integer" ? runtime.comparisonInteger(BigInt(value), locale, grouping)
                      : runtime.comparisonDecimal(runtime.decimal(value), locale, kind === "percent" ? { style: "percent", useGrouping: grouping } : { useGrouping: grouping }));
                    // Intl.NumberFormat with the same options; useGrouping=never is the invariant integer by design.
                    let expected = null;
                    try {
                      if (grouping !== "never") expected = new Intl.NumberFormat(locale, kind === "percent" ? { style: "percent", maximumFractionDigits: 4, useGrouping: grouping }
                        : { maximumFractionDigits: kind === "integer" ? 0 : 6, useGrouping: grouping }).format(kind === "integer" ? BigInt(value) : Number(value));
                    } catch { expected = null; }
                    intl.push(expected);
                  }
                  const dates = input.dates.map(probe => {
                    const [type, value, style] = probe.split(" ");
                    return runtime.comparisonTemporal(value, type, style, locale, "utc");
                  });
                  // Whether the engine has data for the locale: Intl resolves it to the same maximized language, script and region.
                  const maximal = tag => { const value = new Intl.Locale(tag.replace(/-u-.*$/, "")).maximize(); return `${value.language}-${value.script}-${value.region}`; };
                  let covered = false;
                  try { covered = maximal(new Intl.DateTimeFormat(locale).resolvedOptions().locale) === maximal(locale); } catch { covered = false; }
                  output.locales[locale] = { numbers, intl: covered ? intl : intl.map(() => null), dates, covered };
                }
                await writeFile(new URL("./output.json", import.meta.url), JSON.stringify(output));
                """, new UTF8Encoding(false));

            var failures = new List<string>();
            var report = new List<string>();
            List<Divergence> divergences = Divergences();
            int? dotnetIcu = DotNetIcuMajor();
            // A listed divergence must still occur in at least one strictly compared engine.
            var seen = new HashSet<Divergence>();
            var strictHosts = new List<string>();
            foreach (string host in Rmf2DatePercentConformanceTests.EsmHosts())
            {
                Rmf2EsmV5Tests.Run(host, [script], directory);
                JsonNode output = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "output.json")))!;
                string engineIcu = output["icu"]!.GetValue<string>();
                int? esmIcu = int.TryParse(engineIcu.Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ? major : null;
                bool strictDates = dotnetIcu is not null && dotnetIcu == esmIcu;
                // Node reports its CLDR version; Bun only its ICU version, whose data is its own on Linux.
                string engineCldr = output["cldr"]!.GetValue<string>();
                bool strictIntl = engineCldr.Length > 0 ? engineCldr.StartsWith(PinnedCldrMajor + ".", StringComparison.Ordinal) : esmIcu == PinnedIcuMajor && OperatingSystem.IsLinux();
                foreach (string locale in locales)
                {
                    JsonNode esm = output["locales"]![locale]!;
                    for (int index = 0; index < NumberProbes.Length; index++)
                    {
                        string probe = string.Join(' ', NumberProbes[index].Kind, NumberProbes[index].Value, NumberProbes[index].Grouping);
                        string left = dotnet[locale]!["numbers"]![index]!.GetValue<string>(), right = esm["numbers"]![index]!.GetValue<string>();
                        if (left != right) failures.Add(host + " " + locale + " " + probe + ": .NET " + JsonSerializer.Serialize(left) + ", ESM " + JsonSerializer.Serialize(right));
                        // The pinned data equals ICU's where the engine's ICU carries the pinned CLDR version.
                        if (esm["intl"]![index]?.GetValue<string>() is { } intl && intl != right)
                            (strictIntl ? failures : report).Add(host + " " + locale + " " + probe + ": Intl.NumberFormat " + JsonSerializer.Serialize(intl) + ", pinned " + JsonSerializer.Serialize(right));
                    }
                    for (int index = 0; index < dateProbes.Length; index++)
                    {
                        string left = dotnet[locale]!["dates"]![index]!.GetValue<string>(), right = esm["dates"]![index]!.GetValue<string>();
                        if (Rmf2DatePercentConformanceTests.Matches(left, right)) continue;
                        Divergence? listed = divergences.FirstOrDefault(item => item.Locale == locale && item.Probe == dateProbes[index]);
                        if (listed is not null && listed.DotNet == left && listed.Esm == right) { seen.Add(listed); continue; }
                        // A locale the JavaScript engine has no data for formats with the data Intl falls back to.
                        bool covered = esm["covered"]!.GetValue<bool>();
                        (strictDates && covered ? failures : report).Add(host + " " + locale + " " + dateProbes[index] + ": .NET " + JsonSerializer.Serialize(left) + ", ESM " + JsonSerializer.Serialize(right));
                    }
                }
                if (strictDates) strictHosts.Add(host);
                Console.WriteLine(host + ": .NET ICU " + (dotnetIcu?.ToString(CultureInfo.InvariantCulture) ?? "unknown") + ", engine ICU " + engineIcu +
                    (strictDates ? "; dates compared strictly" : "; dates reported only") + (strictIntl ? "; numbers compared with Intl" : ""));
            }
            foreach (Divergence item in divergences.Where(item => !seen.Contains(item) && strictHosts.Any(host => item.Engines is null || item.Engines.Contains(host))))
                failures.Add("The listed divergence " + item.Locale + " " + item.Probe + " no longer occurs as listed.");
            // --test-parameter comparison-report=<file> writes every difference, for reviewing a CLDR or ICU update.
            if (TestContext.Parameters.TryGetValue("comparison-report", out var reportPath) && reportPath.Count > 0)
                File.WriteAllLines(reportPath[^1], failures.Select(item => "fail " + item).Concat(report.Select(item => "report " + item)), new UTF8Encoding(false));
            if (report.Count > 0) Console.WriteLine(report.Count + " differences outside the strict comparison:\n" + string.Join("\n", report.Take(200)));
            Assert.True(failures.Count == 0, failures.Count + " differences:\n" + string.Join("\n", failures.Take(200)));
        }
        finally { Directory.Delete(directory, true); }
    }

    // The ICU major version .NET loaded, where the library file names it (Linux libicuuc.so.NN); null elsewhere.
    private static int? DotNetIcuMajor()
    {
        _ = CultureInfo.GetCultureInfo("de").DateTimeFormat.LongDatePattern;
        foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
        {
            Match match = Regex.Match(Path.GetFileName(module.FileName ?? ""), @"^libicuuc\.so\.(\d+)");
            if (match.Success) return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        return null;
    }
}
