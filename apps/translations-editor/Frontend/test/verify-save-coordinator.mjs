// The one save: Ctrl+S and the toolbar's Save write every unsaved document (each validated, an invalid one blocks the
// save), then the review changes, and the unsaved summary names what is left.
import assert from "node:assert/strict";
import { deferred, settle } from "./svelte-modules.mjs";

const { DraftStore } = await import("../src/lib/state/draft-store.svelte.ts");
const { ReviewStore } = await import("../src/lib/state/review-store.svelte.ts");
const { SaveCoordinator } = await import("../src/lib/state/save-coordinator.svelte.ts");

const documentOf = (path, content, revision = "rev-1") => ({
  path, locale: path.slice(0, 2), layer: "base", isManifest: false, isMalformed: false, revision, content, entries: [],
});
const reviewOf = (entries, revision = "review-1") => ({ path: "product.review.json", revision, entries, terminology: [] });

// A host with files on disk: `save` writes them (or refuses a listed path), `load` reads them back as a snapshot.
function createHost(options = {}) {
  const disk = { "en.rmf2": "greeting = Hi\n", "de.rmf2": "greeting = Hallo\n" };
  const revisions = { "en.rmf2": 1, "de.rmf2": 1 };
  let review = reviewOf([{ key: "greeting", locale: "de", state: "approved", samples: {}, implicit: false }]);
  const calls = [];
  const refuse = new Set(options.refuse ?? []);
  const snapshotOf = () => ({
    root: "/work",
    catalog: { id: "product", schemaVersion: 1, defaultLocale: "en", locales: [{ tag: "en" }, { tag: "de" }], layers: [] },
    catalogs: [],
    documents: Object.entries(disk).map(([path, content]) => documentOf(path, content, `rev-${revisions[path]}`)),
    review: structuredClone(review),
    diagnostics: [],
    success: true,
  });
  const bridge = {
    async validate(path, content) {
      calls.push(`validate ${path}`);
      return { success: !content.includes("{{"), diagnostics: [] };
    },
    async save(path, content, revision) {
      calls.push(`save ${path}`);
      if (options.beforeSave !== undefined) await options.beforeSave(path);
      if (refuse.has(path) || revision !== `rev-${revisions[path]}`) {
        return { ok: false, kind: "conflict", message: { code: "ui_backend_document_conflict", args: [{ name: "path", value: path }] } };
      }
      disk[path] = content;
      revisions[path] += 1;
      return { ok: true, kind: "saved" };
    },
    async load() {
      calls.push("load");
      return snapshotOf();
    },
    async saveReview(request) {
      calls.push("saveReview");
      if (options.refuseReview) return { ok: false, message: { code: "ui_backend_review_conflict", args: [] } };
      review = reviewOf(request.entries, `review-${calls.length}`);
      return { ok: true, review: structuredClone(review) };
    },
  };
  return { bridge, calls, disk, snapshotOf, review: () => review };
}

function memoryStorage() {
  const values = new Map();
  return { values, get: (key) => values.get(key) ?? null, set: (key, value) => values.set(key, value), remove: (key) => values.delete(key) };
}

// The page's wiring: the stores share the snapshot, and the coordinator installs the reloaded workspace as the page does.
function createEditor(host) {
  let snapshot = host.snapshotOf();
  const storage = memoryStorage();
  const validated = [];
  const drafts = new DraftStore({ bridge: host.bridge, snapshot: () => snapshot, storage, wait: () => Promise.resolve() });
  const review = new ReviewStore({ bridge: host.bridge, snapshot: () => snapshot, errorNotice: (error) => String(error), storage });
  drafts.reset(snapshot);
  review.reset(snapshot);
  const coordinator = new SaveCoordinator({
    bridge: host.bridge,
    drafts,
    review,
    snapshot: () => snapshot,
    validated: (path, result) => validated.push({ path, success: result.success }),
    install(next) {
      snapshot = next;
      drafts.prune();
      if (!review.dirty) review.install(next);
    },
  });
  return { drafts, review, coordinator, storage, validated, snapshot: () => snapshot };
}

// Nothing unsaved: the toolbar says "Saved", and a save does nothing.
{
  const host = createHost();
  const { coordinator } = createEditor(host);
  assert.deepEqual(coordinator.unsavedSummary, { documents: [], review: false });
  assert.equal(coordinator.hasUnsaved, false);
  assert.deepEqual(await coordinator.saveAll(), { kind: "nothing" });
  assert.deepEqual(host.calls, []);
}

// Text in two documents and a review note: one save validates both documents, writes them, reloads once, then saves
// the review changes against the saved text.
{
  const host = createHost();
  const { drafts, review, coordinator, storage, validated } = createEditor(host);
  drafts.set("de.rmf2", "greeting = Servus\n");
  drafts.set("en.rmf2", "greeting = Hello\n");
  drafts.persist();
  review.setNote("greeting", "de", "Regional", true);
  assert.deepEqual(coordinator.unsavedSummary, { documents: ["en.rmf2", "de.rmf2"], review: true },
    "The summary names the unsaved documents in workspace order and the review changes.");
  assert.equal(storage.values.size, 2, "Text drafts and review changes are both kept for recovery.");
  const outcome = await coordinator.saveAll();
  assert.equal(outcome.kind, "saved");
  assert.deepEqual(outcome.saved, ["en.rmf2", "de.rmf2"]);
  assert.equal(outcome.newer, false);
  assert.deepEqual(outcome.message, { code: "app_saved", args: [] });
  assert.deepEqual(host.calls, ["validate en.rmf2", "validate de.rmf2", "save en.rmf2", "save de.rmf2", "load", "saveReview"],
    "Every document is validated before any is written, and the review is saved last.");
  assert.deepEqual(validated, [{ path: "en.rmf2", success: true }, { path: "de.rmf2", success: true }]);
  assert.equal(host.disk["de.rmf2"], "greeting = Servus\n");
  assert.equal(host.review().entries[0].note, "Regional");
  assert.deepEqual(coordinator.unsavedSummary, { documents: [], review: false });
  assert.equal(storage.values.size, 0, "Nothing is left to recover after the save.");
  assert.equal(coordinator.saving, false);
}

// An invalid document blocks the whole save: nothing is written, the drafts and the review changes stay.
{
  const host = createHost();
  const { drafts, review, coordinator, validated } = createEditor(host);
  drafts.set("en.rmf2", "greeting = Hello\n");
  drafts.set("de.rmf2", "greeting = {{broken\n");
  review.setNote("greeting", "de", "Pending", true);
  const outcome = await coordinator.saveAll();
  assert.equal(outcome.kind, "invalid");
  assert.equal(outcome.path, "de.rmf2");
  assert.deepEqual(host.calls, ["validate en.rmf2", "validate de.rmf2"]);
  assert.deepEqual(validated.at(-1), { path: "de.rmf2", success: false }, "The invalid document's diagnostics are shown.");
  assert.deepEqual(coordinator.unsavedSummary, { documents: ["en.rmf2", "de.rmf2"], review: true });
}

// A refused document stops the save; the documents written before it are saved, the review changes are not.
{
  const host = createHost({ refuse: ["de.rmf2"] });
  const { drafts, review, coordinator } = createEditor(host);
  drafts.set("en.rmf2", "greeting = Hello\n");
  drafts.set("de.rmf2", "greeting = Servus\n");
  review.setNote("greeting", "de", "Pending", true);
  const outcome = await coordinator.saveAll();
  assert.equal(outcome.kind, "refused");
  assert.equal(outcome.path, "de.rmf2");
  assert.deepEqual(outcome.saved, ["en.rmf2"]);
  assert.deepEqual(outcome.message, { code: "ui_backend_document_conflict", args: [{ name: "path", value: "de.rmf2" }] });
  assert.ok(!host.calls.includes("saveReview"), "Review changes wait until every document is saved.");
  assert.deepEqual(coordinator.unsavedSummary, { documents: ["de.rmf2"], review: true });
}

// A refused review save after the documents: the documents are saved, the review changes stay unsaved.
{
  const host = createHost({ refuseReview: true });
  const { drafts, review, coordinator } = createEditor(host);
  drafts.set("en.rmf2", "greeting = Hello\n");
  review.setNote("greeting", "de", "Pending", true);
  const outcome = await coordinator.saveAll();
  assert.equal(outcome.kind, "review-failed");
  assert.deepEqual(outcome.saved, ["en.rmf2"]);
  assert.deepEqual(outcome.message, { code: "ui_backend_review_conflict", args: [] });
  assert.deepEqual(coordinator.unsavedSummary, { documents: [], review: true });
}

// Only review changes: no document is validated or written, and no reload is needed.
{
  const host = createHost();
  const { review, coordinator } = createEditor(host);
  review.setState("greeting", "de", "needs-review", {}, true);
  assert.equal(review.dirty, false, "Without the base text's fingerprints no state is recorded.");
  review.setNote("greeting", "de", "Only review", true);
  assert.deepEqual(coordinator.unsavedSummary, { documents: [], review: true });
  assert.equal((await coordinator.saveAll()).kind, "saved");
  assert.deepEqual(host.calls, ["saveReview"]);
}

// An edit typed back to the saved text leaves nothing to save.
{
  const host = createHost();
  const { drafts, coordinator } = createEditor(host);
  drafts.set("en.rmf2", "greeting = Hello\n");
  assert.equal(coordinator.hasUnsaved, true);
  drafts.set("en.rmf2", "greeting = Hi\n");
  assert.equal(coordinator.hasUnsaved, false, "Save is disabled again after the edit is reverted.");
  assert.deepEqual(await coordinator.saveAll(), { kind: "nothing" });
}

// A draft typed while its document is saved survives the save; a queued edit lands before the save reads the text.
{
  let editor;
  const host = createHost({ beforeSave: async (path) => { if (path === "en.rmf2") editor.drafts.set("en.rmf2", "greeting = Newer\n"); } });
  editor = createEditor(host);
  editor.drafts.set("en.rmf2", "greeting = Hello\n");
  const outcome = await editor.coordinator.saveAll();
  assert.equal(outcome.kind, "saved");
  assert.equal(outcome.newer, true);
  assert.equal(host.disk["en.rmf2"], "greeting = Hello\n");
  assert.equal(editor.drafts.drafts["en.rmf2"], "greeting = Newer\n", "The newer draft stays open.");

  const transformed = deferred();
  const queuedHost = createHost();
  queuedHost.bridge.transformDocument = () => transformed.promise;
  const queued = createEditor(queuedHost);
  void queued.drafts.transform("de.rmf2", "greeting = Hallo\n", "greeting", "Grüß Gott", () => {});
  const saving = queued.coordinator.saveAll();
  await settle();
  assert.equal(queued.coordinator.saving, true);
  assert.deepEqual(queuedHost.calls, [], "The save waits for the queued composer edit.");
  transformed.resolve({ success: true, content: "greeting = Grüß Gott\n", entries: [], diagnostics: [] });
  assert.equal((await saving).kind, "saved");
  assert.equal(queuedHost.disk["de.rmf2"], "greeting = Grüß Gott\n");
}

console.log("PASS: the save coordinator saves every unsaved document, then the review changes, blocks on invalid text, and reports what is unsaved.");
