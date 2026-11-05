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
// (rmf2-execution-v2.md, "Bidirectional text"): MF2's default strategy with the catalog's bidiIsolation on, off and default
// (decided per call from the requested, catalog and content locales, so a right-to-left request or overlay on a
// left-to-right catalog is isolated), u:dir on every function, lists with isolated items, locals and literals. Each content
// locale and isolation setting compiles into one project; .NET formats the linked message and the loaded artifact-v5 pack,
// ESM the generated module and the dynamically decoded artifact, each in the vector's requested locale.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2BidiConformanceTests
{
    private sealed record Vector(string Id, string Locale, string RequestedLocale, string Isolation, string Message, JsonObject Arguments, string Expected);

    private static List<Vector> Vectors()
    {
        var corpus = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-bidi-conformance.json")))!;
        var vectors = corpus["cases"]!.AsArray().Select(item => new Vector(
            item!["id"]!.GetValue<string>(), item["locale"]!.GetValue<string>(), (item["requestedLocale"] ?? item["locale"])!.GetValue<string>(),
            item["bidiIsolation"]!.GetValue<string>(),
            item["message"]!.GetValue<string>(), item["arguments"]!.AsObject(), item["expected"]!.GetValue<string>())).ToList();
        Assert.True(vectors.Count >= 20, "The bidi vectors are missing.");
        Assert.Equal(vectors.Count, vectors.Select(vector => vector.Id).Distinct(StringComparer.Ordinal).Count(), "Duplicate bidi vector ids.");
        foreach (string mode in new[] { "on", "off", "default" })
            Assert.True(vectors.Any(vector => vector.Isolation == mode), "No vector covers bidiIsolation " + mode);
        foreach (string isolate in new[] { "⁦", "⁧", "⁨" })
            Assert.True(vectors.Any(vector => vector.Expected.Contains(isolate, StringComparison.Ordinal)), "No vector covers U+" + ((int)isolate[0]).ToString("X4", CultureInfo.InvariantCulture));
        return vectors;
    }

    private static IEnumerable<IGrouping<(string Locale, string Isolation), Vector>> Groups() =>
        Vectors().GroupBy(vector => (vector.Locale, vector.Isolation));

    private static Rmf2ProjectV5 Project(string locale, string isolation, IReadOnlyList<Vector> vectors)
    {
        var source = new StringBuilder();
        for (int index = 0; index < vectors.Count; index++)
        {
            source.Append("vector").Append(index).Append(" =\n");
            foreach (string line in vectors[index].Message.Split('\n')) source.Append("  ").Append(line).Append('\n');
        }
        Rmf2ProjectV5 project = Rmf2EsmV5Tests.CompileProject("bidi", locale, [locale], false, isolation, (locale, source.ToString()));
        Assert.Equal(Mode(isolation), project.BidiIsolation, "bidiIsolation");
        return project;
    }

    private static TranslationBidiIsolation Mode(string isolation) => isolation switch
    {
        "on" => TranslationBidiIsolation.On,
        "off" => TranslationBidiIsolation.Off,
        "default" => TranslationBidiIsolation.Default,
        _ => throw new InvalidOperationException("Unknown bidiIsolation " + isolation),
    };

    private static TextArgument[] Arguments(JsonObject values) => values.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
    {
        string type = item.Value!["type"]!.GetValue<string>();
        if (type == "list")
            return TextArgument.CreateRmf2(item.Key, new TextArgument("_", item.Value["value"]!.AsArray().Select(element => element!.GetValue<string>()).ToArray()));
        string value = item.Value["value"]!.GetValue<string>();
        TextArgument carrier = type switch
        {
            "int64" => new TextArgument("_", long.Parse(value, CultureInfo.InvariantCulture)),
            "decimal" => new TextArgument("_", decimal.Parse(value, CultureInfo.InvariantCulture)),
            "string" => new TextArgument("_", value),
            "boolean" => new TextArgument("_", value == "true"),
            "guid" => new TextArgument("_", Guid.Parse(value)),
            "date" => new TextArgument("_", DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
            _ => throw new InvalidOperationException("Unsupported bidi corpus carrier " + type),
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
                // The CLDR currency, unit and compact data the compiler embeds with the message, as a generated catalog does.
                if (project.FormatData([linked.ContentLocale], [linked.Message]) is { } formats) direct.FormatData = Rmf2FormatData.Parse(System.Text.Encoding.UTF8.GetBytes(formats));
                Check(vectors[index], Format(direct, arguments, vectors[index]), "linked");
                CompiledRmf2Message loaded = pack.Messages.Single(item => item.Key.Name == key).Message!.Rmf2V5!;
                Check(vectors[index], Format(loaded, arguments, vectors[index]), "pack");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));

        static string Format(CompiledRmf2Message message, TextArgument[] arguments, Vector vector) =>
            Rmf2RuntimeEvaluator.Format(message, arguments, vector.Locale, TextPatternFormatter.DefaultMaximumOutputLength, vector.RequestedLocale,
                vector.Isolation switch { "on" => true, "off" => false, _ => null });

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
                    ["id"] = vector.Id, ["key"] = "vector" + index, ["locale"] = vector.RequestedLocale, ["arguments"] = vector.Arguments.DeepClone(), ["expected"] = vector.Expected,
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

    [Test, NotInParallel, DisplayName("a pack whose u:dir names a variable is rejected by the .NET and ESM pack validators, as the compiler rejects it")]
    public void PackVariableDirection()
    {
        Rmf2ProjectV5 project = Rmf2EsmV5Tests.CompileProject("bidi", "en", ["en"], false, "default",
            ("en", "vector0 =\n  .input {$d :string}\n  {{{$v :string u:dir=ltr} {$d}}}\n"));
        string valid = Encoding.UTF8.GetString(Rmf2LocaleArtifactV5.Render(project, "en").GetUtf8Bytes());
        // The u:dir option's literal becomes a reference to the declared input $d, which a hand-edited pack could carry.
        const string literal = "{\"name\":\"u:dir\",\"value\":{\"kind\":\"string-literal\",\"value\":\"ltr\"}}";
        Assert.True(valid.Contains(literal, StringComparison.Ordinal), "The artifact has no literal u:dir option: " + valid);
        string forged = valid.Replace(literal, "{\"name\":\"u:dir\",\"value\":{\"kind\":\"input\",\"value\":\"d\"}}", StringComparison.Ordinal);

        _ = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(Encoding.UTF8.GetBytes(valid)), Rmf2V1CorpusTests.PackContract(project, "en")).AsTask().GetAwaiter().GetResult();
        try
        {
            _ = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(Encoding.UTF8.GetBytes(forged)), Rmf2V1CorpusTests.PackContract(project, "en")).AsTask().GetAwaiter().GetResult();
            throw new InvalidOperationException("The .NET pack loader accepted a variable u:dir.");
        }
        catch (TranslationPackException)
        {
        }

        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            File.WriteAllText(Path.Combine(directory, "valid.json"), valid, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "forged.json"), forged, new UTF8Encoding(false));
            string script = Path.Combine(directory, "variable-direction.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { decodeLocaleArtifact } from "./bidi.esm-v5/dynamic.js";
                const read = async name => JSON.parse(await readFile(new URL(name, import.meta.url), "utf8"));
                const valid = decodeLocaleArtifact(await read("./valid.json"));
                if (!valid.ok) throw new Error(`valid artifact rejected: ${valid.reason}`);
                const forged = decodeLocaleArtifact(await read("./forged.json"));
                if (forged.ok) throw new Error("the ESM artifact decoder accepted a variable u:dir");
                """, new UTF8Encoding(false));
            foreach (string host in Rmf2DatePercentConformanceTests.EsmHosts()) Rmf2EsmV5Tests.Run(host, [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("runtime.bidiIsolation keeps default for the runtime to decide per locale, and u:dir takes only its four literals")]
    public void Resolution()
    {
        static Rmf2ProjectCompilation Compile(string locales, string? runtime, string message = "x = {$v}")
        {
            string json = "{\"schemaVersion\":1,\"catalog\":\"bidi\",\"code\":{\"namespace\":\"Example\",\"className\":\"Text\"},\"baseLocale\":\"en\",\"locales\":[" + locales + "]" +
                (runtime is null ? "" : ",\"runtime\":{\"bidiIsolation\":" + runtime + "}") + ",\"validation\":{\"translationCompleteness\":\"allow\"}}";
            return TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(json)),
                [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(message + "\n"))]);
        }
        static TranslationBidiIsolation Resolved(string locales, string? runtime) => Compile(locales, runtime).Project!.BidiIsolation;

        // default is not resolved from the declared locales: an RTL overlay may add a locale the catalog does not declare.
        Assert.Equal(TranslationBidiIsolation.Default, Resolved("\"en\",\"de\"", null), "LTR catalog, default");
        Assert.Equal(TranslationBidiIsolation.Default, Resolved("\"en\",\"ar\"", "\"default\""), "RTL locale, default");
        Assert.Equal(TranslationBidiIsolation.On, Resolved("\"en\"", "\"on\""), "on");
        Assert.Equal(TranslationBidiIsolation.Off, Resolved("\"en\",\"he\"", "\"off\""), "off");
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
