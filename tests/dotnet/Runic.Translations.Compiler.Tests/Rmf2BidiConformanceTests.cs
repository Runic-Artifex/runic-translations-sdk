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

// specs/translations/corpus/rmf2-bidi-conformance.json is the shared .NET/ESM oracle for placeholder bidi isolation
// (rmf2-execution-v2.md, "Bidirectional text"): MF2's default strategy with and without the catalog's bidiIsolation, u:dir
// on every function, locals and literals. Each content locale and isolation setting compiles into one project; .NET formats
// the linked message and the loaded artifact-v5 pack, ESM the generated module and the dynamically decoded artifact.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2BidiConformanceTests
{
    private sealed record Vector(string Id, string Locale, bool Isolation, string Message, JsonObject Arguments, string Expected);

    private static List<Vector> Vectors()
    {
        var corpus = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-bidi-conformance.json")))!;
        var vectors = corpus["cases"]!.AsArray().Select(item => new Vector(
            item!["id"]!.GetValue<string>(), item["locale"]!.GetValue<string>(), item["bidiIsolation"]!.GetValue<bool>(),
            item["message"]!.GetValue<string>(), item["arguments"]!.AsObject(), item["expected"]!.GetValue<string>())).ToList();
        Assert.True(vectors.Count >= 20, "The bidi vectors are missing.");
        Assert.Equal(vectors.Count, vectors.Select(vector => vector.Id).Distinct(StringComparer.Ordinal).Count(), "Duplicate bidi vector ids.");
        foreach (string isolate in new[] { "⁦", "⁧", "⁨" })
            Assert.True(vectors.Any(vector => vector.Expected.Contains(isolate, StringComparison.Ordinal)), "No vector covers U+" + ((int)isolate[0]).ToString("X4", CultureInfo.InvariantCulture));
        return vectors;
    }

    private static IEnumerable<IGrouping<(string Locale, bool Isolation), Vector>> Groups() =>
        Vectors().GroupBy(vector => (vector.Locale, vector.Isolation));

    private static Rmf2ProjectV5 Project(string locale, bool isolation, IReadOnlyList<Vector> vectors)
    {
        var source = new StringBuilder();
        for (int index = 0; index < vectors.Count; index++)
        {
            source.Append("vector").Append(index).Append(" =\n");
            foreach (string line in vectors[index].Message.Split('\n')) source.Append("  ").Append(line).Append('\n');
        }
        Rmf2ProjectV5 project = Rmf2EsmV5Tests.CompileProject("bidi", locale, [locale], false, isolation ? "on" : "off", (locale, source.ToString()));
        Assert.Equal(isolation, project.BidiIsolation, "resolved bidiIsolation");
        return project;
    }

    private static TextArgument[] Arguments(JsonObject values) => values.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
    {
        string value = item.Value!["value"]!.GetValue<string>();
        TextArgument carrier = item.Value["type"]!.GetValue<string>() switch
        {
            "int64" => new TextArgument("_", long.Parse(value, CultureInfo.InvariantCulture)),
            "decimal" => new TextArgument("_", decimal.Parse(value, CultureInfo.InvariantCulture)),
            "string" => new TextArgument("_", value),
            "boolean" => new TextArgument("_", value == "true"),
            "guid" => new TextArgument("_", Guid.Parse(value)),
            "date" => new TextArgument("_", DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
            string type => throw new InvalidOperationException("Unsupported bidi corpus carrier " + type),
        };
        return TextArgument.CreateRmf2(item.Key, carrier);
    }).ToArray();

    [Test, DisplayName("RMF2 placeholder bidi isolation formats identically in linked .NET messages and loaded packs")]
    public void DotNet()
    {
        var failures = new List<string>();
        foreach (var group in Groups())
        {
            Vector[] vectors = group.ToArray();
            Rmf2ProjectV5 project = Project(group.Key.Locale, group.Key.Isolation, vectors);
            Rmf2LocaleV5 locale = project.Locales.Single();
            TranslationGeneratedOutput artifact = Rmf2LocaleArtifactV5.Render(project, group.Key.Locale);
            VerifiedExternalTranslationPack pack = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(artifact.GetUtf8Bytes()),
                Rmf2V1CorpusTests.PackContract(project, group.Key.Locale)).AsTask().GetAwaiter().GetResult();
            for (int index = 0; index < vectors.Length; index++)
            {
                string key = "vector" + index;
                Rmf2TranslationV5 linked = locale.ResolvedResources.Single(item => item.Key == key);
                Rmf2MessageContractV5 contract = project.CanonicalMessages.Single(item => item.Key == key);
                TextArgument[] arguments = Arguments(vectors[index].Arguments);
                CompiledRmf2Message direct = Rmf2V1CorpusTests.Lower(linked.Message with { Inputs = contract.Inputs }, linked.ContentLocale);
                Check(vectors[index], Format(direct, arguments, vectors[index]), "linked");
                CompiledRmf2Message loaded = pack.Messages.Single(item => item.Key.Name == key).Message!.Rmf2V5!;
                Check(vectors[index], Format(loaded, arguments, vectors[index]), "pack");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));

        static string Format(CompiledRmf2Message message, TextArgument[] arguments, Vector vector) =>
            Rmf2RuntimeEvaluator.Format(message, arguments, vector.Locale, TextPatternFormatter.DefaultMaximumOutputLength, bidiIsolation: vector.Isolation);

        void Check(Vector vector, string actual, string route)
        {
            if (!string.Equals(vector.Expected, actual, StringComparison.Ordinal))
                failures.Add(vector.Id + "/" + route + ": expected " + JsonSerializer.Serialize(vector.Expected) + ", got " + JsonSerializer.Serialize(actual));
        }
    }

    [Test, NotInParallel, DisplayName("RMF2 placeholder bidi isolation formats identically in generated and dynamic ESM")]
    public void Esm()
    {
        foreach (var group in Groups())
        {
            Vector[] vectors = group.ToArray();
            Rmf2ProjectV5 project = Project(group.Key.Locale, group.Key.Isolation, vectors);
            string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
            try
            {
                File.WriteAllBytes(Path.Combine(directory, "artifact.json"), Rmf2LocaleArtifactV5.Render(project, group.Key.Locale).GetUtf8Bytes());
                var cases = new JsonArray(vectors.Select((vector, index) => (JsonNode)new JsonObject
                {
                    ["id"] = vector.Id, ["key"] = "vector" + index, ["locale"] = vector.Locale, ["arguments"] = vector.Arguments.DeepClone(), ["expected"] = vector.Expected,
                }).ToArray());
                string script = Path.Combine(directory, "bidi-vectors.mjs");
                File.WriteAllText(script, "import { readFile } from \"node:fs/promises\";\nimport { m } from \"./bidi.esm-v5/messages.js\";\n" +
                    "import { decodeLocaleArtifact, formatDynamicMessage } from \"./bidi.esm-v5/dynamic.js\";\nconst cases = " + cases.ToJsonString() + ";\n" + """
                    const artifact = decodeLocaleArtifact(JSON.parse(await readFile(new URL("./artifact.json", import.meta.url), "utf8")));
                    if (!artifact.ok) throw new Error(`artifact rejected: ${artifact.reason}`);
                    const carrier = value => value.type === "int64" ? BigInt(value.value) : value.type === "boolean" ? value.value === "true" : value.value;
                    const failures = [];
                    for (const item of cases) {
                      const inputs = Object.fromEntries(Object.entries(item.arguments).map(([name, value]) => [name, carrier(value)]));
                      const generated = m[item.key](inputs, { locale: item.locale });
                      if (generated !== item.expected) failures.push(`${item.id}: expected ${JSON.stringify(item.expected)}, generated ${JSON.stringify(generated)}`);
                      const dynamic = formatDynamicMessage(artifact.value, item.key, inputs, { locale: item.locale });
                      if (dynamic !== item.expected) failures.push(`${item.id}: expected ${JSON.stringify(item.expected)}, dynamic ${JSON.stringify(dynamic)}`);
                    }
                    if (failures.length) throw new Error(failures.join("\n"));
                    """, new UTF8Encoding(false));
                foreach (string host in Rmf2DatePercentConformanceTests.EsmHosts()) Rmf2EsmV5Tests.Run(host, [script], directory);
            }
            finally { Directory.Delete(directory, true); }
        }
    }

    [Test, DisplayName("runtime.bidiIsolation resolves on for a catalog that declares a right-to-left locale, and u:dir takes only its four literals")]
    public void Resolution()
    {
        static Rmf2ProjectCompilation Compile(string locales, string? runtime, string message = "x = {$v}")
        {
            string json = "{\"schemaVersion\":1,\"catalog\":\"bidi\",\"code\":{\"namespace\":\"Example\",\"className\":\"Text\"},\"baseLocale\":\"en\",\"locales\":[" + locales + "]" +
                (runtime is null ? "" : ",\"runtime\":{\"bidiIsolation\":" + runtime + "}") + ",\"validation\":{\"translationCompleteness\":\"allow\"}}";
            return TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(json)),
                [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(message + "\n"))]);
        }
        static bool Resolved(string locales, string? runtime) => Compile(locales, runtime).Project!.BidiIsolation;

        Assert.Equal(false, Resolved("\"en\",\"de\"", null), "LTR catalog, default");
        Assert.Equal(true, Resolved("\"en\",\"ar\"", null), "RTL locale, default");
        Assert.Equal(true, Resolved("\"en\",\"pa-PK\"", "\"default\""), "likely Arabic script");
        Assert.Equal(false, Resolved("\"en\",\"pa\"", "\"default\""), "likely Gurmukhi script");
        Assert.Equal(true, Resolved("\"en\"", "\"on\""), "on");
        Assert.Equal(false, Resolved("\"en\",\"he\"", "\"off\""), "off");
        foreach (string invalid in new[] { "\"yes\"", "true" })
        {
            Rmf2ProjectCompilation result = Compile("\"en\"", invalid);
            Assert.True(!result.Success && result.Diagnostics.Any(item => item.Id == "RTR0019" && item.Message.Contains("bidiIsolation", StringComparison.Ordinal)), invalid);
        }

        Assert.True(Compile("\"en\"", null, "x = {$v :number u:dir=rtl}").Success, "u:dir on a function");
        foreach (string message in new[] { "x = {$v :string u:dir=up}", ".input {$d :string}\nx = {{{$v :string u:dir=$d}}}" })
            Assert.True(!Compile("\"en\"", null, message).Success, message);
    }
}
