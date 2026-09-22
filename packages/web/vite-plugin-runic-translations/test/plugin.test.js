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
    await assert.rejects(() => plugin.handleHotUpdate({ file: manifest, server }), /integrity/);
    assert.equal(await plugin.resolveId("virtual:runic-translations/app"), "\0virtual:runic-translations/app/messages");
    assert.equal(await plugin.resolveId("virtual:runic-translations/renamed"), null);
    assert.equal(await readFile(declarationPath, "utf8"), beforeDeclarations);
    assert.deepEqual(invalidated, []);

    await writeV3Fixture(generated, { catalog: "renamed" });
    const modules = await plugin.handleHotUpdate({ file: manifest, server });
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
    await assert.rejects(() => plugin.handleHotUpdate({ file: english, server }));
    const changed = { schemaVersion: 1, catalog: "renamed", sourceRoots: [{ path: "../second", namespace: ["second"] }] };
    await writeFile(config, JSON.stringify(changed));
    await assert.rejects(() => plugin.handleHotUpdate({ file: config, server }));
    assert.equal(await plugin.resolveId("virtual:runic-translations/app"), "\0virtual:runic-translations/app/messages");
    assert.equal(await plugin.resolveId("virtual:runic-translations/renamed"), null);
    await writeFile(english, "title = Fixed\n");
    await plugin.handleHotUpdate({ file: config, server });

    await writeFile(config, "{");
    await assert.rejects(() => plugin.handleHotUpdate({ file: config, server }), /Could not read/);
    assert.equal(await plugin.resolveId("virtual:runic-translations/renamed"), "\0virtual:runic-translations/renamed/messages");
    await writeFile(config, JSON.stringify(changed));
    await plugin.handleHotUpdate({ file: config, server });
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
    assert.deepEqual(await plugin.handleHotUpdate({ file: join(output, "renamed.esm-v5", "messages.js"), server }), []);
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
