// The review store: unsaved review states, notes and terminology over the saved review file, saved through the bridge
// against the revision they started from, and rebased when another writer changes the file.
import assert from "node:assert/strict";
import "./svelte-modules.mjs";
import { mockExecute } from "../src/lib/mock-bridge.ts";

const { ReviewStore } = await import("../src/lib/state/review-store.svelte.ts");

const fingerprints = { common_save: "sha256:save", common_cancel: "sha256:cancel", dashboard_welcome: "sha256:welcome" };
let snapshot = { ...await mockExecute("LoadWorkspace", {}), review: undefined };
snapshot.review = { ...(await mockExecute("LoadWorkspace", {})).review, fingerprints };
const saves = [];
const review = new ReviewStore({
  bridge: { saveReview: (request) => { saves.push(request); return mockExecute("SaveReview", request); } },
  snapshot: () => snapshot,
  errorNotice: (error) => `failed: ${error.message}`,
});

review.install(snapshot);
assert.equal(review.dirty, false);
assert.equal(review.entries.length, 2);
assert.equal(review.terminology.length, 1);
assert.equal(review.revision, "mock-review-1");
assert.equal(review.index.get("common_save\u0000de")?.state, "approved", "The index finds an entry by message and locale.");
assert.notEqual(review.entries[0], snapshot.review.entries[0], "The store edits a copy of the saved data.");

// A state the user sets is recorded for the loaded base text, with the reviewer's samples; a note alone keeps it.
review.setState("common_cancel", "de", "approved", { count: "2" }, true);
assert.equal(review.dirty, true);
assert.deepEqual(
  { ...review.index.get("common_cancel\u0000de"), samples: { ...review.index.get("common_cancel\u0000de").samples } },
  { key: "common_cancel", locale: "de", state: "approved", note: undefined, sourceFingerprint: "sha256:cancel", samples: { count: "2" }, stale: false, implicit: false },
);
review.setNote("dashboard_welcome", "en", "", true);
assert.equal(review.index.get("dashboard_welcome\u0000en").note, undefined, "An empty note clears the note.");
assert.equal(review.index.get("dashboard_welcome\u0000en").state, "needs-review", "A note keeps the review state.");
review.setNote("common_cancel", "fr", "Ask legal.", false);
assert.deepEqual(
  [review.index.get("common_cancel\u0000fr").state, review.index.get("common_cancel\u0000fr").implicit],
  ["draft", true],
  "A note on a missing translation records no state, only the implicit one.",
);
review.mark(["common_save", "dashboard_welcome"], "fr", "needs-review");
assert.equal(review.index.get("common_save\u0000fr").state, "needs-review");
assert.deepEqual(review.message, { code: "ui_count_review_marked", args: [{ name: "count", number: 2 }, { name: "state", value: "needs-review" }] });
review.addTerm({ source: "Cancel", preferred: "Abbrechen", locale: "de" });
review.removeTerm(0);
assert.deepEqual(review.terminology.map((term) => term.source), ["Cancel"]);

// Save: the edits go to the bridge with the data they started from, and the saved file becomes the new base.
await review.save();
assert.equal(saves.length, 1);
assert.equal(saves[0].expectedRevision, "mock-review-1");
assert.equal(saves[0].original.length, 2, "The save names the data the edits started from.");
assert.equal(saves[0].entries.length, 6, "Two saved entries and four new ones (cancel de/fr, save fr, welcome fr).");
assert.equal(review.dirty, false);
assert.equal(review.saving, false);
assert.notEqual(review.revision, "mock-review-1");
assert.deepEqual(review.message, { code: "ui_feedback_workflow_saved", args: [] });
assert.equal(snapshot.review.revision, review.revision, "The workspace snapshot carries the saved review data.");
assert.equal(snapshot.review.fingerprints, fingerprints, "The editor keeps the fingerprints of the base text it shows.");
await review.save();
assert.equal(saves.length, 1, "Without edits nothing is saved.");

// A refused save keeps the edits; a failed one reports the error.
review.setNote("common_save", "de", "Shorter?", true);
review.revision = "someone-else";
await review.save();
assert.equal(review.dirty, true);
assert.match(String(review.message), /changed on disk/);
const failing = new ReviewStore({ bridge: { saveReview: async () => { throw new Error("offline"); } }, snapshot: () => snapshot, errorNotice: (error) => `failed: ${error.message}` });
failing.install(snapshot);
failing.setNote("common_save", "de", "x", true);
await failing.save();
assert.equal(failing.message, "failed: offline");
assert.equal(failing.dirty, true);
assert.equal(failing.saving, false);

// Without the base text's fingerprints no review state can be recorded.
{
  const unfingerprinted = { ...snapshot, review: { ...snapshot.review, fingerprints: undefined } };
  const blocked = new ReviewStore({ bridge: {}, snapshot: () => unfingerprinted, errorNotice: String });
  blocked.install(unfingerprinted);
  blocked.setState("common_save", "de", "approved", {}, true);
  blocked.mark(["common_save"], "de", "approved");
  assert.equal(blocked.dirty, false);
  assert.deepEqual(blocked.message, { code: "ui_review_states_need_base", args: [] });
}

// Another writer changed the review file: without edits the new data is installed, with them the edits are rebased
// on top of it; the shown fingerprints stay.
{
  const external = (entries, revision) => ({ path: snapshot.review.path, revision, entries, terminology: [] });
  const clean = new ReviewStore({ bridge: {}, snapshot: () => snapshot, errorNotice: String });
  clean.install(snapshot);
  clean.applyExternal(external([{ key: "common_save", locale: "de", state: "rejected", samples: {} }], "external-1"));
  assert.equal(clean.dirty, false);
  assert.equal(clean.revision, "external-1");
  assert.deepEqual(clean.entries.map((entry) => entry.state), ["rejected"]);
  assert.deepEqual(clean.message, { code: "ui_feedback_review_external", args: [] });
  assert.equal(snapshot.review.fingerprints, fingerprints);

  clean.setNote("common_save", "de", "Mine", true);
  clean.applyExternal(external([
    { key: "common_save", locale: "de", state: "rejected", samples: {} },
    { key: "common_cancel", locale: "de", state: "approved", samples: {} },
  ], "external-2"));
  assert.equal(clean.dirty, true, "Rebased edits stay unsaved.");
  assert.equal(clean.revision, "external-2", "A later save targets the new revision.");
  assert.equal(clean.index.get("common_save\u0000de").note, "Mine", "The user's edit is kept.");
  assert.equal(clean.index.get("common_cancel\u0000de").state, "approved", "The other writer's change is taken.");
  assert.deepEqual(clean.message, { code: "ui_feedback_review_external_kept", args: [] });

  clean.discard();
  assert.equal(clean.dirty, false);
  assert.equal(clean.index.get("common_save\u0000de").note, undefined, "Discarding returns to the file's data.");
}

function memoryStorage() {
  const values = new Map();
  return { values, get: (key) => values.get(key) ?? null, set: (key, value) => values.set(key, value), remove: (key) => values.delete(key) };
}

// Dirty is content-based: an edit back to the saved entry, or a new entry that records nothing, is no change.
{
  const storage = memoryStorage();
  const store = new ReviewStore({ bridge: {}, snapshot: () => snapshot, errorNotice: String, storage });
  store.install(snapshot);
  const saved = store.index.get("common_save\u0000de");
  store.setNote("common_save", "de", "Temporary", true);
  assert.equal(store.dirty, true);
  assert.equal(storage.values.size, 1, "An unsaved review change is kept for recovery.");
  store.setNote("common_save", "de", saved.note ?? "", true);
  assert.equal(store.dirty, false, "A note typed back to the saved one is no change.");
  assert.equal(storage.values.size, 0, "The recovery copy goes with the last change.");
  store.setNote("dashboard_welcome", "fr", "Ask legal.", false);
  assert.equal(store.dirty, true);
  store.setNote("dashboard_welcome", "fr", "", false);
  assert.equal(store.dirty, false, "A new entry whose note is cleared again records nothing.");
  assert.equal(store.index.has("dashboard_welcome\u0000fr"), false, "The empty new entry is dropped.");
  store.addTerm({ source: "Save", preferred: "Sichern" });
  assert.equal(store.dirty, true);
  store.removeTerm(store.terminology.length - 1);
  assert.equal(store.dirty, false, "Terminology equal to the saved list is no change.");
  assert.equal(storage.values.size, 0);
}

// Recovery: unsaved review changes are stored per workspace and catalog, offered to the next session, and restored
// on top of the review file as it is then, the way an external change is rebased.
{
  const storage = memoryStorage();
  const recoverySnapshot = structuredClone({ ...snapshot, history: undefined });
  const first = new ReviewStore({ bridge: {}, snapshot: () => recoverySnapshot, errorNotice: String, storage });
  first.install(recoverySnapshot);
  first.setState("common_cancel", "en", "approved", { count: "2" }, true);
  first.setNote("common_save", "de", "Recovered note", true);
  first.addTerm({ source: "Cancel", preferred: "Abbrechen", locale: "de" });
  const key = [...storage.values.keys()][0];
  assert.match(key, /^runic-translations:review-drafts:1:/);
  const stored = JSON.parse(storage.values.get(key));
  assert.equal(stored.version, 1);
  assert.deepEqual(stored.entries.map((change) => `${change.entry?.key}/${change.base?.key}`).sort(),
    ["common_cancel/undefined", "common_save/common_save"], "Only changed entries are stored, with the entry each started from.");
  assert.ok(stored.terminology !== undefined, "Changed terminology is stored with its base.");

  // The next session: another writer changed the review file meanwhile (a new entry, and the same note).
  const reopened = structuredClone(recoverySnapshot);
  reopened.review = {
    ...reopened.review,
    revision: "external-3",
    entries: [
      ...reopened.review.entries.map((entry) => entry.key === "common_save" && entry.locale === "de" ? { ...entry, note: "CLI note" } : entry),
      { key: "dashboard_welcome", locale: "de", state: "needs-review", samples: {} },
    ],
  };
  const second = new ReviewStore({ bridge: {}, snapshot: () => reopened, errorNotice: String, storage });
  second.reset(reopened);
  assert.equal(second.dirty, false, "Recovered changes are only offered.");
  assert.equal(second.recovered?.entries.length, 2);
  second.restoreRecovered();
  assert.equal(second.recovered, undefined);
  assert.equal(second.dirty, true);
  assert.equal(second.index.get("common_cancel\u0000en").state, "approved", "A recovered state is restored.");
  assert.equal(second.index.get("common_cancel\u0000en").stale, false, "Recorded for the same base text, it is current.");
  assert.equal(second.index.get("dashboard_welcome\u0000de").state, "needs-review", "The file's newer entry is kept.");
  assert.equal(second.index.get("common_save\u0000de").note, "Recovered note", "An entry changed on both sides keeps the recovered value.");
  assert.deepEqual(second.message, { code: "ui_feedback_review_external_conflict", args: [{ name: "entries", value: "common_save (de)" }] },
    "…and is reported as a conflict, as an external change is.");
  assert.deepEqual(second.terminology.at(-1), { source: "Cancel", preferred: "Abbrechen", locale: "de" }, "Recovered terminology is restored.");
  assert.equal(second.terminology.length, reopened.review.terminology.length + 1);
  assert.equal(second.revision, "external-3", "A later save targets the file as it is now.");

  // Declining keeps only this session's changes; with none, the stored copy is removed.
  const third = new ReviewStore({ bridge: {}, snapshot: () => reopened, errorNotice: String, storage });
  third.reset(reopened);
  assert.ok(third.recovered !== undefined);
  third.discard();
  third.reset(reopened);
  assert.equal(third.recovered, undefined, "Discarding the review changes removes their stored copy.");
  storage.set(key, "{not json");
  third.reset(reopened);
  assert.equal(third.recovered, undefined, "An unreadable stored copy offers nothing.");
}

// A stale state (approved for source text that changed since, RTR0090) is confirmed in the editor (#225 P20): the
// current state is recorded again for the base text the editor loaded, as an unsaved review change the one save writes.
{
  const staleSnapshot = structuredClone({ ...snapshot, history: undefined });
  staleSnapshot.review = (await mockExecute("LoadWorkspace", {})).review;
  staleSnapshot.review.fingerprints = fingerprints;
  const staleSaves = [];
  const store = new ReviewStore({
    bridge: { saveReview: (request) => { staleSaves.push(request); return mockExecute("SaveReview", request); } },
    snapshot: () => staleSnapshot,
    errorNotice: String,
  });
  store.install(staleSnapshot);
  const before = store.index.get("common_save\u0000de");
  assert.deepEqual([before.state, before.stale, before.sourceFingerprint], ["approved", true, "sha256:outdated"], "The fixture's approval is stale.");

  store.confirm("common_save", "de", { count: "1" }, true);
  const confirmed = store.index.get("common_save\u0000de");
  assert.deepEqual([confirmed.state, confirmed.stale, confirmed.sourceFingerprint, confirmed.implicit], ["approved", false, "sha256:save", false],
    "Confirming records the same state for the current source text.");
  assert.equal(store.dirty, true, "A confirmation is an unsaved review change.");
  assert.deepEqual(store.changes.entries.map((change) => `${change.entry?.key}:${change.base?.sourceFingerprint}`), ["common_save:sha256:outdated"]);

  // Choosing the same state again on a stale entry re-records it too (the workflow panel's status select).
  const reselect = new ReviewStore({ bridge: {}, snapshot: () => staleSnapshot, errorNotice: String });
  reselect.install(staleSnapshot);
  reselect.setState("common_save", "de", "approved", {}, true);
  assert.deepEqual([reselect.index.get("common_save\u0000de").stale, reselect.dirty], [false, true], "Re-selecting the stale state records it anew.");

  // A stale "needs review" stays "needs review"; an entry without a recorded state confirms its implicit one.
  store.update("dashboard_welcome", "en", { state: "needs-review", sourceFingerprint: "sha256:old" }, true);
  assert.equal(store.index.get("dashboard_welcome\u0000en").stale, true);
  store.confirm("dashboard_welcome", "en", {}, true);
  assert.deepEqual([store.index.get("dashboard_welcome\u0000en").state, store.index.get("dashboard_welcome\u0000en").stale], ["needs-review", false]);
  assert.equal(store.index.has("files_selected\u0000fr"), false);
  store.confirm("files_selected", "fr", {}, true);
  assert.equal(store.index.get("files_selected\u0000fr").state, "translated", "Without a recorded state, the translation's presence is confirmed.");

  // The one save writes the confirmation; the saved entry is current.
  assert.equal(await store.save(), true);
  assert.equal(staleSaves.length, 1);
  assert.equal(staleSaves[0].entries.find((entry) => entry.key === "common_save" && entry.locale === "de").sourceFingerprint, "sha256:save");
  assert.equal(store.dirty, false);

  // Without the base text's fingerprints nothing can be confirmed.
  const blocked = new ReviewStore({ bridge: {}, snapshot: () => ({ ...staleSnapshot, review: { ...staleSnapshot.review, fingerprints: undefined } }), errorNotice: String });
  blocked.install(staleSnapshot);
  blocked.confirm("common_save", "de", {}, true);
  assert.equal(blocked.dirty, false);
  assert.deepEqual(blocked.message, { code: "ui_review_states_need_base", args: [] });
}

console.log("PASS: the review store edits, saves, refuses, rebases and discards review data like the editor page did, counts only real changes as unsaved, recovers them, and confirms stale states.");
