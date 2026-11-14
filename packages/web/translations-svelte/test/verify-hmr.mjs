// In-place updates of edited messages in a real dev server and Chromium (translations#176). The repository's compiler
// generates the catalogs and @runic-artifex/vite-plugin-runic-translations (its built dist) serves them to three pages:
// a plain-JS page that follows the text-change channel itself, a Svelte page through useMessageLocale, and a page that
// follows nothing. A library catalog regenerated with `dependencies: "auto"` shows the application's override. Each
// text edit must update the page without a navigation, keep its state and fetch only the edited message's module;
// structural edits and the page without a listener must reload. Finally a production build with the plugin must equal
// the plain re-export of the same generated catalog.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { svelte } from "@sveltejs/vite-plugin-svelte";
import { chromium } from "playwright-core";
import { build, createLogger, createServer } from "vite";
import { watchPage } from "./browser-page.mjs";

const repository = fileURLToPath(new URL("../../../../", import.meta.url));
const pluginEntry = resolve(repository, "packages/web/vite-plugin-runic-translations/dist/index.js");
assert.ok(existsSync(pluginEntry), "build the plugin first: (cd packages/web/vite-plugin-runic-translations && bun run build)");
const { runicTranslations } = await import(pluginEntry);
// The page imports @runic-artifex/translations-svelte through the workspace, as its published build (bun run build).
assert.ok(existsSync(fileURLToPath(new URL("../dist/index.js", import.meta.url))), "build translations-svelte first: bun run build");
const configuration = process.env.RUNIC_HMR_CONFIGURATION ?? "Release";
execFileSync("dotnet", ["build", resolve(repository, "tools/dotnet-runic-translations"), "--configuration", configuration, "--nologo", "-v", "quiet"],
  { cwd: repository, stdio: ["ignore", "ignore", "inherit"] });
const tool = resolve(repository, `tools/dotnet-runic-translations/bin/${configuration}/net10.0/dotnet-runic-translations.dll`);
assert.ok(existsSync(tool), tool);

// Inside the package, so the fixture resolves svelte and esm-env like an application that installed them.
const fixture = mkdtempSync(join(fileURLToPath(new URL(".", import.meta.url)), "hmr-fixture-"));
const write = (path, content) => { mkdirSync(dirname(join(fixture, path)), { recursive: true }); writeFileSync(join(fixture, path), content); };
const english = ({ title = "Hello", taps = "{{{$count} taps}}", extra = "" } = {}) =>
  `title = ${title}\nclicks =\n  .input {$count :integer}\n  .match $count\n  one {{One tap}}\n  * ${taps}\n${extra}`;
const german = ({ title = "Hallo", extra = "" } = {}) =>
  `title = ${title}\nclicks =\n  .input {$count :integer}\n  .match $count\n  one {{Ein Tipp}}\n  * {{{$count} Tipps}}\n${extra}`;
const pause = milliseconds => new Promise(resolveDelay => setTimeout(resolveDelay, milliseconds));
const textIs = (selector, expected) => `document.querySelector(${JSON.stringify(selector)})?.textContent === ${JSON.stringify(expected)}`;

let vite;
let browser;
// The dev server's log, shown when a check fails.
const logged = [];
let current;
try {
  // The library: its packed sources, its prebuilt catalog, and code that imports the catalog by a relative path.
  const library = "node_modules/@acme/grid";
  write(`${library}/package.json`, JSON.stringify({ name: "@acme/grid", version: "1.0.0", type: "module",
    exports: { ".": "./index.js", "./runic-translations": "./translations/acme.grid.esm-v5/web-module-manifest-v3.json" },
    runicTranslations: { catalog: "acme.grid", project: "./runic/acme.grid/runic.json" } }));
  write(`${library}/runic/acme.grid/runic.json`, JSON.stringify({ schemaVersion: 1, catalog: "acme.grid",
    code: { namespace: "Acme.Grid", className: "GridText" }, baseLocale: "en", locales: ["en", "de"] }));
  write(`${library}/runic/acme.grid/en.rmf2`, "empty = No rows\n");
  write(`${library}/runic/acme.grid/de.rmf2`, "empty = Keine Zeilen\n");
  write(`${library}/index.js`, 'import { m } from "./translations/acme.grid.esm-v5/messages.js";\nexport const empty = () => m.empty();\n');
  execFileSync("dotnet", [tool, "generate", "--project", join(fixture, library, "runic/acme.grid/runic.json"), "--output", join(fixture, library, "translations"), "--emit-esm"],
    { cwd: fixture, stdio: ["ignore", "ignore", "inherit"] });

  // The application: its catalog, an override of the library's text, and the pages.
  write("package.json", JSON.stringify({ name: "hmr-fixture", private: true, type: "module", dependencies: { "@acme/grid": "1.0.0" } }));
  write("translations/runic.json", JSON.stringify({ schemaVersion: 1, catalog: "app", code: { namespace: "App", className: "AppText" },
    baseLocale: "en", locales: ["en", "de"], overrides: [{ catalog: "acme.grid", from: "@acme/grid" }] }));
  write("translations/en.rmf2", english());
  write("translations/de.rmf2", german());
  write("translations/overrides/acme.grid/en.rmf2", "empty = Nothing to show\n");
  const page = (name, script) => write(`${name}.html`, '<!doctype html><html lang="en"><body><h1 id="title"></h1><p id="clicks"></p>' +
    `<p id="library"></p><button id="tap">tap</button><span id="count"></span><button id="load">load</button><p id="lazy"></p>` +
    `<script type="module" src="/${script}"></script></body></html>`);
  page("plain", "plain.js");
  write("plain.js", `import { m } from "virtual:runic-translations/app";
import { empty } from "@acme/grid";
let count = 0;
const render = () => {
  document.querySelector("#title").textContent = m.title();
  document.querySelector("#clicks").textContent = m.clicks({ count });
  document.querySelector("#library").textContent = empty();
  document.querySelector("#count").textContent = String(count);
};
document.querySelector("#tap").addEventListener("click", () => { count++; render(); });
document.querySelector("#load").addEventListener("click", () => import("./lazy.js"));
// The text-change channel: whoever comes first creates it.
const key = Symbol.for("runic.translations.text");
(globalThis[key] ??= { version: 1, revision: 0, listeners: new Set() }).listeners.add(render);
render();
`);
  // Loaded after an edit: Vite gives the catalog's modules a new timestamp, so it imports the updated instances.
  write("lazy.js", `import { m } from "virtual:runic-translations/app";
const render = () => { document.querySelector("#lazy").textContent = m.title(); };
globalThis[Symbol.for("runic.translations.text")].listeners.add(render);
render();
`);
  page("unfollowed", "unfollowed.js");
  write("unfollowed.js", 'import { m } from "virtual:runic-translations/app";\ndocument.querySelector("#title").textContent = m.title();\n');
  // Not svelte.js: the optimizer would take that file for the svelte package.
  write("svelte.html", '<!doctype html><html lang="en"><body><div id="app"></div><script type="module" src="/svelte-page.js"></script></body></html>');
  write("svelte-page.js", 'import { mount } from "svelte";\nimport App from "./App.svelte";\nmount(App, { target: document.querySelector("#app") });\n');
  write("App.svelte", `<script>
  import { m } from "virtual:runic-translations/app";
  import { m as grid } from "virtual:runic-translations/acme.grid";
  import { useMessageLocale } from "@runic-artifex/translations-svelte";
  let locale = $state("en");
  let count = $state(0);
  useMessageLocale(() => locale);
</script>

<h1 id="title">{m.title()}</h1>
<p id="clicks">{m.clicks({ count })}</p>
<p id="library">{grid.empty()}</p>
<button id="tap" onclick={() => count++}>tap</button>
<span id="count">{count}</span>
<button id="german" onclick={() => (locale = "de")}>de</button>
`);
  write("ssr.js", 'import { m } from "virtual:runic-translations/app";\nexport const title = () => m.title();\n');

  const logger = createLogger("info", { allowClearScreen: false });
  for (const level of ["info", "warn", "error"]) {
    const log = logger[level].bind(logger);
    logger[level] = (message, options) => { logged.push(`${level}: ${message}`); log(message, options); };
  }
  vite = await createServer({
    root: fixture, configFile: false, customLogger: logger,
    plugins: [svelte(), runicTranslations({ project: join(fixture, "translations"), cwd: fixture, dependencies: "auto", command: "dotnet", commandArguments: [tool] })],
    // The package as published (bun run build); a workspace link is not guaranteed.
    resolve: { alias: { "@runic-artifex/translations-svelte": fileURLToPath(new URL("../dist/index.js", import.meta.url)) } },
    // Every dependency is bundled before the first page loads, so the optimizer never reloads a page mid-check.
    optimizeDeps: { entries: ["plain.js", "unfollowed.js", "svelte-page.js"] },
    server: { host: "127.0.0.1", port: 0 },
  });
  await vite.listen();
  const origin = vite.resolvedUrls.local[0];
  browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || process.env.WEBUI_BROWSER_PATH, headless: true });

  // Opens a page and records its navigations and the modules it fetches with an HMR timestamp.
  async function open(name) {
    const tab = await browser.newPage();
    current = tab;
    const watch = watchPage(tab);
    const state = { navigations: 0, hot: [] };
    tab.on("framenavigated", frame => { if (frame === tab.mainFrame()) state.navigations++; });
    tab.on("request", request => { if (/[?&]t=\d+/.test(request.url())) state.hot.push(new URL(request.url()).pathname); });
    await tab.goto(`${origin}${name}`);
    await watch.waitForResult("document.querySelector('#title')?.textContent?.length > 0", `${name}: the first render`);
    await pause(300);
    state.navigations = 0;
    return {
      tab, watch, state,
      text: selector => tab.locator(selector).textContent(),
      until: (selector, expected, description) => watch.waitForResult(textIs(selector, expected), `${name}: ${description}`),
      // The watcher drops a second change of one file within 50 ms (chokidar's throttle), so saves are spaced as a person's are.
      async edit(path, content) { await pause(150); state.hot.length = 0; write(path, content); },
      fetched: () => state.hot.map(path => path.split("/").slice(-3).join("/")),
    };
  }

  // --- The plain page follows the channel itself. ---
  {
    const view = await open("plain.html");
    for (let tap = 0; tap < 3; tap++) await view.tab.click("#tap");
    assert.equal(await view.text("#clicks"), "3 taps");
    assert.equal(await view.text("#library"), "Nothing to show");
    await view.tab.evaluate(() => { document.querySelector("#title").dataset.kept = "yes"; });
    assert.equal((await vite.ssrLoadModule("/ssr.js")).title(), "Hello");

    await view.edit("translations/en.rmf2", english({ title: "Hello, edited" }));
    await view.until("#title", "Hello, edited", "the edited title");
    assert.equal(view.state.navigations, 0, "no reload");
    assert.equal(await view.text("#count"), "3", "the page's state is kept");
    assert.equal(await view.tab.evaluate(() => document.querySelector("#title").dataset.kept), "yes");
    await pause(300);
    assert.deepEqual(view.fetched(), ["app.esm-v5/messages/r_7469746c65.js"], "only the edited message's module is fetched");
    // The SSR environment swapped the same table, so the server renders the new text.
    let ssr = "";
    for (let attempt = 0; attempt < 100 && ssr !== "Hello, edited"; attempt++, await pause(50)) ssr = (await vite.ssrLoadModule("/ssr.js")).title();
    assert.equal(ssr, "Hello, edited", "the server renders the edited text");

    // A module loaded after the edit shows the edited text, and follows the next edits too.
    await view.tab.click("#load");
    await view.until("#lazy", "Hello, edited", "the lazily loaded module");

    await view.edit("translations/en.rmf2", english({ title: "Hello, edited", taps: "{{{$count} clicks}}" }));
    await view.until("#clicks", "3 clicks", "the edited plural variant");
    assert.equal(view.state.navigations, 0);

    // The application's override of the library's text, regenerated with dependencies: "auto".
    await view.edit("translations/overrides/acme.grid/en.rmf2", "empty = Nothing here\n");
    await view.until("#library", "Nothing here", "the edited library override");
    assert.equal(view.state.navigations, 0, "no reload for an override");
    assert.equal(await view.text("#count"), "3");
    await pause(300);
    assert.deepEqual(view.fetched(), ["acme.grid.esm-v5/messages/r_656d707479.js"], "only the library message's module is fetched");

    // A compile error shows Vite's overlay over the last good text; the next good save updates in place.
    await view.edit("translations/en.rmf2", "title = {\n");
    await view.watch.waitForResult("document.querySelector('vite-error-overlay') !== null", "the compile error overlay");
    assert.equal(await view.text("#title"), "Hello, edited");
    await view.edit("translations/en.rmf2", english({ title: "Hello again", taps: "{{{$count} clicks}}" }));
    await view.until("#title", "Hello again", "the text after the fix");
    await view.until("#lazy", "Hello again", "the edit in the lazily loaded module");
    await view.watch.waitForResult("document.querySelector('vite-error-overlay') === null", "the overlay to close");
    assert.equal(view.state.navigations, 0, "no reload after the fix");
    assert.equal(await view.text("#count"), "3");

    // An added message reloads, with its reason in the server's log.
    write("translations/de.rmf2", german({ extra: "added = Neu\n" }));
    write("translations/en.rmf2", english({ title: "Hello again", taps: "{{{$count} clicks}}", extra: "added = New\n" }));
    await view.watch.waitForResult(textIs("#count", "0"), "the reload after an added message");
    assert.ok(view.state.navigations >= 1, "the page reloaded");
    assert.ok(logged.some(line => line.includes("page reload: messages were added, removed or renamed in catalog 'app'")), logged.join("\n"));
    await view.tab.close();
  }

  // --- The Svelte page follows text changes through useMessageLocale. ---
  {
    const view = await open("svelte.html");
    for (let tap = 0; tap < 3; tap++) await view.tab.click("#tap");
    assert.equal(await view.text("#clicks"), "3 clicks");
    await view.tab.evaluate(() => { document.querySelector("#title").dataset.kept = "yes"; });
    await view.edit("translations/en.rmf2", english({ title: "Svelte, edited", taps: "{{{$count} clicks}}", extra: "added = New\n" }));
    await view.until("#title", "Svelte, edited", "the edited title in Svelte");
    assert.equal(view.state.navigations, 0, "no reload in Svelte");
    assert.equal(await view.text("#count"), "3", "Svelte state is kept");
    assert.equal(await view.tab.evaluate(() => document.querySelector("#title").dataset.kept), "yes", "the same element, not remounted");

    await view.edit("translations/overrides/acme.grid/en.rmf2", "empty = Nothing in Svelte\n");
    await view.until("#library", "Nothing in Svelte", "the edited library override in Svelte");
    assert.equal(view.state.navigations, 0);

    // German text while German shows.
    await view.tab.click("#german");
    await view.until("#title", "Hallo", "the German title");
    await view.edit("translations/de.rmf2", german({ title: "Hallo, bearbeitet", extra: "added = Neu\n" }));
    await view.until("#title", "Hallo, bearbeitet", "the edited German title");
    assert.equal(view.state.navigations, 0);
    assert.equal(await view.text("#count"), "3");
    assert.deepEqual(view.watch.errors, []);
    await view.tab.close();
  }

  // --- A page that follows no text changes reloads instead of showing stale text. ---
  {
    const view = await open("unfollowed.html");
    await view.edit("translations/en.rmf2", english({ title: "Reloaded", taps: "{{{$count} clicks}}", extra: "added = New\n" }));
    await view.until("#title", "Reloaded", "the text after the reload");
    assert.equal(view.state.navigations, 1, "the page reloaded once");
    await view.tab.close();
  }
  await browser.close();
  browser = undefined;
  await vite.close();
  vite = undefined;

  // --- Production: the plugin adds nothing to a build. ---
  write("entry.js", 'import { m } from "virtual:runic-translations/app";\nexport const text = count => [m.title(), m.clicks({ count })].join("|");\n');
  const generated = join(fixture, ".runic/translations/app.esm-v5");
  const reference = {
    name: "reference-re-export",
    resolveId: id => id === "virtual:runic-translations/app" ? "\0virtual:runic-translations/app/messages" : null,
    load: id => id === "\0virtual:runic-translations/app/messages" ? `export * from ${JSON.stringify(join(generated, "messages.js").split("\\").join("/"))};\n` : null,
  };
  const bundle = async (name, plugin) => {
    const outDir = join(fixture, "dist", name);
    await build({ root: fixture, configFile: false, logLevel: "silent", mode: "production", plugins: [plugin],
      build: { outDir, minify: false, lib: { entry: join(fixture, "entry.js"), formats: ["es"], fileName: () => "bundle.js" } } });
    return Object.fromEntries(readdirSync(outDir, { recursive: true }).sort().map(file => [file, readFileSync(join(outDir, file), "utf8")]));
  };
  // The plugin regenerates the catalog for the build; the reference bundles that same output.
  const withPlugin = await bundle("plugin", runicTranslations({ project: join(fixture, "translations"), cwd: fixture, command: "dotnet", commandArguments: [tool] }));
  const plain = await bundle("reference", reference);
  assert.deepEqual(withPlugin, plain, "a production build with the plugin is the plain re-export of the generated catalog");
  for (const marker of ["import.meta.hot", "runicMessageSetters", "runic.translations.text", "runic-translations:hmr"])
    assert.ok(!withPlugin["bundle.js"].includes(marker), marker);
  assert.match(withPlugin["bundle.js"], /Reloaded/);
  console.log("Vite in-place updates passed: plain JS, Svelte and a library override in Chromium, the reloads, SSR and the production build.");
} catch (error) {
  console.error(`Dev server log:\n${logged.join("\n")}`);
  const overlay = await current?.evaluate(() => document.querySelector("vite-error-overlay")?.shadowRoot?.textContent).catch(() => undefined);
  if (overlay) console.error(`Error overlay:\n${overlay}`);
  throw error;
} finally {
  await browser?.close();
  await vite?.close();
  // RUNIC_HMR_KEEP=1 keeps the fixture for debugging.
  if (process.env.RUNIC_HMR_KEEP !== "1") rmSync(fixture, { recursive: true, force: true });
}
