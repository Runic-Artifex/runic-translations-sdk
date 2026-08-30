import { readFile, readdir } from "node:fs/promises";
import { extname } from "node:path";

const build = new URL("../build/", import.meta.url);
const manifest = JSON.parse(
  await readFile(new URL("../../Contract/bridge.manifest.json", import.meta.url), "utf8"));
const pinned = /fingerprint:\s*"([0-9a-f]{64})"/
  .exec(await readFile(new URL("../src/lib/editor-contract.ts", import.meta.url), "utf8"))?.[1];
if (manifest.contractFingerprint === undefined || !/^[0-9a-f]{64}$/.test(manifest.contractFingerprint))
  throw new Error("Contract/bridge.manifest.json does not carry a generated contractFingerprint.");
if (pinned === undefined)
  throw new Error("Frontend/src/lib/editor-contract.ts pins no bridge contract fingerprint.");
if (manifest.contractFingerprint !== pinned)
  throw new Error(
    `Bridge contract drift: Contract/bridge.manifest.json carries ${manifest.contractFingerprint} but editor-contract.ts pins ${pinned}. Regenerate Contract/ from Frontend/contract/editor-contract.mjs and update the editor-contract.ts constant.`);
const index = await readFile(new URL("index.html", build), "utf8");
if (!index.includes('src="./runic-desktop.js"')) throw new Error("The production shell omitted the relative Runic Desktop bootstrap.");
if (!index.includes("./_app/immutable/")) throw new Error("The relocatable SvelteKit client entry was not emitted.");

const scripts = [];
await collect(build, scripts);
const bundled = (await Promise.all(scripts.map((file) => readFile(file, "utf8")))).join("\n");
for (const text of ["Translations", "Übersetzungen", "runic.translations.editor", "InitializeApplication", "LoadWorkspace", "SaveDocument", "SaveReview", "RecoverTransaction", "UndoApplied", "RedoApplied", "AboutLoaded", "CreateDiagnosticBundle", "MessagePreviewed", "Translate the message", "Create new variable", "Message source", "Preview", "Editor settings", "Runic Gold", "Fjord", "Ember", "Resize Languages and Messages", "Quality report", "About & diagnostics", "Terminology", "schema", "Saved the earlier draft; your newer edit is still open.", "Recovery completed; reload required", "Discard unsaved document drafts, repair text, and workflow/terminology changes", "Local editor state", "Clear local state"]) {
  if (!bundled.includes(text)) throw new Error(`The production client omitted '${text}'.`);
}
if (!bundled.includes(manifest.contractFingerprint))
  throw new Error("The production client shipped a stale bridge contract fingerprint.");
if (bundled.includes("node:fs") || bundled.includes("Runic.Translations.Compiler.dll")) {
  throw new Error("Server/compiler implementation details leaked into the browser bundle.");
}

console.log(`PASS: static SvelteKit client contains the Runic Desktop bootstrap and generated Runic ESM (${scripts.length} scripts).`);

async function collect(directory, result) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const location = new URL(entry.name + (entry.isDirectory() ? "/" : ""), directory);
    if (entry.isDirectory()) await collect(location, result);
    else if (extname(entry.name) === ".js") result.push(location);
  }
}
