// Next missing and Save and next (D074.1): the next message in tree order of the kind the list's filter names
// (missing; outdated or to review while those filters are on), wrapping at the end; the current selection not in the
// list; and Save and next, which stays on a blocked save.
import assert from "node:assert/strict";
import "./svelte-modules.mjs";

const { RuneValue } = await import("./rune-value.svelte.ts");
const { adjacentMessage, messageTreeOrder, nextMessageKind } = await import("../src/lib/next-message.ts");
const { DraftStore } = await import("../src/lib/state/draft-store.svelte.ts");
const { EditorFeedback } = await import("../src/lib/state/feedback.svelte.ts");
const { NextMessageController, noneNotice } = await import("../src/lib/state/next-message.svelte.ts");
const { PreviewController } = await import("../src/lib/state/preview-controller.svelte.ts");
const { ReviewStore } = await import("../src/lib/state/review-store.svelte.ts");
const { SelectionController } = await import("../src/lib/state/selection-controller.svelte.ts");
const { WorkspaceView } = await import("../src/lib/state/workspace-view.svelte.ts");

// The pure selection: wraps at both ends, never answers the current message, and starts from the top (or the bottom)
// when the current message is not in the tree.
const order = ["a", "b", "c", "d", "e"];
assert.deepEqual(adjacentMessage(order, new Set(["b", "d"]), "a", "next"), { key: "b", wrapped: false });
assert.deepEqual(adjacentMessage(order, new Set(["b", "d"]), "b", "next"), { key: "d", wrapped: false });
assert.deepEqual(adjacentMessage(order, new Set(["b", "d"]), "d", "next"), { key: "b", wrapped: true }, "Next wraps to the top.");
assert.deepEqual(adjacentMessage(order, new Set(["b", "d"]), "b", "previous"), { key: "d", wrapped: true }, "Previous wraps to the bottom.");
assert.deepEqual(adjacentMessage(order, new Set(["b", "d"]), "e", "previous"), { key: "d", wrapped: false });
assert.deepEqual(adjacentMessage(order, new Set(["b", "d"]), "c", "next"), { key: "d", wrapped: false },
  "A current message that is no candidate still marks the position.");
assert.deepEqual(adjacentMessage(order, new Set(["b", "d"]), "c", "previous"), { key: "b", wrapped: false });
assert.equal(adjacentMessage(order, new Set(["c"]), "c", "next"), undefined, "The only candidate is the current message: nowhere to go.");
assert.equal(adjacentMessage(order, new Set(), "a", "next"), undefined);
assert.equal(adjacentMessage([], new Set(["a"]), "a", "next"), undefined);
assert.deepEqual(adjacentMessage(order, new Set(["b", "d"]), "", "next"), { key: "b", wrapped: false }, "No selection starts at the top.");
assert.deepEqual(adjacentMessage(order, new Set(["b", "d"]), "gone", "previous"), { key: "d", wrapped: false }, "No position starts at the bottom going back.");

// The filter decides the kind: outdated and to-review filters step through those, every other filter through missing.
assert.equal(nextMessageKind("all"), "missing");
assert.equal(nextMessageKind("missing"), "missing");
assert.equal(nextMessageKind("structured"), "missing");
assert.equal(nextMessageKind("quality"), "missing");
assert.equal(nextMessageKind("stale"), "stale");
assert.equal(nextMessageKind("needs-review"), "needs-review");

// Tree order is the namespace tree's (siblings by segment, a group's messages before the next sibling), not the
// order of the flattened keys: files.x (group "files") comes before the top-level files_old.
const message = (key, path) => ({ key, path });
assert.deepEqual(messageTreeOrder([
  message("zeta", ["zeta"]), message("files_old", ["files_old"]), message("files_x", ["files", "x"]),
  message("alpha_sub_delta", ["alpha", "sub", "delta"]), message("alpha_beta", ["alpha", "beta"]),
]), ["alpha_beta", "alpha_sub_delta", "files_x", "files_old", "zeta"]);

// The controller over a real selection, view and review.
const entries = [
  ["alpha_beta", ["alpha", "beta"]], ["alpha_gamma", ["alpha", "gamma"]], ["alpha_sub_delta", ["alpha", "sub", "delta"]],
  ["beta_one", ["beta", "one"]], ["beta_two", ["beta", "two"]], ["files_x", ["files", "x"]], ["files_old", ["files_old"]],
  ["zeta", ["zeta"]],
];
const documentOf = (path, locale, keys) => ({
  path, locale, layer: "base", isManifest: false, isMalformed: false, revision: "rev-1",
  content: keys.map((key) => `${key} = Text\n`).join(""),
  entries: entries.filter(([key]) => keys.includes(key)).map(([key, segments]) => ({ key, path: segments, content: `Text ${key}` })),
});
const snapshot = {
  root: "/work",
  catalog: { id: "product", schemaVersion: 1, defaultLocale: "en", locales: [{ tag: "en" }, { tag: "de", fallback: "en" }], layers: [] },
  catalogs: [{ id: "product", manifestPaths: ["/work/runic.json"] }],
  documents: [
    documentOf("en.rmf2", "en", entries.map(([key]) => key)),
    documentOf("de.rmf2", "de", ["alpha_gamma", "beta_one", "files_x"]),
  ],
  review: {
    path: "product.review.json",
    revision: "review-1",
    fingerprints: {},
    entries: [
      { key: "alpha_gamma", locale: "de", state: "needs-review", samples: {}, implicit: false },
      { key: "beta_one", locale: "de", state: "approved", samples: {}, implicit: false, stale: true },
    ],
    terminology: [],
  },
  diagnostics: [],
  success: true,
};
const workspace = new RuneValue(snapshot);
const storage = { get: () => null, set: () => undefined, remove: () => undefined };
const drafts = new DraftStore({ bridge: {}, snapshot: () => workspace.value, storage });
const review = new ReviewStore({ bridge: {}, snapshot: () => workspace.value, errorNotice: (error) => error });
review.install(snapshot);
const preview = new PreviewController({
  bridge: {}, pseudoLocalization: () => false, defaultSample: () => "x", errorNotice: (error) => error, setTimer: () => 0, clearTimer: () => undefined,
});
const savedHasMessage = (key, locale) => workspace.value.documents.some((document) => document.locale === locale && document.entries.some((entry) => entry.key === key));
const selection = new SelectionController({
  bridge: { openDocument: () => new Promise(() => undefined) },
  snapshot: () => workspace.value,
  drafts,
  messageDrafts: { get: () => undefined, isLoading: () => false, open: async () => undefined },
  review,
  preview,
  diagnostics: { clear() {} },
  feedback: new EditorFeedback(),
  savedHasMessage,
  editingBlocked: () => false,
  storage,
});
const view = new WorkspaceView({
  snapshot: () => workspace.value, selection, drafts, review, diagnostics: { displayed: [] },
  samples: () => ({}), reviewBlocked: () => false,
  labels: () => ({ all: "All", missing: "Missing", structured: "Structured", review: "Review", stale: "Stale" }),
  localeName: (tag) => tag, uiDirection: () => "ltr", ui: { text: (key) => key },
});
let saveResult = { kind: "nothing" };
const saves = [];
const busy = new RuneValue(false);
const next = new NextMessageController({
  selection, view,
  saveAll: async () => { saves.push(selection.selectedKey); return saveResult; },
  busy: () => busy.value,
  localeName: (tag) => `name of ${tag}`,
});
selection.install(snapshot, true);
selection.selectLocale("de");
const select = (key) => selection.selectVisible(key, view.visibleRows);

assert.equal(selection.selectedKey, "alpha_beta");
assert.deepEqual(view.treeOrder, ["alpha_beta", "alpha_gamma", "alpha_sub_delta", "beta_one", "beta_two", "files_x", "files_old", "zeta"]);
assert.equal(next.kind, "missing");

// Next missing follows the tree: alpha.beta → alpha.sub.delta → beta.two → files_old (after the files group) → zeta.
const visited = [];
for (let index = 0; index < 4; index += 1) {
  assert.equal(next.step("next"), true);
  visited.push(selection.selectedKey);
  assert.equal(next.notice, undefined, "A step within the list reports nothing.");
}
assert.deepEqual(visited, ["alpha_sub_delta", "beta_two", "files_old", "zeta"]);
// At the end it wraps to the top and says so.
assert.equal(next.step("next"), true);
assert.equal(selection.selectedKey, "alpha_beta");
assert.deepEqual(next.notice, { code: "ui_workbench_wrapped_next", args: [] });
assert.equal(next.step("previous"), true);
assert.equal(selection.selectedKey, "zeta");
assert.deepEqual(next.notice, { code: "ui_workbench_wrapped_previous", args: [] });
assert.equal(next.step("previous"), true);
assert.equal(selection.selectedKey, "files_old");
assert.equal(next.notice, undefined);

// The current selection need not be missing: from a translated message the next missing one after it is chosen.
select("alpha_gamma");
assert.equal(next.nextTarget.key, "alpha_sub_delta");
assert.equal(next.previousTarget.key, "alpha_beta");
select("files_x");
assert.equal(next.nextTarget.key, "files_old");

// The outdated filter steps through outdated messages; the only one is beta.one.
selection.filter = "stale";
assert.equal(next.kind, "stale");
assert.deepEqual(view.visibleRows.map((row) => row.key), ["beta_one"]);
assert.equal(next.nextTarget.key, "beta_one", "The selection the filter hides still marks the position.");
assert.equal(next.step("next"), true);
assert.equal(selection.selectedKey, "beta_one");
assert.equal(next.nextTarget, undefined, "The only outdated message is selected: nowhere to go.");
assert.equal(next.step("next"), false);
assert.deepEqual(next.notice, noneNotice("stale", "name of de"));
assert.deepEqual(next.notice, { code: "ui_workbench_none_stale", args: [{ name: "locale", value: "name of de" }] });
// The to-review filter steps through the messages that need review.
selection.filter = "needs-review";
assert.equal(next.kind, "needs-review");
assert.equal(next.nextTarget.key, "alpha_gamma");
assert.equal(next.notice, undefined, "A notice belongs to the step that reported it.");
// A search narrows what Next visits: only missing messages the list shows.
selection.filter = "all";
selection.query = "alpha";
select("alpha_beta");
assert.equal(next.nextTarget.key, "alpha_sub_delta");
assert.equal(next.previousTarget.key, "alpha_sub_delta", "With two candidates, previous wraps to the other.");
selection.query = "";

// Save and next is offered for a message that was missing (or outdated) when it was selected, and steps through its
// kind after the save.
select("alpha_beta");
assert.equal(next.saveAndNextOffered, true);
assert.equal(next.saveAndNextKind, "missing");
select("alpha_gamma");
assert.equal(next.saveAndNextOffered, false, "A translated message that is not outdated offers no Save and next.");
select("beta_one");
assert.equal(next.saveAndNextOffered, true, "An outdated message offers Save and next.");
assert.equal(next.saveAndNextKind, "stale");

// A blocked save (invalid text) stays on the message and says why.
select("beta_two");
saveResult = { kind: "invalid", path: "de.rmf2", result: { success: false, diagnostics: [] } };
assert.equal(await next.saveAndNext(), false);
assert.equal(selection.selectedKey, "beta_two");
assert.deepEqual(next.notice, { code: "ui_workbench_save_and_next_invalid", args: [] });
saveResult = { kind: "refused", saved: [], path: "de.rmf2", message: "changed on disk" };
assert.equal(await next.saveAndNext(), false);
assert.equal(selection.selectedKey, "beta_two");
assert.deepEqual(next.notice, { code: "ui_workbench_save_and_next_failed", args: [] });
// A save that did not run (another save, a history step) does nothing.
saveResult = undefined;
assert.equal(await next.saveAndNext(), false);
assert.equal(selection.selectedKey, "beta_two");
busy.value = true;
saves.length = 0;
assert.equal(await next.saveAndNext(), false);
assert.deepEqual(saves, [], "Save and next does not start while the editor is busy.");
busy.value = false;

// A save that writes the message: the selected message is no longer missing, and Save and next still goes to the next
// missing message after it.
saveResult = { kind: "saved", saved: ["de.rmf2"], newer: false, message: "Saved" };
const saved = { ...snapshot, documents: [snapshot.documents[0], documentOf("de.rmf2", "de", ["alpha_gamma", "beta_one", "beta_two", "files_x"])] };
const nextAfterSave = next.saveAndNext();
workspace.value = saved;
assert.equal(await nextAfterSave, true);
assert.equal(selection.selectedKey, "files_old");
assert.equal(next.notice, undefined);
assert.deepEqual(saves.slice(-1), ["beta_two"]);

// Save and next of an outdated message steps to the next outdated message (none other here: it stays and says so).
select("beta_one");
saveResult = { kind: "saved", saved: [], newer: false, message: "Saved" };
assert.equal(await next.saveAndNext(), false);
assert.equal(selection.selectedKey, "beta_one");
assert.deepEqual(next.notice, noneNotice("stale", "name of de"));

console.log("PASS: Next missing and Save and next follow the tree order, the list's filter and search, wrap at the end, and stay on a blocked save.");
