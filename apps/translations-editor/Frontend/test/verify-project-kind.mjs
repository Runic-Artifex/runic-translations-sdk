// One-language projects are content-editing projects (D074.2): the mode's derivation from the catalog (pseudo-locales
// don't count), the content wording in both interface locales, what the workspace view hides in a content-editing
// project (translation filters and states, the review workflow), Save and next to the next text, the keys of hidden
// controls, and the switch to translation mode when a second language is added.
import assert from "node:assert/strict";
import "./svelte-modules.mjs";
import { readUiMessages } from "./ui-messages.mjs";

const { RuneValue } = await import("./rune-value.svelte.ts");
const { contentWording, projectKind, projectTextKey, projectUiText, realLocales } = await import("../src/lib/project-kind.ts");
const { editorShortcut } = await import("../src/lib/editor-keyboard.ts");
const { DraftStore } = await import("../src/lib/state/draft-store.svelte.ts");
const { EditorFeedback } = await import("../src/lib/state/feedback.svelte.ts");
const { NextMessageController, noneNotice } = await import("../src/lib/state/next-message.svelte.ts");
const { PreviewController } = await import("../src/lib/state/preview-controller.svelte.ts");
const { ReviewStore } = await import("../src/lib/state/review-store.svelte.ts");
const { SelectionController } = await import("../src/lib/state/selection-controller.svelte.ts");
const { WorkspaceView } = await import("../src/lib/state/workspace-view.svelte.ts");
const { reviewIdentity } = await import("../src/lib/review-model.ts");

// --- The derivation: exactly one real locale, the base locale. ---
const catalogOf = (defaultLocale, tags, pseudoLocales) => ({
  id: "app", schemaVersion: 1, defaultLocale, layers: [], pseudoLocales,
  locales: tags.map((tag) => ({ tag, fallback: tag === defaultLocale ? undefined : defaultLocale })),
});
assert.equal(projectKind(undefined), "translation", "Without a catalog the editor is a translation editor.");
assert.equal(projectKind(catalogOf("de", ["de"])), "content", "A German-only project is a content-editing project.");
assert.equal(projectKind(catalogOf("de", ["de", "en"])), "translation");
assert.equal(projectKind(catalogOf("de", ["de"], ["en-XA", "ar-XB"])), "content", "Configured pseudo-locales are developer tools, not languages.");
assert.equal(projectKind(catalogOf("en", ["en", "en-XA"], ["en-XA"])), "content", "A pseudo-locale listed among the locales does not count either.");
assert.equal(projectKind(catalogOf("en", ["en", "EN-xa"], ["en-XA"])), "content", "Pseudo-locales match case-insensitively.");
assert.equal(projectKind(catalogOf("en", ["en", "en-XA"])), "translation", "A locale that is not configured as a pseudo-locale is a language.");
assert.equal(projectKind(catalogOf("en", ["de"])), "translation", "The one locale must be the base locale.");
assert.equal(projectKind(catalogOf("en", ["en", "de", "fr"], ["en-XA"])), "translation");
assert.deepEqual(realLocales(catalogOf("de", ["de", "en-XA"], ["en-XA"])).map((locale) => locale.tag), ["de"]);

// --- The content wording: every replacement exists in both interface locales with the original's placeholders. ---
const placeholders = (text) => [...new Set([...text.matchAll(/\{\$(\w+)/g)].map((match) => match[1]))].sort();
for (const locale of ["en", "de"]) {
  const messages = await readUiMessages(locale);
  for (const [translationKey, contentKey] of Object.entries(contentWording)) {
    assert.equal(typeof messages[translationKey], "string", `${locale}: ${translationKey} is not in the editor catalog.`);
    assert.equal(typeof messages[contentKey], "string", `${locale}: the content wording ${contentKey} is missing.`);
    assert.deepEqual(placeholders(messages[contentKey]), placeholders(messages[translationKey]),
      `${locale}: ${contentKey} does not take the arguments of ${translationKey}.`);
  }
  for (const key of ["ui_content_texts", "ui_content_search", "ui_content_no_selection", "ui_content_no_results", "ui_content_no_matching_texts",
    "ui_content_add_text", "ui_content_text_filters", "ui_content_text", "ui_content_language", "ui_content_developer_note",
    "ui_content_only_language", "ui_content_project_summary", "ui_workbench_save_and_next_text", "ui_workbench_none_text"]) {
    assert.equal(typeof messages[key], "string", `${locale}: ${key} is missing.`);
  }
}
const english = await readUiMessages("en");
const german = await readUiMessages("de");
assert.equal(english.ui_composer_translate_message, "Translate the message");
assert.equal(english[contentWording.ui_composer_translate_message], "Edit the text");
assert.equal(german[contentWording.ui_composer_translate_message], "Text bearbeiten");
assert.equal(english.ui_content_texts, "Texts");
assert.equal(german.ui_content_texts, "Texte");
for (const [key, value] of Object.entries(contentWording)) {
  assert.ok(!/translat|source|target/i.test(english[value]), `The content wording of ${key} still speaks of translation: ${english[value]}`);
}
assert.equal(projectTextKey("ui_composer_translate_message", "translation"), "ui_composer_translate_message");
assert.equal(projectTextKey("ui_composer_translate_message", "content"), "ui_content_composer_heading");
assert.equal(projectTextKey("ui_message_heading_code_identifier", "content"), "ui_message_heading_code_identifier",
  "Developer vocabulary (the code identifier) stays.");
const kind = new RuneValue("translation");
const wording = projectUiText({ text: (key, args) => args === undefined ? key : `${key}${JSON.stringify(args)}` }, () => kind.value);
assert.equal(wording.text("ui_composer_translate_message"), "ui_composer_translate_message");
kind.value = "content";
assert.equal(wording.text("ui_composer_translate_message"), "ui_content_composer_heading", "The wording follows the project kind as it changes.");
assert.equal(wording.text("ui_message_tree_group_label", { name: "settings", count: 3 }), 'ui_content_tree_group_label{"name":"settings","count":3}');

// --- The workspace view of a German-only project with a pseudo-locale and a review file kept from earlier. ---
const entries = [
  ["settings_title", ["settings", "title"], "Einstellungen"],
  ["settings_language", ["settings", "language"], "Anzeigesprache"],
  ["notes_empty", ["notes", "empty"], "Dieser Ordner enthält noch keine Notizen."],
  ["notes_count", ["notes", "count"], ".input {$count :integer}\n.match $count\none {{Eine Notiz}}\n* {{{$count} Notizen}}"],
];
const documentOf = (path, locale, rows) => ({
  path, locale, layer: "base", isManifest: false, isMalformed: false, revision: "rev-1",
  content: rows.map(([key, , text]) => `${key} = ${text}\n`).join(""),
  entries: rows.map(([key, segments, text]) => ({ key, path: segments, content: text })),
});
const reviewData = {
  path: "app.review.json",
  revision: "review-1",
  fingerprints: {},
  entries: [
    { key: "settings_title", locale: "de", state: "needs-review", samples: {}, implicit: false },
    { key: "notes_empty", locale: "de", state: "approved", samples: {}, implicit: false, stale: true },
  ],
  terminology: [],
};
const germanOnly = {
  root: "/work",
  catalog: catalogOf("de", ["de"], ["en-XA"]),
  catalogs: [{ id: "app", manifestPaths: ["/work/runic.json"] }],
  documents: [documentOf("de.rmf2", "de", entries)],
  review: reviewData,
  diagnostics: [],
  success: true,
};
const workspace = new RuneValue(germanOnly);
const storage = { get: () => null, set: () => undefined, remove: () => undefined };
const drafts = new DraftStore({ bridge: {}, snapshot: () => workspace.value, storage });
const review = new ReviewStore({ bridge: {}, snapshot: () => workspace.value, errorNotice: (error) => error });
review.install(germanOnly);
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
  localeName: (tag) => `name of ${tag}`, uiDirection: () => "ltr", ui: { text: (key) => key },
});
const saves = [];
const next = new NextMessageController({
  selection, view,
  saveAll: async () => { saves.push(selection.selectedKey); return { kind: "saved" }; },
  busy: () => false,
  localeName: (tag) => `name of ${tag}`,
});
selection.install(germanOnly, true);

assert.equal(view.projectKind, "content");
assert.equal(view.contentProject, true);
assert.equal(selection.selectedLocale, "de");
// No missing, outdated or to-review filter; no translation state on any row; no group counts of them.
assert.deepEqual(view.filterOptions.map((option) => option.value), ["all", "structured", "quality"]);
assert.ok(view.messageListItems.every((item) => !item.missing && !item.stale && !item.needsReview),
  "A text of a content-editing project has no translation state, even with a review file on disk.");
assert.deepEqual(view.messageGroupCounts, []);
// A translation filter kept from the time the project had more languages shows every text.
selection.filter = "needs-review";
assert.equal(view.filter, "all");
assert.equal(view.visibleRows.length, entries.length);
selection.filter = "stale";
assert.equal(view.filter, "all");
selection.filter = "structured";
assert.equal(view.filter, "structured");
assert.deepEqual(view.visibleRows.map((row) => row.key), ["notes_count"]);
selection.filter = "all";

// The review file stays untouched: its states don't apply, and the review actions record nothing.
selection.selectVisible("settings_title", view.visibleRows);
assert.equal(view.currentReview, undefined);
assert.equal(view.currentReviewState, "translated", "Without a review entry, an existing text is in its implicit state.");
assert.equal(view.currentIsStale, false);
view.setCurrentReviewState("approved");
view.setCurrentReviewNote("note");
view.confirmCurrentReview();
view.markVisible("approved");
assert.equal(review.dirty, false, "A content-editing project changes no review data.");
assert.equal(review.index.get(reviewIdentity("settings_title", "de"))?.state, "needs-review", "The review file's own entries are kept as they were.");

// Save and next: always offered, to the next text in tree order (notes before settings; count before empty).
assert.deepEqual(view.treeOrder, ["notes_count", "notes_empty", "settings_language", "settings_title"]);
assert.equal(next.kind, "text");
assert.equal(next.saveAndNextKind, "text");
assert.equal(next.saveAndNextOffered, true, "Every text offers Save and next.");
selection.selectVisible("notes_count", view.visibleRows);
assert.equal(await next.saveAndNext(), true);
assert.deepEqual(saves, ["notes_count"]);
assert.equal(selection.selectedKey, "notes_empty");
assert.equal(await next.saveAndNext(), true);
assert.equal(selection.selectedKey, "settings_language");
selection.selectVisible("settings_title", view.visibleRows);
assert.equal(await next.saveAndNext(), true);
assert.equal(selection.selectedKey, "notes_count", "Save and next wraps to the first text.");
assert.deepEqual(next.notice, { code: "ui_workbench_wrapped_next", args: [] });
// With one text shown there is nowhere to go, and the notice says so without naming a language.
selection.query = "Anzeigesprache";
selection.selectVisible("settings_language", view.visibleRows);
assert.equal(next.step("next"), false);
assert.deepEqual(next.notice, noneNotice("text", "de"));
assert.deepEqual(noneNotice("text", "de"), { code: "ui_workbench_none_text", args: [] });
selection.query = "";

// Alt+↓ and Alt+↑ target Next and Previous, which a content-editing project does not show; Ctrl+Enter stays.
const key = (init) => ({ altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, ...init });
assert.equal(editorShortcut(key({ altKey: true, key: "ArrowDown" }), false, false, { platform: "other" }), "next-message");
assert.equal(editorShortcut(key({ altKey: true, key: "ArrowDown" }), false, false, { platform: "other", messageSteps: false }), undefined);
assert.equal(editorShortcut(key({ altKey: true, key: "ArrowUp" }), false, false, { platform: "other", messageSteps: false }), undefined);
assert.equal(editorShortcut(key({ ctrlKey: true, key: "Enter" }), false, false, { platform: "other", messageSteps: false }), "save-and-next");
assert.equal(editorShortcut(key({ ctrlKey: true, key: "s" }), false, false, { platform: "other", messageSteps: false }), "save");

// --- Adding a second language switches to translation mode, without a restart: the next snapshot decides. ---
const translated = {
  ...germanOnly,
  catalog: catalogOf("de", ["de", "en"], ["en-XA"]),
  documents: [...germanOnly.documents, documentOf("en.rmf2", "en", entries.slice(0, 1).map(([k, segments]) => [k, segments, "Settings"]))],
};
workspace.value = translated;
selection.install(translated, false);
assert.equal(view.projectKind, "translation", "A second language makes it a translation project.");
assert.equal(view.contentProject, false);
assert.deepEqual(view.filterOptions.map((option) => option.value), ["all", "missing", "structured", "needs-review", "stale", "quality"]);
assert.equal(next.kind, "missing");
selection.selectLocale("en");
assert.equal(view.messageListItems.filter((item) => item.missing).length, entries.length - 1, "The new language's missing texts show.");
selection.selectLocale("de");
assert.ok(view.messageListItems.some((item) => item.needsReview), "The kept review file applies again.");
selection.filter = "needs-review";
assert.equal(view.filter, "needs-review", "Translation filters apply again.");
selection.filter = "all";
// And back: removing it again makes it a content-editing project.
workspace.value = germanOnly;
selection.install(germanOnly, false);
assert.equal(view.projectKind, "content");
assert.equal(next.kind, "text");

console.log("PASS: one-language projects are content-editing projects: derived once from the catalog (pseudo-locales don't count), worded for texts in en and de, without translation filters, states, review or Next missing, Save and next to the next text, and switching modes when a language is added or removed.");
