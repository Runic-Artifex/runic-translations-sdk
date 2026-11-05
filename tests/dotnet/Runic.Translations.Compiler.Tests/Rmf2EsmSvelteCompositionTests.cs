using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// An application catalog and a library catalog on one SvelteKit page: the request handle's one runWithLocale renders both
// in the request's locale on the server, and useMessageLocale(locale) keeps both in it after hydration, so library text
// never renders in its base locale first and flips. Runs the generated ESM with the built translations-svelte and
// translations-sveltekit packages (bun run build), in the sveltekit package's vitest and happy-dom.
[Category("rmf2-semantic-v5"), NotInParallel]
internal sealed class Rmf2EsmSvelteCompositionTests
{
    [Test, DisplayName("RMF2 v5 ESM app and library catalogs render one locale through SvelteKit SSR and hydration")]
    public void SsrAndHydration()
    {
        string package = RepositoryPaths.Resolve("packages", "web", "translations-sveltekit");
        // Inside the package, so svelte, vite, vitest and @runic-artifex/translations-svelte resolve from its node_modules.
        string directory = Path.Combine(package, "test", "generated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (TranslationGeneratedOutput output in Rmf2EsmCompositionTests.App().Concat(Rmf2EsmCompositionTests.Library()))
            {
                string path = Path.Combine(directory, output.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, output.GetUtf8Bytes());
            }

            Write(directory, "vite.config.js", """
                import { svelte } from "@sveltejs/vite-plugin-svelte";
                import { defineConfig } from "vitest/config";
                export default defineConfig({ plugins: [svelte()], resolve: { conditions: ["browser"] }, test: { environment: "node" } });
                """);
            // follow: whether the page calls useMessageLocale; the server must not need it.
            Write(directory, "Page.svelte", """
                <script>
                  import { m as app } from "./shop.esm-v5/messages.js";
                  import { m as grid } from "./acme-grid.esm-v5/messages.js";
                  import { useMessageLocale } from "@runic-artifex/translations-svelte";
                  let { requested, follow, onRender } = $props();
                  // svelte-ignore state_referenced_locally
                  if (follow) useMessageLocale(() => requested);
                  // The server reports what the shared channel holds while it renders: nothing, as it installs no resolver.
                  onRender?.(globalThis[Symbol.for("runic.translations.locale")]?.resolver ?? null);
                </script>

                <h1>{app.title()}</h1>
                <p data-grid>{grid.grid_empty()}</p>
                <p data-rows>{grid.grid_rows({ count: 1234 })}</p>
                """);
            Write(directory, "composition.test.js", """
                // @vitest-environment happy-dom
                import { flushSync, hydrate, unmount } from "svelte";
                import { resolve } from "node:path";
                import { createServer } from "vite";
                import { afterAll, expect, test } from "vitest";
                import Page from "./Page.svelte";
                import { createLocaleHandle, createLocaleRouting, requestedLocaleFromLocals } from "../../dist/translations/index.js";

                const root = import.meta.dirname;
                const routing = createLocaleRouting({ locales: ["en", "de", "fr"], baseLocale: "en", resolutionOrder: ["application", "url"] });
                let starting;
                afterAll(async () => (await starting)?.close());

                // seen collects what the shared channel held during each server render; storage: false leaves runWithLocale out.
                async function serverRender(requestedTag, follow, { delay = 5, seen = [], storage = true } = {}) {
                  // One Vite server for every render, started once even when renders run concurrently.
                  starting ??= createServer({ root, configFile: resolve(root, "vite.config.js"), cacheDir: resolve(root, ".vite"),
                    appType: "custom", server: { middlewareMode: true, hmr: false, ws: false } });
                  const server = await starting;
                  const { default: ServerPage } = await server.ssrLoadModule("/Page.svelte");
                  const { render } = await server.ssrLoadModule("svelte/server");
                  // Only the application's runWithLocale: the library's catalog reads the same request storage.
                  const { runWithLocale } = await server.ssrLoadModule("/shop.esm-v5/server.js");
                  const handle = createLocaleHandle(routing, { runWithLocale: storage ? runWithLocale : undefined, cookie: false, canonicalRedirect: false, applicationLocale: () => requestedTag });
                  const locals = {};
                  const event = { url: new URL("http://localhost/"), request: new Request("http://localhost/"), locals, cookies: { get: () => undefined, set() {} } };
                  const response = await handle({ event, resolve: async () => {
                    // A request handler awaits before rendering; the request's locale survives it.
                    await new Promise(done => setTimeout(done, delay));
                    const requested = requestedLocaleFromLocals(locals, routing);
                    return new Response(render(ServerPage, { props: { requested, follow, onRender: resolver => seen.push(resolver) } }).body);
                  } });
                  return { body: await response.text(), requested: requestedLocaleFromLocals(locals, routing) };
                }

                const text = () => [...document.querySelectorAll("h1, p")].map(node => node.textContent).join("|");

                test("the server renders both catalogs in the request's locale, and hydration keeps it", async () => {
                  const rendered = await serverRender("de-CH", true);
                  expect(rendered.requested).toBe("de-CH");
                  document.body.innerHTML = rendered.body;
                  const serverText = text();
                  expect(serverText).toMatch(/^Laden\|Keine Zeilen\|1['’]234 Zeilen$/);

                  const heading = document.querySelector("h1"), grid = document.querySelector("[data-grid]");
                  const component = hydrate(Page, { target: document.body, props: { requested: rendered.requested, follow: true } });
                  flushSync();
                  // No flip: the same nodes and the same text, library included.
                  expect(document.querySelector("h1")).toBe(heading);
                  expect(document.querySelector("[data-grid]")).toBe(grid);
                  expect(text()).toBe(serverText);
                  await unmount(component);
                  expect(globalThis[Symbol.for("runic.translations.locale")].resolver).toBe(null);
                }, 60_000);

                test("runWithLocale alone sets the library's server locale", async () => {
                  const rendered = await serverRender("de", false);
                  document.body.innerHTML = rendered.body;
                  expect(text()).toBe("Laden|Keine Zeilen|1.234 Zeilen");
                  // A locale only the application declares: the library resolves it under its own policy.
                  const french = await serverRender("fr", false);
                  document.body.innerHTML = french.body;
                  expect(text()).toBe("Boutique|No rows|1,234 rows");
                }, 60_000);

                test("concurrent server renders keep their own locale, and the server installs no process-wide resolver", async () => {
                  const seen = [];
                  // Interleaved: the German request starts first and renders last.
                  const [german, french, english] = await Promise.all([
                    serverRender("de", true, { delay: 30, seen }), serverRender("fr", true, { delay: 1, seen }), serverRender("en", true, { delay: 15, seen })]);
                  const textOf = body => { document.body.innerHTML = body; return text(); };
                  expect(textOf(german.body)).toBe("Laden|Keine Zeilen|1.234 Zeilen");
                  expect(textOf(french.body)).toBe("Boutique|No rows|1,234 rows");
                  expect(textOf(english.body)).toBe("Shop|No rows|1,234 rows");
                  expect(seen).toEqual([null, null, null]);
                  expect(globalThis[Symbol.for("runic.translations.locale")]?.resolver ?? null).toBe(null);
                }, 60_000);

                test("a server without runWithLocale renders the base locale; useMessageLocale does not set it", async () => {
                  const seen = [];
                  const rendered = await serverRender("de", true, { seen, storage: false });
                  document.body.innerHTML = rendered.body;
                  expect(text()).toBe("Shop|No rows|1,234 rows");
                  expect(seen).toEqual([null]);
                }, 60_000);
                """);
            Rmf2EsmV5Tests.Run("node", [Path.Combine(package, "node_modules", "vitest", "vitest.mjs"), "run", "--root", directory, "--config", Path.Combine(directory, "vite.config.js")], package);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Write(string directory, string name, string text) =>
        File.WriteAllText(Path.Combine(directory, name), text, new UTF8Encoding(false));
}
