using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

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

    // Replaces the "$writer" and "$reader" fingerprint placeholders.
    private static string Wire(JsonNode reference, Rmf2ProjectV5 writer, Rmf2ProjectV5 reader) =>
        reference.ToJsonString().Replace("\"$writer\"", JsonSerializer.Serialize(writer.CallerFingerprint), StringComparison.Ordinal)
            .Replace("\"$reader\"", JsonSerializer.Serialize(reader.CallerFingerprint), StringComparison.Ordinal);

    private static CompiledTranslationSnapshot Snapshot(Rmf2ProjectV5 project, string locale)
    {
        VerifiedExternalTranslationPack pack = TranslationPackLoader.VerifyAsync(
            new ExternalTranslationPack(Rmf2LocaleArtifactV5.Render(project, locale).GetUtf8Bytes()), Rmf2V1CorpusTests.PackContract(project, locale))
            .AsTask().GetAwaiter().GetResult();
        CompiledTranslationDefinition[] definitions = project.CanonicalMessages.Select(message =>
            CompiledTranslationDefinition.FromRmf2Inputs(message.Key, message.Inputs.Select(input => new CompiledRmf2Input(input.Name, input.Type switch
            {
                "string" => TextArgumentType.String, "int64" => TextArgumentType.Int, "decimal" => TextArgumentType.Number, "list" => TextArgumentType.List,
                string type => throw new InvalidOperationException("Unexpected corpus carrier " + type),
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

    [Test, DisplayName("persisted text references resolve after catalog changes in .NET as the corpus expects")]
    public void DotNet()
    {
        JsonObject corpus = Corpus();
        Rmf2ProjectV5 writer = Project(corpus, "writer"), reader = Project(corpus, "reader");
        Assert.True(writer.CallerFingerprint != reader.CallerFingerprint, "The corpus catalogs must have different contracts.");
        var snapshots = new Dictionary<string, CompiledTranslationSnapshot>(StringComparer.Ordinal);
        JsonArray cases = corpus["cases"]!.AsArray();
        Assert.True(cases.Count >= 20, "The persisted-reference cases are missing.");
        foreach (JsonNode? item in cases)
        {
            string id = item!["id"]!.GetValue<string>(), locale = item["locale"]!.GetValue<string>();
            if (!snapshots.TryGetValue(locale, out CompiledTranslationSnapshot? snapshot)) snapshots[locale] = snapshot = Snapshot(reader, locale);
            string wire = Wire(item["reference"]!, writer, reader);
            TranslationReference reference = JsonSerializer.Deserialize(wire, TranslationReferenceJsonContext.Default.TranslationReference)!;
            // The corpus spells every reference in the writer's canonical form.
            Assert.Equal(wire, JsonSerializer.Serialize(reference, TranslationReferenceJsonContext.Default.TranslationReference), id + "/round trip");
            TranslationReferenceResolution resolution = TranslationReferenceResolver.Resolve(snapshot, reader.CallerFingerprint, reference);
            string actual = Describe(resolution.Outcome switch
            {
                TranslationReferenceOutcome.Translated => "translated", TranslationReferenceOutcome.Fallback => "fallback", _ => "failed",
            }, resolution.Text, resolution.Diagnostics.Select(diagnostic => (diagnostic.Path, diagnostic.Reason)));
            Assert.Equal(Expected(item["expected"]!.AsObject()), actual, id);
        }
        foreach (JsonNode? item in corpus["rejected"]!.AsArray())
        {
            string id = item!["id"]!.GetValue<string>();
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(Wire(item["reference"]!, writer, reader), TranslationReferenceJsonContext.Default.TranslationReference), id);
        }
    }

    [Test, NotInParallel, DisplayName("persisted text references resolve after catalog changes in generated ESM as the corpus expects")]
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
                ["reference"] = JsonNode.Parse(Wire(item["reference"]!, writer, reader)), ["expected"] = Expected(item["expected"]!.AsObject()),
            }).ToArray());
            var rejected = new JsonArray(corpus["rejected"]!.AsArray().Select(item => (JsonNode)new JsonObject
            {
                ["id"] = item!["id"]!.GetValue<string>(), ["reference"] = JsonNode.Parse(Wire(item["reference"]!, writer, reader)),
                ["reason"] = item["reason"]!.GetValue<string>(),
            }).ToArray());
            string script = Path.Combine(directory, "persisted.mjs");
            File.WriteAllText(script, "const cases = " + cases.ToJsonString() + ";\nconst rejected = " + rejected.ToJsonString() + ";\n" + """
                import { m } from "./ops.esm-v5/messages.js";
                import { resolveTextReference } from "./ops.esm-v5/transport.js";
                const describe = (outcome, text, diagnostics) => `${outcome} ${text === undefined ? "null" : `«${text}»`} [${diagnostics.map(item => `${item.path}:${item.reason}`).join(", ")}]`;
                for (const item of cases) {
                  const result = resolveTextReference(item.reference, m, { locale: item.locale });
                  const actual = describe(result.outcome, result.text, result.diagnostics);
                  if (actual !== item.expected) throw new Error(`${item.id}: expected ${item.expected}, got ${actual}`);
                  if (!Object.isFrozen(result) || !Object.isFrozen(result.diagnostics)) throw new Error(`${item.id}: resolution is not frozen`);
                }
                for (const item of rejected) {
                  const result = resolveTextReference(item.reference, m, { locale: "en" });
                  const expected = `failed null [$:${item.reason}]`, actual = describe(result.outcome, result.text, result.diagnostics);
                  if (actual !== expected) throw new Error(`${item.id}: expected ${expected}, got ${actual}`);
                }
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
                import { createTextReference, decodeTextReference, formatTextReference, resolveTextReference } from "./ops.esm-v5/transport.js";
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
