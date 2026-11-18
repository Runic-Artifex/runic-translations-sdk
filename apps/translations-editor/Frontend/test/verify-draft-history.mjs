// Undo and redo of unsaved edits (#225 P4): per document, every committed draft change is one step, typing in one field
// is one step per pause, a save clears the stacks, and an undo returns the draft, its recovery copy and the starters of
// missing translations to the state before the step, so a document can be clean again.
import assert from "node:assert/strict";
import { settle } from "./svelte-modules.mjs";
import { mockExecute } from "../src/lib/mock-bridge.ts";

const { DraftHistory, caretAfterChange } = await import("../src/lib/state/draft-history.svelte.ts");
const { DraftStore } = await import("../src/lib/state/draft-store.svelte.ts");
const { MessageDraftStore } = await import("../src/lib/state/message-draft-store.svelte.ts");

const state = (draft, starters = {}) => ({ draft, placements: {}, starters });
const clock = () => {
  let now = 0;
  return { now: () => now, advance: (milliseconds) => { now += milliseconds; } };
};

// Push, coalesce, undo, redo.
{
  const time = clock();
  const history = new DraftHistory({ now: time.now, pause: 1000 });
  assert.equal(history.canUndo, false);
  assert.equal(history.record({ path: "en", kind: "edit", field: "f", before: state(undefined), after: state(undefined) }), undefined,
    "A change that changed nothing is no step.");
  history.record({ path: "en", kind: "edit", key: "greeting", message: "home.greeting", field: "f", before: state(undefined), after: state("a") });
  time.advance(400);
  history.record({ path: "en", kind: "edit", key: "greeting", message: "home.greeting", field: "f", before: state("a"), after: state("ab") });
  assert.deepEqual(history.depth("en"), { undo: 1, redo: 0 }, "Typing in one field within the pause is one step.");
  assert.equal(history.peekUndo("en").before.draft, undefined, "The coalesced step starts where the typing started.");
  assert.equal(history.peekUndo("en").after.draft, "ab");
  time.advance(1500);
  history.record({ path: "en", kind: "edit", field: "f", before: state("ab"), after: state("abc") });
  assert.deepEqual(history.depth("en"), { undo: 2, redo: 0 }, "A pause starts a new step.");
  time.advance(10);
  history.record({ path: "en", kind: "edit", field: "g", before: state("abc"), after: state("abcd") });
  assert.deepEqual(history.depth("en"), { undo: 3, redo: 0 }, "Another field starts a new step.");
  time.advance(10);
  history.record({ path: "en", kind: "structure", before: state("abcd"), after: state("abcde") });
  time.advance(10);
  history.record({ path: "en", kind: "structure", before: state("abcde"), after: state("abcdef") });
  assert.deepEqual(history.depth("en"), { undo: 5, redo: 0 }, "Changes outside a field are never merged.");
  assert.equal(history.canUndo, true);

  const undone = history.takeUndo("en");
  assert.equal(undone.before.draft, "abcde");
  assert.deepEqual(history.depth("en"), { undo: 4, redo: 1 });
  assert.equal(history.canRedo, true);
  assert.equal(history.takeRedo("en").after.draft, "abcdef");
  history.takeUndo("en");
  history.takeUndo("en");
  // Typing after an undo is a new step and clears the redo stack; it never extends the step before the undo.
  history.record({ path: "en", kind: "edit", field: "g", before: state("abcd"), after: state("abcdX") });
  assert.deepEqual(history.depth("en"), { undo: 4, redo: 0 });
  assert.equal(history.canRedo, false);

  // Typing back to where the step started removes the step.
  time.advance(5000);
  history.record({ path: "en", kind: "edit", field: "h", before: state("abcdX"), after: state("abcdXY") });
  time.advance(100);
  history.record({ path: "en", kind: "edit", field: "h", before: state("abcdXY"), after: state("abcdX") });
  assert.deepEqual(history.depth("en"), { undo: 4, redo: 0 }, "A step typed back to its start is no step.");

  // Starter state counts as a change of its own.
  history.record({ path: "en", kind: "structure", before: state("s", { a: { text: "x" } }), after: state("s", { a: { text: "y" } }) });
  assert.deepEqual(history.depth("en"), { undo: 5, redo: 0 });
}

// Targets: the selected document while it has a step, else the document changed (or undone) last.
{
  const time = clock();
  const history = new DraftHistory({ now: time.now });
  history.record({ path: "en", kind: "edit", before: state(undefined), after: state("1") });
  history.record({ path: "de", kind: "edit", before: state(undefined), after: state("2") });
  assert.equal(history.undoTarget("en"), "en");
  assert.equal(history.undoTarget("fr"), "de", "Without a step in the selected document, the latest change is undone.");
  assert.equal(history.undoTarget(undefined), "de");
  history.takeUndo("en");
  assert.equal(history.redoTarget("fr"), "en", "Redo takes the document undone last.");
  assert.equal(history.undoTarget("en"), "de");
  history.clear("de");
  assert.equal(history.undoTarget("en"), undefined);
  assert.deepEqual(history.paths(), ["en"]);
  history.clearAll();
  assert.equal(history.canRedo, false);
}

// The cap: at most `maxSteps` steps and a byte budget per document; the newest step is always kept.
{
  const history = new DraftHistory({ maxSteps: 3, maxBytes: 1_000_000 });
  for (let index = 0; index < 5; index += 1) history.record({ path: "en", kind: "structure", before: state(`${index}`), after: state(`${index + 1}`) });
  assert.deepEqual(history.depth("en"), { undo: 3, redo: 0 });
  assert.equal(history.takeUndo("en").before.draft, "4");
  assert.equal(history.takeUndo("en").before.draft, "3");
  assert.equal(history.takeUndo("en").before.draft, "2", "The oldest steps were dropped.");
  assert.equal(history.takeUndo("en"), undefined);

  const budget = new DraftHistory({ maxSteps: 100, maxBytes: 4_100 });
  const text = (index) => String(index).repeat(500);
  for (let index = 0; index < 4; index += 1) budget.record({ path: "en", kind: "structure", before: state(text(index)), after: state(text(index + 1)) });
  assert.deepEqual(budget.depth("en"), { undo: 2, redo: 0 }, "Steps past the byte budget are dropped, oldest first.");
  const huge = new DraftHistory({ maxBytes: 10 });
  huge.record({ path: "en", kind: "structure", before: state(undefined), after: state("x".repeat(100)) });
  assert.deepEqual(huge.depth("en"), { undo: 1, redo: 0 }, "A step larger than the budget is still the one undo.");
}

// The caret after an undo or redo: at the end of the changed run.
{
  assert.equal(caretAfterChange("Cancel {$oops", "Cancel"), 6, "An undone insertion leaves the caret where it began.");
  assert.equal(caretAfterChange("Cancel", "Cancel {$oops"), 13, "A redone insertion puts the caret after it.");
  assert.equal(caretAfterChange("one {{Eine}} * {{x}}", "one {{}} * {{x}}"), 6);
  assert.equal(caretAfterChange("same", "same"), 4);
}

// --- The draft store: steps of composer edits, authoring operations, raw edits, quick fixes and recovery. ---
const path = "en.rmf2";
const saved = "home_greeting = Hi\nhome_farewell = Bye\n";
const entriesOf = (content) => content.split("\n").filter((line) => line.includes(" = ")).map((line) => {
  const [key, value] = line.split(" = ");
  return { key, path: key.split("_"), content: value, valueStartByte: 0, valueLengthBytes: value.length, authoring: { supported: true, revision: value, selectors: [], inputs: [] } };
});
const workspace = (content = saved) => ({
  root: "/work",
  catalog: { id: "product", schemaVersion: 1, defaultLocale: "en", locales: [{ tag: "en" }, { tag: "de" }], layers: [{ name: "base", priority: 0 }] },
  catalogs: [],
  documents: [
    { path, locale: "en", layer: "base", isManifest: false, isMalformed: false, revision: `rev-${content.length}`, content, entries: entriesOf(content) },
    { path: "de.rmf2", locale: "de", layer: "base", isManifest: false, isMalformed: false, revision: "rev-de", content: "home_greeting = Hallo\n", entries: entriesOf("home_greeting = Hallo\n") },
  ],
  diagnostics: [],
  success: true,
});

// A host that replaces `key = value` (or applies a pattern operation); MF2 with `{$oops` does not compile.
function createHost() {
  const calls = [];
  const answer = (document, content) => {
    const broken = content.includes("{$oops");
    return {
      success: !broken, content, entries: entriesOf(content),
      diagnostics: broken ? [{ id: "RTR0001", severity: "error", message: "Unclosed placeholder.", path: document, line: 1, column: 1, endLine: 1, endColumn: 2 }] : [],
    };
  };
  const replace = (content, key, value) => content.replace(new RegExp(`^${key} = .*$`, "m"), `${key} = ${value}`);
  return {
    calls,
    bridge: {
      transformDocument: async (document, content, key, value) => {
        calls.push({ kind: "transform", document, content, key, value });
        return answer(document, key === undefined ? content : replace(content, key, value));
      },
      applyAuthoringOperation: async (document, content, key, revision, operation) => {
        calls.push({ kind: "authoring", document, key, operation });
        if (operation.kind === "set-pattern") return answer(document, replace(content, key, operation.pattern));
        return answer(document, replace(content, key, `${entriesOf(content).find((entry) => entry.key === key).content} [${operation.kind}]`));
      },
      applyDiagnosticFix: async (document, content, fix) => {
        calls.push({ kind: "fix", document, fix });
        return answer(document, content.replace(" {$oops", ""));
      },
    },
  };
}

function memoryStorage() {
  const values = new Map();
  return { values, get: (key) => values.get(key) ?? null, set: (key, value) => values.set(key, value), remove: (key) => values.delete(key) };
}

function createStore(options = {}) {
  const host = options.host ?? createHost();
  const time = clock();
  let snapshot = options.snapshot ?? workspace();
  const storage = options.storage ?? memoryStorage();
  const store = new DraftStore({
    bridge: host.bridge, snapshot: () => snapshot, storage, wait: () => Promise.resolve(),
    setTimer: (callback) => { callback(); return 1; }, clearTimer: () => undefined,
    history: { now: time.now, pause: 1000 },
  });
  return { host, store, storage, time, setSnapshot(value) { snapshot = value; } };
}

const noop = () => undefined;

// P4: a broken edit of the message source is one step; undo restores the valid text, the document is clean again and
// the recovery copy is gone; redo brings the broken text back.
{
  const { store, storage, host } = createStore();
  await store.transform(path, saved, "home_greeting", "Hi {$oops", noop);
  assert.equal(store.isDirty(path), true);
  assert.equal(store.history.depth(path).undo, 1);
  const step = store.history.peekUndo(path);
  assert.equal(step.kind, "edit");
  assert.equal(step.key, "home_greeting");
  assert.equal(step.message, "home.greeting", "A step names its message by the dotted path.");
  assert.equal(storage.values.size, 1, "The broken draft has a recovery copy.");

  const generation = store.generation(path);
  const undone = store.undo(path);
  assert.equal(undone.id, step.id);
  assert.equal(store.isDirty(path), false, "Undo back to the saved text leaves the document clean (content-based).");
  assert.equal(store.drafts[path], undefined);
  assert.equal(storage.values.size, 0, "The recovery copy follows the undo.");
  assert.ok(store.generation(path) > generation, "Answers for the undone text are dropped.");
  assert.deepEqual(store.history.depth(path), { undo: 0, redo: 1 });

  const outcomes = [];
  await store.refresh(path, (outcome) => outcomes.push(outcome));
  assert.equal(outcomes.at(-1).kind, "applied");
  assert.equal(outcomes.at(-1).result.success, true, "The restored text validates without the error.");
  assert.equal(host.calls.at(-1).content, saved);

  store.redo(path);
  assert.equal(store.drafts[path], saved.replace("Hi", "Hi {$oops"));
  assert.equal(store.parsed[path].content, store.drafts[path], "Redo restores the compiler's reading of the text.");
  assert.equal(storage.values.size, 1);
  assert.deepEqual(store.history.depth(path), { undo: 1, redo: 0 });
}

// Typing in one composer field is one step per pause; an operation of the composer's structure is its own step;
// pattern edits of one variant coalesce like typing.
{
  const { store, time } = createStore();
  await store.transform(path, saved, "home_greeting", "Hel", noop);
  time.advance(300);
  await store.transform(path, saved, "home_greeting", "Hello", noop);
  assert.equal(store.history.depth(path).undo, 1, "Typing in one field is one step.");
  time.advance(2000);
  await store.transform(path, saved, "home_greeting", "Hello!", noop);
  assert.equal(store.history.depth(path).undo, 2, "Typing after a pause is the next step.");
  await store.transform(path, saved, "home_farewell", "Ciao", noop);
  assert.equal(store.history.depth(path).undo, 3, "Typing in another message is the next step.");
  await store.applyOperation(path, saved, "home_greeting", "en", { kind: "set-pattern", variantId: "0", pattern: "Hey" }, noop);
  time.advance(100);
  await store.applyOperation(path, saved, "home_greeting", "en", { kind: "set-pattern", variantId: "0", pattern: "Hey you" }, noop);
  assert.equal(store.history.depth(path).undo, 4, "Pattern edits of one variant are one step.");
  await store.applyOperation(path, saved, "home_greeting", "en", { kind: "add-variant", keys: ["one"], pattern: "" }, noop);
  assert.equal(store.history.peekUndo(path).kind, "structure");
  assert.equal(store.history.depth(path).undo, 5);

  // Each undo goes one step back, across messages; all of them back to the saved text.
  const texts = [];
  while (store.history.peekUndo(path) !== undefined) {
    store.undo(path);
    texts.push(store.content(path, saved));
  }
  assert.deepEqual(texts, [
    "home_greeting = Hey you\nhome_farewell = Ciao\n",
    "home_greeting = Hello!\nhome_farewell = Ciao\n",
    "home_greeting = Hello!\nhome_farewell = Bye\n",
    "home_greeting = Hello\nhome_farewell = Bye\n",
    saved,
  ]);
  assert.equal(store.isDirty(path), false);
  // Redo all, then a new edit clears the redo stack.
  for (let index = 0; index < 2; index += 1) store.redo(path);
  assert.equal(store.content(path, saved), "home_greeting = Hello!\nhome_farewell = Bye\n");
  await store.transform(path, saved, "home_farewell", "Adieu", noop);
  assert.deepEqual(store.history.depth(path), { undo: 3, redo: 0 });
}

// Raw-source typing (the file view) records its own steps, coalesced like the composer's; a quick fix is a step named
// after its message.
{
  const { store, time } = createStore();
  const type = (text) => {
    const before = store.capture(path);
    store.set(path, text);
    store.persist();
    store.recordStep(path, { kind: "file-edit", field: `${path}\u0000file` }, before);
  };
  type(`${saved}x`);
  time.advance(100);
  type(`${saved}xy`);
  assert.equal(store.history.depth(path).undo, 1);
  assert.equal(store.history.peekUndo(path).kind, "file-edit");
  assert.equal(store.history.peekUndo(path).message, undefined, "A step of the whole file names no message.");
  time.advance(100);
  type(saved);
  assert.equal(store.history.depth(path).undo, 0, "Typing the file back to the saved text within the pause leaves no step.");
  assert.equal(store.isDirty(path), false);

  await store.transform(path, saved, "home_greeting", "Hi {$oops", noop);
  await store.applyFix(path, saved, { title: "Close", edits: [] }, undefined, noop, "home_greeting");
  assert.equal(store.history.peekUndo(path).kind, "quick-fix");
  assert.equal(store.history.peekUndo(path).message, "home.greeting");
  assert.equal(store.isDirty(path), false, "The fix restored the saved text.");
  store.undo(path);
  assert.equal(store.drafts[path], saved.replace("Hi", "Hi {$oops"), "Undo of the fix brings the broken text back.");
  // A fix of the repair dialog's text is no step of the draft.
  const depth = store.history.depth(path).undo;
  await store.applyFix(path, saved, { title: "Close", edits: [] }, { text: () => "home_greeting = x {$oops\n" }, noop);
  assert.equal(store.history.depth(path).undo, depth);
}

// A save clears the saved documents' stacks; a change of the saved text (a saved change undone) clears them too; an
// unchanged document keeps its stacks; opening, discarding and reloading a workspace clear all.
{
  const { store, setSnapshot } = createStore();
  await store.transform(path, saved, "home_greeting", "Hello", noop);
  await store.transform("de.rmf2", "home_greeting = Hallo\n", "home_greeting", "Servus", noop);
  assert.deepEqual(store.history.paths().sort(), ["de.rmf2", "en.rmf2"]);
  const savedText = store.drafts[path];
  setSnapshot(workspace(savedText));
  store.prune();
  store.drop(path);
  assert.deepEqual(store.history.depth(path), { undo: 0, redo: 0 }, "A save clears the saved document's stacks.");
  assert.equal(store.history.depth("de.rmf2").undo, 1, "Another document keeps its stacks.");
  store.undo("de.rmf2");
  setSnapshot(workspace(savedText));
  store.prune();
  assert.deepEqual(store.history.depth("de.rmf2"), { undo: 0, redo: 1 }, "An unchanged saved text keeps the redo step.");

  await store.transform(path, savedText, "home_farewell", "Ciao", noop);
  setSnapshot(workspace(saved));
  store.prune();
  assert.deepEqual(store.history.depth(path), { undo: 0, redo: 0 }, "A changed saved text (a saved change undone) clears the stacks.");

  store.discard();
  assert.deepEqual(store.history.paths(), []);
  await store.transform(path, saved, "home_greeting", "Again", noop);
  store.reset(workspace());
  assert.deepEqual(store.history.paths(), [], "Another workspace starts without history.");
}

// Recovery: restoring the recovered drafts is one step per document, which Undo takes back (and the recovery copy with
// it, because the document is clean again).
{
  const storage = memoryStorage();
  const first = createStore({ storage });
  await first.store.transform(path, saved, "home_greeting", "Recovered", noop);
  assert.equal(storage.values.size, 1);
  const second = createStore({ storage });
  second.store.reset(workspace());
  assert.deepEqual(Object.keys(second.store.recovered), [path]);
  second.store.restoreRecovered();
  assert.equal(second.store.drafts[path], saved.replace("Hi", "Recovered"));
  assert.equal(second.store.history.peekUndo(path).kind, "recovery");
  second.store.undo(path);
  assert.equal(second.store.isDirty(path), false);
  assert.equal(storage.values.size, 0, "Undoing the recovery leaves no recovery copy.");
  second.store.redo(path);
  assert.equal(second.store.drafts[path], saved.replace("Hi", "Recovered"));
  assert.equal(storage.values.size, 1);
}

// An answer that arrives after an undo belongs to the undone text and is dropped.
{
  const host = createHost();
  let release;
  const slow = { ...host.bridge, transformDocument: (...args) => new Promise((resolve) => { release = () => resolve(host.bridge.transformDocument(...args)); }) };
  const { store } = createStore({ host: { calls: host.calls, bridge: slow } });
  const first = store.transform(path, saved, "home_greeting", "One", noop);
  await settle();
  release();
  await first;
  const second = store.transform(path, saved, "home_greeting", "Two", noop);
  await settle();
  store.undo(path);
  release();
  await second;
  assert.equal(store.isDirty(path), false, "The answer for the undone text did not come back.");
  assert.deepEqual(store.history.depth(path), { undo: 0, redo: 1 });
}

// --- Starters of missing translations take part: undoing the insert of a starter returns to the empty starter. ---
await mockExecute("LoadWorkspace", {});
{
  const frPath = "fr.rmf2";
  const frSaved = "files {\n  open = Ouvrir\n}\n";
  const snapshot = {
    ...workspace(),
    catalog: { id: "product", schemaVersion: 1, defaultLocale: "en", locales: [{ tag: "en" }, { tag: "fr" }], layers: [{ name: "base", priority: 0 }] },
    documents: [{ path: frPath, locale: "fr", layer: "base", isManifest: false, isMalformed: false, revision: "rev-fr", content: frSaved, entries: [] }],
  };
  const transforms = [];
  const bridge = {
    transformDocument: async (document, content, key, value) => {
      transforms.push({ key, value });
      let next = content;
      if (key !== undefined) {
        const name = key.split("_").at(-1);
        const block = `  ${name} =\n${value.split("\n").map((line) => `    ${line}`).join("\n")}\n`;
        const existing = new RegExp(`  ${name} =\\n(?:    .*\\n)*`);
        next = existing.test(content) ? content.replace(existing, block) : content.replace(/\}\n$/, `${block}}\n`);
      }
      return { success: true, content: next, entries: [], diagnostics: [] };
    },
    applyAuthoringOperation: async () => { throw new Error("unused"); },
    applyDiagnosticFix: async () => { throw new Error("unused"); },
    createMessageDraft: (key, locale) => mockExecute("CreateMessageDraft", { key, locale }),
    applyMessageDraftOperation: (key, locale, text, expectedRevision, operation) =>
      mockExecute("ApplyMessageDraftOperation", { key, locale, text, expectedRevision, operation }),
  };
  const time = clock();
  const drafts = new DraftStore({ bridge, snapshot: () => snapshot, storage: memoryStorage(), wait: () => Promise.resolve(), history: { now: time.now, pause: 1000 } });
  const starters = new MessageDraftStore({ bridge, drafts, wait: () => Promise.resolve() });
  const target = { path: frPath, saved: frSaved, key: "files_selected", locale: "fr" };
  await starters.open(target);
  const initial = starters.get(target).text;
  assert.deepEqual(drafts.history.paths(), [], "Opening an empty starter is no step.");

  // Typing into a variant: the starter's text is placed into the document; typing on is the same step.
  await starters.apply(target, { kind: "set-pattern", variantId: "0", pattern: "Un" }, noop);
  await drafts.settledAll();
  time.advance(200);
  await starters.apply(target, { kind: "set-pattern", variantId: "0", pattern: "Un fichier" }, noop);
  await drafts.settledAll();
  assert.equal(drafts.isDirty(frPath), true);
  assert.equal(drafts.history.depth(frPath).undo, 1, "Typing into a starter variant is one step.");
  const placedDraft = drafts.drafts[frPath];
  const typed = starters.get(target).text;

  drafts.undo(frPath);
  assert.equal(drafts.isDirty(frPath), false, "Undoing the insert leaves the document clean.");
  assert.equal(starters.get(target).text, initial, "Undoing the insert returns to the empty starter.");
  assert.equal(starters.get(target).placed, false);
  assert.equal(starters.get(target).projection.empty, true);

  drafts.redo(frPath);
  assert.equal(drafts.drafts[frPath], placedDraft, "Redo places the message again.");
  assert.equal(starters.get(target).text, typed);
  assert.equal(starters.get(target).placed, true);

  // Cleared back to empty: the message is taken out again; that is a step, and undoing it puts the text back.
  time.advance(5000);
  await starters.apply(target, { kind: "set-pattern", variantId: "0", pattern: "" }, noop);
  await drafts.settledAll();
  assert.equal(drafts.isDirty(frPath), false, "A starter cleared to empty leaves the document clean.");
  assert.equal(drafts.history.depth(frPath).undo, 2);
  drafts.undo(frPath);
  assert.equal(drafts.drafts[frPath], placedDraft);
  assert.equal(starters.get(target).text, typed);

  // After undoing to the empty starter, new typing places and clears the message as before (the placement record
  // went back with the undo).
  drafts.undo(frPath);
  assert.equal(drafts.isDirty(frPath), false);
  time.advance(5000);
  await starters.apply(target, { kind: "set-pattern", variantId: "0", pattern: "Une" }, noop);
  await drafts.settledAll();
  assert.equal(drafts.isDirty(frPath), true);
  time.advance(5000);
  await starters.apply(target, { kind: "set-pattern", variantId: "0", pattern: "" }, noop);
  await drafts.settledAll();
  assert.equal(drafts.isDirty(frPath), false, "The placement taken back by the undo did not leave a stale record.");

  // The starter's MF2 source typed by hand hands the message over to the document; undo brings the empty starter back.
  time.advance(5000);
  const handed = initial.replace("one {{}}", "one {{Un}}");
  starters.edit(target, handed);
  await drafts.settledAll();
  assert.equal(starters.get(target), undefined, "The typed source handed the message over to the document.");
  drafts.undo(frPath);
  assert.equal(drafts.isDirty(frPath), false);
  assert.equal(starters.get(target).text, initial);
  drafts.redo(frPath);
  assert.equal(starters.get(target), undefined, "Redo hands the message over again.");
  assert.equal(drafts.isDirty(frPath), true);
  drafts.undo(frPath);

  // Copy source is one step.
  time.advance(5000);
  await starters.copySource(target, [{ keys: ["one"], pattern: "One file" }, { keys: ["*"], pattern: "{$count} files" }], noop);
  await drafts.settledAll();
  assert.equal(drafts.history.peekUndo(frPath).kind, "copy-source");
  assert.equal(drafts.history.peekUndo(frPath).message, "files_selected", "An unknown path falls back to the key.");
  drafts.undo(frPath);
  assert.equal(starters.get(target).text, initial);
  assert.equal(drafts.isDirty(frPath), false);

  // A save of the placed starter clears the document's stacks.
  drafts.redo(frPath);
  drafts.drop(frPath);
  assert.deepEqual(drafts.history.depth(frPath), { undo: 0, redo: 0 });
}

console.log("PASS: unsaved-edit undo history covers push, coalescing, undo, redo, the cap, save, recovery, content-based dirty state and starters.");
