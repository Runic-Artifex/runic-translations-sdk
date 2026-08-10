import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { promisify } from "node:util";
import { build } from "vite";
import { runicTranslations } from "../index.js";

const execFileAsync = promisify(execFile);

test("resolves generated entrypoints and declares watch inputs", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-"));
  const generated = join(root, "app.esm");
  await mkdir(generated);
  await writeFile(join(generated, "messages.js"), "export const value = 1;\n");
  await writeFile(join(generated, "runtime.js"), "export const locale = 'en';\n");
  const manifest = join(generated, "web-module-manifest-v1.json");
  await writeFile(manifest, JSON.stringify({
    webModuleManifestVersion: 1,
    esmAbiVersion: 2,
    catalog: "app",
    entrypoints: { messages: "messages.js", runtime: "runtime.js", types: "messages.d.ts" },
    assets: [
      { path: "messages.js" },
      { path: "runtime.js" },
    ],
  }));
  const source = join(root, "en.json");
  await writeFile(source, "{}");
  const plugin = runicTranslations({ manifest, sourceFiles: [source] });
  const watched = [];
  await plugin.buildStart.call({ addWatchFile(path) { watched.push(path); } });
  assert.ok(watched.includes(manifest));
  assert.ok(watched.includes(source));
  const id = await plugin.resolveId("virtual:runic-translations/app");
  assert.equal(id, "\0virtual:runic-translations/app/messages");
  const module = await plugin.load(id);
  assert.match(module, /export \* from .*app\.esm\/messages\.js/);
});

test("rejects manifest paths that escape the generated root", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-hostile-"));
  const manifest = join(root, "web-module-manifest-v1.json");
  await writeFile(manifest, JSON.stringify({
    webModuleManifestVersion: 1,
    esmAbiVersion: 2,
    catalog: "app",
    entrypoints: { messages: "../messages.js", runtime: "runtime.js" },
    assets: [],
  }));
  const plugin = runicTranslations({ manifest });
  await assert.rejects(() => plugin.resolveId("virtual:runic-translations/app"), /escapes/);
});

test("runs the pinned compiler workflow before loading generated modules", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-compiler-"));
  try {
    const generated = join(root, "generated", "app.esm");
    await mkdir(generated, { recursive: true });
    await writeFile(join(generated, "messages.js"), "export const m = {};\n");
    await writeFile(join(generated, "runtime.js"), "export const locale = 'en';\n");
    await writeFile(join(generated, "transport.js"), "export const version = 1;\n");
    await writeFile(join(generated, "dynamic.js"), "export const version = 2;\n");
    const manifest = join(generated, "web-module-manifest-v1.json");
    await writeFile(manifest, JSON.stringify({
      webModuleManifestVersion: 1,
      esmAbiVersion: 2,
      catalog: "app",
      entrypoints: { messages: "messages.js", runtime: "runtime.js", types: "messages.d.ts", dynamic: "dynamic.js" },
      assets: [
        { path: "messages.js" }, { path: "runtime.js" }, { path: "transport.js" }, { path: "dynamic.js" },
      ],
    }));
    const catalog = join(root, "app.catalog.json");
    const document = join(root, "app.en.json");
    const calls = join(root, "compiler-calls.txt");
    await writeFile(catalog, "{}\n");
    await writeFile(document, "{}\n");
    const compiler = join(root, "compiler.mjs");
    await writeFile(compiler, `import { appendFile } from "node:fs/promises"; await appendFile(${JSON.stringify(calls)}, process.argv.slice(2).join("|") + "\\n");\n`);
    const plugin = runicTranslations({
      manifest,
      compiler: {
        catalog,
        documents: [document],
        output: join(root, "generated"),
        command: process.execPath,
        commandArguments: [compiler],
      },
    });
    const watched = [];
    await plugin.buildStart.call({ addWatchFile(path) { watched.push(path); } });
    assert.ok(watched.includes(catalog));
    assert.ok(watched.includes(document));
    await plugin.handleHotUpdate({
      file: document,
      server: { moduleGraph: { getModuleById() { return undefined; }, invalidateModule() {} } },
    });
    const invocations = (await readFile(calls, "utf8")).trim().split("\n");
    assert.equal(invocations.length, 2);
    for (const invocation of invocations) {
      assert.match(invocation, /^generate\|--catalog\|/);
      assert.match(invocation, /\|--documents\|/);
      assert.match(invocation, /\|--output\|/);
      assert.match(invocation, /\|--emit-esm$/);
    }
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("Vite production build tree-shakes unrelated generated messages", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-production-"));
  try {
    const generated = join(root, "generated", "app.esm");
    const messages = join(generated, "messages");
    await mkdir(messages, { recursive: true });
    await writeFile(join(messages, "used.js"), "export const internalUsed = () => 'USED_MESSAGE';\n");
    await writeFile(join(messages, "unused.js"), "export const internalUnused = () => 'UNRELATED_MESSAGE_SENTINEL';\n");
    await writeFile(join(messages, "_index.js"), "export { internalUsed as used } from './used.js';\nexport { internalUnused as unused } from './unused.js';\n");
    await writeFile(join(generated, "messages.js"), "export * as m from './messages/_index.js';\n");
    await writeFile(join(generated, "runtime.js"), "export const locale = 'en';\n");
    await writeFile(join(generated, "transport.js"), "export const version = 1;\n");
    const manifest = join(generated, "web-module-manifest-v1.json");
    await writeFile(manifest, JSON.stringify({
      webModuleManifestVersion: 1,
      esmAbiVersion: 2,
      catalog: "app",
      entrypoints: { messages: "messages.js", runtime: "runtime.js", types: "messages.d.ts" },
      assets: [
        { path: "messages.js" }, { path: "messages/_index.js" }, { path: "messages/used.js" }, { path: "messages/unused.js" },
        { path: "runtime.js" }, { path: "transport.js" },
      ],
    }));
    const entry = join(root, "main.js");
    await writeFile(entry, "import { m } from 'virtual:runic-translations/app'; export const result = m.used();\n");
    const outDir = join(root, "dist");
    await build({
      configFile: false,
      logLevel: "silent",
      plugins: [runicTranslations({ manifest })],
      build: { outDir, minify: false, lib: { entry, formats: ["es"], fileName: () => "bundle.js" } },
    });
    const bundle = await readFile(join(outDir, "bundle.js"), "utf8");
    assert.match(bundle, /USED_MESSAGE/);
    assert.doesNotMatch(bundle, /UNRELATED_MESSAGE_SENTINEL/);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("source changes invalidate every loaded Runic virtual module for HMR", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-hmr-"));
  try {
    const generated = join(root, "app.esm");
    await mkdir(generated);
    for (const name of ["messages.js", "runtime.js", "transport.js", "dynamic.js"])
      await writeFile(join(generated, name), "export {};\n");
    const manifest = join(generated, "web-module-manifest-v1.json");
    await writeFile(manifest, JSON.stringify({
      webModuleManifestVersion: 1, catalog: "app",
      esmAbiVersion: 2,
      entrypoints: { messages: "messages.js", runtime: "runtime.js", types: "messages.d.ts" },
      assets: [{ path: "messages.js" }, { path: "runtime.js" }, { path: "transport.js" }, { path: "dynamic.js" }],
    }));
    const source = join(root, "en.json");
    await writeFile(source, "{}\n");
    const plugin = runicTranslations({ manifest, sourceFiles: [source] });
    await plugin.buildStart.call({ addWatchFile() {} });
    const ids = ["messages", "runtime", "transport", "dynamic"].map(kind => `\0virtual:runic-translations/app/${kind}`);
    const modules = new Map(ids.map(id => [id, { id }]));
    const invalidated = [];
    const result = await plugin.handleHotUpdate({
      file: source,
      server: { moduleGraph: { getModuleById: id => modules.get(id), invalidateModule: module => invalidated.push(module.id) } },
    });
    assert.deepEqual(result.map(module => module.id), ids);
    assert.deepEqual(invalidated, ids);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("published package inventory contains only declared runtime files", async () => {
  const packageRoot = new URL("..", import.meta.url);
  const { stdout } = await execFileAsync("npm", ["pack", "--dry-run", "--json"], { cwd: packageRoot });
  const files = JSON.parse(stdout)[0].files.map(file => file.path).sort();
  assert.deepEqual(files, ["README.md", "index.d.ts", "index.js", "package.json"]);
});
