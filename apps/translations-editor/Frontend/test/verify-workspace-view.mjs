// What the open catalog shows for the selected locale: the message list's filters and counts, its items, a selection
// the filter hides, per-locale coverage, and the review actions on the selection and on the visible messages.
import assert from "node:assert/strict";
import "./svelte-modules.mjs";

const { RuneValue } = await import("./rune-value.svelte.ts");
const { DraftStore } = await import("../src/lib/state/draft-store.svelte.ts");
const { EditorFeedback } = await import("../src/lib/state/feedback.svelte.ts");
const { PreviewController } = await import("../src/lib/state/preview-controller.svelte.ts");
const { ReviewStore } = await import("../src/lib/state/review-store.svelte.ts");
const { SelectionController } = await import("../src/lib/state/selection-controller.svelte.ts");
const { WorkspaceView } = await import("../src/lib/state/workspace-view.svelte.ts");
const { reviewIdentity } = await import("../src/lib/review-model.ts");

const plural = ".input {$count :integer}\n.match $count\none {{One file}}\n* {{{$count} files}}";
const documentOf = (path, locale, messages) => ({
  path, locale, layer: "base", isManifest: false, isMalformed: false, revision: "rev-1",
  content: messages.map(([key, text]) => `${key} = ${text}\n`).join(""),
  entries: messages.map(([key, text]) => ({ key, path: key.split("_"), content: text })),
});
const snapshot = {
  root: "/work",
  catalog: { id: "product", schemaVersion: 1, defaultLocale: "en", locales: [{ tag: "en" }, { tag: "de", fallback: "en" }], layers: [] },
  catalogs: [{ id: "product", manifestPaths: ["/work/runic.json"] }],
  documents: [
    documentOf("en.rmf2", "en", [["common_farewell", "Bye"], ["common_greeting", "Hello"], ["files_count", plural]]),
    documentOf("de.rmf2", "de", [["common_greeting", "Hallo"], ["files_count", plural]]),
  ],
  review: {
    path: "product.review.json",
    revision: "review-1",
    fingerprints: { common_farewell: "fp-f", common_greeting: "fp-g", files_count: "fp-c" },
    entries: [
      { key: "common_greeting", locale: "de", state: "needs-review", samples: {}, implicit: false },
      { key: "files_count", locale: "de", state: "approved", samples: {}, implicit: false, stale: true },
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
const selection = new SelectionController({
  bridge: { openDocument: () => new Promise(() => undefined) },
  snapshot: () => workspace.value,
  drafts,
  messageDrafts: { get: () => undefined, isLoading: () => false, open: async () => undefined },
  review,
  preview,
  diagnostics: { clear() {} },
  feedback: new EditorFeedback(),
  savedHasMessage: () => true,
  editingBlocked: () => false,
  storage,
});
const reviewBlocked = new RuneValue(false);
const ui = { text: (key) => key };
const view = new WorkspaceView({
  snapshot: () => workspace.value,
  selection,
  drafts,
  review,
  diagnostics: { displayed: [] },
  samples: () => ({ count: "3" }),
  reviewBlocked: () => reviewBlocked.value,
  labels: () => ({ all: "All", missing: "Missing", structured: "Structured", review: "Review", stale: "Stale" }),
  localeName: (tag) => `name of ${tag}`,
  uiDirection: () => "ltr",
  ui,
});
selection.install(snapshot, true);
selection.selectLocale("de");

// Coverage per locale, with the locale's name and whether it is the source.
assert.deepEqual(view.localeSummaries.map((locale) => [locale.tag, locale.name, locale.isSource, locale.fallback]), [
  ["en", "name of en", true, undefined],
  ["de", "name of de", false, "en"],
]);

// The filters count the selected locale's messages.
const counts = Object.fromEntries(view.filterOptions.map((option) => [option.value, option.count]));
assert.deepEqual({ ...counts, quality: undefined }, { all: 3, missing: 1, structured: 1, "needs-review": 1, stale: 1, quality: undefined });
assert.equal(view.filterOptions.find((option) => option.value === "quality").label, "ui_page_quality_title");

// The list shows what the filter and the search let through, with each message's state in the locale.
const visible = () => view.visibleRows.map((row) => row.key);
assert.deepEqual(visible(), ["common_farewell", "common_greeting", "files_count"]);
const greeting = view.messageListItems.find((item) => item.key === "common_greeting");
assert.deepEqual({ ...greeting, path: [...greeting.path] }, {
  key: "common_greeting", path: ["common", "greeting"], preview: "Hallo", missing: false, structured: false, structure: undefined, stale: false, needsReview: true,
});
assert.equal(view.messageListItems.find((item) => item.key === "common_farewell").missing, true);
assert.equal(view.messageListItems.find((item) => item.key === "files_count").stale, true);
assert.deepEqual(view.messageGroupCounts, ["missing", "stale", "needsReview"]);
selection.filter = "missing";
assert.deepEqual(visible(), ["common_farewell"]);
assert.deepEqual(view.messageGroupCounts, [], "A collapsed group does not repeat what the filter implies.");
selection.filter = "structured";
assert.deepEqual(visible(), ["files_count"]);
selection.filter = "needs-review";
assert.deepEqual(visible(), ["common_greeting"]);
selection.filter = "stale";
assert.deepEqual(visible(), ["files_count"]);
selection.filter = "all";
selection.query = "greet";
assert.deepEqual(visible(), ["common_greeting"]);

// #225 P15: a selection the search hides stays selected, and the list says so.
assert.equal(selection.selectedKey, "common_farewell");
assert.equal(view.selectedHiddenByFilter, true);
selection.selectVisible("common_farewell", view.visibleRows);
assert.equal(selection.selectedKey, "common_farewell", "A message the list does not show cannot be chosen from it.");
selection.selectVisible("common_greeting", view.visibleRows);
assert.equal(selection.selectedKey, "common_greeting");
assert.equal(view.selectedHiddenByFilter, false);
selection.clearFilter();

// The selected message's review.
assert.equal(view.currentReviewState, "needs-review");
assert.equal(view.currentIsStale, false);
assert.equal(view.currentSourceDiff, undefined);
assert.equal(view.malformedDocuments.length, 0);
assert.deepEqual(view.diagnosticSources.documents, snapshot.documents);

// Review actions: on the selection with the preview's samples, and on every visible message; not while blocked.
view.setCurrentReviewState("approved");
let entry = review.index.get(reviewIdentity("common_greeting", "de"));
assert.equal(entry.state, "approved");
assert.deepEqual({ ...entry.samples }, { count: "3" });
view.setCurrentReviewNote("checked");
assert.equal(review.index.get(reviewIdentity("common_greeting", "de")).note, "checked");
reviewBlocked.value = true;
view.setCurrentReviewState("needs-review");
view.markVisible("needs-review");
assert.equal(review.index.get(reviewIdentity("common_greeting", "de")).state, "approved", "Blocked review actions change nothing.");
reviewBlocked.value = false;
selection.filter = "missing";
view.markVisible("needs-review");
assert.equal(review.index.get(reviewIdentity("common_farewell", "de")).state, "needs-review");
assert.equal(review.index.get(reviewIdentity("common_greeting", "de")).state, "approved", "Only the visible messages are marked.");
assert.equal(review.message.code, "ui_count_review_marked");

console.log("PASS: the workspace view counts and filters the selected locale's messages, flags a hidden selection, summarizes coverage, and applies review actions to the selection and the visible messages unless blocked.");
