import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const sourceUrl = new URL("../src/lib/local-state.ts", import.meta.url);
const source = await readFile(sourceUrl, "utf8");
assert.equal(source.includes("localStorage"), false,
  "Editor-owned state must not fall back to browser-origin localStorage.");
const transpiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  fileName: sourceUrl.pathname,
});
const localState = await import(`data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString("base64")}`);

const persisted = new Map([
  ["runic-translations.theme-mode", "dark"],
  ["runic.sidebar.languages-share", "0.500"],
  ["runic-translations:recent:1", '[{"root":"/private/customer","catalogId":"product"}]'],
  ["runic-translations:drafts:1:/private/customer\nproduct", '{"documents":{"de.json":{"content":"secret translation"}}}'],
]);
let saves = 0;
localState.configureLocalEditorState({
  async loadLocalState() {
    return { entries: [...persisted].map(([key, value]) => ({ key, value })), recovered: false };
  },
  async saveLocalState(entries) {
    persisted.clear();
    for (const entry of entries) persisted.set(entry.key, entry.value);
    saves++;
    return { recovered: false };
  },
  async clearLocalState() {
    const removedEntries = persisted.size;
    persisted.clear();
    return { removedEntries, recovered: false };
  },
});

await localState.loadLocalEditorState();
const summary = localState.inspectLocalEditorState();
assert.deepEqual(
  { entries: summary.entries, preferenceEntries: summary.preferenceEntries, recentProjectEntries: summary.recentProjectEntries, draftEntries: summary.draftEntries },
  { entries: 4, preferenceEntries: 2, recentProjectEntries: 1, draftEntries: 1 },
  "The local-state inventory must count only editor-owned state.",
);
assert.ok(summary.bytes > 0, "The local-state inventory must provide a bounded size signal.");
assert.equal(JSON.stringify(summary).includes("private"), false,
  "The local-state inspection surface must never reveal a workspace path or draft content.");

localState.setLocalEditorState("runic-translations.theme-mode", "light");
localState.setLocalEditorState("runic-translations.theme-palette", "fjord");
await localState.flushLocalEditorState();
assert.equal(persisted.get("runic-translations.theme-mode"), "light",
  "Queued writes must atomically publish the newest complete native projection.");
assert.equal(persisted.get("runic-translations.theme-palette"), "fjord");
assert.ok(saves >= 2, "Each changed projection must be sent through the native bridge.");
assert.throws(() => localState.setLocalEditorState("unrelated.application", "keep me"), /not owned/,
  "The UI must not use the editor store for another application's data.");

assert.equal(await localState.clearLocalEditorState(), 5,
  "Clear must remove every editor-owned native entry.");
assert.deepEqual(localState.inspectLocalEditorState(), {
  entries: 0, bytes: 0, preferenceEntries: 0, recentProjectEntries: 0, draftEntries: 0, recovered: false,
}, "Cleared state must inspect as empty.");

console.log("PASS: native editor state is privacy-safely inspected, atomically projected, and cleared without browser-origin storage.");
