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

// specs/translations/corpus/transport-v2-persisted-references.json is the shared .NET/ESM oracle for persisted references:
// references written against the "writer" catalog are resolved by the changed "reader" catalog. .NET resolves with
// TranslationReferenceResolver over a compiled snapshot, ESM with resolveTextReference over the generated modules.
[Category("rmf2-semantic-v5")]
internal sealed class TransportV2ConformanceTests
{
    private static JsonObject Corpus() =>
        JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "transport-v2-persisted-references.json")))!.AsObject();

    private static Rmf2ProjectV5 Project(JsonObject corpus, string side)
    {
        JsonObject sources = corpus[side]!.AsObject();
        return Rmf2EsmV5Tests.CompileProject(corpus["catalog"]!.GetValue<string>(), corpus["baseLocale"]!.GetValue<string>(),
            corpus["locales"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray(), false,
            sources.Select(item => (item.Key, item.Value!.GetValue<string>())).ToArray());
    }

    private static readonly JsonSerializerOptions Literal = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Expands the corpus macros that keep large limit cases readable: {"$text": c, "length": n} is a string of n copies of c,
    // {"$items": v, "count": n} an array of n copies of v, and {"$arguments": v, "count": n, "prefix": p} an object with the
    // members p0 to p(n-1), each a copy of v.
    private static JsonNode? Expand(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject macro when macro.ContainsKey("$text"):
                return (JsonNode)(string.Concat(Enumerable.Repeat(macro["$text"]!.GetValue<string>(), macro["length"]!.GetValue<int>())));
            case JsonObject macro when macro.ContainsKey("$items"):
                return new JsonArray(Enumerable.Range(0, macro["count"]!.GetValue<int>()).Select(_ => Expand(macro["$items"])).ToArray());
            case JsonObject macro when macro.ContainsKey("$arguments"):
                return new JsonObject(Enumerable.Range(0, macro["count"]!.GetValue<int>())
                    .Select(index => KeyValuePair.Create(macro["prefix"]!.GetValue<string>() + index, Expand(macro["$arguments"]))));
            case JsonObject value:
                return new JsonObject(value.Select(item => KeyValuePair.Create(item.Key, Expand(item.Value))));
            case JsonArray array:
                return new JsonArray(array.Select(Expand).ToArray());
            default:
                return node?.DeepClone();
        }
    }

    // The JSON text of a case, either written in the corpus ("json") or the expanded "reference", with the "$writer" and "$reader"
    // fingerprint placeholders replaced.
    private static string Wire(JsonNode item, Rmf2ProjectV5 writer, Rmf2ProjectV5 reader) =>
        (item["json"]?.GetValue<string>() ?? Expand(item["reference"])!.ToJsonString(Literal))
            .Replace("\"$writer\"", JsonSerializer.Serialize(writer.CallerFingerprint), StringComparison.Ordinal)
            .Replace("\"$reader\"", JsonSerializer.Serialize(reader.CallerFingerprint), StringComparison.Ordinal);

    private static TranslationReference Build(JsonObject description, string fingerprint)
    {
        Dictionary<string, TranslationReferenceArgument> arguments = Arguments(description["arguments"]!.AsObject(), fingerprint);
        string key = description["key"]!.GetValue<string>(), catalog = "ops";
        string? fallback = description["fallbackText"]?.GetValue<string>();
        return description["version"]?.GetValue<int>() == 1
            ? TranslationReference.CreateVersion1(catalog, fingerprint, key, arguments, fallback)
            : new TranslationReference(catalog, fingerprint, key, arguments, fallback);
    }

    private static Dictionary<string, TranslationReferenceArgument> Arguments(JsonObject arguments, string fingerprint) =>
        arguments.ToDictionary(item => item.Key, item =>
        {
            JsonNode value = item.Value!["value"]!;
            return item.Value["type"]!.GetValue<string>() switch
            {
                "message" => new TranslationReferenceArgument(Build(value.AsObject(), fingerprint)),
                "list" => new TranslationReferenceArgument(value.AsArray().Select(element => element!.GetValue<string>()).ToArray()),
                "boolean" => new TranslationReferenceArgument(TextArgumentType.Bool, value.GetValue<bool>() ? "true" : "false"),
                "string" => new(TextArgumentType.String, value.GetValue<string>()),
                "int64" => new(TextArgumentType.Int, value.GetValue<string>()),
                "decimal" => new(TextArgumentType.Number, value.GetValue<string>()),
                "date" => new(TextArgumentType.Date, value.GetValue<string>()),
                "time" => new(TextArgumentType.Time, value.GetValue<string>()),
                "datetime" => new(TextArgumentType.DateTime, value.GetValue<string>()),
                "guid" => new(TextArgumentType.Guid, value.GetValue<string>()),
                string type => throw new InvalidOperationException("Unknown corpus type " + type),
            };
        }, StringComparer.Ordinal);

    private static CompiledTranslationSnapshot Snapshot(Rmf2ProjectV5 project, string locale)
    {
        VerifiedExternalTranslationPack pack = TranslationPackLoader.VerifyAsync(
            new ExternalTranslationPack(Rmf2LocaleArtifactV5.Render(project, locale).GetUtf8Bytes()), Rmf2V1CorpusTests.PackContract(project, locale))
            .AsTask().GetAwaiter().GetResult();
        CompiledTranslationDefinition[] definitions = project.CanonicalMessages.Select(message =>
            CompiledTranslationDefinition.FromRmf2Inputs(message.Key, message.Inputs.Select(input => new CompiledRmf2Input(input.Name, input.Type switch
            {
                "string" => TextArgumentType.String, "int64" => TextArgumentType.Int, "decimal" => TextArgumentType.Number, "list" => TextArgumentType.List,
                "boolean" => TextArgumentType.Bool, "date" => TextArgumentType.Date, "time" => TextArgumentType.Time, "datetime" => TextArgumentType.DateTime,
                "guid" => TextArgumentType.Guid, string type => throw new InvalidOperationException("Unexpected corpus carrier " + type),
            })).ToArray())).ToArray();
        var catalog = new CompiledTranslationCatalog(project.Id, locale, definitions,
            [new CompiledTranslationLocale(locale, null, pack.Messages.Select(message => new CompiledTranslationValue(message.Key.Id, string.Empty, message.Message))
                .OrderBy(value => value.Id).ToArray())]);
        return new CompiledTranslationSnapshot(catalog, locale);
    }

    private static string Describe(string outcome, string? text, IEnumerable<(string Path, string Reason)> diagnostics) =>
        outcome + " " + (text is null ? "null" : "«" + text + "»") + " [" + string.Join(", ", diagnostics.Select(item => item.Path + ":" + item.Reason)) + "]";

    private static string Expected(JsonObject expected) => Describe(expected["outcome"]!.GetValue<string>(), expected["text"]?.GetValue<string>(),
        expected["diagnostics"]!.AsArray().Select(item => (item!["path"]!.GetValue<string>(), item["reason"]!.GetValue<string>())));

    private static string Expected(JsonNode expected) => expected is JsonObject value ? Expected(value) : "(no expectation)";

    [Test, DisplayName("persisted text references read, resolve and write in .NET as the corpus expects")]
    public void DotNet()
    {
        JsonObject corpus = Corpus();
        Rmf2ProjectV5 writer = Project(corpus, "writer"), reader = Project(corpus, "reader");
        Assert.True(writer.CallerFingerprint != reader.CallerFingerprint, "The corpus catalogs must have different contracts.");
        var snapshots = new Dictionary<string, CompiledTranslationSnapshot>(StringComparer.Ordinal);
        var failures = new List<string>();
        JsonArray cases = corpus["cases"]!.AsArray();
        Assert.True(cases.Count >= 50, "The persisted-reference cases are missing.");
        foreach (JsonNode? item in cases)
        {
            string id = item!["id"]!.GetValue<string>(), locale = item["locale"]!.GetValue<string>();
            if (!snapshots.TryGetValue(locale, out CompiledTranslationSnapshot? snapshot)) snapshots[locale] = snapshot = Snapshot(reader, locale);
            string wire = Wire(item, writer, reader);
            TranslationReference reference;
            try { reference = TranslationReference.Parse(wire); }
            catch (JsonException exception) { failures.Add(id + ": rejected: " + (exception.InnerException ?? exception).Message); continue; }
            // Writing the reference back keeps its content; a version 1 null fallback is written as no fallback.
            string written = JsonSerializer.Serialize(reference, TranslationReferenceJsonContext.Default.TranslationReference);
            if (item["reference"]?["fallbackText"] is not null || item["reference"]?.AsObject().ContainsKey("fallbackText") != true)
                if (!JsonNode.DeepEquals(JsonNode.Parse(wire), JsonNode.Parse(written))) failures.Add(id + ": round trip " + written);
            TranslationReferenceResolution resolution = TranslationReferenceResolver.Resolve(snapshot, reader.CallerFingerprint, reference);
            string actual = Describe(resolution.Outcome switch
            {
                TranslationReferenceOutcome.Translated => "translated", TranslationReferenceOutcome.Fallback => "fallback", _ => "failed",
            }, resolution.Text, resolution.Diagnostics.Select(diagnostic => (diagnostic.Path, TranslationReferenceDiagnosticReasons.ToWire(diagnostic.Reason))));
            string expected = Expected(item["expected"]!);
            if (expected != actual) failures.Add(id + ": expected " + expected + ", got " + actual);
        }
        foreach (JsonNode? item in corpus["rejected"]!.AsArray())
        {
            string id = item!["id"]!.GetValue<string>();
            try { _ = TranslationReference.Parse(Wire(item, writer, reader)); failures.Add(id + ": accepted"); }
            catch (JsonException) { }
            // The converter checks the size itself when it is called through JsonSerializer.
            try { _ = JsonSerializer.Deserialize(Wire(item, writer, reader), TranslationReferenceJsonContext.Default.TranslationReference); failures.Add(id + ": accepted by the converter"); }
            catch (JsonException) { }
        }
        foreach (JsonNode? item in corpus["written"]!.AsArray())
        {
            string id = item!["id"]!.GetValue<string>();
            string expected = item["expected"]!.GetValue<string>().Replace("$reader", reader.CallerFingerprint, StringComparison.Ordinal);
            string actual = JsonSerializer.Serialize(Build(Expand(item["reference"])!.AsObject(), reader.CallerFingerprint), TranslationReferenceJsonContext.Default.TranslationReference);
            if (actual != expected) failures.Add(id + ": wrote " + actual);
            string reread = JsonSerializer.Serialize(TranslationReference.Parse(expected), TranslationReferenceJsonContext.Default.TranslationReference);
            if (reread != expected) failures.Add(id + ": rewrote " + reread);
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Test, NotInParallel, DisplayName("persisted text references read, resolve and write in generated ESM as the corpus expects")]
    public void Esm()
    {
        JsonObject corpus = Corpus();
        Rmf2ProjectV5 writer = Project(corpus, "writer"), reader = Project(corpus, "reader");
        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(reader));
        try
        {
            var cases = new JsonArray(corpus["cases"]!.AsArray().Select(item => (JsonNode)new JsonObject
            {
                ["id"] = item!["id"]!.GetValue<string>(), ["locale"] = item["locale"]!.GetValue<string>(),
                ["json"] = Wire(item, writer, reader), ["expected"] = Expected(item["expected"]!),
            }).ToArray());
            var rejected = new JsonArray(corpus["rejected"]!.AsArray().Select(item => (JsonNode)new JsonObject
            {
                ["id"] = item!["id"]!.GetValue<string>(), ["json"] = Wire(item, writer, reader), ["reason"] = item["reason"]!.GetValue<string>(),
            }).ToArray());
            var written = new JsonArray(corpus["written"]!.AsArray().Select(item => (JsonNode)new JsonObject
            {
                ["id"] = item!["id"]!.GetValue<string>(), ["reference"] = Expand(item["reference"]),
                ["expected"] = item["expected"]!.GetValue<string>().Replace("$reader", reader.CallerFingerprint, StringComparison.Ordinal),
            }).ToArray());
            string script = Path.Combine(directory, "persisted.mjs");
            File.WriteAllText(script, "const cases = " + cases.ToJsonString() + ";\nconst rejected = " + rejected.ToJsonString() + ";\nconst written = " + written.ToJsonString() + ";\n" + """
                import { m } from "./ops.esm-v5/messages.js";
                import { createTextReference, resolveTextReference } from "./ops.esm-v5/transport.js";
                const describe = (outcome, text, diagnostics) => `${outcome} ${text === undefined ? "null" : `«${text}»`} [${diagnostics.map(item => `${item.path}:${item.reason}`).join(", ")}]`;
                const failures = [];
                for (const item of cases) {
                  const result = resolveTextReference(item.json, m, { locale: item.locale });
                  const actual = describe(result.outcome, result.text, result.diagnostics);
                  if (actual !== item.expected) failures.push(`${item.id}: expected ${item.expected}, got ${actual}`);
                  if (!Object.isFrozen(result) || !Object.isFrozen(result.diagnostics)) failures.push(`${item.id}: resolution is not frozen`);
                  // An object parsed elsewhere resolves the same way.
                  const object = describe(...Object.values(resolveTextReference(JSON.parse(item.json), m, { locale: item.locale })));
                  if (object !== item.expected) failures.push(`${item.id}: object input gave ${object}`);
                }
                for (const item of rejected) {
                  const result = resolveTextReference(item.json, m, { locale: "en" });
                  const expected = `failed null [$:${item.reason}]`, actual = describe(result.outcome, result.text, result.diagnostics);
                  if (actual !== expected) failures.push(`${item.id}: expected ${expected}, got ${actual}`);
                }
                const inputs = args => Object.fromEntries(Object.entries(args).map(([name, { type, value }]) => [name,
                  type === "message" ? create(value) : type === "int64" ? BigInt(value) : value]));
                const create = description => createTextReference(description.key, inputs(description.arguments),
                  description.fallbackText === undefined ? {} : { fallbackText: description.fallbackText });
                for (const item of written) {
                  // ESM writes version 2 only; the version 1 cases stay as reader cases.
                  if (item.reference.version === 1) continue;
                  const actual = JSON.stringify(create(item.reference));
                  if (actual !== item.expected) failures.push(`${item.id}: wrote ${actual}`);
                }
                if (failures.length) throw new Error(failures.join("\n"));
                console.log("persisted references OK");
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, NotInParallel, DisplayName("transport v2 decodes strictly, formats message arguments and rejects cycles in generated ESM")]
    public void EsmStrict()
    {
        JsonObject corpus = Corpus();
        Rmf2ProjectV5 writer = Project(corpus, "writer"), reader = Project(corpus, "reader");
        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(reader));
        try
        {
            // The ESM writer and the .NET writer produce the same bytes.
            string Text(string value) => JsonSerializer.Serialize(value);
            var step = new TranslationReference("ops", reader.CallerFingerprint, "steps_build",
                new Dictionary<string, TranslationReferenceArgument> { ["name"] = new(TextArgumentType.String, "gimp") }, "Building gimp");
            string[] written =
            [
                JsonSerializer.Serialize(new TranslationReference("ops", reader.CallerFingerprint, "operation_failed",
                    new Dictionary<string, TranslationReferenceArgument> { ["step"] = new(step) }, "Building gimp failed"), TranslationReferenceJsonContext.Default.TranslationReference),
                JsonSerializer.Serialize(new TranslationReference("ops", reader.CallerFingerprint, "operation_retry",
                    new Dictionary<string, TranslationReferenceArgument> { ["attempt"] = new(TextArgumentType.Int, "2"), ["total"] = new(TextArgumentType.Int, "-3") }),
                    TranslationReferenceJsonContext.Default.TranslationReference),
                JsonSerializer.Serialize(new TranslationReference("ops", reader.CallerFingerprint, "operation_summary",
                    new Dictionary<string, TranslationReferenceArgument> { ["names"] = new(["a", "b"]) }), TranslationReferenceJsonContext.Default.TranslationReference),
            ];
            string script = Path.Combine(directory, "strict.mjs");
            File.WriteAllText(script, "const writerFingerprint = " + Text(writer.CallerFingerprint) + ";\nconst dotnet = [" + string.Join(",", written.Select(Text)) + "];\n" + """
                import { m } from "./ops.esm-v5/messages.js";
                import { contractFingerprint } from "./ops.esm-v5/runtime.js";
                import { createTextReference, decodeTextReference, formatTextReference, parseTextReference, resolveTextReference } from "./ops.esm-v5/transport.js";
                const check = (condition, message) => { if (!condition) throw new Error(message); };
                const step = { type: "message", value: { key: "steps_build", arguments: { name: { type: "string", value: "firefox" } }, fallbackText: "Building firefox" } };
                const reference = { version: 2, catalog: "ops", contractFingerprint: writerFingerprint, key: "operation_failed", arguments: { step } };
                const decoded = decodeTextReference(reference);
                check(decoded.ok && decoded.value.version === 2 && decoded.value.contractFingerprint === writerFingerprint, `strict v2 decode: ${JSON.stringify(decoded)}`);
                check(decoded.value.arguments.step.key === "steps_build" && Object.isFrozen(decoded.value.arguments.step), "decoded message argument");
                check(formatTextReference(decoded.value, m, { locale: "de" }) === "Bau von Paket firefox ist fehlgeschlagen", "strict nested format");
                const handlers = { operation_failed: m.operation_failed };
                check(formatTextReference(decoded.value, handlers, { locale: "en" }) === "Building firefox failed", "missing nested handler uses the nested fallback");
                const unknown = decodeTextReference({ ...reference, arguments: { step: { type: "message", value: { key: "steps_cleanup", arguments: {} } } } });
                check(!unknown.ok && unknown.reason === "unknown-key", `strict nested unknown key: ${JSON.stringify(unknown)}`);
                const mismatched = decodeTextReference({ ...reference, key: "operation_retry", arguments: { attempt: { type: "string", value: "2" }, total: { type: "int64", value: "3" } } });
                check(!mismatched.ok && mismatched.reason === "invalid-argument:attempt", `strict type mismatch: ${JSON.stringify(mismatched)}`);
                const list = decodeTextReference({ ...reference, key: "operation_summary", arguments: { names: { type: "list", value: ["a", "b"] } } });
                check(list.ok && Object.isFrozen(list.value.arguments.names) && formatTextReference(list.value, m, { locale: "en" }) === "Changed a and b", "list transport");
                const version1 = decodeTextReference({ version: 1, catalog: "ops", contractFingerprint: writerFingerprint, key: "steps_build", arguments: { name: "x" } });
                check(!version1.ok && version1.reason === "fingerprint-mismatch", "version 1 still requires the exact contract");
                check(decodeTextReference({ version: 1, catalog: "ops", contractFingerprint, key: "steps_build", arguments: { name: "x" } }).ok, "version 1 decode");
                const cyclic = { key: "operation_failed", arguments: {} }; cyclic.arguments.step = { type: "message", value: cyclic };
                const cycle = resolveTextReference({ ...reference, arguments: { step: { type: "message", value: cyclic } } }, m);
                check(cycle.outcome === "failed" && cycle.diagnostics[0].reason === "cycle", `cycle: ${JSON.stringify(cycle)}`);
                const shared = { type: "message", value: { key: "steps_build", arguments: { name: { type: "string", value: "x" } } } };
                const twice = resolveTextReference({ version: 2, catalog: "ops", contractFingerprint, key: "operation_retry", arguments: { attempt: { type: "int64", value: "1" }, total: { type: "int64", value: "2" } } }, m, { locale: "en" });
                check(twice.outcome === "translated" && twice.text === "Retry 1 of 2" && twice.diagnostics.length === 0, `same contract: ${JSON.stringify(twice)}`);
                const reused = resolveTextReference({ version: 2, catalog: "ops", contractFingerprint, key: "operation_failed", arguments: { step: { type: "message", value: { key: "operation_failed", arguments: { step: shared } } } } }, m, { locale: "en" });
                check(reused.outcome === "translated" && reused.text === "Building package x failed failed", `nested: ${JSON.stringify(reused)}`);
                const noHandler = resolveTextReference(reference, {}, { locale: "en" });
                check(noHandler.outcome === "failed" && noHandler.diagnostics.at(-1).reason === "no-handler", `no handler: ${JSON.stringify(noHandler)}`);
                let reads = 0; const getter = { version: 2, catalog: "ops", contractFingerprint, key: "steps_build", arguments: { name: { type: "string", get value() { reads++; return "x"; } } } };
                const once = resolveTextReference(getter, m, { locale: "en" }); check(once.text === "Building package x" && reads === 1, "arguments are read once");
                const createdStep = createTextReference("steps_build", { name: "gimp" }, { fallbackText: "Building gimp" });
                const created = [
                  createTextReference("operation_failed", { step: createdStep }, { fallbackText: "Building gimp failed" }),
                  createTextReference("operation_retry", { attempt: 2n, total: -3 }),
                  createTextReference("operation_summary", { names: ["a", "b"] }),
                ];
                created.forEach((reference, index) => check(JSON.stringify(reference) === dotnet[index], `writer parity ${index}: ${JSON.stringify(reference)} !== ${dotnet[index]}`));
                check(resolveTextReference(JSON.parse(JSON.stringify(created[0])), m, { locale: "de" }).text === "Bau von Paket gimp ist fehlgeschlagen", "created reference resolves");
                for (const [key, inputs] of [["steps_build", {}], ["steps_build", { name: 1 }], ["operation_retry", { attempt: 1.5, total: 1 }], ["operation_retry", { attempt: createdStep, total: 1 }], ["steps_cleanup", {}]]) {
                  let rejected = false; try { createTextReference(key, inputs); } catch { rejected = true; } check(rejected, `writer accepted ${key} ${JSON.stringify(inputs)}`);
                }
                let nested = createTextReference("steps_build", { name: "x" });
                for (let level = 1; level < 8; level++) nested = createTextReference("operation_failed", { step: nested });
                let tooDeep = false; try { createTextReference("operation_failed", { step: nested }); } catch { tooDeep = true; } check(tooDeep, "writer depth limit");
                // The version 1 writer was removed in 0.8; version 1 references stay readable.
                let writerVersion1 = false; try { createTextReference("operation_retry", { attempt: 2n, total: 3 }, { version: 1 }); } catch (caught) { writerVersion1 = caught instanceof RangeError; } check(writerVersion1, "writer version 1");
                const read1 = `{"version":1,"catalog":"ops","contractFingerprint":"${contractFingerprint}","key":"operation_retry","arguments":{"attempt":"2","total":"3"},"fallbackText":"Retry 2 of 3"}`;
                check(decodeTextReference(read1).ok, "version 1 input decodes");
                let unknownVersion = false; try { createTextReference("steps_build", { name: "x" }, { version: 3 }); } catch { unknownVersion = true; } check(unknownVersion, "writer version 3");
                let lone = false; try { createTextReference("steps_build", { name: "\ud800" }); } catch { lone = true; } check(lone, "writer lone surrogate");
                let large = false; try { createTextReference("steps_build", { name: "x" }, { fallbackText: "\u0001".repeat(60000) }); } catch { large = true; } check(large, "writer encoded size");
                check(parseTextReference(42).reason === "malformed" && parseTextReference("x".repeat(262145)).reason === "limit-exceeded", "parse entry checks");
                check(parseTextReference('{"a":[1,{"b":null}]}').ok && Object.getPrototypeOf(parseTextReference('{"__proto__":{}}').value) === null, "strict JSON reader objects");
                check(!parseTextReference('{"a":1,}').ok && !parseTextReference('{"a":01}').ok && !parseTextReference('"\u0001"').ok, "strict JSON reader syntax");
                console.log("transport v2 strict OK");
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [script], directory);
            string typecheck = Path.Combine(directory, "typecheck.ts");
            File.WriteAllText(typecheck, """
                import { m } from "./ops.esm-v5/messages.js";
                import { resolveTextReference, type TextReferenceWireV2 } from "./ops.esm-v5/transport.js";
                const wire: TextReferenceWireV2 = { version: 2, catalog: "ops", contractFingerprint: "sha256:00", key: "operation_failed",
                  arguments: { step: { type: "message", value: { key: "steps_build", arguments: { name: { type: "string", value: "x" } }, fallbackText: "x" } } } };
                const result = resolveTextReference(wire, m, { locale: "de" });
                const text: string = result.outcome === "failed" ? "" : result.text;
                // @ts-expect-error a boolean argument carries a JSON boolean.
                const invalid: TextReferenceWireV2 = { ...wire, arguments: { flag: { type: "boolean", value: "true" } } };
                void text; void invalid;
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [RepositoryPaths.Resolve("node_modules", "typescript", "bin", "tsc"), "--strict", "--noEmit", "--target", "ES2022", "--module", "NodeNext", "--moduleResolution", "NodeNext", "--lib", "ES2022,DOM", "--skipLibCheck", typecheck], directory);
        }
        finally { Directory.Delete(directory, true); }
    }
}
