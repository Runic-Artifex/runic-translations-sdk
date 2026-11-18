// The external-changes controller: polled changes, the comparison of one file, and the merge that reloads the workspace.
import assert from "node:assert/strict";
import "./svelte-modules.mjs";

const { ExternalChangesController } = await import("../src/lib/state/external-changes.svelte.ts");
const { EditorFeedback } = await import("../src/lib/state/feedback.svelte.ts");

const documentOf = (path, content) => ({
  path, locale: path.slice(0, 2), layer: "base", isManifest: false, isMalformed: false, revision: "rev-1", content, entries: [],
});
const snapshotOf = (...documents) => ({
  root: "/work",
  catalog: { id: "product", schemaVersion: 1, defaultLocale: "en", locales: [{ tag: "en" }, { tag: "de" }], layers: [] },
  catalogs: [],
  documents,
  diagnostics: [],
  success: true,
});
const change = (path, content) => ({ path, exists: true, content, revision: `disk-${path}` });
const defaultSnapshot = () => snapshotOf(documentOf("en.rmf2", "greeting = Hi\n"), documentOf("de.rmf2", "greeting = Hallo\n"));

// Every collaborator records its calls in `log`; `state` records what the controller set on the workspace.
function createHarness(options = {}) {
  const log = [];
  const state = { loading: [], installed: [], configured: 0 };
  let snapshot = options.snapshot ?? defaultSnapshot();
  const drafts = {
    drafts: { ...(options.drafts ?? {}) },
    replace(next) { log.push("replace"); this.drafts = next; },
    persist() { log.push("persist"); },
  };
  const review = {
    saving: options.saving ?? false,
    applied: [],
    applyExternal(incoming) { this.applied.push(incoming); },
  };
  const unsaved = {
    confirmDiscardNonDocumentWork(key) { log.push(`confirm ${key}`); return options.confirm ?? true; },
  };
  const feedback = new EditorFeedback();
  const bridge = {
    async checkExternalChanges() { log.push("check"); return options.check ? options.check() : { paths: [], changes: [] }; },
    async load() { log.push("load"); return options.load ? options.load() : snapshot; },
  };
  const controller = new ExternalChangesController({
    bridge,
    snapshot: () => snapshot,
    drafts,
    review,
    unsaved,
    feedback,
    idle: () => options.idle ?? true,
    historyBusy: () => options.historyBusy ?? false,
    setLoading: (loading) => state.loading.push(loading),
    install: (next) => { state.installed.push(next); snapshot = next; },
    configureEditor: () => { state.configured += 1; log.push("configure"); },
  });
  return { controller, log, drafts, review, feedback, state };
}

// check() does nothing unless the workspace is idle; the host is not asked.
{
  const h = createHarness({ idle: false });
  await h.controller.check();
  assert.deepEqual(h.log, []);
  assert.deepEqual(h.controller.paths, []);
}

// check() merges the paths sorted without duplicates, and a changed file's entry replaces the old one.
{
  const replies = [
    { paths: ["b.rmf2", "a.rmf2"], changes: [change("b.rmf2", "B1"), change("a.rmf2", "A1")] },
    { paths: ["a.rmf2", "c.rmf2"], changes: [change("a.rmf2", "A2"), change("c.rmf2", "C1")] },
  ];
  let next = 0;
  const h = createHarness({ check: async () => replies[next++] });
  await h.controller.check();
  await h.controller.check();
  assert.deepEqual(h.controller.paths, ["a.rmf2", "b.rmf2", "c.rmf2"]);
  assert.deepEqual(h.controller.changes.map((entry) => [entry.path, entry.content]),
    [["a.rmf2", "A2"], ["b.rmf2", "B1"], ["c.rmf2", "C1"]]);
}

// check() applies the review snapshot the host sends, even when no file changed.
{
  const review = { path: "product.review.json", revision: "review-2", entries: [], terminology: [] };
  const h = createHarness({ check: async () => ({ paths: [], changes: [], review }) });
  await h.controller.check();
  assert.deepEqual(h.review.applied, [review]);
  assert.deepEqual(h.controller.paths, []);
}

// check() leaves the review alone while the review is being saved.
{
  const review = { path: "product.review.json", revision: "review-2", entries: [], terminology: [] };
  const h = createHarness({ saving: true, check: async () => ({ paths: [], changes: [], review }) });
  await h.controller.check();
  assert.deepEqual(h.review.applied, []);
}

// check() swallows a failed poll: the next poll retries, and nothing is reset.
{
  const h = createHarness({ check: async () => { throw new Error("host went away"); } });
  await h.controller.check();
  assert.deepEqual(h.controller.paths, []);
  assert.deepEqual(h.controller.changes, []);
}

// clear() forgets the changes.
{
  const h = createHarness({ check: async () => ({ paths: ["a.rmf2"], changes: [change("a.rmf2", "A")] }) });
  await h.controller.check();
  h.controller.clear();
  assert.deepEqual(h.controller.paths, []);
  assert.deepEqual(h.controller.changes, []);
}

// compare() without a change does nothing.
{
  const h = createHarness();
  h.controller.compare();
  assert.equal(h.controller.compared, undefined);
}

// compare() takes the first change and starts the merge from the draft when there is one.
{
  const h = createHarness({
    drafts: { "de.rmf2": "greeting = Entwurf\n" },
    check: async () => ({ paths: ["a.rmf2", "de.rmf2"], changes: [change("a.rmf2", "A"), change("de.rmf2", "Disk\n")] }),
  });
  await h.controller.check();
  h.controller.compare();
  assert.equal(h.controller.compared.path, "a.rmf2");
}
{
  const h = createHarness({
    drafts: { "de.rmf2": "greeting = Entwurf\n" },
    check: async () => ({ paths: ["de.rmf2"], changes: [change("de.rmf2", "Disk\n")] }),
  });
  await h.controller.check();
  h.controller.compare();
  assert.deepEqual(h.controller.compared, change("de.rmf2", "Disk\n"));
  assert.equal(h.controller.merged, "greeting = Entwurf\n", "The draft is the starting point of the merge.");
}

// compare() without a draft starts from the loaded document text.
{
  const h = createHarness({ check: async () => ({ paths: ["de.rmf2"], changes: [change("de.rmf2", "Disk\n")] }) });
  await h.controller.check();
  h.controller.compare();
  assert.equal(h.controller.merged, "greeting = Hallo\n");
}

// compare() with neither a draft nor a loaded document starts from the disk content, else from empty text.
{
  const h = createHarness({
    snapshot: snapshotOf(documentOf("en.rmf2", "greeting = Hi\n")),
    check: async () => ({ paths: ["de.rmf2"], changes: [change("de.rmf2", "Disk\n")] }),
  });
  await h.controller.check();
  h.controller.compare();
  assert.equal(h.controller.merged, "Disk\n");
}
{
  const h = createHarness({
    snapshot: snapshotOf(documentOf("en.rmf2", "greeting = Hi\n")),
    check: async () => ({ paths: ["de.rmf2"], changes: [{ path: "de.rmf2", exists: false }] }),
  });
  await h.controller.check();
  h.controller.compare();
  assert.equal(h.controller.merged, "");
}

// applyMerge() without a comparison does nothing and asks nothing.
{
  const h = createHarness();
  await h.controller.applyMerge();
  assert.deepEqual(h.log, []);
  assert.deepEqual(h.state.loading, []);
}

// applyMerge() during history playback does nothing.
{
  const h = createHarness({ historyBusy: true, check: async () => ({ paths: ["de.rmf2"], changes: [change("de.rmf2", "Disk\n")] }) });
  await h.controller.check();
  h.controller.compare();
  h.log.length = 0;
  await h.controller.applyMerge();
  assert.deepEqual(h.log, []);
  assert.deepEqual(h.state.loading, []);
}

// applyMerge() stops when the user declines to discard the other unsaved work.
{
  const h = createHarness({ confirm: false, check: async () => ({ paths: ["de.rmf2"], changes: [change("de.rmf2", "Disk\n")] }) });
  await h.controller.check();
  h.controller.compare();
  h.log.length = 0;
  await h.controller.applyMerge();
  assert.deepEqual(h.log, ["confirm ui_confirm_external_merge"]);
  assert.deepEqual(h.state.loading, []);
  assert.deepEqual(h.state.installed, []);
  assert.deepEqual(h.controller.compared, change("de.rmf2", "Disk\n"), "A declined merge keeps the comparison open.");
}

// applyMerge(): reloads the workspace, keeps the merged text beside the drafts of files that still exist, and resets.
{
  const next = snapshotOf(documentOf("en.rmf2", "greeting = Hi\n"), documentOf("de.rmf2", "greeting = Hallo\n"));
  const h = createHarness({
    drafts: { "en.rmf2": "greeting = Hello\n", "gone.rmf2": "old\n" },
    check: async () => ({ paths: ["de.rmf2"], changes: [change("de.rmf2", "greeting = Disk\n")] }),
    load: async () => next,
  });
  await h.controller.check();
  h.controller.compare();
  h.controller.merged = "greeting = Merged\n";
  h.log.length = 0;

  await h.controller.applyMerge();

  assert.deepEqual(h.log, ["confirm ui_confirm_external_merge", "load", "replace", "persist", "configure"]);
  assert.deepEqual(h.state.loading, [true, false]);
  assert.equal(h.state.installed.length, 1);
  assert.equal(h.state.installed[0], next);
  assert.deepEqual(h.drafts.drafts, { "en.rmf2": "greeting = Hello\n", "de.rmf2": "greeting = Merged\n" });
  assert.equal(h.feedback.clientError, undefined);
  assert.deepEqual(h.controller.paths, []);
  assert.deepEqual(h.controller.changes, []);
  assert.equal(h.controller.compared, undefined);
  assert.equal(h.state.configured, 1);
}

// applyMerge(): when the compared file is gone from the reloaded workspace, its merged text is lost and reported.
{
  const next = snapshotOf(documentOf("en.rmf2", "greeting = Hi\n"));
  const h = createHarness({
    check: async () => ({ paths: ["de.rmf2"], changes: [change("de.rmf2", "Disk\n")] }),
    load: async () => next,
  });
  await h.controller.check();
  h.controller.compare();
  await h.controller.applyMerge();
  assert.equal(h.feedback.clientError.code, "ui_feedback_deleted_merge");
  assert.deepEqual(h.feedback.clientError.args, [{ name: "path", value: "de.rmf2" }]);
  assert.equal(h.drafts.drafts["de.rmf2"], undefined);
  assert.deepEqual(h.log.filter((entry) => entry === "persist"), ["persist"]);
  assert.equal(h.controller.compared, undefined);
  assert.equal(h.state.configured, 1);
}

// applyMerge(): a failed reload reports the error, keeps the comparison, and clears the loading flag.
{
  const h = createHarness({
    check: async () => ({ paths: ["de.rmf2"], changes: [change("de.rmf2", "Disk\n")] }),
    load: async () => { throw new Error("gone"); },
  });
  await h.controller.check();
  h.controller.compare();
  h.log.length = 0;
  await h.controller.applyMerge();
  assert.equal(h.feedback.clientError.code, "ui_backend_external_error");
  assert.equal(h.feedback.clientError.detail, "gone");
  assert.deepEqual(h.state.loading, [true, false]);
  assert.equal(h.log.includes("replace"), false);
  assert.equal(h.log.includes("configure"), false);
  assert.deepEqual(h.controller.compared, change("de.rmf2", "Disk\n"));
}

console.log("PASS: external changes are polled, merged and compared, and a merge reloads the workspace");
