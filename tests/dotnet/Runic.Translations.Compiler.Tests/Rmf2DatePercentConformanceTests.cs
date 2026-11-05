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

// specs/translations/corpus/rmf2-date-percent-conformance.json is the shared .NET/ESM oracle for localized
// :date/:time/:datetime styles, time zones, percent patterns and the formatting locale (a requested de-CH over de
// content). Each content locale's cases compile into one project; .NET formats the linked message and the loaded
// artifact-v5 pack, ESM the generated module and the dynamically decoded artifact, all for the case's requested
// locale. Every route must produce the expected string, except for the documented platform-data tolerance below.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2DatePercentConformanceTests
{
    // Alternates are documented platform ICU variants of the expected string (see the corpus README).
    private sealed record Vector(string Id, string Locale, string Requested, string Message, JsonObject Arguments, string Expected, string[] Alternates);

    private static List<Vector> Vectors()
    {
        var corpus = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-date-percent-conformance.json")))!;
        var vectors = corpus["cases"]!.AsArray().Select(item => new Vector(
            item!["id"]!.GetValue<string>(), item["locale"]!.GetValue<string>(), item["requested"]?.GetValue<string>() ?? item["locale"]!.GetValue<string>(),
            item["message"]!.GetValue<string>(), item["arguments"]!.AsObject(), item["expected"]!.GetValue<string>(),
            item["alternates"]?.AsArray().Select(value => value!.GetValue<string>()).ToArray() ?? [])).ToList();
        Assert.True(vectors.Count > 100, "The date and percent vectors are missing.");
        // An alternate is the output of a named platform ICU; the note says which, so the corpus stays strict otherwise.
        foreach (JsonNode? item in corpus["cases"]!.AsArray())
            if (item!["alternates"] is not null)
                Assert.True(item["note"]?.GetValue<string>() is { Length: > 0 }, "The alternates of '" + item["id"] + "' have no note naming the platform.");
        Assert.Equal(vectors.Count, vectors.Select(vector => vector.Id).Distinct(StringComparer.Ordinal).Count(), "Duplicate date and percent vector ids.");
        // Every 0.8 locale family has date, time and percent vectors; the other CLDR locales use the same rules.
        foreach (string family in new[] { "cs", "da", "de", "en", "es", "fr", "it", "ja", "nb", "nl", "nn", "no", "sv" })
            foreach (string kind in new[] { ":date style=", ":time style=", ":datetime style=", "style=percent" })
                Assert.True(vectors.Any(vector => vector.Locale == family && vector.Message.Contains(kind, StringComparison.Ordinal)), "No vector covers " + kind + " in '" + family + "'.");
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
        return Rmf2EsmV5Tests.CompileProject("dates", locale, [locale], false, (locale, source.ToString()));
    }

    private static TextArgument[] Arguments(JsonObject values) => values.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
    {
        string value = item.Value!["value"]!.GetValue<string>();
        TextArgument carrier = item.Value["type"]!.GetValue<string>() switch
        {
            "int64" => new TextArgument("_", long.Parse(value, CultureInfo.InvariantCulture)),
            "decimal" => new TextArgument("_", decimal.Parse(value, CultureInfo.InvariantCulture)),
            "string" => new TextArgument("_", value),
            "date" => new TextArgument("_", DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
            "time" => new TextArgument("_", TimeOnly.ParseExact(value, "HH:mm:ss", CultureInfo.InvariantCulture)),
            "datetime" => new TextArgument("_", DateTimeOffset.ParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)),
            string type => throw new InvalidOperationException("Unsupported date and percent corpus carrier " + type),
        };
        return TextArgument.CreateRmf2(item.Key, carrier);
    }).ToArray();

    [Test, DisplayName("RMF2 localized dates, times, time zones and percentages format identically in linked .NET messages and loaded packs")]
    public void DotNet()
    {
        var failures = new List<string>();
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
                Check(vectors[index], direct.Format(arguments, vectors[index].Requested), "linked");
                CompiledRmf2Message loaded = pack.Messages.Single(item => item.Key.Name == key).Message!.Rmf2V5!;
                Check(vectors[index], loaded.Format(arguments, vectors[index].Requested), "pack");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));

        void Check(Vector vector, string actual, string route)
        {
            if (!Matches(vector.Expected, actual) && !vector.Alternates.Any(alternate => Matches(alternate, actual)))
                failures.Add(vector.Id + "/" + route + ": expected " + JsonSerializer.Serialize(vector.Expected) + ", got " + JsonSerializer.Serialize(actual) + Patterns(vector));
        }

        // The platform's date pattern lists, from which the .NET medium and long patterns are derived.
        static string Patterns(Vector vector)
        {
            if (!vector.Message.Contains(":date", StringComparison.Ordinal)) return "";
            DateTimeFormatInfo format = CultureInfo.GetCultureInfo(vector.Requested).DateTimeFormat;
            return " (" + vector.Requested + " d: " + JsonSerializer.Serialize(format.GetAllDateTimePatterns('d')) + ", D: " + JsonSerializer.Serialize(format.GetAllDateTimePatterns('D')) + ")";
        }
    }

    [Test, NotInParallel, DisplayName("RMF2 localized dates, times, time zones and percentages format identically in generated and dynamic ESM")]
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
                    ["id"] = vector.Id, ["key"] = "vector" + index, ["requested"] = vector.Requested, ["arguments"] = vector.Arguments.DeepClone(), ["expected"] = vector.Expected, ["alternates"] = new JsonArray(vector.Alternates.Select(value => (JsonNode)value).ToArray()),
                }).ToArray());
                string script = Path.Combine(directory, "date-vectors.mjs");
                File.WriteAllText(script, "import { readFile } from \"node:fs/promises\";\nimport { m } from \"./dates.esm-v5/messages.js\";\n" +
                    "import { decodeLocaleArtifact, formatDynamicMessage } from \"./dates.esm-v5/dynamic.js\";\nconst cases = " + cases.ToJsonString() + ";\n" + """
                    const artifact = decodeLocaleArtifact(JSON.parse(await readFile(new URL("./artifact.json", import.meta.url), "utf8")));
                    if (!artifact.ok) throw new Error(`artifact rejected: ${artifact.reason}`);
                    // The documented tolerance: platform ICU versions may write U+0020, U+00A0 or U+202F for the same space and
                    // U+0027 or U+2019 for the same apostrophe (de-CH grouping).
                    const tolerant = value => value.replace(/[\u00A0\u202F]/g, " ").replace(/\u2019/g, "'");
                    const same = (left, right) => left === right || tolerant(left) === tolerant(right);
                    const accepted = (actual, item) => [item.expected, ...item.alternates].some(value => same(actual, value));
                    const failures = [];
                    for (const item of cases) {
                      const inputs = Object.fromEntries(Object.entries(item.arguments).map(([name, value]) => [name, value.type === "int64" ? BigInt(value.value) : value.value]));
                      const generated = m[item.key](inputs, { locale: item.requested });
                      if (!accepted(generated, item)) failures.push(`${item.id}: expected ${JSON.stringify(item.expected)}, generated ${JSON.stringify(generated)}`);
                      const dynamic = formatDynamicMessage(artifact.value, item.key, inputs, { locale: item.requested });
                      if (!accepted(dynamic, item)) failures.push(`${item.id}: expected ${JSON.stringify(item.expected)}, dynamic ${JSON.stringify(dynamic)}`);
                    }
                    if (failures.length) throw new Error(failures.join("\n"));
                    """, new UTF8Encoding(false));
                foreach (string host in EsmHosts()) Rmf2EsmV5Tests.Run(host, [script], directory);
            }
            finally { Directory.Delete(directory, true); }
        }
    }

    // --test-parameter esm-host=bun (default), node or all: the JavaScript engines the ESM route runs in. CI runs Bun
    // (JavaScriptCore) and Node (V8) on Linux, Windows and macOS.
    internal static string[] EsmHosts()
    {
        string host = TestContext.Parameters.TryGetValue("esm-host", out var values) && values.Count > 0 ? values[^1] : "bun";
        return host switch { "bun" or "node" => [host], "all" => ["bun", "node"], _ => throw new InvalidOperationException("esm-host must be bun, node or all.") };
    }

    // The documented tolerance: platform ICU versions may write U+0020, U+00A0 or U+202F for the same space, and U+0027
    // or U+2019 for the same apostrophe (the de-CH group separator). Everything else must match exactly.
    internal static bool Matches(string expected, string actual) =>
        string.Equals(expected, actual, StringComparison.Ordinal) || string.Equals(Tolerant(expected), Tolerant(actual), StringComparison.Ordinal);
    private static string Tolerant(string value) => value.Replace('\u00A0', ' ').Replace('\u202F', ' ').Replace('\u2019', '\'');
}
