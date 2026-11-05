using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// generate --emit-json --contract: packs of the current sources for a release that has already shipped, which that release's
// exact-fingerprint loaders (decodeLocaleArtifact, IExternalTranslationSource) and overlays accept.
[Category("rmf2-semantic-v5"), NotInParallel]
internal sealed class Rmf2ContractPackTests
{
    // The shipped release 1: en, de; "later" falls back to English in German.
    private static Rmf2ProjectV5 Shipped()
    {
        const string json = """{"schemaVersion":1,"catalog":"shop","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","locales":["en","de"],"validation":{"translationCompleteness":"allow"}}""";
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(json)), [
            new("translations/en.rmf2", Encoding.UTF8.GetBytes("title = Shop\ntotal =\n  .input {$amount :number}\n  .input {$currency :string}\n  {{Total {$amount} {$currency}}}\nrenamed = Old\nlater = Later\nmarked = Plain\n")),
            new("translations/de.rmf2", Encoding.UTF8.GetBytes("title = Laden\ntotal =\n  .input {$amount :number}\n  .input {$currency :string}\n  {{Summe {$amount} {$currency}}}\nrenamed = Alt\nmarked = Schlicht\n"))]);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        return result.Project!;
    }

    // Release 2: German text fixed, total's inputs declared in another order, renamed's inputs changed, later now
    // translated, marked gains markup, a new key and a new locale.
    private static Rmf2ProjectV5 Current() => Rmf2EsmV5Tests.CompileProject("shop", "en", ["en", "de", "fr"], false,
        ("en", "title = Shop\ntotal =\n  .input {$currency :string}\n  .input {$amount :number}\n  {{Total: {$amount} {$currency}}}\nrenamed =\n  .input {$name :string}\n  {{New {$name}}}\nlater = Later\nmarked = {#strong}Plain{/strong}\nadded = Added\n"),
        ("de", "title = Geschäft\ntotal =\n  .input {$currency :string}\n  .input {$amount :number}\n  {{Summe: {$amount} {$currency}}}\nrenamed =\n  .input {$name :string}\n  {{Neu {$name}}}\nlater = Später\nmarked = {#strong}Schlicht{/strong}\nadded = Neu\n"),
        ("fr", "title = Boutique\ntotal =\n  .input {$currency :string}\n  .input {$amount :number}\n  {{Total : {$amount} {$currency}}}\nrenamed =\n  .input {$name :string}\n  {{Nouveau {$name}}}\nlater = Plus tard\nmarked = Simple\nadded = Ajouté\n"));

    [Test, DisplayName("RMF2 v5 contract packs hold only the messages a shipped release accepts")]
    public void ContractPacks()
    {
        Rmf2ProjectV5 shipped = Shipped(), current = Current();
        var contract = Rmf2ShippedContractV5.Parse(Rmf2LocaleArtifactV5.Render(shipped, "de").GetUtf8Bytes());
        Assert.Equal(shipped.CallerFingerprint, contract.ContractFingerprint);
        var skipped = new List<Rmf2ContractPackSkipV5>();
        TranslationGeneratedOutput german = Rmf2ContractPackV5.Render(current, "de", contract, skipped)!;
        Assert.True(Rmf2ContractPackV5.Render(current, "fr", contract, skipped) is null, "fr is not a shipped locale");
        Assert.True(Rmf2ContractPackV5.Render(current, "en", contract, skipped) is not null, "en pack");
        Assert.Equal("de added unknown-key|de later content-locale-changed|de marked incompatible-markup|de renamed incompatible-inputs|en added unknown-key|en marked incompatible-markup|en renamed incompatible-inputs",
            string.Join('|', skipped.Select(item => $"{item.Locale} {item.Key} {item.Reason}").Order(StringComparer.Ordinal)));
        using (JsonDocument pack = JsonDocument.Parse(german.GetUtf8Bytes()))
        {
            Assert.Equal(shipped.CallerFingerprint, pack.RootElement.GetProperty("contractFingerprint").GetString());
            Assert.Equal("title|total", string.Join('|', pack.RootElement.GetProperty("messages").EnumerateObject().Select(item => item.Name)));
            // The shipped markup contract, byte for byte as the shipped artifacts carry it.
            using JsonDocument original = JsonDocument.Parse(Rmf2LocaleArtifactV5.Render(shipped, "de").GetUtf8Bytes());
            Assert.Equal(original.RootElement.GetProperty("markupContract").GetRawText(), pack.RootElement.GetProperty("markupContract").GetRawText());
        }
        Rmf2ProjectV5 other = Rmf2EsmV5Tests.CompileProject("other", "en", ["en"], false, ("en", "title = Other\n"));
        Assert.Throws<ArgumentException>(() => Rmf2ContractPackV5.Render(other, "en", contract, skipped), "another catalog");
        Assert.Throws<FormatException>(() => Rmf2ShippedContractV5.Parse(Encoding.UTF8.GetBytes("{\"artifactVersion\":4}")), "not a v5 artifact");

        // The shipped release's web runtime loads the pack as an exact-fingerprint artifact and as an overlay.
        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(shipped));
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "pack.de.json"), german.GetUtf8Bytes());
            File.WriteAllBytes(Path.Combine(directory, "plain.de.json"), Rmf2LocaleArtifactV5.Render(current, "de").GetUtf8Bytes());
            string script = Path.Combine(directory, "test.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { m } from "./shop.esm-v5/messages.js";
                import { decodeLocalePack, formatDynamicMessage, installLocalePack } from "./shop.esm-v5/dynamic.js";
                const check = (condition, message) => { if (!condition) throw new Error(message); };
                const equal = (actual, expected, message) => check(actual === expected, `${message}: ${JSON.stringify(actual)} !== ${JSON.stringify(expected)}`);
                const pack = new Uint8Array(await readFile(new URL("pack.de.json", import.meta.url)));
                const plain = new Uint8Array(await readFile(new URL("plain.de.json", import.meta.url)));

                // The release's own exact-fingerprint path rejects the plain release 2 pack and accepts the contract pack.
                equal((await decodeLocalePack(plain, "de")).reason, "RTR0023/contract-fingerprint-mismatch", "plain pack");
                const decoded = await decodeLocalePack(pack, "de");
                check(decoded.ok, JSON.stringify(decoded));
                equal(formatDynamicMessage(decoded.value, "title"), "Geschäft", "fixed text");
                equal(formatDynamicMessage(decoded.value, "total", { amount: "2.5", currency: "EUR" }), "Summe: 2,5 EUR", "reordered inputs");

                // As an overlay: no diagnostics, and the skipped keys keep the release's text.
                const installed = await installLocalePack(pack, { locale: "de" });
                check(installed.ok, JSON.stringify(installed)); equal(installed.value.diagnostics.length, 0, "no diagnostics");
                equal(m.title({ locale: "de" }), "Geschäft", "overlay text"); equal(m.later({ locale: "de" }), "Later", "skipped key");
                console.log("ok");
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("RMF2 v5 contract packs leave out messages that need a later release's functions or options")]
    public void UnsupportedFunctions()
    {
        // A shipped release whose messages use only 0.8.0 features: a date in the long style.
        static Rmf2ProjectV5 Release(string style, string extra = "") => Rmf2EsmV5Tests.CompileProject("shop", "en", ["en", "de"], false,
            ("en", $"updated =\n  .input {{$when :date style={style}}}\n  {{{{Updated {{$when}}}}}}\ntitle = Shop\n{extra}"),
            ("de", $"updated =\n  .input {{$when :date style={style}}}\n  {{{{Aktualisiert {{$when}}}}}}\ntitle = Laden\n{extra}"));
        var old = Rmf2ShippedContractV5.Parse(Rmf2LocaleArtifactV5.Render(Release("long"), "en").GetUtf8Bytes());
        Assert.Equal("0.8.0", Rmf2ReleaseFeaturesV5.Release(old.FeatureLevel));
        var skipped = new List<Rmf2ContractPackSkipV5>();
        TranslationGeneratedOutput pack = Rmf2ContractPackV5.Render(Release("medium"), "de", old, skipped)!;
        Assert.Equal("de updated unsupported-function :date style=medium needs 0.9.0",
            string.Join('|', skipped.Select(item => $"{item.Locale} {item.Key} {item.Reason} {item.Detail}")));
        using (JsonDocument document = JsonDocument.Parse(pack.GetUtf8Bytes()))
            Assert.Equal("title", string.Join('|', document.RootElement.GetProperty("messages").EnumerateObject().Select(item => item.Name)));

        // A release whose own messages use a 0.9.0 feature accepts it; a dynamic option value is checked when it formats.
        var newer = Rmf2ShippedContractV5.Parse(Rmf2LocaleArtifactV5.Render(Release("long", "stamp =\n  .input {$at :datetime timeZone=UTC}\n  {{{$at}}}\n"), "en").GetUtf8Bytes());
        Assert.Equal("0.9.0", Rmf2ReleaseFeaturesV5.Release(newer.FeatureLevel));
        skipped.Clear();
        Rmf2ContractPackV5.Render(Release("medium", "stamp =\n  .input {$at :datetime timeZone=UTC}\n  {{{$at}}}\n"), "de", newer, skipped);
        Assert.Equal(0, skipped.Count, string.Join('|', skipped));
    }

    [Test, DisplayName("RMF2 v5 release feature table: its newest level is what the ESM runtime accepts")]
    public void ReleaseFeatureTable()
    {
        // The runtime's validateResolvedOptions table, evaluated as it is written; the fraction digits and timeZone are checked
        // apart from its enumerations (optionType), and the currency and unit patterns are regular expressions, all as null
        // (any value) in the feature table.
        string runtime = File.ReadAllText(RepositoryPaths.Resolve("tools", "Runic.Translations.Compiler", "Generation", "Rmf2EsmRuntimeV5.js"));
        int start = runtime.IndexOf("const allowed = {", StringComparison.Ordinal), end = runtime.IndexOf("}[fn];", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "validateResolvedOptions table not found");
        string directory = Path.Combine(Path.GetTempPath(), "runic-features-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string script = Path.Combine(directory, "table.mjs");
            File.WriteAllText(script, "const allowed = " + runtime[(start + "const allowed = ".Length)..(end + 1)] + ";\n" +
                "allowed.number.minimumFractionDigits = null; allowed.number.maximumFractionDigits = null;\n" +
                "allowed.unit.minimumFractionDigits = null; allowed.unit.maximumFractionDigits = null; allowed.currency.fractionDigits = null;\n" +
                "for (const options of Object.values(allowed)) for (const [name, value] of Object.entries(options)) if (value instanceof RegExp) options[name] = null;\n" +
                "console.log(JSON.stringify(allowed));\n", new UTF8Encoding(false));
            var info = new System.Diagnostics.ProcessStartInfo("bun") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = directory };
            info.ArgumentList.Add(script);
            using var process = System.Diagnostics.Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode, error);
            using JsonDocument actual = JsonDocument.Parse(output);
            string Describe(IEnumerable<(string Function, string Option, string[]? Values)> items) => string.Join('\n', items
                .Select(item => item.Function + " " + item.Option + " " + (item.Values is null ? "*" : string.Join(',', item.Values.Order(StringComparer.Ordinal))))
                .Order(StringComparer.Ordinal));
            string runtimeTable = Describe(actual.RootElement.EnumerateObject().SelectMany(function => function.Value.EnumerateObject().Select(option =>
                (function.Name, option.Name, option.Value.ValueKind == JsonValueKind.Null ? null : option.Value.EnumerateArray().Select(value => value.GetString()!).ToArray()))));
            string featureTable = Describe(Rmf2ReleaseFeaturesV5.Accepted().SelectMany(function => function.Value.Select(option => (function.Key, option.Key, option.Value))));
            Assert.Equal(runtimeTable, featureTable, "add a level to Rmf2ReleaseFeaturesV5 for what the runtime newly accepts");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("RMF2 v5 contract packs load in the web runtime of a real 0.8.0 release")]
    public void PreviousReleaseWebLoader()
    {
        // tests/fixtures/translations/generated-0.8.0: the shipped release, generated by the published 0.8.0 tool.
        string fixture = RepositoryPaths.Resolve("tests", "fixtures", "translations", "generated-0.8.0");
        var shipped = Rmf2ShippedContractV5.Parse(File.ReadAllBytes(Path.Combine(fixture, "output", "acme-grid.en.locale-v5.json")));
        Assert.Equal("0.8.0", Rmf2ReleaseFeaturesV5.Release(shipped.FeatureLevel));
        Rmf2ProjectV5 current = CurrentOfPreviousRelease(fixture);
        var skipped = new List<Rmf2ContractPackSkipV5>();
        TranslationGeneratedOutput german = Rmf2ContractPackV5.Render(current, "de", shipped, skipped)!;
        Rmf2ContractPackV5.Render(current, "en", shipped, skipped);
        Assert.Equal("de grid_new content-locale-changed|de grid_updated unsupported-function|en grid_updated unsupported-function",
            string.Join('|', skipped.Select(item => $"{item.Locale} {item.Key} {item.Reason}").Order(StringComparer.Ordinal)));

        string directory = Path.Combine(Path.GetTempPath(), "runic-previous-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Rmf2EsmCompositionTests.CopyPreviousRelease(directory);
            File.WriteAllBytes(Path.Combine(directory, "pack.de.json"), german.GetUtf8Bytes());
            File.WriteAllBytes(Path.Combine(directory, "plain.de.json"), Rmf2LocaleArtifactV5.Render(current, "de").GetUtf8Bytes());
            string script = Path.Combine(directory, "test.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { decodeLocalePack, formatDynamicMessage } from "./acme-grid.esm-v5/dynamic.js";
                const check = (condition, message) => { if (!condition) throw new Error(message); };
                const equal = (actual, expected, message) => check(actual === expected, `${message}: ${JSON.stringify(actual)} !== ${JSON.stringify(expected)}`);
                const read = async name => new Uint8Array(await readFile(new URL(name, import.meta.url)));
                const pack = await read("pack.de.json");

                // 0.8.0's exact-fingerprint loader accepts the contract pack.
                const decoded = await decodeLocalePack(pack, "de");
                check(decoded.ok, JSON.stringify(decoded));
                equal(formatDynamicMessage(decoded.value, "grid_empty"), "Leer", "fixed text");
                equal(formatDynamicMessage(decoded.value, "grid_rows", { count: 1 }), "1 Zeile", "plural");
                equal(formatDynamicMessage(decoded.value, "grid_total", { amount: "1234.5" }), "Summe 1.234,5", "number");
                check(!Object.hasOwn(decoded.value.messages, "grid_updated"), "the medium date is left out");

                // With the medium date message the 0.8.0 loader rejects the whole pack, so leaving it out keeps the others.
                const plain = JSON.parse(new TextDecoder().decode(await read("plain.de.json")));
                const forced = JSON.parse(new TextDecoder().decode(pack));
                forced.messages = { ...forced.messages, grid_updated: plain.messages.grid_updated };
                const rejected = await decodeLocalePack(new TextEncoder().encode(JSON.stringify(forced)), "de");
                equal(rejected.ok, false, "a message with a newer option rejects the pack"); equal(rejected.reason, "RTR0023/argument-contract-mismatch", "reason");
                console.log("ok");
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [script], directory);
            Rmf2EsmV5Tests.Run("node", [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("RMF2 v5 contract packs for a 0.8.0 release leave out :currency, :unit and compact messages and formatData")]
    public void PreviousReleaseFormatData()
    {
        string fixture = RepositoryPaths.Resolve("tests", "fixtures", "translations", "generated-0.8.0");
        var shipped = Rmf2ShippedContractV5.Parse(File.ReadAllBytes(Path.Combine(fixture, "output", "acme-grid.en.locale-v5.json")));
        // grid_total as a currency, which 0.9.0 added.
        Rmf2ProjectV5 current = CurrentOfPreviousRelease(fixture, source => source
            .Replace("{{Total {$amount}}}", "{{Total {$amount :currency currency=EUR}}}", StringComparison.Ordinal)
            .Replace("{{Summe {$amount}}}", "{{Summe {$amount :currency currency=EUR}}}", StringComparison.Ordinal));
        var skipped = new List<Rmf2ContractPackSkipV5>();
        TranslationGeneratedOutput german = Rmf2ContractPackV5.Render(current, "de", shipped, skipped)!;
        Assert.Equal("de grid_new content-locale-changed|de grid_total unsupported-function :currency needs 0.9.0|de grid_updated unsupported-function :date style=medium needs 0.9.0",
            string.Join('|', skipped.Select(item => $"{item.Locale} {item.Key} {item.Reason}{(item.Detail is null ? "" : " " + item.Detail)}").Order(StringComparer.Ordinal)));
        using (JsonDocument pack = JsonDocument.Parse(german.GetUtf8Bytes()))
            Assert.True(!pack.RootElement.TryGetProperty("formatData", out _) && !pack.RootElement.GetProperty("messages").TryGetProperty("grid_total", out _),
                "A pack for 0.8.0 carries formatData or a :currency message.");
        // The current compiler's own artifact carries formatData: number records, and EUR for grid_total.
        using (JsonDocument plain = JsonDocument.Parse(Rmf2LocaleArtifactV5.Render(current, "de").GetUtf8Bytes()))
        {
            JsonElement formats = plain.RootElement.GetProperty("formatData");
            Assert.True(formats.TryGetProperty("numbers", out _) && formats.GetProperty("currencies").TryGetProperty("EUR", out _), "The current artifact's formatData.");
        }

        // A release whose own messages use :currency (0.9.0) keeps such messages and gets formatData with them.
        var newer = Rmf2ShippedContractV5.Parse(Rmf2LocaleArtifactV5.Render(current, "en").GetUtf8Bytes());
        Assert.Equal("0.9.0", Rmf2ReleaseFeaturesV5.Release(newer.FeatureLevel));
        skipped.Clear();
        using (JsonDocument pack = JsonDocument.Parse(Rmf2ContractPackV5.Render(current, "de", newer, skipped)!.GetUtf8Bytes()))
        {
            Assert.Equal(0, skipped.Count, string.Join('|', skipped));
            Assert.True(pack.RootElement.GetProperty("formatData").GetProperty("currencies").TryGetProperty("EUR", out _), "A 0.9.0 pack's formatData.");
        }

        string directory = Path.Combine(Path.GetTempPath(), "runic-previous-formats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Rmf2EsmCompositionTests.CopyPreviousRelease(directory);
            File.WriteAllBytes(Path.Combine(directory, "pack.de.json"), german.GetUtf8Bytes());
            File.WriteAllBytes(Path.Combine(directory, "plain.de.json"), Rmf2LocaleArtifactV5.Render(current, "de").GetUtf8Bytes());
            string script = Path.Combine(directory, "test.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { decodeLocalePack, formatDynamicMessage } from "./acme-grid.esm-v5/dynamic.js";
                const check = (condition, message) => { if (!condition) throw new Error(message); };
                const read = async name => new Uint8Array(await readFile(new URL(name, import.meta.url)));
                const pack = await read("pack.de.json");
                // 0.8.0's exact-fingerprint loader accepts the contract pack without the new functions' messages.
                const decoded = await decodeLocalePack(pack, "de");
                check(decoded.ok, JSON.stringify(decoded));
                check(formatDynamicMessage(decoded.value, "grid_rows", { count: 1 }) === "1 Zeile", "plural");
                // With formatData, which 0.9.0 added, the 0.8.0 loader rejects the whole pack, so the pack leaves it out.
                const forced = JSON.parse(new TextDecoder().decode(pack));
                forced.formatData = JSON.parse(new TextDecoder().decode(await read("plain.de.json"))).formatData;
                const rejected = await decodeLocalePack(new TextEncoder().encode(JSON.stringify(forced)), "de");
                check(!rejected.ok && rejected.reason === "RTR0023/unknown-member", JSON.stringify(rejected));
                console.log("ok");
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [script], directory);
            Rmf2EsmV5Tests.Run("node", [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    // The fixture's catalog one release later: German grid_empty fixed, grid_new newly translated, grid_updated in the medium
    // date style that 0.9 added.
    internal static Rmf2ProjectV5 CurrentOfPreviousRelease(string fixture, Func<string, string>? change = null)
    {
        string Source(string locale) => (change ?? (text => text))(File.ReadAllText(Path.Combine(fixture, "source", locale + ".rmf2")).ReplaceLineEndings("\n").Replace("style=long", "style=medium", StringComparison.Ordinal));
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", File.ReadAllBytes(Path.Combine(fixture, "source", "runic.json"))), [
            new("translations/en.rmf2", Encoding.UTF8.GetBytes(Source("en"))),
            new("translations/de.rmf2", Encoding.UTF8.GetBytes(Source("de").Replace("grid_empty = Keine Zeilen", "grid_empty = Leer", StringComparison.Ordinal) + "grid_new = Neue Spalte\n"))]);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        return result.Project!;
    }
}
