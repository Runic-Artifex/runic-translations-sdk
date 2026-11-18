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

console.log("PASS: the review store edits, saves, refuses, rebases and discards review data like the editor page did.");
