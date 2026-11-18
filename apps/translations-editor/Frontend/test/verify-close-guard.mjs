// The question before the window closes (#225 P19): the host asks the page, which answers at once. With nothing
// unsaved the window closes; with unsaved work it stays open and the page asks Save, Discard or Cancel, whose Save and
// Discard close the window through the same question.
import assert from "node:assert/strict";
import { deferred, settle } from "./svelte-modules.mjs";

const { CloseGuard } = await import("../src/lib/state/close-guard.svelte.ts");

// Fakes read the state they close over, so a change after the first read is seen. `closeWindow` asks the guard again,
// as the host does after the page's Close window command, and records whether the window closed.
function createGuard(options = {}) {
  const log = [];
  const state = {
    documents: options.documents ?? ["de.rmf2"],
    review: options.review ?? false,
    repair: options.repair,
    closed: false,
  };
  const unsaved = {
    get hasUnsavedWork() { return state.documents.length > 0 || state.review || state.repair !== undefined; },
    discard() {
      log.push("discard");
      state.documents = [];
      state.review = false;
      state.repair = undefined;
    },
  };
  const save = { get unsavedSummary() { return { documents: state.documents, review: state.review }; } };
  const repair = {
    get hasUnsaved() { return state.repair !== undefined; },
    get document() { return state.repair === undefined ? undefined : { path: state.repair }; },
  };
  let guard;
  guard = new CloseGuard({
    unsaved,
    save,
    repair,
    saveAll: options.saveAll ?? (async () => {
      log.push("saveAll");
      const saved = state.documents;
      state.documents = [];
      state.review = false;
      return { kind: "saved", saved, newer: false, message: "Saved." };
    }),
    flush: options.flush ?? (async () => { log.push("flush"); }),
    closeWindow: async () => {
      log.push("closeWindow");
      state.closed = await guard.answer();
      log.push(state.closed ? "closed" : "kept");
    },
    flushTimeoutMs: options.flushTimeoutMs,
  });
  return { guard, log, state };
}

// Without unsaved work the window closes at once, after the native state was written; nothing is asked.
{
  const { guard, log } = createGuard({ documents: [] });
  assert.equal(await guard.answer(), true);
  assert.equal(guard.open, false);
  assert.deepEqual(log, ["flush"]);
}

// With unsaved work the window stays open and the question names it: the documents with unsaved text, the review
// changes and a repair, which the one save does not include.
{
  const { guard, log } = createGuard({ documents: ["de.rmf2", "fr.rmf2"], review: true, repair: "broken.rmf2" });
  assert.equal(await guard.answer(), false);
  assert.equal(guard.open, true);
  assert.deepEqual(guard.summary, { documents: ["de.rmf2", "fr.rmf2"], review: true, repair: "broken.rmf2" });
  assert.deepEqual(log, [], "The answer waited for something, or wrote state, while work is unsaved.");
  // While the question is shown, a second close (the user clicks the close button again) keeps the window open.
  assert.equal(await guard.answer(), false);
  assert.equal(guard.open, true);
}

// Cancel keeps the window open with the work as it is.
{
  const { guard, log, state } = createGuard();
  await guard.answer();
  guard.cancel();
  assert.equal(guard.open, false);
  assert.equal(state.closed, false);
  assert.deepEqual(state.documents, ["de.rmf2"]);
  assert.deepEqual(log, []);
}

// Save runs the one save, writes the native state and closes the window: the host asks again and finds nothing unsaved.
{
  const { guard, log, state } = createGuard({ review: true });
  await guard.answer();
  await guard.save();
  assert.deepEqual(log, ["saveAll", "flush", "closeWindow", "flush", "closed"]);
  assert.equal(state.closed, true);
  assert.equal(guard.open, false);
  assert.equal(guard.busy, false);
}

// Save that an invalid document blocks writes nothing and keeps the window open, saying which document.
{
  const { guard, log, state } = createGuard({
    saveAll: async () => { log.push("saveAll"); return { kind: "invalid", path: "de.rmf2", result: { success: false, diagnostics: [] } }; },
  });
  await guard.answer();
  await guard.save();
  assert.deepEqual(log, ["saveAll"]);
  assert.equal(state.closed, false);
  assert.equal(guard.open, true);
  assert.deepEqual(guard.message, { code: "ui_close_save_invalid", args: [{ name: "path", value: "de.rmf2" }] });
}

// A save the host refuses, a review save that fails, and a failed save keep the window open with their reason.
for (const [outcome, expected] of [
  [{ kind: "refused", saved: [], path: "de.rmf2", message: { code: "ui_backend_document_conflict", args: [] } }, "ui_backend_document_conflict"],
  [{ kind: "review-failed", saved: ["de.rmf2"], newer: false, message: { code: "ui_feedback_review_save_failed", args: [] } }, "ui_feedback_review_save_failed"],
  [{ kind: "failed", saved: [], error: new Error("disk full") }, "ui_backend_external_error"],
]) {
  const { guard, state } = createGuard({ saveAll: async () => outcome });
  await guard.answer();
  await guard.save();
  assert.equal(state.closed, false, `A ${outcome.kind} save closed the window.`);
  assert.equal(guard.open, true);
  assert.equal(guard.message.code, expected);
}

// A save that throws keeps the window open too.
{
  const { guard, state } = createGuard({ saveAll: async () => { throw new Error("lost host"); } });
  await guard.answer();
  await guard.save();
  assert.equal(state.closed, false);
  assert.equal(guard.message.code, "ui_backend_external_error");
  assert.equal(guard.busy, false);
}

// Another save or an undo running: nothing is saved and the window stays open.
{
  const { guard, state } = createGuard({ saveAll: async () => undefined });
  await guard.answer();
  await guard.save();
  assert.equal(state.closed, false);
  assert.equal(guard.message.code, "ui_close_save_busy");
}

// Work the save leaves unsaved (a newer draft, a repair) keeps the window open and says what is left.
{
  const { guard, state } = createGuard({
    saveAll: async () => ({ kind: "saved", saved: ["de.rmf2"], newer: true, message: "Saved." }),
  });
  await guard.answer();
  await guard.save();
  assert.equal(state.closed, false);
  assert.equal(guard.message.code, "ui_close_still_unsaved");
}
{
  const { guard, state } = createGuard({ repair: "broken.rmf2" });
  await guard.answer();
  await guard.save();
  assert.equal(state.closed, false);
  assert.deepEqual(guard.message, { code: "ui_close_repair_unsaved", args: [{ name: "path", value: "broken.rmf2" }] });
}

// Discard drops the work and its recovery copy (UnsavedWork.discard), writes the native state and closes the window.
{
  const { guard, log, state } = createGuard({ review: true, repair: "broken.rmf2" });
  await guard.answer();
  await guard.discard();
  assert.deepEqual(log, ["discard", "flush", "closeWindow", "flush", "closed"]);
  assert.equal(state.closed, true);
}

// While Save runs, the buttons do nothing and a close keeps the window open; the save's end decides.
{
  const pending = deferred();
  const { guard, log, state } = createGuard({
    saveAll: async () => {
      log.push("saveAll");
      await pending.promise;
      state.documents = [];
      return { kind: "saved", saved: ["de.rmf2"], newer: false, message: "Saved." };
    },
  });
  await guard.answer();
  const saving = guard.save();
  await settle();
  assert.equal(guard.busy, true);
  assert.equal(await guard.answer(), false);
  guard.cancel();
  await guard.discard();
  assert.equal(guard.open, true);
  assert.deepEqual(log, ["saveAll"]);
  pending.resolve();
  await saving;
  assert.equal(state.closed, true);
}

// New work typed between Save and the host's second question shows the question again instead of closing.
{
  const { guard, state } = createGuard();
  const original = guard.answer.bind(guard);
  let typed = false;
  guard.answer = async () => {
    if (!typed) { typed = true; state.documents = ["fr.rmf2"]; }
    return original();
  };
  await original();
  await guard.save();
  assert.equal(state.closed, false);
  assert.equal(guard.open, true);
  assert.deepEqual(guard.summary.documents, ["fr.rmf2"]);
}

// A native state write that hangs or fails does not keep a clean window open.
{
  const { guard } = createGuard({ documents: [], flush: () => new Promise(() => {}), flushTimeoutMs: 10 });
  assert.equal(await guard.answer(), true);
}
{
  const { guard } = createGuard({ documents: [], flush: async () => { throw new Error("not written"); } });
  assert.equal(await guard.answer(), true);
}

// The palette's Close window asks through the host like the close button; it does nothing while the question is shown.
{
  const { guard, log, state } = createGuard();
  await guard.requestClose();
  assert.deepEqual(log, ["closeWindow", "kept"]);
  assert.equal(guard.open, true);
  await guard.requestClose();
  assert.deepEqual(log, ["closeWindow", "kept"]);
  assert.equal(state.closed, false);
}

console.log("PASS: close guard answers at once; Save, Discard and Cancel decide; a blocked or failed save keeps the window open.");
