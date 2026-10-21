using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

using Runic.Translations.Generated;

namespace Runic.Translations.Compiler.Tests;

// specs/translations/corpus/rmf2-grouping-conformance.json is the shared .NET/ESM oracle
// for useGrouping on :integer and :number. Each locale's cases compile into one project;
// .NET formats the linked message and the loaded artifact-v5 pack, ESM the generated
// module and the dynamically decoded artifact. Every route must produce the expected
// string exactly. The expectations are Intl.NumberFormat's output for the same option.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2GroupingConformanceTests
{
    private sealed record Vector(string Id, string Locale, string Message, JsonObject Arguments, string Expected);

    private static List<Vector> Vectors()
    {
        var corpus = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-grouping-conformance.json")))!;
        Assert.Equal("48.2", corpus["cldrVersion"]!.GetValue<string>(), "Grouping corpus CLDR pin");
        var vectors = corpus["cases"]!.AsArray().Select(item => new Vector(
            item!["id"]!.GetValue<string>(), item["locale"]!.GetValue<string>(), item["message"]!.GetValue<string>(),
            item["arguments"]!.AsObject(), item["expected"]!.GetValue<string>())).ToList();
        Assert.True(vectors.Count > 100, "The grouping vectors are missing.");
        Assert.Equal(vectors.Count, vectors.Select(vector => vector.Id).Distinct(StringComparer.Ordinal).Count(), "Duplicate grouping vector ids.");
        foreach (string family in TranslationCapabilityRegistry.CardinalLanguages)
            Assert.True(vectors.Any(vector => vector.Locale.Split('-')[0] == family), "No grouping vector covers locale family '" + family + "'.");
        foreach (string grouping in new[] { "always", "never", "min2", "auto" })
            Assert.True(vectors.Any(vector => vector.Message.Contains("useGrouping=" + grouping, StringComparison.Ordinal)), "No grouping vector uses " + grouping + ".");
        return vectors;
    }

    private static Rmf2ProjectV5 Project(string locale, IReadOnlyList<Vector> vectors)
    {
        var source = new StringBuilder();
        for (int index = 0; index < vectors.Count; index++)
        {
            source.Append("vector").Append(index).Append(" =\n");
            foreach (string line in vectors[index].Message.Split('\n')) source.Append("  ").Append(line).Append('\n');
        }
        return Rmf2EsmV5Tests.CompileProject("grouping", locale, [locale], false, (locale, source.ToString()));
    }

    private static TextArgument[] Arguments(JsonObject values) => values.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
    {
        JsonNode value = item.Value!["value"]!;
        TextArgument carrier = item.Value["type"]!.GetValue<string>() switch
        {
            "int64" => new TextArgument("_", long.Parse(value.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)),
            "decimal" => new TextArgument("_", decimal.Parse(value.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)),
            "string" => new TextArgument("_", value.GetValue<string>()),
            string type => throw new InvalidOperationException("Unsupported grouping corpus carrier " + type),
        };
        return TextArgument.CreateRmf2(item.Key, carrier);
    }).ToArray();

    [Test, DisplayName("RMF2 useGrouping vectors format identically in linked .NET messages and loaded packs")]
    public void DotNet()
    {
        foreach (var group in Vectors().GroupBy(vector => vector.Locale, StringComparer.Ordinal))
        {
            Vector[] vectors = group.ToArray();
            Rmf2ProjectV5 project = Project(group.Key, vectors);
            Rmf2LocaleV5 locale = project.Locales.Single();
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
                Assert.Equal(vectors[index].Expected, direct.Format(arguments, group.Key), vectors[index].Id + "/linked");
                CompiledRmf2Message loaded = pack.Messages.Single(item => item.Key.Name == key).Message!.Rmf2V5!;
                Assert.Equal(vectors[index].Expected, loaded.Format(arguments, group.Key), vectors[index].Id + "/pack");
            }
        }
    }

    [Test, NotInParallel, DisplayName("RMF2 useGrouping vectors format identically in generated and dynamic ESM")]
    public void Esm()
    {
        foreach (var group in Vectors().GroupBy(vector => vector.Locale, StringComparer.Ordinal))
        {
            Vector[] vectors = group.ToArray();
            Rmf2ProjectV5 project = Project(group.Key, vectors);
            string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
            try
            {
                File.WriteAllBytes(Path.Combine(directory, "artifact.json"), Rmf2LocaleArtifactV5.Render(project, group.Key).GetUtf8Bytes());
                var cases = new JsonArray(vectors.Select((vector, index) => (JsonNode)new JsonObject
                {
                    ["id"] = vector.Id, ["key"] = "vector" + index, ["arguments"] = vector.Arguments.DeepClone(), ["expected"] = vector.Expected,
                }).ToArray());
                string script = Path.Combine(directory, "grouping-vectors.mjs");
                File.WriteAllText(script, """
                    import { readFile } from "node:fs/promises";
                    import { m } from "./grouping.esm-v5/messages.js";
                    import { decodeLocaleArtifact, formatDynamicMessage } from "./grouping.esm-v5/dynamic.js";
                    const locale =
                    """ + JsonSerializer.Serialize(group.Key) + ";\nconst cases = " + cases.ToJsonString() + ";\n" + """
                    const artifact = decodeLocaleArtifact(JSON.parse(await readFile(new URL("./artifact.json", import.meta.url), "utf8")));
                    if (!artifact.ok) throw new Error(`artifact rejected: ${artifact.reason}`);
                    for (const item of cases) {
                      const inputs = Object.fromEntries(Object.entries(item.arguments).map(([name, value]) => [name, value.type === "int64" ? BigInt(value.value) : value.value]));
                      const generated = m[item.key](inputs, { locale });
                      if (generated !== item.expected) throw new Error(`${item.id}: expected ${JSON.stringify(item.expected)}, generated ${JSON.stringify(generated)}`);
                      const dynamic = formatDynamicMessage(artifact.value, item.key, inputs, { locale });
                      if (dynamic !== item.expected) throw new Error(`${item.id}: expected ${JSON.stringify(item.expected)}, dynamic ${JSON.stringify(dynamic)}`);
                    }
                    """, new UTF8Encoding(false));
                Rmf2EsmV5Tests.Run("bun", [script], directory);
            }
            finally { Directory.Delete(directory, true); }
        }
    }

    [Test, DisplayName("RMF2 useGrouping defaults to auto and the editor analysis accepts it on :integer and :number")]
    public void Analysis()
    {
        var integer = Rmf2FunctionRegistryV2.Find("integer")!.Options.Single(option => option.Name == "useGrouping");
        var number = Rmf2FunctionRegistryV2.Find("number")!.Options.Single(option => option.Name == "useGrouping");
        foreach (var option in new[] { integer, number })
        {
            Assert.Equal("auto", option.Default);
            Assert.Equal("auto,always,never,min2", string.Join(',', option.Values));
        }
        Rmf2ResourceDocument analyzed = Rmf2ResourceReader.Analyze(new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(
            "count = {{{$n :integer useGrouping=min2}}}\nhours = {$h :number useGrouping=always} hours\nplain = {$p :number style=percent useGrouping=never}\n")));
        Assert.Equal(0, analyzed.Diagnostics.Count, string.Join("; ", analyzed.Diagnostics.Select(item => item.Id + ": " + item.Message)));
        Rmf2ResourceDocument rejected = Rmf2ResourceReader.Analyze(new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes("hours = {$h :number useGrouping=true}\n")));
        Assert.True(rejected.Diagnostics.Any(item => item.Id == "RTR0065"), "useGrouping=true was accepted.");
    }
}
