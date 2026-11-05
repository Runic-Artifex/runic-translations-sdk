using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// Web overlays (dynamic.js installOverlay, installLocalePack): application text and downloaded packs for a library's
// catalog, checked key by key and resolved locale-first like .NET's TranslationOverlay.
[Category("rmf2-semantic-v5"), NotInParallel]
internal sealed class Rmf2EsmOverlayTests
{
    [Test, DisplayName("RMF2 v5 ESM overlays apply locale-first, add locales and skip incompatible keys")]
    public void Overlays()
    {
        // The application's overlay project for the library's catalog: an English override, and French and Italian (added).
        Rmf2ProjectV5 overlay = Rmf2EsmV5Tests.CompileProject("acme-grid", "en", ["en", "fr", "it"], false,
            ("en", "grid_empty = Nothing here\nold_key = Removed\nwrong_inputs =\n  .input {$n :integer}\n  {{{$n}}}\nmarked = {#strong}Bold{/strong}\n"),
            ("fr", "grid_empty = Aucune ligne\nold_key = Supprimé\nwrong_inputs =\n  .input {$n :integer}\n  {{{$n}}}\nmarked = {#strong}Gras{/strong}\n"),
            ("it", "grid_empty = Nessuna riga\nold_key = Rimosso\nwrong_inputs =\n  .input {$n :integer}\n  {{{$n}}}\nmarked = Grassetto\n"));
        // Spanish with a plural: the generated en/de runtime embeds only the plural rules of its own languages.
        Rmf2ProjectV5 spanish = Rmf2EsmV5Tests.CompileProject("acme-grid", "es", ["es"], false,
            ("es", "grid_empty = Sin filas\ngrid_rows =\n  .input {$count :integer}\n  .match $count\n  one {{Una fila}}\n  * {{{$count} filas}}\n"));
        // The next library release: German text changed, too_long takes another input, and one key is new.
        Rmf2ProjectV5 next = Rmf2EsmV5Tests.CompileProject("acme-grid", "en", ["en", "de"], false,
            ("en", "grid_empty = No rows\ngrid_rows =\n  .input {$count :integer}\n  {{{$count} rows}}\ntoo_long =\n  .input {$limit :integer}\n  {{At most {$limit}}}\nadded = Added\n"),
            ("de", "grid_empty = Leer\ngrid_rows =\n  .input {$count :integer}\n  {{{$count} Zeilen}}\ntoo_long =\n  .input {$limit :integer}\n  {{Höchstens {$limit}}}\nadded = Neu\n"));
        // The library's own catalog with wrong_inputs and marked, so those keys exist with other contracts.
        IReadOnlyList<TranslationGeneratedOutput> library = Rmf2EsmCompositionTests.Library("wrong_inputs = Fixed\nmarked = Plain\n");
        string directory = Rmf2EsmV5Tests.Write(library);
        try
        {
            foreach (string locale in new[] { "en", "fr", "it" })
                File.WriteAllBytes(Path.Combine(directory, "overlay." + locale + ".json"), Rmf2LocaleArtifactV5.Render(overlay, locale).GetUtf8Bytes());
            File.WriteAllBytes(Path.Combine(directory, "overlay.es.json"), Rmf2LocaleArtifactV5.Render(spanish, "es").GetUtf8Bytes());
            File.WriteAllBytes(Path.Combine(directory, "next.de.json"), Rmf2LocaleArtifactV5.Render(next, "de").GetUtf8Bytes());
            string script = Path.Combine(directory, "test.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { m } from "./acme-grid.esm-v5/messages.js";
                import { getMessageLocale, getLocale, resolveLocale, onFallback } from "./acme-grid.esm-v5/runtime.js";
                import { installOverlay, installLocalePack, onOverlayChange } from "./acme-grid.esm-v5/dynamic.js";
                const check = (condition, message) => { if (!condition) throw new Error(message); };
                const equal = (actual, expected, message) => check(actual === expected, `${message}: ${JSON.stringify(actual)} !== ${JSON.stringify(expected)}`);
                const artifact = async name => JSON.parse(await readFile(new URL(name, import.meta.url), "utf8"));
                const reasons = result => result.value.diagnostics.map(item => `${item.locale}:${item.key}:${item.reason}`).sort().join(",");
                const de = { locale: "de" }, en = { locale: "en" }, fr = { locale: "fr" };

                equal(m.grid_empty(de), "Keine Zeilen", "before");
                let changes = 0; const stop = onOverlayChange(() => changes++);

                // English override: English changes; German still shows the library's German (locale-first).
                const english = installOverlay(await artifact("overlay.en.json"));
                check(english.ok, JSON.stringify(english));
                equal(english.value.locale, "en", "locale"); equal(english.value.messages, 1, "applied messages");
                equal(reasons(english), "en:marked:incompatible-markup,en:old_key:unknown-key,en:wrong_inputs:incompatible-inputs", "en diagnostics");
                equal(m.grid_empty(en), "Nothing here", "en override"); equal(m.grid_empty(de), "Keine Zeilen", "de keeps the library");
                equal(m.grid_empty({ locale: "de-AT" }), "Keine Zeilen", "de-AT through de");
                equal(m.wrong_inputs(en), "Fixed", "skipped key keeps the library text"); equal(m.marked(en), "Plain", "markup skipped");
                equal(changes, 1, "listener");

                // French is added: it resolves, its keys apply, the others fall back to the base locale.
                check(resolveLocale("fr-CA") !== "fr", "not added yet");
                const french = installOverlay(await artifact("overlay.fr.json"));
                check(french.ok, "fr installed");
                equal(resolveLocale("fr"), "fr", "added locale"); equal(resolveLocale("fr-CA"), "fr", "added parent");
                equal(m.grid_empty(fr), "Aucune ligne", "fr text");
                equal(m.grid_rows({ count: 1234 }, fr), "1,234 rows", "fr falls back to the base locale, and the override does not apply to it");
                equal(m.grid_empty({ locale: "fr" }), "Aucune ligne", "fr");
                equal(getMessageLocale("grid_empty", fr).locale, "fr", "content locale fr");
                equal(getMessageLocale("grid_rows", fr).locale, "en", "content locale en");
                const fallbacks = []; const unlisten = onFallback((key, locale, content) => fallbacks.push(`${key}:${locale}:${content}`));
                m.too_long({ maximum: 3 }, fr); unlisten(); equal(fallbacks.join(), "too_long:fr:en", "fallback reported");

                // Italian falls back to German, as its install names.
                const italian = installOverlay(await artifact("overlay.it.json"), { fallbackLocale: "de" });
                check(italian.ok, "it installed");
                equal(m.grid_empty({ locale: "it" }), "Nessuna riga", "it text"); equal(m.too_long({ maximum: 3 }, { locale: "it" }), "Höchstens 3 Zeichen", "it falls back to de");
                let threw = false; try { installOverlay(await artifact("overlay.it.json"), { fallbackLocale: "en" }); } catch (error) { threw = error instanceof RangeError; } check(threw, "conflicting fallback");
                threw = false; try { installOverlay(await artifact("overlay.it.json"), { fallbackLocale: "xx" }); } catch (error) { threw = error instanceof RangeError; } check(threw, "undeclared fallback");

                // Spanish: the plural message needs Spanish plural rules, which an en/de runtime does not embed.
                const spanish = installOverlay(await artifact("overlay.es.json"));
                check(spanish.ok, "es installed"); equal(m.grid_empty({ locale: "es" }), "Sin filas", "es text");
                const skipped = spanish.value.diagnostics.some(item => item.key === "grid_rows" && item.reason === "unsupported-locale-data");
                // TODO(#131): only the catalog's languages are embedded from #131 on; assert the skip unconditionally then.
                equal(m.grid_rows({ count: 2 }, { locale: "es" }), skipped ? "2 rows" : "2 filas", "es plural");
                spanish.value.remove();

                // A pack of the next library release: shared keys apply, the rest are reported.
                const bytes = new Uint8Array(await readFile(new URL("next.de.json", import.meta.url)));
                const rejected = await installLocalePack(bytes, { locale: "de", integrityVerifier: () => false });
                equal(rejected.reason, "RTR0023/integrity-rejected", "integrity");
                equal((await installLocalePack(bytes, { locale: "fr" })).reason, "RTR0023/locale-mismatch", "expected locale");
                const pack = await installLocalePack(bytes, { locale: "de", integrityVerifier: async content => content.byteLength === bytes.byteLength });
                check(pack.ok, JSON.stringify(pack));
                check(pack.value.contractFingerprint.startsWith("sha256:"), "fingerprint");
                equal(reasons(pack), "de:added:unknown-key,de:too_long:incompatible-inputs", "next release diagnostics");
                equal(m.grid_empty(de), "Leer", "pack text"); equal(m.too_long({ maximum: 5 }, de), "Höchstens 5 Zeichen", "incompatible key keeps the library");

                // A later install of a locale wins per key; removing it restores the earlier one.
                const second = installOverlay({ ...(await artifact("overlay.en.json")), messages: { grid_empty: { contentLocale: "en", ast: (await artifact("overlay.en.json")).messages.grid_empty.ast } } });
                check(second.ok, "second en");
                pack.value.remove(); equal(m.grid_empty(de), "Keine Zeilen", "pack removed"); pack.value.remove();
                french.value.remove(); check(getLocale() === "en", "base");
                threw = false; try { resolveLocale("fr"); } catch { threw = true; }
                check(threw || resolveLocale("fr") === "en", "fr no longer added");
                second.value.remove(); english.value.remove(); equal(m.grid_empty(en), "No rows", "all removed");
                stop();

                // Structural failures are not installed.
                const base = await artifact("overlay.en.json");
                equal(installOverlay({ ...base, catalog: "other" }).reason, "RTR0023/catalog-mismatch", "catalog");
                equal(installOverlay({ ...base, extra: 1 }).reason, "RTR0023/unknown-member", "member");
                equal(installOverlay({ ...base, markupContract: { version: 1 } }).reason, "RTR0023/markup-contract-version-mismatch", "markup version");
                equal(installOverlay({ ...base, messages: { grid_empty: { contentLocale: "de", ast: base.messages.grid_empty.ast } } }).reason, "RTR0023/argument-contract-mismatch", "content locale");
                equal(installOverlay(null).ok, false, "null");
                console.log("ok");
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [script], directory);
            Rmf2EsmV5Tests.Run("node", [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }
}
