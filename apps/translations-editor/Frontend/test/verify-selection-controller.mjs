// Selection and navigation: where an installed workspace puts the selection (this session's view of the catalog, else
// the selection stored at its last launch), what configureEditor shows, and where an undone step navigates.
import assert from "node:assert/strict";
import { deferred, settle } from "./svelte-modules.mjs";

const { RuneValue } = await import("./rune-value.svelte.ts");
const { DraftStore } = await import("../src/lib/state/draft-store.svelte.ts");
const { EditorFeedback } = await import("../src/lib/state/feedback.svelte.ts");
const { PreviewController } = await import("../src/lib/state/preview-controller.svelte.ts");
const { SelectionController, catalogViewKey, currentManifest } = await import("../src/lib/state/selection-controller.svelte.ts");
const { catalogSelectionStateKey, readCatalogSelection } = await import("../src/lib/catalog-selection.ts");

const documentOf = (path, locale, messages) => ({
  path, locale, layer: "base", isManifest: false, isMalformed: false, revision: "rev-1",
  content: messages.map(([key, text]) => `${key} = ${text}\n`).join(""),
  entries: messages.map(([key, text]) => ({ key, path: [key], content: text })),
});
const defaultMessages = { en: [["farewell", "Bye"], ["greeting", "Hello"]], de: [["greeting", "Hallo"]] };
const workspaceOf = (root = "/work", messages = defaultMessages) => ({
  root,
  catalog: { id: "product", schemaVersion: 1, defaultLocale: "en", locales: Object.keys(messages).map((tag) => ({ tag })), layers: [] },
  catalogs: [{ id: "product", manifestPaths: [`${root}/runic.json`] }],
  documents: Object.entries(messages).map(([locale, list]) => documentOf(`${locale}.rmf2`, locale, list)),
  diagnostics: [],
  success: true,
});

function memoryStorage() {
  const values = new Map();
  return { values, get: (key) => values.get(key) ?? null, set: (key, value) => values.set(key, value), remove: (key) => values.delete(key) };
}

function createSelection(options = {}) {
  const workspace = new RuneValue(undefined);
  const storage = options.storage ?? memoryStorage();
  const opened = [];
  const starters = [];
  const host = {
    openDocument(path) {
      const reply = deferred();
      opened.push({ path, reply });
      return reply.promise;
    },
    previewMessage: async () => ({ success: false, diagnostics: [] }),
  };
  const drafts = new DraftStore({ bridge: {}, snapshot: () => workspace.value, storage });
  const preview = new PreviewController({
    bridge: host, pseudoLocalization: () => false, defaultSample: () => "x", errorNotice: (error) => error,
    setTimer: () => 0, clearTimer: () => undefined,
  });
  const messageDrafts = {
    get: () => undefined,
    isLoading: () => false,
    open: async (target) => { starters.push(target); return options.starter ?? { kind: "applied" }; },
  };
  const diagnostics = { cleared: 0, clear() { this.cleared += 1; } };
  const feedback = new EditorFeedback();
  let blocked = false;
  const selection = new SelectionController({
    bridge: host,
    snapshot: () => workspace.value,
    drafts,
    messageDrafts,
    review: { index: new Map() },
    preview,
    diagnostics,
    feedback,
    savedHasMessage: (key, locale) => workspace.value?.documents.some((document) => document.locale === locale &&
      document.entries.some((entry) => entry.key === key)) === true,
    editingBlocked: () => blocked,
    storage,
  });
  const install = (next, reset = true) => {
    workspace.value = next;
    selection.install(next, reset);
  };
  return { selection, workspace, install, storage, opened, starters, preview, diagnostics, feedback, block: (value) => { blocked = value; } };
}

// A new workspace without a remembered selection: its first message in the default locale, shown and previewed.
{
  const { selection, install, opened, preview } = createSelection();
  install(workspaceOf());
  assert.equal(selection.selectedKey, "farewell");
  assert.equal(selection.selectedLocale, "en");
  assert.equal(selection.selectedDocumentPath, "en.rmf2");
  assert.equal(selection.mode, "translation");
  assert.equal(selection.editorText, "Bye");
  assert.equal(selection.selectedRow?.key, "farewell");
  assert.deepEqual(selection.rows.map((row) => row.key), ["farewell", "greeting"]);
  assert.deepEqual(opened.map((request) => request.path), ["en.rmf2"], "The selected message's document is opened.");
  assert.equal(preview.busy, true, "The selected message is previewed.");
  assert.equal(currentManifest(workspaceOf()), "/work/runic.json");
  assert.equal(catalogViewKey(workspaceOf()), "/work\n/work/runic.json");
}

// A missing translation starts empty, its starter is opened, and a failed starter is reported for that selection.
const storage = memoryStorage();
{
  const { selection, install, starters, feedback, diagnostics } = createSelection({ storage, starter: { kind: "failed", error: new Error("no host") } });
  install(workspaceOf());
  feedback.operationMessage = { code: "app_saved", args: [] };
  selection.selectLocale("de");
  assert.equal(diagnostics.cleared, 1, "Selecting a locale clears the diagnostics of the previous selection.");
  assert.equal(feedback.operationMessage, undefined, "Selecting a locale clears the previous operation's notice.");
  assert.equal(selection.editorText, "", "A missing translation starts empty, never with the source text.");
  assert.deepEqual(selection.currentStarterTarget, { path: "de.rmf2", saved: "greeting = Hallo\n", key: "farewell", locale: "de" });
  assert.deepEqual(starters, [{ path: "de.rmf2", key: "farewell", locale: "de" }]);
  await settle();
  assert.equal(feedback.clientError?.code, "ui_backend_external_error");
  assert.equal(feedback.clientError?.detail, "no host");

  // The selection is stored for the next launch, once per change.
  selection.persistSelection();
  assert.deepEqual(readCatalogSelection(storage.get(catalogSelectionStateKey), catalogViewKey(workspaceOf())), { key: "farewell", locale: "de" });
}

// The next launch restores the stored selection.
{
  const { selection, install } = createSelection({ storage });
  install(workspaceOf());
  assert.equal(selection.selectedKey, "farewell");
  assert.equal(selection.selectedLocale, "de");
}

// This session's view of a catalog (selection, filter, search) is restored when the user switches back to it.
{
  const { selection, install } = createSelection();
  install(workspaceOf());
  selection.selectRow(selection.rows.find((row) => row.key === "greeting"));
  selection.selectLocale("de");
  selection.filter = "missing";
  selection.query = "gree";
  selection.rememberCatalogView();
  install(workspaceOf("/other"));
  assert.equal(selection.selectedKey, "farewell", "Another catalog starts at its first message.");
  assert.equal(selection.selectedLocale, "en");
  selection.clearFilter();
  install(workspaceOf());
  assert.equal(selection.selectedKey, "greeting");
  assert.equal(selection.selectedLocale, "de");
  assert.equal(selection.filter, "missing");
  assert.equal(selection.query, "gree");

  // A reload that keeps the selection keeps it while its message and locale exist.
  install(workspaceOf(), false);
  assert.equal(selection.selectedKey, "greeting");
  install(workspaceOf("/work", { en: [["farewell", "Bye"]], de: [] }), false);
  assert.equal(selection.selectedKey, "farewell", "A removed message gives way to the first one.");
  assert.equal(selection.selectedLocale, "de");
}

// The file view shows the document; the opened document replaces it only while it is still the selected one.
{
  const { selection, install, opened, block } = createSelection();
  install(workspaceOf());
  block(true);
  selection.chooseMode("raw");
  assert.equal(selection.mode, "translation", "A blocked editor keeps its mode.");
  block(false);
  selection.chooseMode("raw");
  assert.equal(selection.mode, "raw");
  assert.equal(selection.editorText, "farewell = Bye\ngreeting = Hello\n");
  opened.at(-1).reply.resolve({ path: "en.rmf2", content: "farewell = Bye!\ngreeting = Hello\n", fileRevision: "rev-1" });
  await settle();
  assert.equal(selection.editorText, "farewell = Bye!\ngreeting = Hello\n");
  assert.equal(selection.currentDocument?.content, "farewell = Bye!\ngreeting = Hello\n");

  selection.chooseMode("raw");
  selection.selectLocale("de");
  opened.at(-2).reply.resolve({ path: "en.rmf2", content: "stale", fileRevision: "rev-1" });
  await settle();
  assert.notEqual(selection.editorText, "stale", "A document opened for an earlier selection is ignored.");
  opened.at(-1).reply.resolve({ path: "de.rmf2", content: "other revision", fileRevision: "rev-2" });
  await settle();
  assert.equal(selection.selectedDocumentViewState, undefined, "A document opened at another revision is ignored.");
}

// An undone step shows its message and locale; a step of the whole file shows the file view.
{
  const { selection, install } = createSelection();
  install(workspaceOf());
  selection.showStep({ path: "de.rmf2", key: "greeting" });
  assert.equal(selection.selectedLocale, "de");
  assert.equal(selection.selectedKey, "greeting");
  assert.equal(selection.mode, "translation");
  selection.showStep({ path: "en.rmf2" });
  assert.equal(selection.selectedLocale, "en");
  assert.equal(selection.mode, "raw");
  assert.equal(selection.editorText, "farewell = Bye\ngreeting = Hello\n");

  // A diagnostic's span selects again each time.
  selection.revealSource(2, 5);
  const first = selection.sourceSelection;
  selection.revealSource(2, 5);
  assert.deepEqual([first.start, first.end], [2, 5]);
  assert.ok(selection.sourceSelection.token > first.token);
  selection.configureEditor();
  assert.equal(selection.sourceSelection, undefined, "A new selection forgets the span.");
}

console.log("PASS: selection and navigation place an installed workspace's selection (session view, stored selection, first message), open and show the selected message and its starter, ignore stale documents, and follow undone steps.");
