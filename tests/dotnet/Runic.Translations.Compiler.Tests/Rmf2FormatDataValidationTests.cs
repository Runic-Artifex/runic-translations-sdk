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
    private static Rmf2ProjectV5 Project() => Rmf2EsmV5Tests.CompileProject("shop", "en", ["en"], false, ("en", """
        total = {{{$n :currency currency=EUR}}}
        debt = {{{$n :currency currency=EUR currencySign=accounting}}}
        speed = {{{$n :unit unit=kilometer-per-hour}}}
        reach = {{{$n :number runic:notation=compact}}}
        """));

    private sealed record Case(string Id, JsonArray Path, JsonNode? Value, bool Delete, int Repeat);

    private static Case[] Cases() => JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-format-data-invalid.json")))!["cases"]!
        .AsArray().Select(item => new Case(item!["id"]!.GetValue<string>(), item["path"]!.AsArray(), item["value"]?.DeepClone(), item["delete"]?.GetValue<bool>() == true,
            item["repeat"]?.GetValue<int>() ?? 0)).ToArray();

    // The artifact with the case applied to its formatData.
    private static byte[] Apply(byte[] artifact, Case item)
    {
        JsonObject root = JsonNode.Parse(artifact)!.AsObject();
        JsonNode? value = item.Repeat > 0 ? new JsonArray(Enumerable.Range(0, item.Repeat).Select(_ => item.Value!.DeepClone()).ToArray()) : item.Value?.DeepClone();
        if (item.Path.Count == 0) { root["formatData"] = value; return Encoding.UTF8.GetBytes(root.ToJsonString()); }
        JsonNode parent = root["formatData"]!;
        for (int index = 0; index < item.Path.Count - 1; index++) parent = Step(parent, item.Path[index]!) ?? throw new InvalidOperationException(item.Id + ": no member at " + item.Path.ToJsonString());
        JsonNode last = item.Path[^1]!;
        if (last.GetValueKind() == JsonValueKind.Number)
        {
            JsonArray array = parent.AsArray();
            int position = last.GetValue<int>();
            if (position >= array.Count) throw new InvalidOperationException(item.Id + ": no element at " + item.Path.ToJsonString());
            array[position] = value;
        }
        else if (item.Delete) Assert.True(parent.AsObject().Remove(last.GetValue<string>()), item.Id + ": nothing to delete");
        else parent.AsObject()[last.GetValue<string>()] = value;
        return Encoding.UTF8.GetBytes(root.ToJsonString());

        static JsonNode? Step(JsonNode node, JsonNode key) => key.GetValueKind() == JsonValueKind.Number ? node.AsArray()[key.GetValue<int>()] : node.AsObject()[key.GetValue<string>()];
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
        Rmf2ProjectV5 project = Project();
        byte[] artifact = Rmf2LocaleArtifactV5.Render(project, "en").GetUtf8Bytes();
        TranslationPackContract contract = Rmf2V1CorpusTests.PackContract(project, "en");
        object Exact(byte[] bytes) => TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(bytes), contract).AsTask().GetAwaiter().GetResult();
        object Overlay(byte[] bytes) => TranslationPackV5Loader.ParseOverlay(bytes, "shop", "en", new TranslationPackLimits(), CancellationToken.None);
        Assert.Equal(null, DotNet(() => Exact(artifact)), "the artifact loads as an exact pack");
        Assert.Equal(null, DotNet(() => Overlay(artifact)), "the artifact loads as an overlay");
        var failures = new List<string>();
        foreach (Case item in Cases())
        {
            byte[] bytes = Apply(artifact, item);
            foreach ((string route, Func<byte[], object> load) in new (string, Func<byte[], object>)[] { ("exact", Exact), ("overlay", Overlay) })
                if (DotNet(() => load(bytes)) is var reason && reason != TranslationPackFailureReason.Malformed) failures.Add(item.Id + "/" + route + ": " + (reason?.ToString() ?? "accepted"));
        }
        // A duplicate member, which JSON.parse would let the last one win, is malformed too.
        string text = Encoding.UTF8.GetString(artifact);
        int currencies = text.IndexOf("\"currencies\":{\"EUR\":2}", StringComparison.Ordinal);
        Assert.True(currencies > 0, "the artifact's currencies");
        byte[] duplicate = Encoding.UTF8.GetBytes(text.Insert(currencies + "\"currencies\":{".Length, "\"EUR\":2,"));
        if (DotNet(() => Exact(duplicate)) != TranslationPackFailureReason.Malformed) failures.Add("duplicate-member/exact");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Test, NotInParallel, DisplayName("crafted formatData is RTR0023/malformed in ESM decodeLocaleArtifact and installOverlay, and the pinned data passes")]
    public void EsmRejects()
    {
        Rmf2ProjectV5 project = Project();
        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            byte[] artifact = Rmf2LocaleArtifactV5.Render(project, "en").GetUtf8Bytes();
            File.WriteAllBytes(Path.Combine(directory, "artifact.json"), artifact);
            var cases = new JsonArray(Cases().Select(item => (JsonNode)new JsonObject { ["id"] = item.Id, ["artifact"] = JsonNode.Parse(Apply(artifact, item)) }).ToArray());
            File.WriteAllText(Path.Combine(directory, "cases.json"), cases.ToJsonString(), new UTF8Encoding(false));
            string text = Encoding.UTF8.GetString(artifact);
            File.WriteAllText(Path.Combine(directory, "duplicate.json"), text.Insert(text.IndexOf("\"currencies\":{\"EUR\":2}", StringComparison.Ordinal) + "\"currencies\":{".Length, "\"EUR\":2,"), new UTF8Encoding(false));
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
                const duplicate = await decodeLocalePack(new TextEncoder().encode(await read("duplicate.json")), "en");
                if (duplicate.ok || duplicate.reason !== "RTR0023/malformed") failures.push(`duplicate-member/exact: ${duplicate.reason}`);
                if (failures.length) throw new Error(failures.join("\n"));
                """, new UTF8Encoding(false));
            foreach (string host in Rmf2DatePercentConformanceTests.EsmHosts()) Rmf2EsmV5Tests.Run(host, [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }
}
