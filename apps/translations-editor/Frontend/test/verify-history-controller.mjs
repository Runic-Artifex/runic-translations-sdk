// Undo and redo: the selected document's latest unsaved step first (shown, then validated again), else the host's
// saved-change history; one after another, each waiting while its kind of history is blocked.
import assert from "node:assert/strict";
import { deferred, settle } from "./svelte-modules.mjs";

const { RuneValue } = await import("./rune-value.svelte.ts");
const { EditorFeedback } = await import("../src/lib/state/feedback.svelte.ts");
const { HistoryController } = await import("../src/lib/state/history-controller.svelte.ts");

const ui = { text: (key, args) => args === undefined ? key : `${key} ${JSON.stringify(args)}` };

function createHistory(options = {}) {
  const calls = [];
  const steps = new RuneValue(options.steps ?? {});
  const snapshot = new RuneValue({ history: { canUndo: true, canRedo: false, undoLabel: "Rename" } });
  const blocked = new RuneValue({ history: false, drafts: false });
  const replies = options.replies ?? [];
  const bridge = {
    undo: async () => { calls.push("bridge.undo"); return replies.shift() ?? { ok: true, snapshot: { history: {} } }; },
    redo: async () => { calls.push("bridge.redo"); return replies.shift() ?? { ok: true, snapshot: { history: {} } }; },
  };
  const target = (stack, current) => stack[current]?.length ? current : Object.keys(stack).find((path) => stack[path].length > 0);
  const drafts = {
    history: {
      undoTarget: (current) => target(steps.value, current),
      redoTarget: () => undefined,
      peekUndo: (path) => path === undefined ? undefined : steps.value[path].at(-1),
      peekRedo: () => undefined,
    },
    settledAll: async () => { calls.push("settled"); },
    undo: (path) => {
      calls.push(`drafts.undo ${path}`);
      const stack = [...steps.value[path]];
      const step = stack.pop();
      steps.value = { ...steps.value, [path]: stack };
      return step;
    },
    redo: () => undefined,
    refresh: (path, done) => { calls.push(`refresh ${path}`); done({ kind: "applied", result: { success: true, diagnostics: [] } }); return Promise.resolve(); },
  };
  const feedback = new EditorFeedback();
  const history = new HistoryController({
    bridge,
    snapshot: () => snapshot.value,
    drafts,
    diagnostics: { record: (source, path) => calls.push(`record ${source} ${path}`) },
    selection: { currentDocument: { path: "de.rmf2" }, showStep: (step) => calls.push(`show ${step.path} ${step.key ?? ""}`.trim()) },
    feedback,
    load: async () => { calls.push("load"); },
    install: () => calls.push("install"),
    historyBlocked: () => blocked.value.history,
    draftHistoryBlocked: () => blocked.value.drafts,
    ui,
    focusedField: () => undefined,
  });
  const settleQueue = async () => { await settle(); await settle(); };
  return { history, calls, steps, snapshot, blocked, feedback, settleQueue };
}

// Without unsaved steps, Undo takes back the latest saved change through the host.
{
  const { history, calls, feedback, settleQueue } = createHistory();
  assert.equal(history.canUndo, true);
  assert.equal(history.canRedo, false);
  assert.equal(history.undoLabel, 'ui_toolbar_undo_saved_change_named {"change":"Rename"}');
  assert.equal(history.redoLabel, "ui_toolbar_redo_saved_change");
  history.undo();
  await settleQueue();
  assert.deepEqual(calls, ["settled", "bridge.undo", "install"]);
  assert.equal(feedback.operationMessage.code, "ui_feedback_undo_saved");
  assert.equal(history.revision, 1, "A field that shows text the draft never had gives way to the draft.");
  assert.equal(history.busy, false);
}

// A blocked saved history does nothing; a refusal keeps the host's history and reports why.
{
  const replies = [
    { ok: false, history: { canUndo: false, canRedo: false } },
    { ok: false, message: { code: "ui_backend_history_conflict", args: [] } },
    { ok: true, message: { code: "ui_backend_history_reloaded", args: [] } },
  ];
  const { history, calls, snapshot, blocked, feedback, settleQueue } = createHistory({ replies });
  blocked.value = { history: true, drafts: false };
  assert.equal(history.canUndo, false);
  history.undo();
  await settleQueue();
  assert.ok(!calls.includes("bridge.undo"));
  blocked.value = { history: false, drafts: false };
  const original = snapshot.value;
  history.undo();
  await settleQueue();
  assert.deepEqual(original.history, { canUndo: false, canRedo: false }, "The host's history replaces the stale one.");
  assert.equal(feedback.clientError.code, "ui_feedback_undo_failed");
  history.redo();
  await settleQueue();
  assert.equal(feedback.clientError.code, "ui_backend_history_conflict");
  history.redo();
  await settleQueue();
  assert.ok(calls.includes("load"), "A change the host could not return a snapshot for reloads the workspace.");
  assert.equal(feedback.operationMessage.code, "ui_backend_history_reloaded");
}

// An unsaved step of the selected document is undone first: shown, validated again, and the fields redrawn.
{
  const step = { path: "de.rmf2", key: "greeting", kind: "edit", message: "greeting" };
  const { history, calls, steps, blocked, feedback, settleQueue } = createHistory({ steps: { "de.rmf2": [step], "en.rmf2": [{ path: "en.rmf2", kind: "file-edit" }] } });
  assert.equal(history.undoStep, step);
  assert.equal(history.undoLabel, 'ui_toolbar_undo_unsaved {"change":"ui_history_change_edit","message":"greeting"}');
  assert.equal(history.changeName({ kind: "copy-source" }), "ui_history_change_copy_source");
  feedback.clientError = "old";
  blocked.value = { history: true, drafts: false };
  assert.equal(history.canUndo, true, "Unsaved steps do not wait for the saved history.");
  history.undo();
  await settleQueue();
  assert.deepEqual(calls, ["settled", "drafts.undo de.rmf2", "show de.rmf2 greeting", "refresh de.rmf2", "record validation de.rmf2"]);
  assert.equal(feedback.clientError, undefined);
  assert.equal(history.revision, 1);
  assert.deepEqual(steps.value["de.rmf2"], []);

  // Then another document's latest step; a blocked unsaved history waits.
  assert.equal(history.undoStep.path, "en.rmf2");
  blocked.value = { history: false, drafts: true };
  assert.equal(history.canUndo, false);
  calls.length = 0;
  history.undo();
  await settleQueue();
  assert.deepEqual(calls, ["settled"]);
}

// Undo and redo run one after another: the second waits for the first.
{
  const first = deferred();
  const calls = [];
  const replies = [first.promise, Promise.resolve({ ok: true, snapshot: { history: {} } })];
  const controller = new HistoryController({
    bridge: { undo: () => { calls.push("bridge.undo"); return replies.shift(); }, redo: async () => ({ ok: true }) },
    snapshot: () => ({ history: { canUndo: true } }),
    drafts: { history: { undoTarget: () => undefined, redoTarget: () => undefined, peekUndo: () => undefined, peekRedo: () => undefined }, settledAll: async () => {} },
    diagnostics: { record() {} },
    selection: { currentDocument: undefined, showStep() {} },
    feedback: new EditorFeedback(),
    load: async () => {},
    install: () => calls.push("install"),
    historyBlocked: () => false,
    draftHistoryBlocked: () => false,
    ui,
    focusedField: () => undefined,
  });
  controller.undo();
  controller.undo();
  await settle();
  assert.deepEqual(calls, ["bridge.undo"], "The second undo waits for the first.");
  assert.equal(controller.busy, true);
  first.resolve({ ok: true, snapshot: { history: {} } });
  await settle();
  await settle();
  assert.deepEqual(calls, ["bridge.undo", "install", "bridge.undo", "install"]);
}

console.log("PASS: undo and redo take the selected document's unsaved step first (shown, validated, redrawn), else the host's saved change, one after another, each waiting while its history is blocked.");
