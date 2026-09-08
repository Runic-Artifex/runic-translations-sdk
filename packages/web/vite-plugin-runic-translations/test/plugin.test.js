import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { EventEmitter } from "node:events";
import { createHash } from "node:crypto";
import { mkdtemp, mkdir, readFile, readdir, rename, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";
import { promisify } from "node:util";
import { build } from "vite";
import { runicTranslations } from "../index.js";

const execFileAsync = promisify(execFile);
const fingerprint = `sha256:${"a".repeat(64)}`;

async function writeGeneratedManifest(path, document) {
  const root = dirname(path);
  const assets = await Promise.all(document.assets.map(async asset => {
    const content = await readFile(join(root, asset.path));
    return { ...asset, sha256: createHash("sha256").update(content).digest("hex"), byteLength: content.byteLength, mediaType: asset.mediaType ?? "text/javascript" };
  }));
  await writeFile(path, JSON.stringify({ ...document, contractFingerprint: fingerprint, assets }));
}

test("resolves generated entrypoints and declares watch inputs", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-"));
  const generated = join(root, "app.esm");
  await mkdir(generated);
  await writeFile(join(generated, "messages.js"), "export const value = 1;\n");
  await writeFile(join(generated, "runtime.js"), `export const contractFingerprint = ${JSON.stringify(fingerprint)};\nexport const locale = 'en';\n`);
  const manifest = join(generated, "web-module-manifest-v1.json");
  await writeGeneratedManifest(manifest, {
    webModuleManifestVersion: 1,
    esmAbiVersion: 3,
    catalog: "app",
    entrypoints: { messages: "messages.js", runtime: "runtime.js", types: "messages.d.ts" },
    assets: [
      { path: "messages.js" },
      { path: "runtime.js" },
    ],
  });
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
    esmAbiVersion: 3,
    catalog: "app",
    contractFingerprint: fingerprint,
    entrypoints: { messages: "../messages.js", runtime: "runtime.js" },
    assets: [{ path: "../messages.js", sha256: "a".repeat(64), byteLength: 0, mediaType: "text/javascript" }],
  }));
  const plugin = runicTranslations({ manifest });
  await assert.rejects(() => plugin.resolveId("virtual:runic-translations/app"), /escapes/);
});

test("rejects stale assets and forged generated manifest fingerprints", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-integrity-"));
  try {
    const generated = join(root, "app.esm");
    await mkdir(generated);
    await writeFile(join(generated, "messages.js"), "export const m = {};\n");
    await writeFile(join(generated, "runtime.js"), `export const contractFingerprint = ${JSON.stringify(fingerprint)};\n`);
    const manifest = join(generated, "web-module-manifest-v1.json");
    const document = { webModuleManifestVersion: 1, esmAbiVersion: 3, catalog: "app", entrypoints: { messages: "messages.js", runtime: "runtime.js" }, assets: [{ path: "messages.js" }, { path: "runtime.js" }] };
    await writeGeneratedManifest(manifest, document);
    await writeFile(join(generated, "messages.js"), "export const m = { stale: true };\n");
    await assert.rejects(() => runicTranslations({ manifest }).buildStart.call({ addWatchFile() {} }), /integrity/);
    await writeFile(join(generated, "messages.js"), "export const m = {};\n");
    await writeGeneratedManifest(manifest, document);
    const forged = JSON.parse(await readFile(manifest, "utf8"));
    forged.contractFingerprint = `sha256:${"0".repeat(64)}`;
    await writeFile(manifest, JSON.stringify(forged));
    await assert.rejects(() => runicTranslations({ manifest }).buildStart.call({ addWatchFile() {} }), /fingerprint/);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("runs the pinned compiler workflow before loading generated modules", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-compiler-"));
  try {
    const generated = join(root, "generated", "app.esm");
    await mkdir(generated, { recursive: true });
    await writeFile(join(generated, "messages.js"), "export const m = {};\n");
    await writeFile(join(generated, "runtime.js"), `export const contractFingerprint = ${JSON.stringify(fingerprint)};\nexport const locale = 'en';\n`);
    await writeFile(join(generated, "transport.js"), "export const version = 1;\n");
    await writeFile(join(generated, "dynamic.js"), "export const version = 2;\n");
    const manifest = join(generated, "web-module-manifest-v1.json");
    await writeGeneratedManifest(manifest, {
      webModuleManifestVersion: 1,
      esmAbiVersion: 3,
      catalog: "app",
      entrypoints: { messages: "messages.js", runtime: "runtime.js", types: "messages.d.ts", dynamic: "dynamic.js" },
      assets: [
        { path: "messages.js" }, { path: "runtime.js" }, { path: "transport.js" }, { path: "dynamic.js" },
      ],
    });
    const project = join(root, "translations");
    const catalog = join(project, "runic.json");
    const document = join(project, "en", "application_title.mf2");
    const calls = join(root, "compiler-calls.txt");
    await mkdir(join(project, "en"), { recursive: true });
    await writeFile(catalog, '{"schemaVersion":1,"catalog":"app"}\n');
    await writeFile(document, "Application title\n");
    const compiler = join(root, "compiler.mjs");
    await writeFile(compiler, `import { appendFile } from "node:fs/promises"; await appendFile(${JSON.stringify(calls)}, process.argv.slice(2).join("|") + "\\n");\n`);
    const plugin = runicTranslations({
      project,
      output: join(root, "generated"),
      command: process.execPath,
      commandArguments: [compiler],
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
      assert.match(invocation, /^generate\|--project\|/);
      assert.match(invocation, /\|--output\|/);
      assert.match(invocation, /\|--emit-esm$/);
    }
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("discovers the conventional Runic project without duplicate Vite declarations", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-project-"));
  try {
    const project = join(root, "translations");
    await mkdir(join(project, "en"), { recursive: true });
    await writeFile(join(project, "runic.json"), JSON.stringify({
      schemaVersion: 1,
      catalog: "app",
      code: { namespace: "Example", className: "AppText" },
      baseLocale: "en",
    }));
    const message = join(project, "en", "application_title.mf2");
    await writeFile(message, "Application title\n");
    const generated = join(root, ".runic", "translations", "app.esm");
    await mkdir(generated, { recursive: true });
    for (const name of ["messages.js", "server.js", "transport.js", "dynamic.js"])
      await writeFile(join(generated, name), "export {};\n");
    await writeFile(join(generated, "runtime.js"), `export const contractFingerprint = ${JSON.stringify(fingerprint)};\n`);
    await writeGeneratedManifest(join(generated, "web-module-manifest-v1.json"), {
      webModuleManifestVersion: 1,
      esmAbiVersion: 3,
      catalog: "app",
      entrypoints: { messages: "messages.js", runtime: "runtime.js", server: "server.js", dynamic: "dynamic.js" },
      assets: ["messages.js", "runtime.js", "server.js", "transport.js", "dynamic.js"].map(path => ({ path })),
    });
    const calls = join(root, "compiler-calls.txt");
    const compiler = join(root, "compiler.mjs");
    await writeFile(compiler, `import { appendFile } from "node:fs/promises"; await appendFile(${JSON.stringify(calls)}, process.argv.slice(2).join("|") + "\\n");\n`);
    const plugin = runicTranslations({ cwd: root, command: process.execPath, commandArguments: [compiler] });
    const watched = [];
    await plugin.buildStart.call({ addWatchFile(path) { watched.push(path); } });
    assert.ok(watched.includes(join(project, "runic.json")));
    assert.ok(watched.includes(message));
    assert.match(await readFile(calls, "utf8"), /^generate\|--project\|.*translations\|--output\|.*\.runic.*\|--emit-esm$/m);
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
    await writeFile(join(generated, "runtime.js"), `export const contractFingerprint = ${JSON.stringify(fingerprint)};\nexport const locale = 'en';\n`);
    await writeFile(join(generated, "transport.js"), "export const version = 1;\n");
    const manifest = join(generated, "web-module-manifest-v1.json");
    await writeGeneratedManifest(manifest, {
      webModuleManifestVersion: 1,
      esmAbiVersion: 3,
      catalog: "app",
      entrypoints: { messages: "messages.js", runtime: "runtime.js", types: "messages.d.ts" },
      assets: [
        { path: "messages.js" }, { path: "messages/_index.js" }, { path: "messages/used.js" }, { path: "messages/unused.js" },
        { path: "runtime.js" }, { path: "transport.js" },
      ],
    });
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
    for (const name of ["messages.js", "server.js", "transport.js", "dynamic.js"])
      await writeFile(join(generated, name), "export {};\n");
    await writeFile(join(generated, "runtime.js"), `export const contractFingerprint = ${JSON.stringify(fingerprint)};\n`);
    const manifest = join(generated, "web-module-manifest-v1.json");
    await writeGeneratedManifest(manifest, {
      webModuleManifestVersion: 1, catalog: "app",
      esmAbiVersion: 3,
      entrypoints: { messages: "messages.js", runtime: "runtime.js", types: "messages.d.ts" },
      assets: [{ path: "messages.js" }, { path: "runtime.js" }, { path: "server.js" }, { path: "transport.js" }, { path: "dynamic.js" }],
    });
    const source = join(root, "en.json");
    await writeFile(source, "{}\n");
    const plugin = runicTranslations({ manifest, sourceFiles: [source] });
    await plugin.buildStart.call({ addWatchFile() {} });
    const ids = ["messages", "runtime", "server", "transport", "dynamic"].map(kind => `\0virtual:runic-translations/app/${kind}`);
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
  const output = await mkdtemp(join(tmpdir(), "runic-vite-package-"));
  try {
    await execFileAsync("bun", ["pm", "pack", "--destination", output, "--quiet"], { cwd: packageRoot });
    const archives = (await readdir(output)).filter(file => file.endsWith(".tgz"));
    assert.equal(archives.length, 1);
    const { stdout } = await execFileAsync("tar", ["-tzf", join(output, archives[0])]);
    const files = stdout.split("\n")
      .filter(file => file.startsWith("package/") && !file.endsWith("/"))
      .map(file => file.slice("package/".length))
      .sort();
    assert.deepEqual(files, ["README.md", "dist/index.d.ts", "dist/index.js", "package.json"]);
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});

test("locale TOML membership, config changes and invalid recovery regenerate without output loops", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-toml-"));
  try {
    const project = join(root, "translations");
    const output = join(project, "generated");
    const config = join(project, "runic.json");
    await mkdir(project);
    const settings = { schemaVersion: 1, catalog: "app", sourceLayout: "locale-toml" };
    await writeFile(config, JSON.stringify(settings));
    const english = join(project, "en.ToMl");
    await writeFile(english, "title = 'Hello'\n");
    for (const catalog of ["app", "renamed"]) {
      const generated = join(output, `${catalog}.esm`);
      await mkdir(generated, { recursive: true });
      await writeFile(join(generated, "messages.js"), "export {};\n");
      await writeFile(join(generated, "runtime.js"), `export const contractFingerprint = ${JSON.stringify(fingerprint)};\n`);
      await writeGeneratedManifest(join(generated, "web-module-manifest-v1.json"), {
        webModuleManifestVersion: 1, esmAbiVersion: 3, catalog,
        entrypoints: { messages: "messages.js", runtime: "runtime.js" },
        assets: [{ path: "messages.js" }, { path: "runtime.js" }],
      });
    }
    const calls = join(root, "calls.txt");
    const compiler = join(root, "compiler.mjs");
    // A failing compiler invocation must not poison later successful edits.
    await writeFile(compiler, `import { appendFile, readFile } from 'node:fs/promises';
      await appendFile(${JSON.stringify(calls)}, 'compile\\n');
      if ((await readFile(${JSON.stringify(english)}, 'utf8')).includes('INVALID')) process.exit(1);`);
    const plugin = runicTranslations({ project, output, command: process.execPath, commandArguments: [compiler] });
    const watcher = new EventEmitter();
    const watched = [];
    watcher.add = paths => watched.push(...(Array.isArray(paths) ? paths : [paths]));
    const sent = new EventEmitter();
    const invalidated = [];
    const server = {
      watcher, httpServer: new EventEmitter(),
      ws: { send: value => sent.emit("message", value) },
      moduleGraph: { getModuleById: id => ({ id }), getModulesByFile: path => new Set([{ id: path }]), invalidateModule: module => invalidated.push(module.id) },
    };
    plugin.configureServer(server);
    await plugin.buildStart.call({ addWatchFile: path => watched.push(path) });
    assert.ok(watched.includes(project));
    assert.ok(watched.includes(english));
    const count = async () => (await readFile(calls, "utf8")).trim().split("\n").length;
    const membership = (event, path) => new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error("No membership update received")), 5000);
      sent.once("message", value => { clearTimeout(timer); resolve(value); });
      watcher.emit(event, path);
    });
    const german = join(project, "de.TOML");
    await writeFile(german, "title = 'Hallo'\n");
    assert.equal((await membership("add", german)).type, "full-reload");
    const french = join(project, "fr.tOmL");
    await rename(german, french);
    await membership("unlink", german);
    await membership("add", french);
    await rm(french);
    await membership("unlink", french);
    assert.equal(await count(), 5);
    assert.ok(invalidated.includes(join(output, "app.esm", "messages.js")));
    await writeFile(english, "INVALID");
    await assert.rejects(() => plugin.handleHotUpdate({ file: english, server }));
    await writeFile(english, "title = 'Fixed'\n");
    await plugin.handleHotUpdate({ file: english, server });
    assert.equal(await count(), 7);
    await writeFile(config, "{");
    await assert.rejects(() => plugin.handleHotUpdate({ file: config, server }), /Could not read/);
    await writeFile(config, JSON.stringify({ ...settings, catalog: "renamed" }));
    await plugin.handleHotUpdate({ file: config, server });
    assert.equal(await plugin.resolveId("virtual:runic-translations/renamed"), "\0virtual:runic-translations/renamed/messages");
    assert.ok(invalidated.includes("\0virtual:runic-translations/app/messages"));
    assert.ok(invalidated.includes("\0virtual:runic-translations/renamed/messages"));
    const beforeOutput = await count();
    assert.deepEqual(await plugin.handleHotUpdate({ file: join(output, "renamed.esm", "messages.js"), server }), []);
    watcher.emit("add", join(output, "ignored.toml"));
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(await count(), beforeOutput);
    await writeFile(config, JSON.stringify({ ...settings, sourceLayout: "unknown" }));
    await assert.rejects(() => plugin.handleHotUpdate({ file: config, server }), /sourceLayout/);
    const legacy = join(project, "en", "title.Mf2");
    await mkdir(dirname(legacy));
    await writeFile(legacy, "Hello\n");
    await writeFile(config, JSON.stringify({ schemaVersion: 1, catalog: "app" }));
    await plugin.handleHotUpdate({ file: config, server });
    assert.ok(watched.includes(legacy));
    await plugin.handleHotUpdate({ file: legacy, server });
    const legacyAdded = join(project, "en", "new.MF2");
    const beforeLegacyAdd = await count();
    await writeFile(legacyAdded, "Added\n");
    await membership("add", legacyAdded);
    assert.ok(watched.includes(legacyAdded));
    assert.equal(await count(), beforeLegacyAdd + 1);
    await rm(legacyAdded);
    await membership("unlink", legacyAdded);
    assert.equal(await count(), beforeLegacyAdd + 2);
    plugin.closeBundle();
    assert.equal(watcher.listenerCount("add"), 0);
    assert.equal(watcher.listenerCount("unlink"), 0);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});
