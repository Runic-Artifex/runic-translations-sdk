import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { createHash } from "node:crypto";
import { EventEmitter } from "node:events";
import { mkdtemp, mkdir, readFile, readdir, rm, symlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";
import { promisify } from "node:util";
import { build, createServer } from "vite";
import { runicTranslations } from "../dist/index.js";

const execFileAsync = promisify(execFile);
let hotTimestamp = 0;

// Calls Vite's hotUpdate hook as one environment would for a file change.
function hot(plugin, file, moduleGraph, timestamp = ++hotTimestamp, type = "update") {
  return plugin.hotUpdate.call({ environment: { moduleGraph } }, { type, file, timestamp, modules: [], read: async () => "" });
}
const fingerprint = `sha256:${"a".repeat(64)}`;
const sourceHash = `sha256:${"b".repeat(64)}`;

async function writeGeneratedManifest(path, document) {
  const root = dirname(path);
  const assets = await Promise.all(document.assets.map(async asset => {
    const content = await readFile(join(root, asset.path));
    return { ...asset, sha256: createHash("sha256").update(content).digest("hex"), byteLength: content.byteLength, mediaType: asset.mediaType ?? (asset.path.endsWith(".d.ts") ? "text/typescript" : "text/javascript") };
  }));
  await writeFile(path, JSON.stringify({ ...document, contractFingerprint: fingerprint, assets }));
}

async function writeV3Fixture(root, overrides = {}) {
  await mkdir(root, { recursive: true });
  for (const name of ["messages.js", "server.js", "transport.js", "dynamic.js"])
    await writeFile(join(root, name), "export {};\n");
  await writeFile(join(root, "messages.d.ts"), `import type { MessageOptions } from "./runtime.js";
export declare const m: Readonly<{
  readonly "application_title": (options?: MessageOptions) => string;
  readonly "greeting": (inputs: Readonly<{ readonly "name": string; readonly "count": bigint | number }>, options?: MessageOptions) => string;
}>;
`);
  await writeFile(join(root, "runtime.js"), `export const esmAbiVersion = 4;
export const rmf2RuntimeAbiVersion = 2;
export const messageGrammarVersion = 5;
export const profile = "rmf2-execution-v2";
export const generatedNameVersion = 1;
export const contractFingerprint = ${JSON.stringify(fingerprint)};
export const sourceHash = ${JSON.stringify(sourceHash)};
`);
  await writeFile(join(root, "runtime.d.ts"), `export type MessageOptions = Readonly<{ locale?: string }>;
export declare const baseLocale: string;
export declare function decimal(value: string): Readonly<{ readonly coefficient: bigint; readonly scale: number; readonly negative: boolean }>;
export declare function createDomInlineRenderer(document: Document): Readonly<{ render(): readonly Node[] }>;
`);
  await writeFile(join(root, "server.d.ts"), "export declare function runWithLocale<T>(locale: string, operation: () => T): T;\n");
  await writeFile(join(root, "transport.d.ts"), `import type { MessageOptions } from "./runtime.js";
export declare function decodeTextReference(value: unknown): Readonly<{ ok: boolean }>;
export type { MessageOptions };
`);
  await writeFile(join(root, "dynamic.d.ts"), `import type { MessageOptions } from "./runtime.js";
export declare function formatDynamicMessage(value: unknown, key: string, inputs?: Readonly<Record<string, unknown>>, options?: MessageOptions): string;
`);
  const manifest = join(root, "web-module-manifest-v3.json");
  await writeGeneratedManifest(manifest, {
    webModuleManifestVersion: 3, esmAbiVersion: 4, rmf2RuntimeAbiVersion: 2,
    messageGrammarVersion: 5, profile: "rmf2-execution-v2", generatedNameVersion: 1,
    sourceHash, catalog: "app",
    entrypoints: { messages: "messages.js", types: "messages.d.ts", runtime: "runtime.js", server: "server.js", transport: "transport.js", dynamic: "dynamic.js" },
    assets: ["messages.js", "messages.d.ts", "runtime.js", "runtime.d.ts", "server.js", "server.d.ts", "transport.js", "transport.d.ts", "dynamic.js", "dynamic.d.ts"].map(path => ({ path })),
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

test("generated ambient declarations expose exact manifest-owned virtual module types", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-types-"));
  try {
    const generated = join(root, "app.esm-v5");
    const manifest = await writeV3Fixture(generated);
    const plugin = runicTranslations({ manifest });
    await plugin.buildStart.call({ addWatchFile() {} });
    const declarations = join(generated, "virtual.d.ts");
    assert.match(await readFile(declarations, "utf8"), /declare module "virtual:runic-translations\/app"/);
    const consumer = join(root, "consumer.ts");
    await writeFile(consumer, `import { m } from "virtual:runic-translations/app";
import { m as explicitMessages } from "virtual:runic-translations/app/messages";
import { baseLocale, createDomInlineRenderer, decimal } from "virtual:runic-translations/app/runtime";
import { runWithLocale } from "virtual:runic-translations/app/server";
import { decodeTextReference } from "virtual:runic-translations/app/transport";
import { formatDynamicMessage } from "virtual:runic-translations/app/dynamic";
const greeting: string = m.greeting({ name: "Ada", count: 2n }, { locale: baseLocale });
const title: string = explicitMessages.application_title();
decimal("1.25"); runWithLocale("en", () => greeting); decodeTextReference({}); formatDynamicMessage({}, "greeting");
const renderedNode: Node | undefined = createDomInlineRenderer(document).render()[0];
// @ts-expect-error count is generated as bigint | number.
m.greeting({ name: "Ada", count: "two" });
// @ts-expect-error generated message keys are exact.
m.missing_message();
void title; void renderedNode;
`);
    const tsconfig = join(root, "tsconfig.json");
    await writeFile(tsconfig, JSON.stringify({
      compilerOptions: { module: "ESNext", moduleResolution: "Bundler", target: "ES2022", strict: true, noEmit: true },
      files: [consumer, declarations],
    }));
    await execFileAsync(process.execPath, ["x", "tsc", "-p", tsconfig, "--pretty", "false"], { cwd: new URL("..", import.meta.url) });

    const serverConsumer = join(root, "server-consumer.ts");
    await writeFile(serverConsumer, `import { runWithLocale } from "virtual:runic-translations/app/server";
const result: number = runWithLocale("en", () => 42);
void result;
`);
    const serverTsconfig = join(root, "server-tsconfig.json");
    await writeFile(serverTsconfig, JSON.stringify({
      compilerOptions: { module: "ESNext", moduleResolution: "Bundler", target: "ES2022", lib: ["ES2022"], types: [], strict: true, noEmit: true },
      files: [serverConsumer, declarations],
    }));
    await execFileAsync(process.execPath, ["x", "tsc", "-p", serverTsconfig, "--pretty", "false"], { cwd: new URL("..", import.meta.url) });
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

test("failed manifest refresh keeps the last validated catalog, entries, and declarations atomic", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-atomic-"));
  try {
    const generated = join(root, "app.esm-v5"), manifest = await writeV3Fixture(generated);
    const plugin = runicTranslations({ manifest });
    await plugin.buildStart.call({ addWatchFile() {} });
    const declarationPath = join(generated, "virtual.d.ts");
    const beforeDeclarations = await readFile(declarationPath, "utf8");
    await writeFile(join(generated, "messages.js"), "export const stale = true;\n");
    const malformed = JSON.parse(await readFile(manifest, "utf8"));
    malformed.catalog = "renamed";
    await writeFile(manifest, JSON.stringify(malformed));
    const invalidated = [];
    const server = { moduleGraph: {
      getModuleById: id => ({ id }),
      getModulesByFile: () => new Set(),
      invalidateModule: module => invalidated.push(module.id),
    } };
    await assert.rejects(() => hot(plugin, manifest, server.moduleGraph), /integrity/);
    assert.equal(await plugin.resolveId("virtual:runic-translations/app"), "\0virtual:runic-translations/app/messages");
    assert.equal(await plugin.resolveId("virtual:runic-translations/renamed"), null);
    assert.equal(await readFile(declarationPath, "utf8"), beforeDeclarations);
    assert.deepEqual(invalidated, []);

    await writeV3Fixture(generated, { catalog: "renamed" });
    const modules = await hot(plugin, manifest, server.moduleGraph);
    assert.ok(modules.some(module => module.id === "\0virtual:runic-translations/app/messages"));
    assert.ok(modules.some(module => module.id === "\0virtual:runic-translations/renamed/messages"));
    assert.equal(await plugin.resolveId("virtual:runic-translations/app"), null);
    assert.equal(await plugin.resolveId("virtual:runic-translations/renamed"), "\0virtual:runic-translations/renamed/messages");
    assert.match(await readFile(declarationPath, "utf8"), /virtual:runic-translations\/renamed/);
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

test("generated type declarations reject symlinked parent escapes and canonical asset aliases", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-type-symlink-"));
  try {
    const generated = join(root, "app.esm-v5"), manifest = await writeV3Fixture(generated);
    const outside = join(root, "outside"); await mkdir(outside);
    const escape = join(root, "escape"); await symlink(outside, escape);
    await assert.rejects(
      () => runicTranslations({ manifest, typeDeclarations: join(escape, "virtual.d.ts") }).buildStart.call({ addWatchFile() {} }),
      /must not traverse symbolic links/,
    );
    await assert.rejects(() => readFile(join(outside, "virtual.d.ts")), error => error?.code === "ENOENT");

    const alias = join(root, "generated-alias"); await symlink(generated, alias);
    const messages = join(generated, "messages.js"), before = await readFile(messages, "utf8");
    await assert.rejects(
      () => runicTranslations({ manifest, typeDeclarations: join(alias, "messages.js") }).buildStart.call({ addWatchFile() {} }),
      /must not traverse symbolic links|must not overwrite/,
    );
    assert.equal(await readFile(messages, "utf8"), before);

    await assert.rejects(
      () => runicTranslations({ manifest, typeDeclarations: join(generated, "MESSAGES.JS") }).buildStart.call({ addWatchFile() {} }),
      /must not overwrite/,
    );
    assert.equal(await readFile(messages, "utf8"), before);
    const manifestBefore = await readFile(manifest, "utf8");
    await assert.rejects(
      () => runicTranslations({ manifest, typeDeclarations: join(generated, "WEB-MODULE-MANIFEST-V3.JSON") }).buildStart.call({ addWatchFile() {} }),
      /must not overwrite/,
    );
    assert.equal(await readFile(manifest, "utf8"), manifestBefore);
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

test("project mode accepts symlinked environment ancestors but rejects links below the project", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-linked-home-"));
  try {
    // Like macOS /tmp or a symlinked home: every path below reaches the project through `home`.
    const real = join(root, "real"); await mkdir(real);
    const home = join(root, "home"); await symlink(real, home);
    const project = join(home, "app", "translations"), feature = join(home, "app", "feature"), output = join(home, "app", "generated");
    await mkdir(project, { recursive: true }); await mkdir(feature);
    await writeFile(join(project, "runic.json"), JSON.stringify({ schemaVersion: 1, catalog: "app", sourceRoots: [{ path: "../feature", namespace: ["shop"] }] }));
    await writeFile(join(feature, "en.rmf2"), "title = Shop\n");
    await writeV3Fixture(join(output, "app.esm-v5"));
    const compiler = join(root, "compiler.mjs"); await writeFile(compiler, "");
    const plugin = runicTranslations({ project, output, cwd: join(home, "app"), command: process.execPath, commandArguments: [compiler] });
    await plugin.buildStart.call({ addWatchFile() {} });
    plugin.closeBundle?.();

    const shared = join(root, "shared"); await mkdir(join(shared, "nested"), { recursive: true });
    await symlink(shared, join(home, "app", "linked"));
    await writeFile(join(project, "runic.json"), JSON.stringify({ schemaVersion: 1, catalog: "app", sourceRoots: [{ path: "../linked/nested", namespace: ["shop"] }] }));
    assert.throws(
      () => runicTranslations({ project, output, cwd: join(home, "app"), command: process.execPath, commandArguments: [compiler] }),
      /must not traverse symbolic links/,
    );
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("project mode discovers and watches direct MF2 sources in sourceRoots", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-direct-mf2-"));
  try {
    const project = join(root, "translations"), feature = join(root, "feature"), output = join(root, "generated");
    const config = join(project, "runic.json"), english = join(feature, "en", "title.mf2");
    await mkdir(project); await mkdir(dirname(english), { recursive: true });
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
    const german = join(feature, "de", "title.mf2"); await mkdir(dirname(german)); await writeFile(german, "title = Laden\n");
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

test("project HMR recovers after failures and applies catalog plus source-root config changes", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-hmr-recovery-"));
  try {
    const project = join(root, "translations"), firstRoot = join(root, "first"), secondRoot = join(root, "second"), output = join(root, "generated");
    const config = join(project, "runic.json"), english = join(firstRoot, "en.rmf2"), german = join(secondRoot, "de.rmf2");
    await mkdir(project); await mkdir(firstRoot); await mkdir(secondRoot);
    const initial = { schemaVersion: 1, catalog: "app", sourceRoots: [{ path: "../first", namespace: ["first"] }] };
    await writeFile(config, JSON.stringify(initial));
    await writeFile(english, "title = Hello\n");
    await writeFile(german, "title = Hallo\n");
    await writeV3Fixture(join(output, "app.esm-v5"));
    await writeV3Fixture(join(output, "renamed.esm-v5"), { catalog: "renamed" });
    const calls = join(root, "calls.txt"), compiler = join(root, "compiler.mjs");
    await writeFile(compiler, `import { appendFile, readFile } from "node:fs/promises";
await appendFile(${JSON.stringify(calls)}, "compile\\n");
if ((await readFile(${JSON.stringify(english)}, "utf8")).includes("INVALID")) process.exit(1);
`);
    const plugin = runicTranslations({ project, output, command: process.execPath, commandArguments: [compiler] });
    const watcher = new EventEmitter(), watched = [], sent = new EventEmitter(), invalidated = [];
    watcher.add = paths => watched.push(...(Array.isArray(paths) ? paths : [paths]));
    const server = {
      watcher, httpServer: new EventEmitter(), ws: { send: value => sent.emit("message", value) },
      moduleGraph: {
        getModuleById: id => ({ id }),
        getModulesByFile: path => new Set([{ id: path }]),
        invalidateModule: module => invalidated.push(module.id),
      },
    };
    plugin.configureServer(server);
    await plugin.buildStart.call({ addWatchFile: path => watched.push(path) });
    assert.ok(watched.includes(firstRoot)); assert.ok(watched.includes(english));

    await writeFile(english, "INVALID\n");
    await assert.rejects(() => hot(plugin, english, server.moduleGraph));
    const changed = { schemaVersion: 1, catalog: "renamed", sourceRoots: [{ path: "../second", namespace: ["second"] }] };
    await writeFile(config, JSON.stringify(changed));
    await assert.rejects(() => hot(plugin, config, server.moduleGraph));
    assert.equal(await plugin.resolveId("virtual:runic-translations/app"), "\0virtual:runic-translations/app/messages");
    assert.equal(await plugin.resolveId("virtual:runic-translations/renamed"), null);
    await writeFile(english, "title = Fixed\n");
    await hot(plugin, config, server.moduleGraph);

    await writeFile(config, "{");
    await assert.rejects(() => hot(plugin, config, server.moduleGraph), /Could not read/);
    assert.equal(await plugin.resolveId("virtual:runic-translations/renamed"), "\0virtual:runic-translations/renamed/messages");
    await writeFile(config, JSON.stringify(changed));
    await hot(plugin, config, server.moduleGraph);
    assert.ok(watched.includes(secondRoot)); assert.ok(watched.includes(german));
    assert.equal(await plugin.resolveId("virtual:runic-translations/renamed"), "\0virtual:runic-translations/renamed/messages");
    assert.ok(invalidated.includes("\0virtual:runic-translations/app/messages"));
    assert.ok(invalidated.includes("\0virtual:runic-translations/renamed/messages"));

    const french = join(secondRoot, "fr.rmf2"); await writeFile(french, "title = Bonjour\n");
    const reload = new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error("No source-root membership reload")), 5000);
      sent.once("message", value => { clearTimeout(timer); resolve(value); });
    });
    watcher.emit("add", french);
    assert.equal((await reload).type, "full-reload");
    assert.ok(watched.includes(french));
    assert.deepEqual(await hot(plugin, join(output, "renamed.esm-v5", "messages.js"), server.moduleGraph), []);
    plugin.closeBundle();
    assert.equal(watcher.listenerCount("add"), 0); assert.equal(watcher.listenerCount("unlink"), 0);
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

test("a dev-server change makes the browser re-import the regenerated messages", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-hmr-timestamp-"));
  let server;
  try {
    const generated = join(root, "app.esm-v5"), source = join(root, "en.rmf2");
    const manifest = await writeV3Fixture(generated);
    await writeFile(source, "title = One\n");
    await writeFile(join(root, "main.js"), "import { m } from 'virtual:runic-translations/app'; export { m };\n");
    server = await createServer({
      configFile: false, root, logLevel: "silent", appType: "custom",
      server: { middlewareMode: true, ws: false, watch: null },
      plugins: [runicTranslations({ manifest, sourceFiles: [source], typeDeclarations: false })],
    });
    const client = server.environments.client;
    await client.transformRequest("/main.js");
    const virtualUrl = "virtual:runic-translations/app";
    const before = (await client.transformRequest(virtualUrl)).code;
    assert.doesNotMatch(before, /messages\.js\?t=/);

    // The owning build regenerates the messages; the source save then reaches hotUpdate.
    await writeFile(join(generated, "messages.js"), "export const m = 'two';\n");
    await writeGeneratedManifest(manifest, JSON.parse(await readFile(manifest, "utf8")));
    await writeFile(source, "title = Two\n");
    server.watcher.emit("change", source);
    let after = "";
    for (let attempt = 0; attempt < 100 && !/messages\.js\?t=\d+/.test(after); attempt++) {
      await new Promise(resolveDelay => setTimeout(resolveDelay, 20));
      after = (await client.transformRequest(virtualUrl))?.code ?? "";
    }
    // Without the timestamp the browser keeps its cached module for the unchanged URL.
    assert.match(after, /messages\.js\?t=\d+/);
  } finally {
    await server?.close();
    await rm(root, { recursive: true, force: true });
  }
});

// A fake `runic-translations` that implements serve mode. It logs every process start and request
// to `calls`, fails requests while `failMarker` exists, and crashes on the request numbers listed in
// `crashOn` (counted across process starts). On request numbers in `hangOn` it starts a child process,
// records its pid in `grandchild`, and never answers; on `unreadableOn` it answers like a request it
// could not read (null id).
async function writeServeCompiler(root, { crashOn = [], hangOn = [], unreadableOn = [] } = {}) {
  const calls = join(root, "calls.txt"), failMarker = join(root, "fail"), counter = join(root, "count.txt");
  const compiler = join(root, "serve-compiler.mjs");
  const grandchild = join(root, "grandchild.pid");
  await writeFile(compiler, `import { appendFileSync, existsSync, readFileSync, writeFileSync } from "node:fs";
import { spawn } from "node:child_process";
import { createInterface } from "node:readline";
const calls = ${JSON.stringify(calls)};
const args = process.argv.slice(2);
if (args[0] !== "serve") { appendFileSync(calls, "one-shot " + args[0] + "\\n"); process.exit(0); }
appendFileSync(calls, "serve-start\\n");
process.stdout.write(JSON.stringify({ protocol: "runic-translations-serve/1", event: "ready", version: "test" }) + "\\n");
const lines = createInterface({ input: process.stdin });
let shuttingDown = false;
lines.on("line", line => {
  const request = JSON.parse(line);
  if (request.method === "shutdown") {
    appendFileSync(calls, "serve-shutdown\\n");
    process.stdout.write(JSON.stringify({ id: request.id, ok: true, exitCode: 0 }) + "\\n");
    // Exit only after the reply was read: a client that treats it as unknown would kill us first.
    shuttingDown = true;
    setTimeout(() => { appendFileSync(calls, "serve-shutdown-done\\n"); process.exit(0); }, 300);
    return;
  }
  const count = (existsSync(${JSON.stringify(counter)}) ? Number(readFileSync(${JSON.stringify(counter)}, "utf8")) : 0) + 1;
  writeFileSync(${JSON.stringify(counter)}, String(count));
  appendFileSync(calls, "serve-" + request.method + " " + request.emit.join(",") + "\\n");
  if (${JSON.stringify(crashOn)}.includes(count)) process.exit(3);
  if (${JSON.stringify(hangOn)}.includes(count)) {
    const sleeper = spawn(process.execPath, ["-e", "setTimeout(() => {}, 60000)"], { stdio: "ignore" });
    writeFileSync(${JSON.stringify(grandchild)}, String(sleeper.pid));
    return;
  }
  if (${JSON.stringify(unreadableOn)}.includes(count)) {
    process.stdout.write(JSON.stringify({ id: null, ok: false, exitCode: 2, message: "The request exceeds the supported size.", diagnostics: [] }) + "\\n");
    return;
  }
  const fail = existsSync(${JSON.stringify(failMarker)});
  process.stdout.write(JSON.stringify({ id: request.id, ok: !fail, exitCode: fail ? 1 : 0,
    message: fail ? "en.rmf2(1,1,1,1): error RTR0007: broken" : "", diagnostics: [], elapsedMs: 1 }) + "\\n");
});
lines.on("close", () => { if (shuttingDown) return; appendFileSync(calls, "serve-eof\\n"); process.exit(0); });
`);
  return { compiler, calls, failMarker, grandchild, log: async () => (await readFile(calls, "utf8")).trim().split("\n") };
}

async function servedProject(root, options = {}) {
  const project = join(root, "translations"), output = join(root, "generated"), english = join(project, "en.rmf2");
  await mkdir(project);
  await writeFile(join(project, "runic.json"), JSON.stringify({ schemaVersion: 1, catalog: "app" }));
  await writeFile(english, "title = Shop\n");
  await writeV3Fixture(join(output, "app.esm-v5"));
  const fake = await writeServeCompiler(root, options);
  const plugin = runicTranslations({ project, output, command: process.execPath, commandArguments: [fake.compiler], persistentCompilerTimeout: options.timeout });
  const watcher = new EventEmitter(), warnings = [];
  watcher.add = () => {};
  const moduleGraph = { getModuleById: id => ({ id }), getModulesByFile: () => new Set(), invalidateModule() {} };
  const server = {
    watcher, httpServer: new EventEmitter(), ws: { send() {} }, moduleGraph,
    config: { logger: { warn: message => warnings.push(message) } },
  };
  plugin.configureServer(server);
  return { plugin, english, moduleGraph, warnings, ...fake };
}

test("dev server compiles every save through one persistent compiler and shuts it down", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-serve-"));
  try {
    const { plugin, english, moduleGraph, log, failMarker } = await servedProject(root);
    await plugin.buildStart.call({ addWatchFile() {} });
    await hot(plugin, english, moduleGraph);
    await hot(plugin, english, moduleGraph);
    assert.deepEqual(await log(), ["serve-start", "serve-generate esm", "serve-generate esm", "serve-generate esm"]);

    await writeFile(failMarker, "");
    await assert.rejects(() => hot(plugin, english, moduleGraph), error =>
      error.name === "CompilerFailure" && error.stderr.includes("RTR0007") && error.exitCode === 1);
    await rm(failMarker);
    await hot(plugin, english, moduleGraph);
    assert.equal((await log()).filter(line => line.startsWith("one-shot")).length, 0, "compiler diagnostics must not fall back to one-shot");

    plugin.closeBundle();
    for (let attempt = 0; attempt < 150 && !(await log()).includes("serve-shutdown-done"); attempt++)
      await new Promise(resolveDelay => setTimeout(resolveDelay, 20));
    assert.deepEqual((await log()).slice(-2), ["serve-shutdown", "serve-shutdown-done"], "the shutdown reply must not get the compiler killed");
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("one change compiles once for every Vite environment", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-environments-"));
  try {
    const { plugin, english, log } = await servedProject(root);
    await plugin.buildStart.call({ addWatchFile() {} });
    const graph = name => ({
      getModuleById: id => ({ id: `${name}:${id}` }), getModulesByFile: () => new Set(), invalidated: [],
      invalidateModule(module) { this.invalidated.push(module.id); },
    });
    const client = graph("client"), ssr = graph("ssr");
    const [clientModules, ssrModules] = await Promise.all([hot(plugin, english, client, 1000), hot(plugin, english, ssr, 1000)]);
    assert.ok(clientModules.every(module => module.id.startsWith("client:")) && clientModules.length === 5);
    assert.ok(ssrModules.every(module => module.id.startsWith("ssr:")) && ssrModules.length === 5);
    assert.equal(ssr.invalidated.length, 5);
    assert.equal((await log()).filter(line => line.startsWith("serve-generate")).length, 2, "build start plus one change");

    const fail = join(root, "fail"); await writeFile(fail, "");
    const results = await Promise.allSettled([hot(plugin, english, client, 2000), hot(plugin, english, ssr, 2000)]);
    assert.deepEqual(results.map(result => result.status), ["rejected", "fulfilled"], "a failed change is reported once");
    plugin.closeBundle();
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("a crashed persistent compiler falls back to one-shot generation, restarts, and is disabled after repeated crashes", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-crash-"));
  try {
    const { plugin, english, moduleGraph, log, warnings } = await servedProject(root, { crashOn: [2, 4, 5, 6] });
    await plugin.buildStart.call({ addWatchFile() {} });
    await hot(plugin, english, moduleGraph);
    // The second request crashes the compiler: that save falls back to one-shot generation.
    assert.deepEqual(await log(), ["serve-start", "serve-generate esm", "serve-generate esm", "one-shot generate"]);
    await hot(plugin, english, moduleGraph);
    assert.deepEqual((await log()).slice(4), ["serve-start", "serve-generate esm"], "the next save restarts the compiler");
    for (let save = 0; save < 4; save++) await hot(plugin, english, moduleGraph);
    assert.deepEqual((await log()).slice(6), [
      "serve-generate esm", "one-shot generate",
      "serve-start", "serve-generate esm", "one-shot generate",
      "serve-start", "serve-generate esm", "one-shot generate",
      "one-shot generate",
    ], "three crashes in a row disable the compiler");
    assert.equal(warnings.length, 1, "disabling is reported once");
    assert.match(warnings[0], /one-shot generation.*exited 3 times/);
    plugin.closeBundle();
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("a hung request times out, stops the compiler with its children, and falls back", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-hang-"));
  try {
    const { plugin, english, moduleGraph, log, grandchild } = await servedProject(root, { hangOn: [2], timeout: 500 });
    await plugin.buildStart.call({ addWatchFile() {} });
    const started = Date.now();
    await hot(plugin, english, moduleGraph);
    assert.ok(Date.now() - started < 10_000, "the hung request blocked the change");
    assert.deepEqual(await log(), ["serve-start", "serve-generate esm", "serve-generate esm", "one-shot generate"]);
    const pid = Number(await readFile(grandchild, "utf8"));
    const alive = () => { try { process.kill(pid, 0); return true; } catch { return false; } };
    for (let attempt = 0; attempt < 100 && alive(); attempt++) await new Promise(resolveDelay => setTimeout(resolveDelay, 20));
    if (process.platform !== "win32") assert.equal(alive(), false, "the compiler's child process survived the timeout");
    await hot(plugin, english, moduleGraph);
    assert.deepEqual((await log()).slice(4), ["serve-start", "serve-generate esm"], "the next change restarts the compiler");
    plugin.closeBundle();
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("a request the compiler could not read falls back without restarting it", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-unreadable-"));
  try {
    const { plugin, english, moduleGraph, log } = await servedProject(root, { unreadableOn: [2] });
    await plugin.buildStart.call({ addWatchFile() {} });
    await hot(plugin, english, moduleGraph);
    await hot(plugin, english, moduleGraph);
    assert.deepEqual(await log(), ["serve-start", "serve-generate esm", "serve-generate esm", "one-shot generate", "serve-generate esm"]);
    assert.throws(() => runicTranslations({ persistentCompilerTimeout: 0 }), /persistentCompilerTimeout/);
    plugin.closeBundle();
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("tools without serve mode and persistentCompiler false use one-shot generation", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-one-shot-"));
  try {
    const project = join(root, "translations"), output = join(root, "generated"), english = join(project, "en.rmf2");
    await mkdir(project);
    await writeFile(join(project, "runic.json"), JSON.stringify({ schemaVersion: 1, catalog: "app" }));
    await writeFile(english, "title = Shop\n");
    await writeV3Fixture(join(output, "app.esm-v5"));
    const calls = join(root, "calls.txt"), legacy = join(root, "legacy.mjs");
    // Like a tool release before serve mode: an unknown command fails with a usage error.
    await writeFile(legacy, `import { appendFileSync } from "node:fs";
appendFileSync(${JSON.stringify(calls)}, process.argv[2] + "\\n");
if (process.argv[2] === "serve") { process.stderr.write("runic-translations: unknown command 'serve'.\\n"); process.exit(2); }`);
    const fake = await writeServeCompiler(root);
    for (const [commandArguments, persistentCompiler, expected] of [
      [[legacy], undefined, ["serve", "generate", "generate"]],
      [[fake.compiler], false, ["one-shot generate", "one-shot generate"]],
    ]) {
      await rm(calls, { force: true });
      const plugin = runicTranslations({ project, output, command: process.execPath, commandArguments, persistentCompiler });
      const warnings = [], watcher = new EventEmitter(); watcher.add = () => {};
      const moduleGraph = { getModuleById: () => undefined, getModulesByFile: () => new Set(), invalidateModule() {} };
      plugin.configureServer({ watcher, httpServer: new EventEmitter(), ws: { send() {} }, moduleGraph, config: { logger: { warn: message => warnings.push(message) } } });
      await plugin.buildStart.call({ addWatchFile() {} });
      await hot(plugin, english, moduleGraph);
      assert.deepEqual((await readFile(calls, "utf8")).trim().split("\n"), expected);
      if (persistentCompiler === undefined) assert.match(warnings.join("\n"), /unknown command 'serve'/);
      plugin.closeBundle();
    }
    assert.throws(() => runicTranslations({ project, output, persistentCompiler: "yes" }), /persistentCompiler must be a boolean/);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("published declarations type-check a Vite configuration", async () => {
  // Inside the package, so the import resolves through the package's own exports like a consumer's would.
  const root = await mkdtemp(join(new URL("..", import.meta.url).pathname, ".declarations-"));
  try {
    const config = join(root, "vite.config.ts");
    await writeFile(config, `import { defineConfig, type Plugin } from "vite";
import { runicTranslations, type RunicTranslationsOptions } from "@runic-artifex/vite-plugin-runic-translations";
const options: RunicTranslationsOptions = { project: "translations", persistentCompiler: false, typeDeclarations: false };
const plugin: Plugin = runicTranslations(options);
// @ts-expect-error persistentCompiler is a boolean.
runicTranslations({ persistentCompiler: "yes" });
// @ts-expect-error unknown options are rejected.
runicTranslations({ projects: "translations" });
export default defineConfig({ plugins: [plugin, runicTranslations({ manifest: "generated/web-module-manifest-v3.json" })] });
`);
    const tsconfig = join(root, "tsconfig.json");
    await writeFile(tsconfig, JSON.stringify({
      compilerOptions: { module: "NodeNext", moduleResolution: "NodeNext", target: "ES2022", strict: true, noEmit: true, skipLibCheck: false, types: ["node"] },
      files: [config],
    }));
    await execFileAsync(process.execPath, ["x", "tsc", "-p", tsconfig, "--pretty", "false"], { cwd: new URL("..", import.meta.url) });
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("published package inventory contains only declared runtime files", async () => {
  const packageRoot = new URL("..", import.meta.url), output = await mkdtemp(join(tmpdir(), "runic-vite-package-"));
  try {
    await execFileAsync("bun", ["pm", "pack", "--destination", output, "--quiet"], { cwd: packageRoot });
    const archives = (await readdir(output)).filter(file => file.endsWith(".tgz")); assert.equal(archives.length, 1);
    const { stdout } = await execFileAsync("tar", ["-tzf", join(output, archives[0])]);
    const files = stdout.split("\n").filter(file => file.startsWith("package/") && !file.endsWith("/")).map(file => file.slice("package/".length)).sort();
    assert.deepEqual(files, ["LICENSE", "README.md", "dist/compiler-service.d.ts", "dist/compiler-service.js", "dist/index.d.ts", "dist/index.js", "package.json"]);
  } finally { await rm(output, { recursive: true, force: true }); }
});
