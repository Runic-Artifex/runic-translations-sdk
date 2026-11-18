// Unsaved work of every kind (document text, review changes, a repair) and the repair of a malformed file: validated, saved, reloaded.
import assert from "node:assert/strict";
import { deferred, settle } from "./svelte-modules.mjs";

const { UnsavedWork } = await import("../src/lib/state/unsaved-work.svelte.ts");
const { RepairController } = await import("../src/lib/state/repair-controller.svelte.ts");
const { EditorFeedback } = await import("../src/lib/state/feedback.svelte.ts");

const documentOf = (path, content, revision = "rev-1") => ({
  path, locale: path.slice(0, 2), layer: "base", isManifest: false, isMalformed: true, revision, content, entries: [],
});
const snapshotOf = (...documents) => ({
  root: "/work",
  catalog: { id: "product", schemaVersion: 1, defaultLocale: "en", locales: [{ tag: "en" }, { tag: "de" }], layers: [] },
  catalogs: [],
  documents,
  diagnostics: [],
  success: true,
});

// Fakes read the variables they close over, so a change after the first read is seen.
function createUnsaved(options = {}) {
  const log = [];
  const state = { saveUnsaved: options.saveUnsaved ?? false, reviewDirty: options.reviewDirty ?? false, confirm: options.confirm ?? true };
  const review = {
    get dirty() { return state.reviewDirty; },
    discard() { log.push("review.discard"); },
  };
  const drafts = { discard() { log.push("drafts.discard"); } };
  const messageDrafts = { reset() { log.push("messageDrafts.reset"); } };
  const save = { get hasUnsaved() { return state.saveUnsaved; } };
  const feedback = new EditorFeedback();
  const repair = new RepairController({
    bridge: {
      validate: async () => ({ success: true, diagnostics: [] }),
      save: async () => ({ ok: true }),
      load: async () => snapshotOf(),
    },
    drafts: { generation: () => 0, hasNewer: () => false, drop: () => {} },
    feedback,
    install: () => {},
    configureEditor: () => {},
    historyBusy: () => false,
    confirm: (key) => { log.push(`confirm ${key}`); return state.confirm; },
  });
  const unsaved = new UnsavedWork({
    save,
    drafts,
    messageDrafts,
    review,
    repair,
    confirm: (key) => { log.push(`confirm ${key}`); return state.confirm; },
  });
  return { unsaved, repair, log, state };
}

// Nothing unsaved: hasUnsavedWork is false and confirmDiscard asks nothing.
{
  const { unsaved, log } = createUnsaved();
  assert.equal(unsaved.hasUnsavedWork, false);
  assert.equal(unsaved.confirmDiscard("ui_confirm_discard"), true);
  assert.deepEqual(log, []);
}

// Unsaved document text makes hasUnsavedWork true, and confirmDiscard asks with its message and returns the answer.
{
  const { unsaved, log, state } = createUnsaved({ saveUnsaved: true, confirm: false });
  assert.equal(unsaved.hasUnsavedWork, true);
  assert.equal(unsaved.confirmDiscard("ui_confirm_discard"), false);
  assert.deepEqual(log, ["confirm ui_confirm_discard"]);
  state.confirm = true;
  assert.equal(unsaved.confirmDiscard("ui_confirm_discard"), true);
}

// Unsaved repair text makes hasUnsavedWork true, even when the document text is saved.
{
  const { unsaved, repair } = createUnsaved();
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n"));
  assert.equal(unsaved.hasUnsavedWork, false, "A repair that is not edited is not unsaved work.");
  repair.text = "greeting = Hallo\nmore\n";
  assert.equal(unsaved.hasUnsavedWork, true);
}

// discard() drops document drafts, message drafts and review changes; a repair is reset only when it is unsaved.
{
  const { unsaved, repair, log } = createUnsaved();
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n"));
  unsaved.discard();
  assert.deepEqual(log, ["drafts.discard", "messageDrafts.reset", "review.discard"]);
  assert.equal(repair.document !== undefined, true, "An unedited repair stays open.");
}
{
  const { unsaved, repair, log } = createUnsaved();
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n"));
  repair.text = "edited\n";
  unsaved.discard();
  assert.deepEqual(log, ["drafts.discard", "messageDrafts.reset", "review.discard"]);
  assert.equal(repair.document, undefined);
  assert.equal(repair.text, "");
}

// confirmDiscardNonDocumentWork(): nothing to drop asks nothing; a declined confirmation drops nothing.
{
  const { unsaved, log } = createUnsaved();
  assert.equal(unsaved.confirmDiscardNonDocumentWork("ui_confirm_external_merge"), true);
  assert.deepEqual(log, []);
}
{
  const { unsaved, log } = createUnsaved({ reviewDirty: true, confirm: false });
  assert.equal(unsaved.confirmDiscardNonDocumentWork("ui_confirm_external_merge"), false);
  assert.deepEqual(log, ["confirm ui_confirm_external_merge"]);
}

// confirmDiscardNonDocumentWork(): a confirmed discard drops the review changes and keeps the document drafts.
{
  const { unsaved, log } = createUnsaved({ reviewDirty: true, saveUnsaved: true });
  assert.equal(unsaved.confirmDiscardNonDocumentWork("ui_confirm_external_merge"), true);
  assert.deepEqual(log, ["confirm ui_confirm_external_merge", "review.discard"]);
}

// confirmDiscardNonDocumentWork(): a confirmed discard also drops an unsaved repair.
{
  const { unsaved, repair, log } = createUnsaved();
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n"));
  repair.text = "edited\n";
  assert.equal(unsaved.confirmDiscardNonDocumentWork("ui_confirm_external_merge"), true);
  assert.deepEqual(log, ["confirm ui_confirm_external_merge", "review.discard"]);
  assert.equal(repair.document, undefined);
}

// begin(): opens the document, validates its text and shows the validation.
{
  const { repair } = createUnsaved();
  const document = documentOf("de.rmf2", "greeting = Hallo\n");
  repair.begin(document);
  assert.equal(repair.document.path, "de.rmf2");
  assert.equal(repair.text, "greeting = Hallo\n");
  assert.equal(repair.hasUnsaved, false);
  await settle();
  assert.deepEqual(repair.validation, { success: true, diagnostics: [] });
}

// begin(): an unsaved repair of another file is confirmed first; a declined confirmation keeps the open repair.
{
  const feedbackConfirm = [];
  const feedback = new EditorFeedback();
  const repair = new RepairController({
    bridge: { validate: async () => ({ success: true, diagnostics: [] }), save: async () => ({ ok: true }), load: async () => snapshotOf() },
    drafts: { generation: () => 0, hasNewer: () => false, drop: () => {} },
    feedback,
    install: () => {},
    configureEditor: () => {},
    historyBusy: () => false,
    confirm: (key) => { feedbackConfirm.push(key); return false; },
  });
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n"));
  repair.text = "edited\n";
  repair.begin(documentOf("fr.rmf2", "salut = Salut\n"));
  assert.deepEqual(feedbackConfirm, ["ui_feedback_repair_open_confirm"]);
  assert.equal(repair.document.path, "de.rmf2");
  assert.equal(repair.text, "edited\n");
}

// begin(): a validation reply for a file that was replaced in the meantime is ignored.
{
  const replies = { "de.rmf2": deferred(), "fr.rmf2": deferred() };
  const repair = new RepairController({
    bridge: { validate: (path) => replies[path].promise, save: async () => ({ ok: true }), load: async () => snapshotOf() },
    drafts: { generation: () => 0, hasNewer: () => false, drop: () => {} },
    feedback: new EditorFeedback(),
    install: () => {},
    configureEditor: () => {},
    historyBusy: () => false,
    confirm: () => true,
  });
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n"));
  repair.begin(documentOf("fr.rmf2", "salut = Salut\n"));
  const frenchResult = { success: true, diagnostics: [] };
  const germanResult = { success: false, diagnostics: [{ message: "stale" }] };
  replies["fr.rmf2"].resolve(frenchResult);
  replies["de.rmf2"].resolve(germanResult);
  await settle();
  assert.deepEqual(repair.validation, frenchResult);
}

// close(): an unsaved repair is confirmed first; a declined confirmation keeps it, a confirmed one closes it.
{
  const { repair, log, state } = createUnsaved({ confirm: false });
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n"));
  repair.text = "edited\n";
  log.length = 0;
  repair.close();
  assert.equal(repair.document.path, "de.rmf2");
  assert.deepEqual(log, ["confirm ui_feedback_discard_repair"]);
  state.confirm = true;
  repair.close();
  assert.equal(repair.document, undefined);
  assert.equal(repair.text, "");
}

// close(): an unedited repair closes without asking.
{
  const { repair, log } = createUnsaved();
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n"));
  log.length = 0;
  repair.close();
  assert.equal(repair.document, undefined);
  assert.deepEqual(log, []);
}

// save(): an invalid repair shows the first diagnostic and saves nothing.
{
  const saves = [];
  const repair = new RepairController({
    bridge: {
      validate: async () => ({ success: false, diagnostics: [{ notice: { code: "ui_diagnostic_bad", args: [] }, message: "bad" }] }),
      save: async (...args) => { saves.push(args); return { ok: true }; },
      load: async () => snapshotOf(),
    },
    drafts: { generation: () => 0, hasNewer: () => false, drop: () => {} },
    feedback: new EditorFeedback(),
    install: () => {},
    configureEditor: () => {},
    historyBusy: () => false,
    confirm: () => true,
  });
  repair.begin(documentOf("de.rmf2", "greeting = \n"));
  await repair.save();
  assert.equal(repair.message.code, "ui_diagnostic_bad");
  assert.deepEqual(saves, []);
  assert.equal(repair.document.path, "de.rmf2", "The repair stays open to be fixed.");
  assert.equal(repair.busy, false);
}

// save(): a refused write shows the host's message and keeps the repair open.
{
  const repair = new RepairController({
    bridge: {
      validate: async () => ({ success: true, diagnostics: [] }),
      save: async () => ({ ok: false, message: { code: "ui_feedback_revision_conflict", args: [] } }),
      load: async () => snapshotOf(),
    },
    drafts: { generation: () => 0, hasNewer: () => false, drop: () => {} },
    feedback: new EditorFeedback(),
    install: () => {},
    configureEditor: () => {},
    historyBusy: () => false,
    confirm: () => true,
  });
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n"));
  await repair.save();
  assert.equal(repair.message.code, "ui_feedback_revision_conflict");
  assert.equal(repair.document.path, "de.rmf2");
  assert.equal(repair.busy, false);
}

// save(): a successful save writes against the opened revision, reloads the workspace, drops the file's draft and closes the repair.
{
  const log = [];
  const feedback = new EditorFeedback();
  const next = snapshotOf(documentOf("de.rmf2", "greeting = Hallo\n", "rev-2"));
  const repair = new RepairController({
    bridge: {
      validate: async () => ({ success: true, diagnostics: [] }),
      save: async (path, text, revision) => { log.push(`save ${path} ${revision} ${JSON.stringify(text)}`); return { ok: true }; },
      load: async () => { log.push("load"); return next; },
    },
    drafts: {
      generation: () => 7,
      hasNewer: (path, content, generation) => { log.push(`hasNewer ${path} ${generation}`); return false; },
      drop: (path) => log.push(`drop ${path}`),
    },
    feedback,
    install: (snapshot, resetSelection) => log.push(`install ${resetSelection}`),
    configureEditor: () => log.push("configure"),
    historyBusy: () => false,
    confirm: () => true,
  });
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n", "rev-1"));
  repair.text = "greeting = Hallo!\n";
  await repair.save();
  assert.deepEqual(log, [
    "save de.rmf2 rev-1 \"greeting = Hallo!\\n\"",
    "load",
    "hasNewer de.rmf2 7",
    "install false",
    "drop de.rmf2",
    "configure",
  ]);
  assert.equal(feedback.operationMessage.code, "app_saved");
  assert.equal(repair.document, undefined);
  assert.equal(repair.busy, false);
}

// save(): a newer draft of the file is kept, and the user is told so.
{
  const log = [];
  const feedback = new EditorFeedback();
  const repair = new RepairController({
    bridge: {
      validate: async () => ({ success: true, diagnostics: [] }),
      save: async () => ({ ok: true }),
      load: async () => snapshotOf(documentOf("de.rmf2", "greeting = Hallo!\n", "rev-2")),
    },
    drafts: {
      generation: () => 0,
      hasNewer: () => true,
      drop: (path) => log.push(`drop ${path}`),
    },
    feedback,
    install: () => {},
    configureEditor: () => {},
    historyBusy: () => false,
    confirm: () => true,
  });
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n"));
  repair.text = "greeting = Hallo!\n";
  await repair.save();
  assert.deepEqual(log, [], "A newer draft is not dropped.");
  assert.equal(feedback.operationMessage.code, "ui_feedback_newer_draft");
}

// save(): nothing happens while a save is running or history is replayed.
{
  const saves = [];
  const pending = deferred();
  const repair = new RepairController({
    bridge: {
      validate: async () => ({ success: true, diagnostics: [] }),
      save: async () => { saves.push("save"); return pending.promise; },
      load: async () => snapshotOf(),
    },
    drafts: { generation: () => 0, hasNewer: () => false, drop: () => {} },
    feedback: new EditorFeedback(),
    install: () => {},
    configureEditor: () => {},
    historyBusy: () => false,
    confirm: () => true,
  });
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n"));
  const first = repair.save();
  await repair.save();
  assert.deepEqual(saves, ["save"]);
  pending.resolve({ ok: false });
  await first;
  assert.equal(repair.busy, false);
}
{
  let saves = 0;
  const repair = new RepairController({
    bridge: { validate: async () => ({ success: true, diagnostics: [] }), save: async () => { saves += 1; return { ok: true }; }, load: async () => snapshotOf() },
    drafts: { generation: () => 0, hasNewer: () => false, drop: () => {} },
    feedback: new EditorFeedback(),
    install: () => {},
    configureEditor: () => {},
    historyBusy: () => true,
    confirm: () => true,
  });
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n"));
  await repair.save();
  assert.equal(saves, 0);
  assert.equal(repair.document.path, "de.rmf2");
}

// save(): a thrown error is shown as an external error, and the busy flag is released.
{
  const repair = new RepairController({
    bridge: {
      validate: async () => ({ success: true, diagnostics: [] }),
      save: async () => { throw new Error("disk full"); },
      load: async () => snapshotOf(),
    },
    drafts: { generation: () => 0, hasNewer: () => false, drop: () => {} },
    feedback: new EditorFeedback(),
    install: () => {},
    configureEditor: () => {},
    historyBusy: () => false,
    confirm: () => true,
  });
  repair.begin(documentOf("de.rmf2", "greeting = Hallo\n"));
  await repair.save();
  assert.equal(repair.message.code, "ui_backend_external_error");
  assert.equal(repair.message.detail, "disk full");
  assert.equal(repair.busy, false);
}

console.log("PASS: unsaved work is confirmed and dropped by kind, and a malformed file's repair is validated, saved and reloaded");
