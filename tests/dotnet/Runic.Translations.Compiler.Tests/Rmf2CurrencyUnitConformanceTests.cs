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

using Runic.Translations.Generated;

namespace Runic.Translations.Compiler.Tests;

// specs/translations/corpus/rmf2-currency-unit-conformance.json is the shared .NET/ESM oracle for :currency, :unit and
// compact notation (D055.4). Each content locale's cases compile into one project whose runic.json declares the run-time
// currencies and units; .NET formats the linked message (with the compiler's format data) and the loaded artifact-v5
// pack, ESM the generated module and the dynamically decoded artifact, all for the case's requested locale. The data is
// pinned CLDR, so every route must produce the expected string exactly.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2CurrencyUnitConformanceTests
{
    private sealed record Vector(string Id, string Locale, string Requested, string Message, JsonObject Arguments, string Expected);

    private static (List<Vector> Vectors, string[] Currencies, string[] Units) Corpus()
    {
        var corpus = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-currency-unit-conformance.json")))!;
        Assert.Equal("48.2", corpus["cldrVersion"]!.GetValue<string>(), "Currency and unit corpus CLDR pin");
        var vectors = corpus["cases"]!.AsArray().Select(item => new Vector(
            item!["id"]!.GetValue<string>(), item["locale"]!.GetValue<string>(), item["requested"]?.GetValue<string>() ?? item["locale"]!.GetValue<string>(),
            item["message"]!.GetValue<string>(), item["arguments"]!.AsObject(), item["expected"]!.GetValue<string>())).ToList();
        Assert.True(vectors.Count > 500, "The currency and unit vectors are missing.");
        Assert.Equal(vectors.Count, vectors.Select(vector => vector.Id).Distinct(StringComparer.Ordinal).Count(), "Duplicate currency and unit vector ids.");
        foreach (string kind in new[] { ":currency", ":unit", "runic:notation=compact", "currencyDisplay=name", "currencySign=accounting", "-per-", "unitDisplay=long", "runic:compactDisplay=long" })
            Assert.True(vectors.Count(vector => vector.Message.Contains(kind, StringComparison.Ordinal)) >= 20, "Too few vectors cover " + kind + ".");
        string[] Strings(string name) => corpus["formats"]![name]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
        return (vectors, Strings("currencies"), Strings("units"));
    }

    internal static Rmf2ProjectV5 Project(string id, string locale, string source, IReadOnlyList<string> currencies, IReadOnlyList<string> units)
    {
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, catalog = id, code = new { @namespace = "Example", className = "Text" }, baseLocale = locale, locales = new[] { locale },
            formats = new { currencies, units },
            // Isolation has its own corpus (rmf2-bidi-conformance.json), as for the other formatting corpora.
            runtime = new { bidiIsolation = "off" },
        });
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(
            new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(json)),
            [new TranslationSource("translations/" + locale + ".rmf2", Encoding.UTF8.GetBytes(source))]);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Id + " " + item.Message)));
        return result.Project!;
    }

    private static Rmf2ProjectV5 Project(string locale, IReadOnlyList<Vector> vectors, string[] currencies, string[] units)
    {
        var source = new StringBuilder();
        for (int index = 0; index < vectors.Count; index++)
        {
            source.Append("vector").Append(index).Append(" =\n");
            foreach (string line in vectors[index].Message.Split('\n')) source.Append("  ").Append(line).Append('\n');
        }
        return Project("formats", locale, source.ToString(), currencies, units);
    }

    private static TextArgument[] Arguments(JsonObject values) => values.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
    {
        string value = item.Value!["value"]!.GetValue<string>();
        TextArgument carrier = item.Value["type"]!.GetValue<string>() switch
        {
            "int64" => new TextArgument("_", long.Parse(value, CultureInfo.InvariantCulture)),
            "decimal" => new TextArgument("_", decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)),
            "string" => new TextArgument("_", value),
            string type => throw new InvalidOperationException("Unsupported currency and unit corpus carrier " + type),
        };
        return TextArgument.CreateRmf2(item.Key, carrier);
    }).ToArray();

    [Test, DisplayName("RMF2 currencies, units and compact numbers format exactly in linked .NET messages and loaded packs")]
    public void DotNet()
    {
        var (all, currencies, units) = Corpus();
        var failures = new List<string>();
        foreach (var group in all.GroupBy(vector => vector.Locale, StringComparer.Ordinal))
        {
            Vector[] vectors = group.ToArray();
            Rmf2ProjectV5 project = Project(group.Key, vectors, currencies, units);
            Rmf2LocaleV5 locale = project.Locales.Single();
            Rmf2FormatData data = Rmf2FormatData.Parse(Encoding.UTF8.GetBytes(project.FormatData([group.Key], locale.ResolvedResources.Select(item => item.Message))!));
            TranslationGeneratedOutput artifact = Rmf2LocaleArtifactV5.Render(project, group.Key);
            VerifiedExternalTranslationPack pack = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(artifact.GetUtf8Bytes()),
                Rmf2V1CorpusTests.PackContract(project, group.Key)).AsTask().GetAwaiter().GetResult();
            for (int index = 0; index < vectors.Length; index++)
            {
                string key = "vector" + index;
                Rmf2TranslationV5 linked = locale.ResolvedResources.Single(item => item.Key == key);
                Rmf2MessageContractV5 contract = project.CanonicalMessages.Single(item => item.Key == key);
                TextArgument[] arguments = Arguments(vectors[index].Arguments);
                CompiledRmf2Message direct = Rmf2V1CorpusTests.Lower(linked.Message with { Inputs = contract.Inputs }, linked.ContentLocale);
                direct.FormatData = data;
                Check(vectors[index], () => direct.Format(arguments, vectors[index].Requested), "linked");
                CompiledRmf2Message loaded = pack.Messages.Single(item => item.Key.Name == key).Message!.Rmf2V5!;
                Check(vectors[index], () => loaded.Format(arguments, vectors[index].Requested), "pack");
            }
        }
        Assert.True(failures.Count == 0, failures.Count + " failures:\n" + string.Join("\n", failures.Take(200)));

        void Check(Vector vector, Func<string> format, string route)
        {
            string actual;
            try { actual = format(); }
            catch (TranslationFormatException exception) { actual = exception.GetType().Name + ": " + exception.Message + (exception.InnerException is { } inner ? " (" + inner.Message + ")" : ""); }
            if (!string.Equals(vector.Expected, actual, StringComparison.Ordinal))
                failures.Add(vector.Id + "/" + route + ": expected " + JsonSerializer.Serialize(vector.Expected) + ", got " + JsonSerializer.Serialize(actual));
        }
    }

    [Test, NotInParallel, DisplayName("RMF2 currencies, units and compact numbers format exactly in generated and dynamic ESM")]
    public void Esm()
    {
        var (all, currencies, units) = Corpus();
        var failures = new List<string>();
        foreach (var group in all.GroupBy(vector => vector.Locale, StringComparer.Ordinal))
        {
            Vector[] vectors = group.ToArray();
            Rmf2ProjectV5 project = Project(group.Key, vectors, currencies, units);
            string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
            try
            {
                File.WriteAllBytes(Path.Combine(directory, "artifact.json"), Rmf2LocaleArtifactV5.Render(project, group.Key).GetUtf8Bytes());
                var cases = new JsonArray(vectors.Select((vector, index) => (JsonNode)new JsonObject
                {
                    ["id"] = vector.Id, ["key"] = "vector" + index, ["requested"] = vector.Requested, ["arguments"] = vector.Arguments.DeepClone(), ["expected"] = vector.Expected,
                }).ToArray());
                string script = Path.Combine(directory, "format-vectors.mjs");
                File.WriteAllText(script, "import { readFile } from \"node:fs/promises\";\nimport { m } from \"./formats.esm-v5/messages.js\";\n" +
                    "import { decodeLocaleArtifact, formatDynamicMessage } from \"./formats.esm-v5/dynamic.js\";\nconst cases = " + cases.ToJsonString() + ";\n" + """
                    const artifact = decodeLocaleArtifact(JSON.parse(await readFile(new URL("./artifact.json", import.meta.url), "utf8")));
                    if (!artifact.ok) throw new Error(`artifact rejected: ${artifact.reason}`);
                    const failures = [];
                    for (const item of cases) {
                      const inputs = Object.fromEntries(Object.entries(item.arguments).map(([name, value]) => [name, value.type === "int64" ? BigInt(value.value) : value.value]));
                      for (const [route, format] of [["generated", () => m[item.key](inputs, { locale: item.requested })], ["dynamic", () => formatDynamicMessage(artifact.value, item.key, inputs, { locale: item.requested })]]) {
                        let actual; try { actual = format(); } catch (error) { actual = `${error.name}: ${error.message}`; }
                        if (actual !== item.expected) failures.push(`${item.id}/${route}: expected ${JSON.stringify(item.expected)}, got ${JSON.stringify(actual)}`);
                      }
                    }
                    if (failures.length) throw new Error(`${failures.length} failures:\n` + failures.slice(0, 200).join("\n"));
                    """, new UTF8Encoding(false));
                foreach (string host in Rmf2DatePercentConformanceTests.EsmHosts())
                    try { Rmf2EsmV5Tests.Run(host, [script], directory); }
                    catch (Exception exception) { failures.Add(group.Key + " (" + host + "): " + exception.Message); }
            }
            finally { Directory.Delete(directory, true); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
