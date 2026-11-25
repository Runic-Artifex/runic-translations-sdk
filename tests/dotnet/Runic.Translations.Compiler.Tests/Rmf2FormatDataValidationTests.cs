using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// The rules of embedded format data (rmf2-execution-v2.md, "Embedded format data"): the pinned CLDR data passes them in
// both runtimes, and every crafted formatData of specs/translations/corpus/rmf2-format-data-invalid.json fails as
// RTR0023/malformed in .NET's exact-pack and overlay loaders and in ESM's decodeLocaleArtifact and installOverlay, so no
// such pack reaches a formatter (no IndexOutOfRange, NullReference or garbage output) and both runtimes agree.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2FormatDataValidationTests
{
    internal static Rmf2ProjectV5 Project() => Rmf2EsmV5Tests.CompileProject("shop", "en", ["en"], false, ("en", """
        total = {{{$n :currency currency=EUR}}}
        debt = {{{$n :currency currency=EUR currencySign=accounting}}}
        speed = {{{$n :unit unit=kilometer-per-hour}}}
        reach = {{{$n :number runic:notation=compact}}}
        """));

    // The cases are pack recipes over the committed compiled artifact of Project (packBase, corpus README "Pack recipes"):
    // RFC 6902 patches of its formatData and a text edit for a duplicate member, which JSON.parse would let the last one win.
    private sealed record Case(string Id, JsonNode Recipe)
    {
        internal bool IsText => Recipe["textEdit"] is not null;
    }

    private static (byte[] Artifact, Case[] Cases) Corpus()
    {
        JsonNode corpus = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-format-data-invalid.json")))!;
        byte[] artifact = File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", corpus["packBase"]!.GetValue<string>()));
        return (artifact, corpus["cases"]!.AsArray().Select(item => new Case(item!["id"]!.GetValue<string>(), item)).ToArray());
    }

    private static TranslationPackFailureReason? DotNet(Func<object> load)
    {
        try { load(); return null; }
        catch (TranslationPackException exception) { return exception.Reason; }
    }

    [Test, DisplayName("the pinned CLDR format and number data pass the embedded format data rules in .NET")]
    public void PinnedData()
    {
        using JsonDocument pinned = JsonDocument.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "cldr", "runic-formats-48.2.json")));
        Rmf2FormatData data = Rmf2FormatData.Parse(Encoding.UTF8.GetBytes(AllData(pinned)));
        Assert.True(data.HasFormatters, "pinned data");
        Assert.Equal("de", data.NumberFormat("de-AT") is { } format ? "de" : "none", "number records of every language");
    }

    // Every currency and locale of the pinned format data, with the number records of every language.
    private static string AllData(JsonDocument pinned)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("currencies"); pinned.RootElement.GetProperty("currencies").WriteTo(writer);
            writer.WritePropertyName("locales"); pinned.RootElement.GetProperty("locales").WriteTo(writer);
            writer.WritePropertyName("numbers");
            TranslationCapabilityRegistry.WriteNumberData(writer, TranslationCapabilityRegistry.CatalogLanguages(pinned.RootElement.GetProperty("locales").EnumerateObject().Select(item => item.Name)));
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Test, DisplayName("crafted formatData is RTR0023/malformed in .NET exact packs and overlays")]
    public void DotNetRejects()
    {
        (byte[] artifact, Case[] cases) = Corpus();
        TranslationPackContract contract = CompiledCorpus.PackContract("format-data-invalid/en");
        object Exact(byte[] bytes) => TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(bytes), contract).AsTask().GetAwaiter().GetResult();
        object Overlay(byte[] bytes) => TranslationPackV5Loader.ParseOverlay(bytes, "shop", "en", new TranslationPackLimits(), CancellationToken.None);
        Assert.Equal(null, DotNet(() => Exact(artifact)), "the artifact loads as an exact pack");
        Assert.Equal(null, DotNet(() => Overlay(artifact)), "the artifact loads as an overlay");
        var failures = new List<string>();
        foreach (Case item in cases)
        {
            byte[] bytes = CompiledCorpus.Apply(artifact, item.Recipe);
            foreach ((string route, Func<byte[], object> load) in new (string, Func<byte[], object>)[] { ("exact", Exact), ("overlay", Overlay) })
                if (DotNet(() => load(bytes)) is var reason && reason != TranslationPackFailureReason.Malformed) failures.Add(item.Id + "/" + route + ": " + (reason?.ToString() ?? "accepted"));
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Test, NotInParallel, DisplayName("crafted formatData is RTR0023/malformed in ESM decodeLocaleArtifact and installOverlay, and the pinned data passes")]
    public void EsmRejects()
    {
        Rmf2ProjectV5 project = Project();
        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            (byte[] artifact, Case[] all) = Corpus();
            File.WriteAllBytes(Path.Combine(directory, "artifact.json"), artifact);
            // Patched cases are decoded as values; a text-edited pack, which is no JSON value with unique members, as bytes.
            var cases = new JsonArray(all.Where(item => !item.IsText).Select(item => (JsonNode)new JsonObject
                { ["id"] = item.Id, ["artifact"] = JsonNode.Parse(CompiledCorpus.Apply(artifact, item.Recipe)) }).ToArray());
            File.WriteAllText(Path.Combine(directory, "cases.json"), cases.ToJsonString(), new UTF8Encoding(false));
            var packs = new JsonArray(all.Where(item => item.IsText).Select(item => (JsonNode)item.Id).ToArray());
            File.WriteAllText(Path.Combine(directory, "packs.json"), packs.ToJsonString(), new UTF8Encoding(false));
            foreach (Case item in all.Where(item => item.IsText)) File.WriteAllBytes(Path.Combine(directory, item.Id + ".pack.json"), CompiledCorpus.Apply(artifact, item.Recipe));
            // The runtime's validator, evaluated as it is written, on all pinned data.
            using (JsonDocument pinned = JsonDocument.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "cldr", "runic-formats-48.2.json"))))
                File.WriteAllText(Path.Combine(directory, "pinned.json"), AllData(pinned), new UTF8Encoding(false));
            string runtime = File.ReadAllText(RepositoryPaths.Resolve("tools", "Runic.Translations.Compiler", "Generation", "Rmf2EsmRuntimeV5.js"));
            int start = runtime.IndexOf("const formatObject", StringComparison.Ordinal), end = runtime.IndexOf("// @runic-formats-end", StringComparison.Ordinal);
            Assert.True(start > 0 && end > start, "the runtime's format data rules");
            File.WriteAllText(Path.Combine(directory, "rules.mjs"), "function isRecord(value) { return typeof value === \"object\" && value !== null && !Array.isArray(value); }\n" +
                runtime[start..end] + "\nexport { validFormatData };\n", new UTF8Encoding(false));
            string script = Path.Combine(directory, "reject.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { decodeLocaleArtifact, decodeLocalePack, installOverlay } from "./shop.esm-v5/dynamic.js";
                import { validFormatData } from "./rules.mjs";
                const read = async name => readFile(new URL(name, import.meta.url), "utf8");
                if (!validFormatData(JSON.parse(await read("pinned.json")))) throw new Error("the pinned data breaks the rules");
                const base = JSON.parse(await read("artifact.json"));
                if (!decodeLocaleArtifact(base).ok) throw new Error("the artifact does not load");
                const failures = [];
                for (const item of JSON.parse(await read("cases.json"))) {
                  const decoded = decodeLocaleArtifact(structuredClone(item.artifact)), installed = installOverlay(structuredClone(item.artifact));
                  if (installed.ok) installed.value.remove();
                  for (const [route, result] of [["exact", decoded], ["overlay", installed]]) if (result.ok || result.reason !== "RTR0023/malformed") failures.push(`${item.id}/${route}: ${result.ok ? "accepted" : result.reason}`);
                }
                for (const id of JSON.parse(await read("packs.json"))) {
                  const decoded = await decodeLocalePack(new TextEncoder().encode(await read(id + ".pack.json")), "en");
                  if (decoded.ok || decoded.reason !== "RTR0023/malformed") failures.push(`${id}/exact: ${decoded.ok ? "accepted" : decoded.reason}`);
                }
                if (failures.length) throw new Error(failures.join("\n"));
                """, new UTF8Encoding(false));
            foreach (string host in Rmf2DatePercentConformanceTests.EsmHosts()) Rmf2EsmV5Tests.Run(host, [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }
}
