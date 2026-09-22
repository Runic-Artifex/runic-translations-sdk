import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { createHash } from "node:crypto";
import { EventEmitter } from "node:events";
import { mkdtemp, mkdir, readFile, readdir, rm, symlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";
import { promisify } from "node:util";
import { build } from "vite";
import { runicTranslations } from "../index.js";

const execFileAsync = promisify(execFile);
const fingerprint = `sha256:${"a".repeat(64)}`;
const sourceHash = `sha256:${"b".repeat(64)}`;

async function writeGeneratedManifest(path, document) {
  const root = dirname(path);
  const assets = await Promise.all(document.assets.map(async asset => {
    const content = await readFile(join(root, asset.path));
    return { ...asset, sha256: createHash("sha256").update(content).digest("hex"), byteLength: content.byteLength, mediaType: asset.mediaType ?? "text/javascript" };
  }));
  await writeFile(path, JSON.stringify({ ...document, contractFingerprint: fingerprint, assets }));
}

async function writeV3Fixture(root, overrides = {}) {
  await mkdir(root, { recursive: true });
  for (const name of ["messages.js", "messages.d.ts", "server.js", "transport.js", "dynamic.js"])
    await writeFile(join(root, name), "export {};\n");
  await writeFile(join(root, "runtime.js"), `export const esmAbiVersion = 4;
export const rmf2RuntimeAbiVersion = 2;
export const messageGrammarVersion = 5;
export const profile = "rmf2-execution-v2";
export const generatedNameVersion = 1;
export const contractFingerprint = ${JSON.stringify(fingerprint)};
export const sourceHash = ${JSON.stringify(sourceHash)};
`);
  const manifest = join(root, "web-module-manifest-v3.json");
  await writeGeneratedManifest(manifest, {
    webModuleManifestVersion: 3, esmAbiVersion: 4, rmf2RuntimeAbiVersion: 2,
    messageGrammarVersion: 5, profile: "rmf2-execution-v2", generatedNameVersion: 1,
    sourceHash, catalog: "app",
    entrypoints: { messages: "messages.js", types: "messages.d.ts", runtime: "runtime.js", server: "server.js", transport: "transport.js", dynamic: "dynamic.js" },
    assets: ["messages.js", "messages.d.ts", "runtime.js", "server.js", "transport.js", "dynamic.js"].map(path => ({ path })),
    ...overrides,
  });
  return manifest;
}

test("accepts only the complete RMF2 v3 contract", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-v3-"));
  try {
    const manifest = await writeV3Fixture(join(root, "app.esm-v5"));
    const plugin = runicTranslations({ manifest });
    await plugin.buildStart.call({ addWatchFile() {} });
    assert.equal(await plugin.resolveId("virtual:runic-translations/app/runtime"), "\0virtual:runic-translations/app/runtime");
    for (const [field, value, pattern] of [
      ["webModuleManifestVersion", 2, /Unsupported/],
      ["esmAbiVersion", 3, /ESM ABI/],
      ["rmf2RuntimeAbiVersion", 1, /execution contract/],
      ["messageGrammarVersion", 4, /execution contract/],
      ["profile", "future-profile", /execution contract/],
      ["generatedNameVersion", 2, /execution contract/],
      ["sourceHash", "bad", /execution contract/],
    ]) {
      const document = JSON.parse(await readFile(manifest, "utf8"));
      document[field] = value;
      await writeFile(manifest, JSON.stringify(document));
      await assert.rejects(() => runicTranslations({ manifest }).buildStart.call({ addWatchFile() {} }), pattern, field);
      await writeV3Fixture(dirname(manifest));
    }
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("rejects hostile, stale, and forged v3 output", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-hostile-"));
  try {
    const generated = join(root, "app.esm-v5");
    const manifest = await writeV3Fixture(generated);
    await writeFile(join(generated, "extra.js"), "export {};\n");
    const valid = JSON.parse(await readFile(manifest, "utf8"));
    const rejected = [
      ["unknown root member", document => { document.extra = true; }, /unknown member/],
      ["unknown asset member", document => { document.assets[0].extra = true; }, /unknown asset member/],
      ["duplicate non-entrypoint asset path", document => {
        const asset = document.assets.find(item => item.path === "messages.js");
        document.assets.push({ ...asset, path: "extra.js" }, { ...asset, path: "extra.js", mediaType: "text/typescript" });
      }, /invalid generated asset/],
      ["path traversal", document => { document.assets[0].path = "x/../messages.js"; }, /invalid generated asset/],
      ["wrong entrypoint", document => { document.entrypoints.dynamic = "other.js"; }, /invalid entrypoints/],
      ["runtime ABI marker", document => { document.rmf2RuntimeAbiVersion = 1; }, /execution contract/],
    ];
    for (const [label, mutate, pattern] of rejected) {
      const document = JSON.parse(JSON.stringify(valid));
      mutate(document);
      await writeFile(manifest, JSON.stringify(document));
      await assert.rejects(() => runicTranslations({ manifest }).buildStart.call({ addWatchFile() {} }), pattern, label);
    }
    await writeV3Fixture(generated);
    await writeFile(join(generated, "messages.js"), "export const stale = true;\n");
    await assert.rejects(() => runicTranslations({ manifest }).buildStart.call({ addWatchFile() {} }), /integrity/);
    await writeV3Fixture(generated);
    const forged = JSON.parse(await readFile(manifest, "utf8"));
    forged.contractFingerprint = `sha256:${"0".repeat(64)}`;
    await writeFile(manifest, JSON.stringify(forged));
    await assert.rejects(() => runicTranslations({ manifest }).buildStart.call({ addWatchFile() {} }), /fingerprint/);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("pre-generated manifests reject assets that escape through symbolic links", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-symlink-"));
  try {
    const generated = join(root, "app.esm-v5");
    const manifest = await writeV3Fixture(generated);
    const outside = join(root, "outside-messages.js");
    await writeFile(outside, "export const escaped = true;\n");
    await rm(join(generated, "messages.js"));
    await symlink(outside, join(generated, "messages.js"));
    const document = JSON.parse(await readFile(manifest, "utf8"));
    await writeGeneratedManifest(manifest, document);

    await assert.rejects(
      () => runicTranslations({ manifest }).buildStart.call({ addWatchFile() {} }),
      /must not traverse symbolic links/,
    );
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("project mode compiles and watches canonical RMF2 v3 output", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-project-"));
  try {
    const project = join(root, "translations"), feature = join(root, "feature"), output = join(root, "generated");
    const config = join(project, "runic.json"), english = join(feature, "en.rmf2");
    await mkdir(project); await mkdir(feature);
    await writeFile(config, JSON.stringify({ schemaVersion: 1, catalog: "app", sourceRoots: [{ path: "../feature", namespace: ["shop"] }] }));
    await writeFile(english, "title = Shop\n");
    await writeV3Fixture(join(output, "app.esm-v5"));
    const calls = join(root, "calls.txt"), compiler = join(root, "compiler.mjs");
    await writeFile(compiler, `import { appendFile } from "node:fs/promises"; await appendFile(${JSON.stringify(calls)}, process.argv.slice(2).join("|") + "\\n");`);
    const plugin = runicTranslations({ project, output, command: process.execPath, commandArguments: [compiler] });
    const watcher = new EventEmitter(), watched = [], reloads = new EventEmitter();
    watcher.add = paths => watched.push(...(Array.isArray(paths) ? paths : [paths]));
    const server = { watcher, httpServer: new EventEmitter(), ws: { send: value => reloads.emit("reload", value) }, moduleGraph: { getModuleById: () => undefined, getModulesByFile: () => new Set(), invalidateModule() {} } };
    plugin.configureServer(server);
    await plugin.buildStart.call({ addWatchFile: path => watched.push(path) });
    assert.ok(watched.includes(config)); assert.ok(watched.includes(feature)); assert.ok(watched.includes(english));
    assert.equal(await plugin.resolveId("virtual:runic-translations/app"), "\0virtual:runic-translations/app/messages");
    assert.match(await readFile(calls, "utf8"), /^generate\|--project\|.*\|--output\|.*\|--emit-esm$/m);
    const german = join(feature, "de.rmf2"); await writeFile(german, "title = Laden\n");
    const changed = new Promise((resolve, reject) => { const timer = setTimeout(() => reject(new Error("No RMF2 membership update")), 5000); reloads.once("reload", value => { clearTimeout(timer); resolve(value); }); });
    watcher.emit("add", german);
    assert.equal((await changed).type, "full-reload"); assert.ok(watched.includes(german));
    plugin.closeBundle();
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("project mode discovers and watches direct MF2 sources in sourceRoots", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-direct-mf2-"));
  try {
    const project = join(root, "translations"), feature = join(root, "feature"), output = join(root, "generated");
    const config = join(project, "runic.json"), english = join(feature, "en.mf2");
    await mkdir(project); await mkdir(feature);
    await writeFile(config, JSON.stringify({ schemaVersion: 1, catalog: "app", sourceRoots: [{ path: "../feature", namespace: ["shop"] }] }));
    await writeFile(english, "title = Shop\n");
    await writeV3Fixture(join(output, "app.esm-v5"));
    const calls = join(root, "calls.txt"), compiler = join(root, "compiler.mjs");
    await writeFile(compiler, `import { appendFile } from "node:fs/promises"; await appendFile(${JSON.stringify(calls)}, process.argv.slice(2).join("|") + "\\n");`);
    const plugin = runicTranslations({ project, output, command: process.execPath, commandArguments: [compiler] });
    const watcher = new EventEmitter(), watched = [], reloads = new EventEmitter();
    watcher.add = paths => watched.push(...(Array.isArray(paths) ? paths : [paths]));
    const server = { watcher, httpServer: new EventEmitter(), ws: { send: value => reloads.emit("reload", value) }, moduleGraph: { getModuleById: () => undefined, getModulesByFile: () => new Set(), invalidateModule() {} } };
    plugin.configureServer(server);
    await plugin.buildStart.call({ addWatchFile: path => watched.push(path) });
    assert.ok(watched.includes(config)); assert.ok(watched.includes(feature)); assert.ok(watched.includes(english));
    const german = join(feature, "de.mf2"); await writeFile(german, "title = Laden\n");
    const changed = new Promise((resolve, reject) => { const timer = setTimeout(() => reject(new Error("No direct MF2 membership update")), 5000); reloads.once("reload", value => { clearTimeout(timer); resolve(value); }); });
    watcher.emit("add", german);
    assert.equal((await changed).type, "full-reload"); assert.ok(watched.includes(german));
    plugin.closeBundle();
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("project mode propagates compiler rejection of mixed direct and grouped sources", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-mixed-sources-"));
  try {
    const project = join(root, "translations"), output = join(root, "generated");
    await mkdir(project);
    await writeFile(join(project, "runic.json"), JSON.stringify({ schemaVersion: 1, catalog: "app" }));
    await writeFile(join(project, "en.mf2"), "title = Shop\n");
    await writeFile(join(project, "de.rmf2"), "title = Laden\n");
    await writeV3Fixture(join(output, "app.esm-v5"));
    const compiler = join(root, "compiler.mjs");
    await writeFile(compiler, `import { readdir } from "node:fs/promises";
const projectIndex = process.argv.indexOf("--project");
const files = await readdir(process.argv[projectIndex + 1]);
if (files.some(file => file.endsWith(".mf2")) && files.some(file => file.endsWith(".rmf2"))) {
  process.stderr.write("RTR0019: direct .mf2 and grouped .rmf2 sources cannot be mixed\\n");
  process.exitCode = 1;
}`);
    const plugin = runicTranslations({ project, output, command: process.execPath, commandArguments: [compiler] });
    await assert.rejects(
      () => plugin.buildStart.call({ addWatchFile() {} }),
      error => error?.stderr?.includes("RTR0019") === true,
    );
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("Vite production builds retain static v3 message re-exports", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-production-"));
  try {
    const generated = join(root, "app.esm-v5"), messages = join(generated, "messages"); await mkdir(messages, { recursive: true });
    const manifest = await writeV3Fixture(generated);
    await writeFile(join(messages, "used.js"), "export const internalUsed = () => 'USED_MESSAGE';\n");
    await writeFile(join(messages, "unused.js"), "export const internalUnused = () => 'UNRELATED_MESSAGE_SENTINEL';\n");
    await writeFile(join(messages, "_index.js"), "export { internalUsed as used } from './used.js';\nexport { internalUnused as unused } from './unused.js';\n");
    await writeFile(join(generated, "messages.js"), "export * as m from './messages/_index.js';\n");
    const document = JSON.parse(await readFile(manifest, "utf8")); document.assets.push({ path: "messages/_index.js" }, { path: "messages/used.js" }, { path: "messages/unused.js" }); await writeGeneratedManifest(manifest, document);
    const entry = join(root, "main.js"), outDir = join(root, "dist"); await writeFile(entry, "import { m } from 'virtual:runic-translations/app'; export const result = m.used();\n");
    await build({ configFile: false, logLevel: "silent", plugins: [runicTranslations({ manifest })], build: { outDir, minify: false, lib: { entry, formats: ["es"], fileName: () => "bundle.js" } } });
    const bundle = await readFile(join(outDir, "bundle.js"), "utf8"); assert.match(bundle, /USED_MESSAGE/); assert.doesNotMatch(bundle, /UNRELATED_MESSAGE_SENTINEL/);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("published package inventory contains only declared runtime files", async () => {
  const packageRoot = new URL("..", import.meta.url), output = await mkdtemp(join(tmpdir(), "runic-vite-package-"));
  try {
    await execFileAsync("bun", ["pm", "pack", "--destination", output, "--quiet"], { cwd: packageRoot });
    const archives = (await readdir(output)).filter(file => file.endsWith(".tgz")); assert.equal(archives.length, 1);
    const { stdout } = await execFileAsync("tar", ["-tzf", join(output, archives[0])]);
    const files = stdout.split("\n").filter(file => file.startsWith("package/") && !file.endsWith("/")).map(file => file.slice("package/".length)).sort();
    assert.deepEqual(files, ["README.md", "dist/index.d.ts", "dist/index.js", "package.json"]);
  } finally { await rm(output, { recursive: true, force: true }); }
});
