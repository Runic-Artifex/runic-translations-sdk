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
}
