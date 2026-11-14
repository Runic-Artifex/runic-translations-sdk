import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { createHash } from "node:crypto";
import { EventEmitter } from "node:events";
import { mkdtemp, mkdir, readFile, readdir, realpath, rm, symlink, writeFile } from "node:fs/promises";
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
export const rmf2RuntimeAbiVersion = 3;
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
    webModuleManifestVersion: 3, esmAbiVersion: 4, rmf2RuntimeAbiVersion: 3,
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
      ["rmf2RuntimeAbiVersion", 2, /execution contract/],
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

// Source map v3 VLQ, written independently of the plugin's codec.
const base64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
function vlq(value) {
  let remaining = value < 0 ? (-value << 1) | 1 : value << 1, text = "";
  do { let digit = remaining & 31; remaining >>>= 5; if (remaining > 0) digit |= 32; text += base64[digit]; } while (remaining > 0);
  return text;
}
// Every segment of a map, absolute and 0-based: [line, column] or [line, column, source, sourceLine, sourceColumn].
function segments(mappings) {
  const result = []; let source = 0, sourceLine = 0, sourceColumn = 0;
  mappings.split(";").forEach((text, line) => {
    let column = 0;
    for (const field of text ? text.split(",") : []) {
      const values = []; let value = 0, shift = 0;
      for (const character of field) {
        const digit = base64.indexOf(character); assert.ok(digit >= 0, `base64 digit in ${field}`);
        value += (digit & 31) << shift;
        if (digit & 32) { shift += 5; continue; }
        values.push(value & 1 ? -(value >>> 1) : value >>> 1); value = 0; shift = 0;
      }
      assert.ok(values.length === 1 || values.length === 4, `segment ${field}`);
      column += values[0];
      if (values.length === 1) { result.push([line, column]); continue; }
      source += values[1]; sourceLine += values[2]; sourceColumn += values[3];
      result.push([line, column, source, sourceLine, sourceColumn]);
    }
  });
  return result;
}

test("virtual.d.ts.map carries messages.d.ts.map, so Go to Definition on m.key() opens the message source", async () => {
  const root = await realpath(await mkdtemp(join(tmpdir(), "runic-vite-declaration-map-")));
  try {
    const generated = join(root, "generated", "app.esm-v5"), source = join(root, "translations", "en.rmf2");
    await mkdir(dirname(source), { recursive: true });
    await writeFile(source, "application_title = Shop\n\n  greeting = Hello {$name}, {$count}\n");
    const manifest = await writeV3Fixture(generated);
    const messages = `// <auto-generated />
import type { MessageOptions } from "./runtime.js";
export declare const m: Readonly<{
    readonly "application_title": (options?: MessageOptions) => string;
    readonly "greeting": (inputs: Readonly<{ readonly "name": string; readonly "count": bigint | number }>, options?: MessageOptions) => string;
}>;
//# sourceMappingURL=messages.d.ts.map
`;
    await writeFile(join(generated, "messages.d.ts"), messages);
    // As the compiler writes it: a generated-only segment starting every line, then each name and the column after it.
    const mappings = ["A", "A", "A", `A,${vlq(13)}AAA,${vlq(19)}`, `A,${vlq(13)}AEE,${vlq(10)}`, "A", "A"].join(";");
    await writeFile(join(generated, "messages.d.ts.map"), JSON.stringify({ version: 3, file: "messages.d.ts", sourceRoot: "", sources: ["../../translations/en.rmf2"], names: [], mappings }));
    const document = JSON.parse(await readFile(manifest, "utf8"));
    await writeGeneratedManifest(manifest, { ...document, assets: document.assets.map(({ path }) => ({ path })) });
    await runicTranslations({ manifest }).buildStart.call({ addWatchFile() {} });

    const declarations = join(generated, "virtual.d.ts");
    const text = await readFile(declarations, "utf8"), map = JSON.parse(await readFile(`${declarations}.map`, "utf8"));
    assert.match(text, /\n\/\/# sourceMappingURL=virtual\.d\.ts\.map\n$/);
    assert.equal(text.split("sourceMappingURL").length - 1, 1, "only the declarations' own comment");
    assert.deepEqual([map.version, map.file, map.sources], [3, "virtual.d.ts", ["../../translations/en.rmf2"]]);
    // Every segment is valid (one invalid segment silently disables the whole map in tsserver), and every line starts with one.
    const lines = text.split("\n"), sourceLines = (await readFile(source, "utf8")).split("\n"), decoded = segments(map.mappings);
    assert.equal(map.mappings.split(";").length, lines.length - 1);
    for (const [line, column, index, sourceLine, sourceColumn] of decoded) {
      assert.ok(line < lines.length - 1 && column >= 0 && column <= lines[line].length, `generated ${line}:${column}`);
      if (index !== undefined) assert.ok(index === 0 && sourceLine >= 0 && sourceLine < sourceLines.length && sourceColumn >= 0 && sourceColumn <= sourceLines[sourceLine].length, `source ${sourceLine}:${sourceColumn}`);
    }
    for (let line = 0; line < lines.length - 1; line++) assert.ok(decoded.some(item => item[0] === line && item[1] === 0), `line ${line} starts with a segment`);
    // Both copies of messages.d.ts (the catalog and its /messages module) map each key to its line.
    const mapped = decoded.filter(item => item.length === 5).map(([line, column, , sourceLine, sourceColumn]) => [lines[line].slice(column).split(":")[0], sourceLine, sourceColumn]);
    assert.deepEqual(mapped, [['"application_title"', 0, 0], ['"greeting"', 2, 2], ['"application_title"', 0, 0], ['"greeting"', 2, 2]]);

    const consumer = join(root, "consumer.ts");
    await writeFile(consumer, `import { m } from "virtual:runic-translations/app";
import { m as messages } from "virtual:runic-translations/app/messages";
export const greeting = m.greeting({ name: "Ada", count: 2 });
export const title = messages.application_title();
`);
    await writeFile(join(root, "tsconfig.json"), JSON.stringify({ compilerOptions: { module: "ESNext", moduleResolution: "Bundler", target: "ES2022", strict: true, noEmit: true }, files: ["consumer.ts", "generated/app.esm-v5/virtual.d.ts"] }));
    const { definitions } = await import("./tsserver.js");
    const found = await definitions(consumer, [[3, 28], [4, 31], [1, 10]]);
    assert.deepEqual(found.slice(0, 2), [[`${source}:3:3`], [`${source}:1:1`]]);
    assert.match(found[2][0], /virtual\.d\.ts:\d+:\d+$/, "m itself stays in the declarations");

    // A catalog without a map (an older compiler) leaves no map behind: tsserver would read <file>.map without a comment.
    await writeFile(join(generated, "messages.d.ts"), messages.replace("//# sourceMappingURL=messages.d.ts.map\n", ""));
    await writeGeneratedManifest(manifest, { ...document, assets: document.assets.map(({ path }) => ({ path })) });
    await runicTranslations({ manifest }).buildStart.call({ addWatchFile() {} });
    assert.doesNotMatch(await readFile(declarations, "utf8"), /sourceMappingURL/);
    await assert.rejects(() => readFile(`${declarations}.map`), error => error?.code === "ENOENT");
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("rejects hostile, stale, and forged v3 output", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-hostile-"));
  try {
    const generated = join(root, "app.esm-v5");
    const manifest = await writeV3Fixture(generated);
    await writeFile(join(generated, "extra.js"), "export {};\n");
    const valid = JSON.parse(await readFile(manifest, "utf8"));
    // Members a later compiler adds within ESM ABI 4 are ignored (T1e): a library built by a newer patch still loads.
    for (const [label, mutate] of [
      ["unknown root member", document => { document.extra = true; }],
      ["unknown asset member", document => { document.assets[0].extra = true; }],
      ["unknown entrypoint", document => { document.entrypoints.extra = "extra.js"; }],
    ]) {
      const document = JSON.parse(JSON.stringify(valid));
      mutate(document);
      await writeFile(manifest, JSON.stringify(document));
      await runicTranslations({ manifest, typeDeclarations: false }).buildStart.call({ addWatchFile() {} });
      assert.ok(true, label);
    }
    const rejected = [
      ["malformed asset", document => { document.assets[0] = 1; }, /malformed asset/],
      ["shared runtime outside its place", document => { document.sharedRuntime = { path: "../../runtime-core.js", sha256: "0".repeat(64), byteLength: 0 }; }, /shared runtime/],
      ["duplicate non-entrypoint asset path", document => {
        const asset = document.assets.find(item => item.path === "messages.js");
        document.assets.push({ ...asset, path: "extra.js" }, { ...asset, path: "extra.js", mediaType: "text/typescript" });
      }, /invalid generated asset/],
      ["path traversal", document => { document.assets[0].path = "x/../messages.js"; }, /invalid generated asset/],
      ["wrong entrypoint", document => { document.entrypoints.dynamic = "other.js"; }, /invalid entrypoints/],
      ["runtime ABI marker", document => { document.rmf2RuntimeAbiVersion = 2; }, /execution contract/],
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
    // A renamed catalog reloads: the old and the new virtual modules are invalidated, and nothing is left to propagate.
    assert.deepEqual(await hot(plugin, manifest, server.moduleGraph), []);
    assert.ok(invalidated.includes("\0virtual:runic-translations/app/messages"));
    assert.ok(invalidated.includes("\0virtual:runic-translations/renamed/messages"));
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

test("dependencies: runic.json overrides and locales regenerate the dependency catalogs; an invalid runic.json keeps the last settings", async () => {
  const root = await realpath(await mkdtemp(join(tmpdir(), "runic-vite-dependencies-")));
  try {
    const project = join(root, "translations"), output = join(root, "generated"), config = join(project, "runic.json");
    await mkdir(join(project, "overrides", "acme.grid"), { recursive: true });
    await writeFile(join(project, "overrides", "acme.grid", "en.rmf2"), "empty = Nothing\n");
    await writeFile(join(project, "en.rmf2"), "title = Shop\n");
    const settings = locales => JSON.stringify({ schemaVersion: 1, catalog: "app", baseLocale: "en", locales, overrides: [{ catalog: "acme.grid", from: "@acme/grid" }] });
    await writeFile(config, settings(["en", "fr"]));
    await writeFile(join(root, "package.json"), JSON.stringify({ name: "shop", dependencies: { "@acme/grid": "1.0.0" } }));
    const library = join(root, "node_modules", "@acme", "grid");
    await mkdir(join(library, "runic", "acme.grid"), { recursive: true });
    await mkdir(join(library, "translations", "acme.grid.esm-v5"), { recursive: true });
    await writeFile(join(library, "package.json"), JSON.stringify({ name: "@acme/grid", version: "1.0.0",
      exports: { "./runic-translations": "./translations/acme.grid.esm-v5/web-module-manifest-v3.json" }, runicTranslations: { catalog: "acme.grid", project: "./runic/acme.grid/runic.json" } }));
    await writeFile(join(library, "runic", "acme.grid", "runic.json"), "{}");
    await writeFile(join(library, "translations", "acme.grid.esm-v5", "web-module-manifest-v3.json"), JSON.stringify({ catalog: "acme.grid", contractFingerprint: fingerprint }));
    await writeV3Fixture(join(output, "app.esm-v5"));
    await writeV3Fixture(join(output, "dependencies", "acme.grid.esm-v5"), { catalog: "acme.grid" });
    const calls = join(root, "calls.txt"), compiler = join(root, "compiler.mjs");
    await writeFile(compiler, `import { appendFile } from "node:fs/promises"; await appendFile(${JSON.stringify(calls)}, process.argv.slice(2).join("|") + "\\n");`);
    const dependencyCalls = async () => (await readFile(calls, "utf8")).split("\n").filter(line => line.includes("--shared-runtime"));
    const plugin = runicTranslations({ project, output, cwd: root, dependencies: "auto", command: process.execPath, commandArguments: [compiler], persistentCompiler: false, typeDeclarations: false });
    const warnings = [];
    plugin.config({ root: join(root, "src") });
    plugin.configResolved({ logger: { warn: message => warnings.push(message), info() {} }, mode: "production" });
    const watcher = new EventEmitter(), watched = [], reloads = new EventEmitter();
    watcher.add = paths => watched.push(...(Array.isArray(paths) ? paths : [paths]));
    const server = { watcher, httpServer: new EventEmitter(), ws: { send: value => reloads.emit("reload", value) }, moduleGraph: { getModuleById: () => undefined, getModulesByFile: () => new Set(), invalidateModule() {} } };
    plugin.configureServer(server);
    await plugin.buildStart.call({ addWatchFile() {} });
    assert.ok(watched.includes(join(project, "overrides")), "the overrides directory is watched");
    assert.match((await dependencyCalls()).join("\n"), /--overrides\|.*acme\.grid\|--locales\|en,fr$/);

    // Mid-edit, runic.json is invalid: reported once, and the dependencies keep their settings.
    await writeFile(config, "{");
    watcher.emit("change", config);
    await new Promise(resolve => setTimeout(resolve, 200));
    assert.equal((await dependencyCalls()).length, 1);
    assert.ok(warnings.some(message => /runic\.json cannot be read.*last valid overrides and locales/.test(message)), warnings.join("\n"));

    // New locales regenerate the dependencies and reload.
    await writeFile(config, settings(["en", "de", "it"]));
    const reloaded = new Promise((resolve, reject) => { const timer = setTimeout(() => reject(new Error("No reload")), 5000); reloads.once("reload", value => { clearTimeout(timer); resolve(value); }); });
    watcher.emit("change", config);
    assert.equal((await reloaded).type, "full-reload");
    assert.match((await dependencyCalls()).at(-1), /--locales\|en,de,it$/);

    // "from" may list the library's NuGet package and npm package, so the runic.json of a .NET build serves this build too.
    await writeFile(config, JSON.stringify({ schemaVersion: 1, catalog: "app", baseLocale: "en", locales: ["en", "fr"], overrides: [{ catalog: "acme.grid", from: ["Acme.Grid", "@acme/grid"] }] }));
    const listed = new Promise((resolve, reject) => { const timer = setTimeout(() => reject(new Error("No reload")), 5000); reloads.once("reload", value => { clearTimeout(timer); resolve(value); }); });
    watcher.emit("change", config);
    assert.equal((await listed).type, "full-reload");
    assert.match((await dependencyCalls()).at(-1), /--overrides\|.*acme\.grid\|--locales\|en,fr$/);

    // An override of a catalog no dependency ships is an error, as RTR0104 is in a .NET build.
    await writeFile(config, JSON.stringify({ schemaVersion: 1, catalog: "app", baseLocale: "en", overrides: [{ catalog: "acme.grid", from: "@acme/grid" }, { catalog: "acme.menu", from: "@acme/menu" }] }));
    const failed = new Promise((resolve, reject) => { const timer = setTimeout(() => reject(new Error("No error")), 5000); reloads.once("reload", value => { clearTimeout(timer); resolve(value); }); });
    watcher.emit("change", config);
    const error = await failed;
    assert.equal(error.type, "error");
    assert.match(error.err.message, /RTR0104: Catalog 'acme\.menu' to override is not shipped by any dependency/);

    // A list without the dependency's npm package is RTR0104 too.
    await writeFile(config, JSON.stringify({ schemaVersion: 1, catalog: "app", baseLocale: "en", overrides: [{ catalog: "acme.grid", from: ["Acme.Grid"] }] }));
    const unlisted = new Promise((resolve, reject) => { const timer = setTimeout(() => reject(new Error("No error")), 5000); reloads.once("reload", value => { clearTimeout(timer); resolve(value); }); });
    watcher.emit("change", config);
    const mismatch = await unlisted;
    assert.equal(mismatch.type, "error");
    assert.match(mismatch.err.message, /RTR0104: Catalog 'acme\.grid' comes from package '@acme\/grid', not 'Acme\.Grid'/);
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

// Tree-shaking of the generated namespace m is tested on real compiler output, through this plugin, by the Compiler
// suite's "RMF2 v5 ESM namespace m tree-shakes to the called message in a Vite build of compiler output".

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
    // The fake compiler rewrites nothing, so the regenerated catalog is the same and no environment has anything to do.
    const [clientModules, ssrModules] = await Promise.all([hot(plugin, english, client, 1000), hot(plugin, english, ssr, 1000)]);
    assert.deepEqual([clientModules, ssrModules, client.invalidated, ssr.invalidated], [[], [], [], []]);
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

test("pseudoLocales follow Vite's mode unless the option sets them", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-pseudo-"));
  try {
    const project = join(root, "translations"), output = join(root, "generated"), calls = join(root, "calls.txt"), compiler = join(root, "compiler.mjs");
    await mkdir(project);
    await writeFile(join(project, "runic.json"), JSON.stringify({ schemaVersion: 1, catalog: "app", pseudoLocales: ["en-XA"] }));
    await writeFile(join(project, "en.rmf2"), "title = Shop\n");
    await writeV3Fixture(join(output, "app.esm-v5"));
    await writeFile(compiler, `import { appendFile } from "node:fs/promises"; await appendFile(${JSON.stringify(calls)}, process.argv.slice(2).join("|") + "\\n");`);
    for (const [option, mode, expected] of [
      [undefined, "development", true], [undefined, "production", false], [undefined, "staging", false],
      [false, "development", false], [true, "production", true],
    ]) {
      await rm(calls, { force: true });
      const plugin = runicTranslations({ project, output, command: process.execPath, commandArguments: [compiler], persistentCompiler: false, pseudoLocales: option });
      plugin.configResolved({ mode });
      await plugin.buildStart.call({ addWatchFile() {} });
      const line = (await readFile(calls, "utf8")).trim();
      assert.equal(line.split("|").includes("--pseudo-locales"), expected, `${option} ${mode}: ${line}`);
    }
    assert.throws(() => runicTranslations({ project, output, pseudoLocales: "yes" }), /pseudoLocales must be a boolean/);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test("manifest mode leaves a Debug build's pseudo-locales out of a production bundle", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-pseudo-manifest-"));
  try {
    const generated = join(root, "app.esm-v5");
    const manifest = await writeV3Fixture(generated);
    // The generator's own spelling (Rmf2EsmOutputRenderer): the declared locales and one message module.
    await writeFile(join(generated, "runtime.js"), (await readFile(join(generated, "runtime.js"), "utf8")) +
      'export const locales = Object.freeze(["ar-XB","en","en-XA"]);\n');
    await mkdir(join(generated, "messages"));
    const module = `// <auto-generated />
import { formatCompiledMessage, freezeGeneratedMessage } from "../runtime.js";
const messages = /* @__PURE__ */ (() => {
    const contract = JSON.parse("{\\"key\\":\\"title\\",\\"structured\\":false,\\"inputs\\":[]}");
    const messages = Object.create(null);
    messages["ar-XB"] = freezeGeneratedMessage(contract, JSON.parse("{\\"contentLocale\\":\\"ar-XB\\",\\"text\\":\\"MIRRORED\\"}"));
    messages["en"] = freezeGeneratedMessage(contract, JSON.parse("{\\"contentLocale\\":\\"en\\",\\"text\\":\\"Shop\\"}"));
    messages["en-XA"] = freezeGeneratedMessage(contract, JSON.parse("{\\"contentLocale\\":\\"en-XA\\",\\"text\\":\\"ACCENTED\\"}"));
    return Object.freeze(messages);
})();
`;
    await writeFile(join(generated, "messages", "r_7469746c65.js"), module);
    const document = JSON.parse(await readFile(manifest, "utf8"));
    await writeGeneratedManifest(manifest, { ...document, assets: [...document.assets.map(({ path }) => ({ path })), { path: "messages/r_7469746c65.js" }] });

    for (const [option, mode, kept] of [[undefined, "production", false], [undefined, "development", true], [true, "production", true], [false, "development", false]]) {
      const plugin = runicTranslations({ manifest, typeDeclarations: false, pseudoLocales: option });
      plugin.configResolved({ mode });
      await plugin.buildStart.call({ addWatchFile() {} });
      const runtime = plugin.transform(await readFile(join(generated, "runtime.js"), "utf8"), join(generated, "runtime.js"));
      const messages = plugin.transform(module, join(generated, "messages", "r_7469746c65.js") + "?v=1");
      const label = `${option} ${mode}`;
      if (kept) {
        assert.equal(runtime, null, label);
        assert.equal(messages, null, label);
        continue;
      }
      assert.match(runtime.code, /^export const locales = Object\.freeze\(\["en"\]\);$/m, label);
      assert.doesNotMatch(messages.code, /ACCENTED|MIRRORED|en-XA|ar-XB/, label);
      assert.match(messages.code, /messages\["en"\] = freezeGeneratedMessage/, label);
      // A file outside the manifest is not changed.
      assert.equal(plugin.transform(module, join(root, "other.js")), null, label);
      // A pseudo-locale message the filter does not recognize fails the build instead of shipping.
      assert.throws(() => plugin.transform(module.replace('messages["en-XA"] = freezeGeneratedMessage(', 'messages["en-XA"] =\n  freezeGeneratedMessage('), join(generated, "messages", "r_7469746c65.js")),
        /cannot remove/, label);
    }
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
    assert.deepEqual(files, ["LICENSE", "README.md", "dist/compiler-service.d.ts", "dist/compiler-service.js", "dist/declaration-map.d.ts", "dist/declaration-map.js", "dist/dependencies.d.ts", "dist/dependencies.js", "dist/hmr.d.ts", "dist/hmr.js", "dist/index.d.ts", "dist/index.js", "package.json"]);
  } finally { await rm(output, { recursive: true, force: true }); }
});
