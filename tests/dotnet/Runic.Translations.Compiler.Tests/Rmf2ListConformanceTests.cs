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

// specs/translations/corpus/rmf2-list-conformance.json is the shared .NET/ESM oracle
// for :runic:list. Each locale's cases compile into one project; .NET formats the
// linked message and the loaded artifact-v5 pack, ESM the generated module and the
// dynamically decoded artifact. Every route must produce the expected string exactly.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2ListConformanceTests
{
    private sealed record Vector(string Id, string Locale, string Message, JsonObject Arguments, string Expected);

    private static List<Vector> Vectors()
    {
        var corpus = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-list-conformance.json")))!;
        Assert.Equal("48.2", corpus["cldrVersion"]!.GetValue<string>(), "List corpus CLDR pin");
        var vectors = corpus["cases"]!.AsArray().Select(item => new Vector(
            item!["id"]!.GetValue<string>(), item["locale"]!.GetValue<string>(), item["message"]!.GetValue<string>(),
            item["arguments"]!.AsObject(), item["expected"]!.GetValue<string>())).ToList();
        Assert.True(vectors.Count > 40, "The list vectors are missing.");
        Assert.Equal(vectors.Count, vectors.Select(vector => vector.Id).Distinct(StringComparer.Ordinal).Count(), "Duplicate list vector ids.");
        foreach (string family in TranslationCapabilityRegistry.ListLanguages)
            Assert.True(vectors.Any(vector => vector.Locale.Split('-')[0] == family), "No list vector covers locale family '" + family + "'.");
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
        return Rmf2EsmV5Tests.CompileProject("lists", locale, [locale], false, (locale, source.ToString()));
    }

    private static TextArgument[] Arguments(JsonObject values) => values.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
    {
        JsonNode value = item.Value!["value"]!;
        TextArgument carrier = item.Value["type"]!.GetValue<string>() switch
        {
            "list" => new TextArgument("_", value.AsArray().Select(element => element!.GetValue<string>()).ToArray()),
            "string" => new TextArgument("_", value.GetValue<string>()),
            string type => throw new InvalidOperationException("Unsupported list corpus carrier " + type),
        };
        return TextArgument.CreateRmf2(item.Key, carrier);
    }).ToArray();

    [Test, DisplayName("RMF2 list vectors format identically in linked .NET messages and loaded packs")]
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
                Assert.True(contract.Inputs.Any(input => input.Name == "items" && input.Type == "list"), vectors[index].Id + ": items is not a list input");
                TextArgument[] arguments = Arguments(vectors[index].Arguments);
                CompiledRmf2Message direct = Rmf2V1CorpusTests.Lower(linked.Message with { Inputs = contract.Inputs }, linked.ContentLocale);
                Assert.Equal(vectors[index].Expected, direct.Format(arguments, group.Key), vectors[index].Id + "/linked");
                CompiledRmf2Message loaded = pack.Messages.Single(item => item.Key.Name == key).Message!.Rmf2V5!;
                Assert.Equal(vectors[index].Expected, loaded.Format(arguments, group.Key), vectors[index].Id + "/pack");
            }
        }
    }

    [Test, NotInParallel, DisplayName("RMF2 list vectors format identically in generated and dynamic ESM")]
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
                string script = Path.Combine(directory, "list-vectors.mjs");
                File.WriteAllText(script, """
                    import { readFile } from "node:fs/promises";
                    import { m } from "./lists.esm-v5/messages.js";
                    import { decodeLocaleArtifact, formatDynamicMessage } from "./lists.esm-v5/dynamic.js";
                    const locale =
                    """ + JsonSerializer.Serialize(group.Key) + ";\nconst cases = " + cases.ToJsonString() + ";\n" + """
                    const artifact = decodeLocaleArtifact(JSON.parse(await readFile(new URL("./artifact.json", import.meta.url), "utf8")));
                    if (!artifact.ok) throw new Error(`artifact rejected: ${artifact.reason}`);
                    for (const item of cases) {
                      const inputs = Object.fromEntries(Object.entries(item.arguments).map(([name, value]) => [name, value.value]));
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

    [Test, NotInParallel, DisplayName("RMF2 list inputs are type-checked arrays of strings in generated ESM")]
    public void EsmInputs()
    {
        Rmf2ProjectV5 project = Rmf2EsmV5Tests.CompileProject("lists", "en", ["en"], false,
            ("en", "names = {{{$names :runic:list}}}\nchoice =\n  .input {$names :runic:list type=disjunction}\n  {{{$names}}}\n"));
        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "artifact.json"), Rmf2LocaleArtifactV5.Render(project, "en").GetUtf8Bytes());
            string script = Path.Combine(directory, "inputs.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { m } from "./lists.esm-v5/messages.js";
                import { decodeTextReference } from "./lists.esm-v5/transport.js";
                const check = (condition, message) => { if (!condition) throw new Error(message); };
                const names = ["Ana", "Bo"];
                check(m.names({ names }, { locale: "en" }) === "Ana and Bo", "array input");
                check(m.choice({ names: Object.freeze(["Ana"]) }, { locale: "en" }) === "Ana", "frozen single-item input");
                for (const invalid of ["Ana", ["Ana", 1], ["Ana", null], { 0: "Ana", length: 1 }, undefined]) {
                  let rejected = false; try { m.names({ names: invalid }, { locale: "en" }); } catch (error) { rejected = error instanceof TypeError; }
                  check(rejected, `invalid list input accepted: ${JSON.stringify(invalid)}`);
                }
                // Transport version 1 carries no list value.
                const { contractFingerprint } = JSON.parse(await readFile(new URL("./artifact.json", import.meta.url), "utf8"));
                for (const names of [["Ana"], "Ana"]) {
                  const reference = decodeTextReference({ version: 1, catalog: "lists", contractFingerprint, key: "names", arguments: { names } });
                  check(!reference.ok && reference.reason === "invalid-argument:names", `list transport accepted: ${JSON.stringify(reference)}`);
                }
                console.log("RMF2 ESM list inputs OK");
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [script], directory);
            string typecheck = Path.Combine(directory, "typecheck.ts");
            File.WriteAllText(typecheck, """
                import { m } from "./lists.esm-v5/messages.js";
                const mutable: string[] = ["Ana", "Bo"];
                const shared: readonly string[] = mutable;
                const text: string = m.names({ names: shared }) + m.choice({ names: mutable }, { locale: "en" });
                // @ts-expect-error a list input is not a single string.
                m.names({ names: "Ana" });
                // @ts-expect-error list items are strings.
                m.names({ names: [1, 2] });
                void text;
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [RepositoryPaths.Resolve("node_modules", "typescript", "bin", "tsc"), "--strict", "--noEmit", "--target", "ES2022", "--module", "NodeNext", "--moduleResolution", "NodeNext", "--lib", "ES2022,DOM", "--skipLibCheck", typecheck], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("RMF2 list inputs are typed by :runic:list and reject other uses")]
    public void Semantics()
    {
        Rmf2SemanticResultV5 inferred = Compile("{{{$items :runic:list type=unit style=narrow}}}");
        Assert.True(inferred.Success, string.Join("; ", inferred.Diagnostics.Select(item => item.Id + ": " + item.Message)));
        Assert.Equal("items:list", string.Join("|", inferred.Message!.Inputs.Select(input => input.Name + ":" + input.Type)));
        Rmf2SemanticResultV5 declared = Compile(".input {$items :runic:list}\n.input {$type :string}\n.local $any = {$items :runic:list type=$type}\n{{{$any} {$items}}}");
        Assert.True(declared.Success, string.Join("; ", declared.Diagnostics.Select(item => item.Id + ": " + item.Message)));
        Assert.Equal("items:list|type:string", string.Join("|", declared.Message!.Inputs.Select(input => input.Name + ":" + input.Type)));
        foreach (string rejected in new[]
        {
            "{{{|a, b| :runic:list}}}",
            "{{{1 :runic:list}}}",
            "{{{$items :runic:list type=both}}}",
            "{{{$items :runic:list style=wide}}}",
            "{{{$items :runic:list select=exact}}}",
            "{{{$items :runic:list type=$type}}}",
            ".input {$items :runic:list}\n{{{$items :string}}}",
            ".input {$items :string}\n{{{$items :runic:list}}}",
            "{{{$items :runic:list} {$items :number}}}",
        })
        {
            Rmf2SemanticResultV5 result = Compile(rejected);
            Assert.True(!result.Success && result.Diagnostics.Any(item => item.Id == "RTR0065"), "Unexpectedly accepted: " + rejected);
        }
        Rmf2SemanticResultV5 selector = Compile(".input {$items :runic:list}\n.match $items\n* {{any}}");
        Assert.True(!selector.Success, "A list selector was accepted.");
        // The editor and language-server analysis accepts the same function and options.
        Rmf2ResourceDocument analyzed = Rmf2ResourceReader.Analyze(new TranslationSource("translations/en.rmf2",
            Encoding.UTF8.GetBytes("names = {{{$names :runic:list type=unit style=narrow}}}\nother = {$names :runic:list type=disjunction}\n")));
        Assert.Equal(0, analyzed.Diagnostics.Count, string.Join("; ", analyzed.Diagnostics.Select(item => item.Id + ": " + item.Message)));
    }

    [Test, DisplayName("RMF2 list formatting reports RTR0031 for a content locale without CLDR list patterns")]
    public void UnsupportedLocale()
    {
        const string manifest = """{"schemaVersion":1,"catalog":"lists","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","locales":["en","pl"]}""";
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(manifest)),
        [
            new("translations/en.rmf2", Encoding.UTF8.GetBytes("names = {{{$names :runic:list}}}\n")),
            // Without an explicit function the list input still formats with list patterns.
            new("translations/pl.rmf2", Encoding.UTF8.GetBytes("names = {{{$names}}}\n")),
        ]);
        TranslationDiagnostic diagnostic = result.Diagnostics.Single(item => item.Id == "RTR0031");
        Assert.True(diagnostic.Message.Contains("list registry", StringComparison.Ordinal) && diagnostic.Message.Contains("'pl'", StringComparison.Ordinal), diagnostic.Message);
    }

    private static Rmf2SemanticResultV5 Compile(string text) => Rmf2SemanticCompilerV5.Compile(new TranslationSource("message.mf2", Encoding.UTF8.GetBytes(text)));
}
