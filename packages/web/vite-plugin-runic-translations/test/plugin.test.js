import assert from "node:assert/strict";
import { mkdtemp, mkdir, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { runicTextResources } from "../index.js";

test("resolves generated entrypoints and declares watch inputs", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-"));
  const generated = join(root, "app.esm");
  await mkdir(generated);
  await writeFile(join(generated, "messages.js"), "export const value = 1;\n");
  await writeFile(join(generated, "runtime.js"), "export const locale = 'en';\n");
  const manifest = join(generated, "web-module-manifest-v1.json");
  await writeFile(manifest, JSON.stringify({
    webModuleManifestVersion: 1,
    catalog: "app",
    entrypoints: { messages: "messages.js", runtime: "runtime.js", types: "messages.d.ts" },
    assets: [
      { path: "messages.js" },
      { path: "runtime.js" },
    ],
  }));
  const source = join(root, "en.json");
  await writeFile(source, "{}");
  const plugin = runicTextResources({ manifest, sourceFiles: [source] });
  const watched = [];
  await plugin.buildStart.call({ addWatchFile(path) { watched.push(path); } });
  assert.ok(watched.includes(manifest));
  assert.ok(watched.includes(source));
  const id = await plugin.resolveId("virtual:runic-text-resources/app");
  assert.equal(id, "\0virtual:runic-text-resources/app/messages");
  const module = await plugin.load(id);
  assert.match(module, /export \* from .*app\.esm\/messages\.js/);
});

test("rejects manifest paths that escape the generated root", async () => {
  const root = await mkdtemp(join(tmpdir(), "runic-vite-hostile-"));
  const manifest = join(root, "web-module-manifest-v1.json");
  await writeFile(manifest, JSON.stringify({
    webModuleManifestVersion: 1,
    catalog: "app",
    entrypoints: { messages: "../messages.js", runtime: "runtime.js" },
    assets: [],
  }));
  const plugin = runicTextResources({ manifest });
  await assert.rejects(() => plugin.resolveId("virtual:runic-text-resources/app"), /escapes/);
});
