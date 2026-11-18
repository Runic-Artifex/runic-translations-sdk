// The source text is not a translation (#225, real-window check R1): the base locale of a translation project is
// edited with the wording of a content-editing project ("Edit the text", "Default text"), without translation review
// states, review actions, translation filters or Next missing. The rule is the same "editing the source" condition a
// content-editing project uses (D074.2), derived once in `WorkspaceView.textRole`.
import assert from "node:assert/strict";
import "./svelte-modules.mjs";
import { readUiMessages } from "./ui-messages.mjs";

const { RuneValue } = await import("./rune-value.svelte.ts");
const { contentWording, projectTextKey, projectUiText, sourceTextWording, textRole } = await import("../src/lib/project-kind.ts");
const { DraftStore } = await import("../src/lib/state/draft-store.svelte.ts");
const { EditorFeedback } = await import("../src/lib/state/feedback.svelte.ts");
const { NextMessageController } = await import("../src/lib/state/next-message.svelte.ts");
const { PreviewController } = await import("../src/lib/state/preview-controller.svelte.ts");
const { ReviewStore } = await import("../src/lib/state/review-store.svelte.ts");
const { SelectionController } = await import("../src/lib/state/selection-controller.svelte.ts");
const { WorkspaceView } = await import("../src/lib/state/workspace-view.svelte.ts");
const { reviewIdentity } = await import("../src/lib/review-model.ts");

// --- The rule: the base locale and every text of a content-editing project are the source text. ---
assert.equal(textRole("translation", "en", "en"), "source");
assert.equal(textRole("translation", "EN", "en"), "source", "Locale tags compare case-insensitively.");
assert.equal(textRole("translation", "de", "en"), "translation");
assert.equal(textRole("content", "de", "de"), "source");
assert.equal(textRole("translation", "en", ""), "translation", "Without a catalog nothing is the source text.");

// --- The wording: the source text speaks of the text, with the keys and arguments #247 introduced. ---
assert.equal(projectTextKey("ui_composer_translate_message", "translation", "source"), "ui_content_composer_heading");
assert.equal(projectTextKey("ui_composer_default_translation", "translation", "source"), "ui_content_composer_default_text");
assert.equal(projectTextKey("ui_composer_translation_for", "translation", "source"), "ui_content_composer_text_for");
assert.equal(projectTextKey("ui_composer_translate_message", "translation", "translation"), "ui_composer_translate_message");
assert.equal(projectTextKey("ui_composer_translate_message", "translation"), "ui_composer_translate_message");
assert.equal(projectTextKey("ui_sidebar_panels_messages_panel", "translation", "source"), "ui_sidebar_panels_messages_panel",
  "A translation project still lists messages while its base locale is selected.");
assert.equal(projectTextKey("ui_sidebar_panels_messages_panel", "content", "source"), "ui_content_texts_panel");
for (const [key, value] of Object.entries(sourceTextWording)) {
  assert.equal(contentWording[key], value, `The content wording of ${key} is not the source-text wording.`);
}
const english = await readUiMessages("en");
const german = await readUiMessages("de");
assert.equal(english[sourceTextWording.ui_composer_translate_message], "Edit the text");
assert.equal(english[sourceTextWording.ui_composer_default_translation], "Default text");
assert.equal(german[sourceTextWording.ui_composer_translate_message], "Text bearbeiten");
assert.equal(german[sourceTextWording.ui_composer_default_translation], "Standardtext");
const role = new RuneValue("translation");
const wording = projectUiText({ text: (key) => key }, () => "translation", () => role.value);
assert.equal(wording.text("ui_composer_translate_message"), "ui_composer_translate_message");
role.value = "source";
assert.equal(wording.text("ui_composer_translate_message"), "ui_content_composer_heading", "The wording follows the selected locale.");

// --- The workspace view of a translation project with English as the base locale. ---
const catalog = {
  id: "app", schemaVersion: 1, defaultLocale: "en", layers: [],
  locales: [{ tag: "en" }, { tag: "de", fallback: "en" }, { tag: "fr", fallback: "en" }],
};
const documentOf = (path, locale, rows) => ({
  path, locale, layer: "base", isManifest: false, isMalformed: false, revision: "rev-1",
  content: rows.map(([key, text]) => `${key} = ${text}\n`).join(""),
  entries: rows.map(([key, text]) => ({ key, path: key.split("_"), content: text })),
});
const snapshot = {
  root: "/work",
  catalog,
  catalogs: [{ id: "app", manifestPaths: ["/work/runic.json"] }],
  documents: [
    documentOf("en.rmf2", "en", [["common_save", "Save"], ["common_cancel", "Cancel"], ["notes_title", "Notes"]]),
    documentOf("de.rmf2", "de", [["common_save", "Speichern"], ["common_cancel", "Abbrechen"]]),
    documentOf("fr.rmf2", "fr", [["common_save", "Enregistrer"]]),
  ],
  review: {
    path: "app.review.json", revision: "review-1", fingerprints: {}, terminology: [],
    // A review entry of the base text, as a tool may have written it: it does not apply to the source text.
    entries: [
      { key: "common_save", locale: "en", state: "needs-review", samples: {}, implicit: false, stale: true },
      { key: "common_save", locale: "de", state: "needs-review", samples: {}, implicit: false },
    ],
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
  savedHasMessage: (key, locale) => workspace.value.documents.some((document) => document.locale === locale && document.entries.some((entry) => entry.key === key)),
  editingBlocked: () => false,
  storage,
});
const view = new WorkspaceView({
  snapshot: () => workspace.value, selection, drafts, review, diagnostics: { displayed: [] },
  samples: () => ({}), reviewBlocked: () => false,
  labels: () => ({ all: "All", missing: "Missing", structured: "Structured", review: "Review", stale: "Stale" }),
  localeName: (tag) => `name of ${tag}`, uiDirection: () => "ltr", ui: { text: (key) => key },
});
const next = new NextMessageController({
  selection, view, saveAll: async () => ({ kind: "saved" }), busy: () => false, localeName: (tag) => `name of ${tag}`,
});
selection.install(snapshot, true);

assert.equal(view.projectKind, "translation");
assert.equal(selection.selectedLocale, "en");
assert.equal(view.textRole, "source", "The base locale of a translation project holds the source text.");
assert.equal(view.editingSource, true);
// No translation review state for the base text: no review entry, no stale state, no needs-review row.
selection.selectVisible("common_save", view.visibleRows);
assert.equal(selection.selectedKey, "common_save");
assert.equal(view.currentReview, undefined, "The base text has a review entry.");
assert.equal(view.currentIsStale, false);
assert.ok(view.messageListItems.every((item) => !item.missing && !item.stale && !item.needsReview),
  "A message of the base locale shows a translation state.");
assert.deepEqual(view.messageGroupCounts, []);
assert.deepEqual(view.filterOptions.map((option) => option.value), ["all", "structured", "quality"]);
// A translation filter chosen in another locale shows every message here.
selection.filter = "missing";
assert.equal(view.filter, "all");
selection.filter = "all";
// The review actions record nothing for the base text.
view.setCurrentReviewState("approved");
view.confirmCurrentReview();
view.setCurrentReviewNote("note");
view.markVisible("approved");
assert.equal(review.dirty, false, "A review action changed review data of the base text.");
assert.equal(review.index.get(reviewIdentity("common_save", "en"))?.state, "needs-review", "The review file's entry is kept as it was.");
// Next goes through every text of the source, Save and next to the next text.
assert.equal(view.stepKind, "text");
assert.equal(next.kind, "text");
assert.equal(next.saveAndNextOffered, true);

// German is a translation: its states, filters and review actions apply.
selection.selectLocale("de");
selection.selectVisible("common_save", view.visibleRows);
assert.equal(selection.selectedKey, "common_save");
assert.equal(view.textRole, "translation");
assert.equal(view.editingSource, false);
assert.equal(view.currentReview?.state, "needs-review");
assert.deepEqual(view.filterOptions.map((option) => option.value), ["all", "missing", "structured", "needs-review", "stale", "quality"]);
assert.equal(view.messageListItems.find((item) => item.key === "notes_title")?.missing, true);
assert.equal(next.kind, "missing");
view.setCurrentReviewState("approved");
assert.equal(review.dirty, true, "A review action of a translation was not recorded.");
review.discard();

console.log("PASS: the source text (the base locale, or a content-editing project) is edited as text, without translation review states, review actions, translation filters or Next missing.");
