using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// An application catalog and a library catalog in one page or server process: the shared locale channel, one request
// storage for runWithLocale, and references of both catalogs resolved by one resolver.
[Category("rmf2-semantic-v5"), NotInParallel]
internal sealed class Rmf2EsmCompositionTests
{
    private const string AppEnglish = "title = Shop\ntotal =\n  .input {$amount :number}\n  {{Total {$amount}}}\nsaved = Saved {$name}\n";
    private const string AppGerman = "title = Laden\ntotal =\n  .input {$amount :number}\n  {{Summe {$amount}}}\nsaved = {$name} gespeichert\n";
    private const string AppFrench = "title = Boutique\ntotal =\n  .input {$amount :number}\n  {{Total {$amount}}}\nsaved = {$name} enregistré\n";
    private const string LibraryEnglish = "grid_empty = No rows\ngrid_rows =\n  .input {$count :integer}\n  {{{$count} rows}}\ntoo_long =\n  .input {$maximum :integer}\n  {{At most {$maximum} characters}}\n";
    private const string LibraryGerman = "grid_empty = Keine Zeilen\ngrid_rows =\n  .input {$count :integer}\n  {{{$count} Zeilen}}\ntoo_long =\n  .input {$maximum :integer}\n  {{Höchstens {$maximum} Zeichen}}\n";

    internal static IReadOnlyList<TranslationGeneratedOutput> App() => TranslationOutputRenderer.RenderRmf2V5EsmModules(
        Rmf2EsmV5Tests.CompileProject("shop", "en", ["en", "de", "fr"], false, ("en", AppEnglish), ("de", AppGerman), ("fr", AppFrench)));

    internal static IReadOnlyList<TranslationGeneratedOutput> Library(string? extra = null) => TranslationOutputRenderer.RenderRmf2V5EsmModules(
        Rmf2EsmV5Tests.CompileProject("acme-grid", "en", ["en", "de"], false, ("en", LibraryEnglish + extra), ("de", LibraryGerman + extra)));

    [Test, DisplayName("RMF2 v5 ESM catalogs follow one shared locale, and one runWithLocale per request")]
    public void SharedLocale()
    {
        string directory = Rmf2EsmV5Tests.Write(App().Concat(Library()).ToArray());
        try
        {
            string script = Path.Combine(directory, "test.mjs");
            File.WriteAllText(script, """
                import { m as app } from "./shop.esm-v5/messages.js";
                import { m as grid } from "./acme-grid.esm-v5/messages.js";
                import * as appRuntime from "./shop.esm-v5/runtime.js";
                import * as gridRuntime from "./acme-grid.esm-v5/runtime.js";
                import { runWithLocale, getRequestLocale } from "./shop.esm-v5/server.js";
                import * as gridServer from "./acme-grid.esm-v5/server.js";
                const check = (condition, message) => { if (!condition) throw new Error(message); };
                const equal = (actual, expected, message) => check(actual === expected, `${message}: ${JSON.stringify(actual)} !== ${JSON.stringify(expected)}`);

                // Nothing set: the base locale (no document in Bun).
                equal(app.title(), "Shop", "app base"); equal(grid.grid_empty(), "No rows", "library base");

                // One shared resolver drives both catalogs; each resolves the requested tag under its own locales.
                let requested = "de-CH";
                const remove = appRuntime.configureSharedLocaleResolver(() => requested);
                equal(app.title(), "Laden", "app de"); equal(grid.grid_empty(), "Keine Zeilen", "library de");
                equal(appRuntime.getLocale(), "de", "app locale"); equal(gridRuntime.getLocale(), "de", "library locale");
                equal(gridRuntime.getRequestedLocale(), "de-CH", "library requested");
                // Numbers follow the requested region in both catalogs.
                // Swiss grouping (an apostrophe, U+2019 or U+0027 by ICU version), not German "1.234".
                const swiss = /^1['’]234/;
                check(swiss.test(app.total({ amount: "1234.5" }).slice(6)), "app Swiss number: " + app.total({ amount: "1234.5" }));
                check(swiss.test(grid.grid_rows({ count: 1234 })), "library Swiss number: " + grid.grid_rows({ count: 1234 }));
                requested = "fr";
                equal(app.title(), "Boutique", "app fr"); equal(grid.grid_empty(), "No rows", "library fr falls back under its policy");

                // A catalog's own resolver wins over the shared one, for that catalog only.
                const own = gridRuntime.configureLocaleResolver(() => "de");
                equal(grid.grid_empty(), "Keine Zeilen", "library own resolver"); equal(app.title(), "Boutique", "app keeps the shared locale");
                own(); equal(grid.grid_empty(), "No rows", "own resolver removed");

                // The channel is one object on globalThis, shaped as the runtimes' protocol.
                const channel = globalThis[Symbol.for("runic.translations.locale")];
                check(channel.version === 1 && typeof channel.resolver === "function", "channel shape");
                remove(); equal(channel.resolver, null, "shared resolver removed"); equal(app.title(), "Shop", "app back to base");

                // One storage for the process: the application's runWithLocale is also the library's request locale, and
                // concurrent requests keep their own locale across awaits.
                check(gridServer.runWithLocale !== runWithLocale, "two server modules");
                const shared = appRuntime.configureSharedLocaleResolver(() => "fr");
                const render = async (locale, requestedLocale) => runWithLocale(locale, async () => {
                  const before = [app.title(), grid.grid_empty()];
                  await new Promise(resolve => setTimeout(resolve, locale === "de" ? 20 : 5));
                  return [...before, app.title(), grid.grid_empty(), grid.grid_rows({ count: 1234 }), getRequestLocale(), gridServer.getRequestLocale()];
                }, { requestedLocale });
                const [german, english] = await Promise.all([render("de", "de-CH"), render("en")]);
                check(swiss.test(german[4]), "German request formats Swiss numbers: " + german[4]); german.splice(4, 1);
                equal(german.join("|"), "Laden|Keine Zeilen|Laden|Keine Zeilen|de|de", "German request");
                equal(english.join("|"), "Shop|No rows|Shop|No rows|1,234 rows|en|en", "English request");
                // The library's runWithLocale is the application's too.
                equal(gridServer.runWithLocale("de", () => app.title()), "Laden", "library runWithLocale drives the app");
                // A request locale the library lacks: each catalog resolves it.
                equal(runWithLocale("fr", () => `${app.title()} ${grid.grid_empty()} ${gridServer.getRequestLocale()}`), "Boutique No rows en", "fr request");
                // Outside a request, the shared resolver applies again.
                equal(app.title(), "Boutique", "outside requests");
                shared();

                // A channel written by another runtime copy is reused, not replaced, and an unknown value is replaced.
                check(globalThis[Symbol.for("runic.translations.locale")] === channel, "one channel");
                console.log("ok");
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [script], directory);
            Rmf2EsmV5Tests.Run("node", [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("RMF2 v5 ESM createReferenceResolver resolves references of several catalogs")]
    public void ReferenceResolver()
    {
        // The library's ViewModels (NuGet) are one release ahead of its Views (npm): an added message changes the
        // fingerprint, and references of unchanged keys still translate.
        string directory = Rmf2EsmV5Tests.Write(App().Concat(Library()).ToArray());
        string newer = Rmf2EsmV5Tests.Write(Library("added = Added\n"));
        try
        {
            Directory.Move(Path.Combine(newer, "acme-grid.esm-v5"), Path.Combine(directory, "acme-grid-next.esm-v5"));
            string script = Path.Combine(directory, "test.mjs");
            File.WriteAllText(script, """
                import { m as app } from "./shop.esm-v5/messages.js";
                import { m as grid } from "./acme-grid.esm-v5/messages.js";
                import * as appTransport from "./shop.esm-v5/transport.js";
                import * as gridTransport from "./acme-grid.esm-v5/transport.js";
                import * as nextTransport from "./acme-grid-next.esm-v5/transport.js";
                const check = (condition, message) => { if (!condition) throw new Error(message); };
                const equal = (actual, expected, message) => check(actual === expected, `${message}: ${JSON.stringify(actual)} !== ${JSON.stringify(expected)}`);

                equal(appTransport.catalog, "shop", "catalog export"); equal(gridTransport.catalog, "acme-grid", "library catalog export");
                check(nextTransport.contractFingerprint !== gridTransport.contractFingerprint, "the releases differ");
                const resolve = gridTransport.createReferenceResolver([{ transport: appTransport, messages: app }, { transport: gridTransport, messages: grid }]);
                const de = { locale: "de" };

                const appReference = appTransport.createTextReference("saved", { name: "Ada" });
                const own = resolve(appReference, de);
                equal(own.outcome, "translated", "app outcome"); equal(own.text, "Ada gespeichert", "app text"); equal(own.diagnostics.length, 0, "app diagnostics");
                const library = resolve(JSON.stringify(gridTransport.createTextReference("too_long", { maximum: 80 })), de);
                equal(library.text, "Höchstens 80 Zeichen", "library reference as JSON text");

                // A reference written by the newer library release: checked key by key.
                const skewed = resolve(nextTransport.createTextReference("too_long", { maximum: 80 }, { fallbackText: "At most 80 characters" }), de);
                equal(skewed.outcome, "translated", "skewed outcome"); equal(skewed.text, "Höchstens 80 Zeichen", "skewed text");
                equal(skewed.diagnostics.map(item => item.reason).join(), "contract-changed", "skewed diagnostic");
                const added = resolve(nextTransport.createTextReference("added", {}, { fallbackText: "Added" }), de);
                equal(added.outcome, "fallback", "a key this release lacks shows its fallback"); equal(added.text, "Added", "added fallback");

                // A catalog that is not listed, and a malformed value.
                const other = resolve({ ...appReference, catalog: "other" });
                equal(other.outcome, "failed", "unlisted catalog without fallback text"); equal(other.diagnostics[0].reason, "catalog-mismatch", "unlisted reason");
                equal(resolve(JSON.stringify({ ...appReference, catalog: "other", fallbackText: "Saved" })).text, "Saved", "unlisted catalog fallback");
                equal(resolve("{").outcome, "failed", "malformed JSON");
                equal(resolve(42).outcome, "failed", "not a reference");

                for (const bad of [[], [{ transport: appTransport }], [{ transport: appTransport, messages: app }, { transport: appTransport, messages: app }], null])
                  { let threw = false; try { gridTransport.createReferenceResolver(bad); } catch (error) { threw = error instanceof TypeError; } check(threw, "rejects " + JSON.stringify(bad)); }
                console.log("ok");
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [script], directory);
        }
        finally
        {
            Directory.Delete(directory, true);
            Directory.Delete(newer, true);
        }
    }
}
