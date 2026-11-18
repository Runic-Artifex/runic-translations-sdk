// The draft store's one pipeline: composer edits, authoring operations, raw-source validation and quick fixes run in
// one queue per document, and an answer is accepted only for the current workspace, draft generation and request.
import assert from "node:assert/strict";
import { deferred, settle } from "./svelte-modules.mjs";
import { mockExecute } from "../src/lib/mock-bridge.ts";

const { DraftStore } = await import("../src/lib/state/draft-store.svelte.ts");

const path = "en.rmf2";
const saved = "greeting = Hi\n";
const authoring = { supported: true, revision: "r1", selectors: [], inputs: [] };
const workspace = (root = "/work") => ({
  root,
  catalog: { id: "product", schemaVersion: 1, defaultLocale: "en", locales: [{ tag: "en" }], layers: [{ name: "base", priority: 0 }] },
  catalogs: [],
  documents: [{
    path, locale: "en", layer: "base", isManifest: false, isMalformed: false, revision: "rev-1", content: saved,
    entries: [{ key: "greeting", content: "Hi", valueStartByte: 11, valueLengthBytes: 2, authoring }],
  }],
  diagnostics: [],
  success: true,
});
const draft = (content, extra = {}) => ({
  success: true, content, entries: [{ key: "greeting", content, valueStartByte: 0, valueLengthBytes: content.length, authoring }],
  diagnostics: [], ...extra,
});

// A host whose replies the test releases, recording each request in the order the store sent it.
function scriptedHost() {
  const calls = [];
  const request = (kind) => (...args) => {
    const reply = deferred();
    calls.push({ kind, args, reply });
    return reply.promise;
  };
  return {
    calls,
    bridge: {
      transformDocument: request("transform"),
      applyAuthoringOperation: request("authoring"),
      applyDiagnosticFix: request("fix"),
    },
  };
}

function memoryStorage() {
  const values = new Map();
  return { values, get: (key) => values.get(key) ?? null, set: (key, value) => values.set(key, value), remove: (key) => values.delete(key) };
}

// Timers the test fires: the queue's debounce resolves at once, the raw validation debounce on `fire()`.
function createStore(options = {}) {
  let snapshot = options.snapshot ?? workspace();
  const storage = options.storage ?? memoryStorage();
  const timers = [];
  const store = new DraftStore({
    bridge: options.bridge,
    snapshot: () => snapshot,
    storage,
    wait: options.wait ?? (() => Promise.resolve()),
    setTimer: (callback) => { timers.push(callback); return timers.length; },
    clearTimer: (handle) => { timers[handle - 1] = undefined; },
  });
  return {
    store,
    storage,
    setSnapshot(value) { snapshot = value; },
    fire() { const pending = timers.splice(0).filter(Boolean); for (const callback of pending) callback(); return pending.length; },
  };
}

const outcomes = () => {
  const list = [];
  const record = (label) => (outcome) => list.push({ label, ...outcome });
  return { list, record };
};

// A composer edit through the mock bridge commits the draft, its parsed entries and the recovery copy.
{
  const snapshot = await mockExecute("LoadWorkspace", {});
  const document = snapshot.documents.find((candidate) => candidate.path === "de/common_save.mf2");
  const { store, storage } = createStore({ snapshot, bridge: { transformDocument: (...args) => mockExecute("TransformDocument", { path: args[0], content: args[1], key: args[2], value: args[3] }) } });
  const { list, record } = outcomes();
  assert.equal(store.hasDrafts, false);
  assert.equal(store.isDirty(document.path), false);
  await store.transform(document.path, document.content, "common_save", "Sichern", record("transform"));
  assert.deepEqual(list.map((item) => item.kind), ["applied"]);
  assert.equal(store.drafts[document.path], "Sichern\n");
  assert.equal(store.parsed[document.path].content, "Sichern\n");
  assert.equal(store.generation(document.path), 1);
  assert.equal(store.hasDrafts, true);
  assert.equal(store.isDirty(document.path), true, "A document with a draft is dirty.");
  const stored = JSON.parse([...storage.values.values()][0]);
  assert.deepEqual(stored.documents[document.path], { content: "Sichern\n", baseRevision: document.revision },
    "The draft is kept for recovery against the revision it started from.");
  // Today's dirty semantics: a draft equal to the saved text still counts.
  store.set(document.path, document.content);
  assert.equal(store.isDirty(document.path), true);
}

// The race of the former raw validation, which bypassed the queue: a raw edit after a queued composer edit now waits
// for it, and the composer's late answer cannot overwrite the newer raw draft.
{
  const host = scriptedHost();
  const { store, fire } = createStore({ bridge: host.bridge });
  const { list, record } = outcomes();
  const transformed = store.transform(path, saved, "greeting", "Hello", record("transform"));
  await settle();
  assert.equal(host.calls.length, 1, "The composer edit reached the host.");
  store.set(path, "greeting = Hey\n");
  store.validate(path, "greeting = Hey\n", record("validation"));
  assert.equal(store.validating, true);
  fire();
  await settle();
  assert.equal(host.calls.length, 1, "The raw validation must wait for the queued composer edit.");
  assert.equal(store.settled(path) !== undefined, true, "A save waits for the queued requests.");
  host.calls[0].reply.resolve(draft("greeting = Hello\n"));
  await transformed;
  await settle();
  assert.deepEqual(list, [], "The composer's answer for an older draft generation must be dropped.");
  assert.equal(host.calls.length, 2);
  assert.deepEqual(host.calls[1].args, [path, "greeting = Hey\n"]);
  host.calls[1].reply.resolve(draft("greeting = Hey\n"));
  await settle();
  assert.deepEqual(list.map((item) => `${item.label}:${item.kind}`), ["validation:applied"]);
  assert.equal(store.drafts[path], "greeting = Hey\n", "The raw draft survives the queued composer edit.");
  assert.equal(store.parsed[path].content, "greeting = Hey\n", "The parsed entries belong to the current draft.");
  assert.equal(store.validating, false);
  assert.equal(store.settled(path), undefined);
}

// A raw validation scheduled before a queued composer edit commits on top of the raw draft: the validation of the
// older text is dropped, so parsed entries and draft stay together.
{
  const host = scriptedHost();
  const delay = deferred();
  const { store, fire } = createStore({ bridge: host.bridge, wait: () => delay.promise });
  const { list, record } = outcomes();
  store.set(path, "greeting = Hey\n");
  store.validate(path, "greeting = Hey\n", record("validation"));
  void store.transform(path, saved, "greeting", "Hello", record("transform"));
  fire();
  delay.resolve();
  await settle();
  assert.equal(host.calls.length, 1);
  assert.deepEqual(host.calls[0].args, [path, "greeting = Hey\n", "greeting", "Hello"], "The composer edit starts from the current draft.");
  host.calls[0].reply.resolve(draft("greeting = Hello\n"));
  await settle();
  assert.equal(host.calls.length, 1, "The validation of a superseded draft is not sent.");
  assert.deepEqual(list.map((item) => `${item.label}:${item.kind}`), ["transform:applied"]);
  assert.equal(store.parsed[path].content, store.drafts[path]);
  assert.equal(store.validating, false, "A dropped validation ends the busy state.");
}

// Generation and workspace guards drop stale answers; a newer edit of the same message supersedes a queued one.
{
  const host = scriptedHost();
  const { store } = createStore({ bridge: host.bridge });
  const { list, record } = outcomes();
  void store.transform(path, saved, "greeting", "Hello", record("first"));
  void store.transform(path, saved, "greeting", "Hello!", record("second"));
  await settle();
  assert.equal(host.calls.length, 1, "The superseded edit never reaches the host.");
  assert.equal(host.calls[0].args[3], "Hello!");
  store.nextWorkspace();
  host.calls[0].reply.resolve(draft("greeting = Hello!\n"));
  await settle();
  assert.deepEqual(list, [], "An answer for another workspace is dropped.");
  assert.equal(store.drafts[path], undefined);

  void store.transform(path, saved, "greeting", "Hallo", record("third"));
  await settle();
  store.set(path, "greeting = typed\n");
  host.calls[1].reply.resolve(draft("greeting = Hallo\n"));
  await settle();
  assert.deepEqual(list, [], "An answer for an older draft generation is dropped.");
  assert.equal(store.drafts[path], "greeting = typed\n");
}

// Refusals and failures are reported; a failure does not block the document's queue.
{
  const host = scriptedHost();
  const { store } = createStore({ bridge: host.bridge });
  const { list, record } = outcomes();
  void store.transform(path, saved, "greeting", "{", record("refused"));
  void store.applyFix(path, saved, { title: "Fix", edits: [] }, undefined, record("fix"));
  await settle();
  host.calls[0].reply.resolve(draft(saved, { success: false, diagnostics: [{ id: "EDITOR-TRANSFORM", severity: "error", message: "No." }] }));
  await settle();
  host.calls[1].reply.reject(new Error("host gone"));
  await settle();
  assert.deepEqual(list.map((item) => `${item.label}:${item.kind}`), ["refused:rejected", "fix:failed"]);
  assert.equal(list[1].error.message, "host gone");
  assert.equal(store.hasDrafts, false, "A refused edit leaves no draft.");
  void store.transform(path, saved, "greeting", "Hello", record("after"));
  await settle();
  host.calls[2].reply.resolve(draft("greeting = Hello\n"));
  await settle();
  assert.equal(list.at(-1).kind, "applied");
}

// Only the latest raw validation is sent and reported; the debounce restarts with each keystroke.
{
  const host = scriptedHost();
  const { store, fire } = createStore({ bridge: host.bridge });
  const { list, record } = outcomes();
  store.set(path, "a");
  store.validate(path, "a", record("a"));
  store.set(path, "ab");
  store.validate(path, "ab", record("ab"));
  assert.equal(fire(), 1, "The earlier debounce was cancelled.");
  await settle();
  assert.equal(host.calls.length, 1);
  store.set(path, "abc");
  store.validate(path, "abc", record("abc"));
  host.calls[0].reply.resolve(draft("ab"));
  await settle();
  assert.deepEqual(list, [], "A validation superseded while in flight reports nothing.");
  assert.equal(store.validating, true, "The newer validation is still pending.");
  fire();
  await settle();
  host.calls[1].reply.reject(new Error("offline"));
  await settle();
  assert.deepEqual(list.map((item) => `${item.label}:${item.kind}`), ["abc:failed"]);
  assert.equal(store.validating, false);
}

// Authoring: a pattern edit is debounced and superseded, other operations are not; a message the composer cannot edit
// sends nothing.
{
  const host = scriptedHost();
  const { store } = createStore({ bridge: host.bridge });
  const { list, record } = outcomes();
  const first = store.applyOperation(path, saved, "greeting", "en", { kind: "set-pattern", variantId: "v", pattern: [] }, record("p1"));
  const second = store.applyOperation(path, saved, "greeting", "en", { kind: "set-pattern", variantId: "v", pattern: [] }, record("p2"));
  assert.equal(store.authoringPending, 2);
  await settle();
  assert.equal(host.calls.length, 1, "The superseded pattern edit is not sent.");
  assert.equal(host.calls[0].args[3], "r1", "The operation names the projection revision it was made for.");
  host.calls[0].reply.resolve(draft("greeting = Hi there\n"));
  await Promise.all([first, second]);
  assert.deepEqual(list.map((item) => `${item.label}:${item.kind}`), ["p2:applied"]);
  assert.equal(store.authoringPending, 0);

  const third = store.applyOperation(path, saved, "greeting", "en", { kind: "add-variant" }, record("v1"));
  const fourth = store.applyOperation(path, saved, "greeting", "en", { kind: "add-variant" }, record("v2"));
  await settle();
  host.calls[1].reply.resolve(draft("greeting = one\n"));
  await settle();
  host.calls[2].reply.resolve(draft("greeting = two\n"));
  await Promise.all([third, fourth]);
  assert.deepEqual(list.slice(1).map((item) => item.label), ["v1", "v2"], "Structural operations are never dropped as superseded.");
  assert.equal(host.calls[2].args[1], "greeting = one\n", "Each operation starts from the previous one's draft.");

  await store.applyOperation(path, saved, "missing", "en", { kind: "add-variant" }, record("missing"));
  assert.equal(host.calls.length, 3, "A message without a supported projection sends nothing.");
}

// A quick fix in the repair dialog edits the dialog's text, not the draft, and drops an answer for older text.
{
  const host = scriptedHost();
  const { store } = createStore({ bridge: host.bridge });
  const { list, record } = outcomes();
  let repairText = "broken";
  const fixed = store.applyFix(path, saved, { title: "Fix", edits: [] }, { text: () => repairText }, record("repair"));
  await settle();
  assert.equal(host.calls[0].args[1], "broken");
  host.calls[0].reply.resolve(draft("fixed"));
  await fixed;
  assert.deepEqual(list.map((item) => item.kind), ["applied"]);
  assert.equal(store.hasDrafts, false, "A repair fix leaves the drafts alone.");
  assert.equal(store.parsed[path], undefined);
  const stale = store.applyFix(path, saved, { title: "Fix", edits: [] }, { text: () => repairText }, record("stale"));
  await settle();
  repairText = "edited meanwhile";
  host.calls[1].reply.resolve(draft("fixed again"));
  await stale;
  assert.equal(list.length, 1, "A fix for repair text that changed meanwhile is dropped.");
}

// Recovery: drafts stored by an earlier session are offered after a reset, restored, saved away, or declined.
{
  const storage = memoryStorage();
  const first = createStore({ bridge: scriptedHost().bridge, storage });
  first.store.set(path, "greeting = Recovered\n");
  first.store.persist();
  const second = createStore({ bridge: { transformDocument: async (_path, content) => draft(content) }, storage });
  second.store.reset(workspace());
  assert.deepEqual(second.store.recovered, { [path]: { content: "greeting = Recovered\n", baseRevision: "rev-1" } });
  second.store.restoreRecovered();
  assert.equal(second.store.drafts[path], "greeting = Recovered\n");
  assert.deepEqual(second.store.recovered, {});
  assert.equal(await second.store.parse(path, "greeting = Recovered\n"), true);
  assert.equal(second.store.parsed[path].content, "greeting = Recovered\n");
  second.store.set(path, "greeting = Edited\n");
  const generation = second.store.generation(path);
  assert.equal(second.store.hasNewer(path, "greeting = Edited\n", generation), false);
  second.store.set(path, "greeting = Newer\n");
  assert.equal(second.store.hasNewer(path, "greeting = Edited\n", generation), true, "A draft typed during a save survives it.");
  second.store.drop(path);
  assert.equal(second.store.hasDrafts, false);
  assert.equal(storage.values.size, 0, "Without drafts the stored copy is removed.");
  second.store.set(path, "x");
  second.store.persist();
  second.store.discard();
  assert.equal(second.store.hasDrafts, false);
  assert.equal(storage.values.size, 0, "Discarding drafts removes the stored copy.");
  second.store.set(path, "y");
  second.store.persist();
  second.store.reset(workspace());
  second.store.discardRecovered();
  assert.deepEqual(second.store.recovered, {});
  assert.equal(storage.values.size, 0);
  storage.set([...storage.values.keys()][0] ?? "runic-translations:drafts:1:/work\nproduct", "{not json");
  second.store.reset(workspace());
  assert.deepEqual(second.store.recovered, {}, "An unreadable stored copy offers nothing.");
}

console.log("PASS: the draft store serializes composer edits, authoring, raw validation and quick fixes per document and drops stale answers.");
