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
// Each import is a fresh module instance, so a case can start from an unloaded store bound to its own bridge.
let instances = 0;
const importLocalState = () => import(`data:text/javascript;base64,${Buffer.from(`${transpiled.outputText}\n// instance ${++instances}\n`).toString("base64")}`);
const localState = await importLocalState();
const unhandled = [];
process.on("unhandledRejection", reason => { unhandled.push(reason); });
const settle = () => new Promise(resolveSettle => setTimeout(resolveSettle, 0));

const persisted = new Map([
  ["runic-translations.theme-mode", "dark"],
  // A colour palette an earlier version stored (removed by D074): never taken over, so the next write drops it.
  ["runic-translations.theme-palette", "moss"],
  ["runic.sidebar.languages-share", "0.500"],
  ["runic-translations:recent:1", '[{"root":"/private/customer","catalogId":"product"}]'],
  ["runic-translations:drafts:1:/private/customer\nproduct", '{"documents":{"de.json":{"content":"secret translation"}}}'],
  ["runic-translations:review-drafts:1:/private/customer\nproduct", '{"version":1,"entries":[{"entry":{"key":"k","locale":"de","state":"approved","note":"private note"}}]}'],
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
  { entries: 5, preferenceEntries: 2, recentProjectEntries: 1, draftEntries: 2 },
  "The local-state inventory must count only editor-owned state.",
);
assert.ok(summary.bytes > 0, "The local-state inventory must provide a bounded size signal.");
assert.equal(localState.getLocalEditorState("runic-translations.theme-palette"), null,
  "A stored palette from an earlier version must not be loaded.");
assert.equal(JSON.stringify(summary).includes("private"), false,
  "The local-state inspection surface must never reveal a workspace path or draft content.");

localState.setLocalEditorState("runic-translations.theme-mode", "light");
localState.setLocalEditorState("runic-translations.density", "compact");
await localState.flushLocalEditorState();
assert.equal(persisted.get("runic-translations.theme-mode"), "light",
  "Queued writes must atomically publish the newest complete native projection.");
assert.equal(persisted.get("runic-translations.density"), "compact", "The density is editor-owned state.");
assert.equal(persisted.has("runic-translations.theme-palette"), false,
  "The next write must leave a stored palette out of the native record.");
assert.throws(() => localState.setLocalEditorState("runic-translations.theme-palette", "fjord"), /not owned/,
  "The editor no longer stores a colour palette.");
assert.ok(saves >= 2, "Each changed projection must be sent through the native bridge.");
localState.setLocalEditorState("runic-translations:message-tree:1", '{"version":1,"catalogs":[]}');
await localState.flushLocalEditorState();
assert.equal(persisted.get("runic-translations:message-tree:1"), '{"version":1,"catalogs":[]}',
  "The collapsed message groups are editor-owned state.");
assert.throws(() => localState.setLocalEditorState("unrelated.application", "keep me"), /not owned/,
  "The UI must not use the editor store for another application's data.");

assert.equal(await localState.clearLocalEditorState(), 7,
  "Clear must remove every editor-owned native entry.");
assert.deepEqual(localState.inspectLocalEditorState(), {
  entries: 0, bytes: 0, preferenceEntries: 0, recentProjectEntries: 0, draftEntries: 0, recovered: false,
}, "Cleared state must inspect as empty.");

// A reload, navigation or closed window ends the Bridge while a write is in flight. The rejected write must never
// surface as an unhandled rejection (CI: `BridgeError: The Bridge is disconnected.` on reload), and the projection
// is written again once the host reconnects.
{
  const disconnected = Object.assign(new Error("The Bridge is disconnected."), { kind: "disconnected" });
  const stored = new Map([["runic-translations.theme-mode", "dark"]]);
  let connected = true;
  const written = [];
  const warnings = [];
  const warn = console.warn;
  console.warn = (...parts) => { warnings.push(parts); };
  try {
    const interrupted = await importLocalState();
    interrupted.configureLocalEditorState({
      async loadLocalState() {
        return { entries: [...stored].map(([key, value]) => ({ key, value })), recovered: false };
      },
      async saveLocalState(entries) {
        await settle();
        if (!connected) throw disconnected;
        stored.clear();
        for (const entry of entries) stored.set(entry.key, entry.value);
        written.push(entries.map(entry => `${entry.key}=${entry.value}`).join(","));
        return { recovered: false };
      },
      async clearLocalState() { throw new Error("not used"); },
    });
    await interrupted.loadLocalEditorState();

    connected = false;
    interrupted.setLocalEditorState("runic-translations.ui-locale", "de");
    interrupted.setLocalEditorState("runic-translations.density", "compact");
    // Nothing awaits these writes, as in the page; let both fail before anything observes the queue.
    for (let turn = 0; turn < 6; turn++) await settle();
    assert.deepEqual(unhandled, [], "An interrupted native write must not surface as an unhandled rejection.");
    await assert.rejects(interrupted.flushLocalEditorState(), error => error === disconnected,
      "A flush must report a write the lost connection interrupted.");
    assert.equal(warnings.length, 2, "Each interrupted native write is reported as a warning.");
    assert.equal(interrupted.getLocalEditorState("runic-translations.ui-locale"), "de",
      "An interrupted write must keep the change in the projection.");
    assert.deepEqual(written, [], "Nothing reached the disconnected host.");

    // The host reconnected: the newest complete projection is written once, then nothing is pending.
    connected = true;
    interrupted.resumeLocalEditorState();
    await interrupted.flushLocalEditorState();
    assert.deepEqual(written, [
      "runic-translations.density=compact,runic-translations.theme-mode=dark,runic-translations.ui-locale=de",
    ], "A reconnect must write the interrupted change again, as one complete replacement.");
    interrupted.resumeLocalEditorState();
    await interrupted.flushLocalEditorState();
    assert.equal(written.length, 1, "A reconnect without an interrupted write must not write again.");

    // A failed write is also retried by the next change, without waiting for a reconnect event.
    connected = false;
    interrupted.setLocalEditorState("runic-translations.ui-locale", "en");
    await assert.rejects(interrupted.flushLocalEditorState());
    connected = true;
    interrupted.setLocalEditorState("runic-translations.theme-mode", "light");
    await interrupted.flushLocalEditorState();
    assert.equal(written.at(-1),
      "runic-translations.density=compact,runic-translations.theme-mode=light,runic-translations.ui-locale=en",
      "The next change must include the change whose write failed.");

    // A failed load leaves the native record unknown: no write may replace it with this session's values alone,
    // and the refused writes must not surface either.
    const unloaded = await importLocalState();
    let unloadedWrites = 0;
    unloaded.configureLocalEditorState({
      async loadLocalState() { throw disconnected; },
      async saveLocalState() { unloadedWrites++; return { recovered: false }; },
      async clearLocalState() { throw new Error("not used"); },
    });
    await assert.rejects(unloaded.loadLocalEditorState());
    unloaded.setLocalEditorState("runic-translations.ui-locale", "de");
    unloaded.resumeLocalEditorState();
    for (let turn = 0; turn < 3; turn++) await settle();
    assert.deepEqual(unhandled, [], "Writes after a failed load must not surface as unhandled rejections.");
    await assert.rejects(unloaded.flushLocalEditorState(), error => error === disconnected);
    assert.equal(unloadedWrites, 0, "A store whose load failed must not be replaced by a partial projection.");
  } finally {
    console.warn = warn;
  }
}

console.log("PASS: native editor state is privacy-safely inspected, atomically projected, cleared without browser-origin storage, and survives an interrupted write without an unhandled rejection.");
