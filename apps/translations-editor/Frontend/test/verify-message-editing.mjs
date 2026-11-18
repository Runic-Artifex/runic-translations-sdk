// The edits of the selected message: file-view typing and the composer's text reach the right draft (a missing
// translation's starter or the document), formatting is a step of its own, quick fixes reach the repair dialog of a
// malformed file, and a diagnostic navigates to its message and span.
import assert from "node:assert/strict";
import { settle } from "./svelte-modules.mjs";

const { EditorFeedback } = await import("../src/lib/state/feedback.svelte.ts");
const { MessageEditingController } = await import("../src/lib/state/message-editing.svelte.ts");

const document = { path: "de.rmf2", locale: "de", content: "greeting = Hallo\n", revision: "rev-1", isMalformed: false };
const sha256 = async (text) => Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text))),
  (byte) => byte.toString(16).padStart(2, "0")).join("");

function createEditing(options = {}) {
  const calls = [];
  let blocked = false;
  const selection = {
    mode: options.mode ?? "translation",
    editorText: "",
    selectedKey: "greeting",
    selectedLocale: "de",
    selectedDocumentPath: "de.rmf2",
    selectedDocumentViewState: undefined,
    currentDocument: document,
    currentStarterTarget: options.starter ? { path: "de.rmf2", saved: document.content, key: "greeting", locale: "de" } : undefined,
    currentStarter: options.starter ? { text: "" } : undefined,
    sourceAuthoring: { supported: true, variants: [] },
    rows: [{ key: "greeting" }, { key: "farewell" }],
    revealSource(start, end) { calls.push(`reveal ${start}-${end}`); },
  };
  const drafts = {
    workspaceGeneration: 1,
    history: { seal: () => calls.push("seal") },
    capture: (path) => { calls.push(`capture ${path}`); return "before"; },
    set: (path, value) => calls.push(`set ${path} ${JSON.stringify(value)}`),
    persist: () => calls.push("persist"),
    recordStep: (path, info) => calls.push(`step ${path} ${info.kind}`),
    validate: (path, value, done) => { calls.push(`validate ${path}`); done({ kind: "applied", result: { success: true, diagnostics: [] } }); },
    transform: async (path, saved, key, value, done) => {
      calls.push(`transform ${path} ${key} ${value}`);
      options.beforeDone?.(selection);
      done(options.transform ?? { kind: "applied", result: { content: `greeting = ${value}\n`, entries: [], diagnostics: [] } });
    },
    applyOperation: async () => undefined,
    applyFix: async (path, saved, fix, repair, done) => {
      calls.push(`fix ${path} ${repair === undefined ? "draft" : "repair"}`);
      done({ kind: "applied", result: { success: true, content: "fixed", entries: [{ key: "greeting", content: "Fixed" }], diagnostics: [] } });
    },
    content: (path, saved) => saved,
  };
  const messageDrafts = {
    retain: () => calls.push("starters.retain"),
    edit: (target, value) => calls.push(`starter.edit ${value}`),
    copySource: async () => calls.push("starter.copy"),
  };
  const repair = { document: undefined, text: "", input: null, message: undefined, validation: undefined, begin: (doc) => calls.push(`repair ${doc.path}`) };
  const feedback = new EditorFeedback();
  const editing = new MessageEditingController({
    bridge: { transformDocument: options.transformDocument ?? (async () => { throw new Error("not expected"); }) },
    snapshot: () => ({ documents: [document, { ...document, path: "en.rmf2", locale: "en", content: "{ bad" , isMalformed: true }] }),
    selection,
    drafts,
    messageDrafts,
    diagnostics: { record: (source, path) => calls.push(`record ${source} ${path}`) },
    preview: { schedule: (path, content, key, locale) => calls.push(`preview ${path} ${key} ${locale}`) },
    repair,
    feedback,
    diagnosticSources: () => ({ documents: [document], drafts: {} }),
    editingBlocked: () => blocked,
  });
  return { editing, calls, selection, repair, feedback, block: (value) => { blocked = value; } };
}

// Typing in the file view is the document's draft: one step, validated; the file now decides its starters.
{
  const { editing, calls, selection, block } = createEditing({ mode: "raw" });
  block(true);
  editing.edit("greeting = Hi\n");
  assert.deepEqual(calls, [], "A blocked editor takes no edit.");
  block(false);
  editing.edit("greeting = Hi\n");
  assert.equal(selection.editorText, "greeting = Hi\n");
  assert.deepEqual(calls, [
    "capture de.rmf2", 'set de.rmf2 "greeting = Hi\\n"', "persist", "starters.retain", "step de.rmf2 file-edit", "validate de.rmf2", "record validation de.rmf2",
  ]);

  // Formatting the file view (line endings, one final newline) is a step of its own.
  calls.length = 0;
  selection.editorText = "greeting = Hi\r\n\n\n";
  editing.formatRaw();
  assert.equal(calls[0], "seal");
  assert.equal(calls.at(-1), "seal");
  assert.equal(selection.editorText, "greeting = Hi\n");
}
{
  const { editing, calls } = createEditing();
  editing.formatRaw();
  assert.deepEqual(calls, [], "Only the file view is formatted.");
}

// The composer's text goes to the starter of a missing translation, else into the document, and previews the message.
{
  const { editing, calls } = createEditing({ starter: true });
  editing.editResourceValue("Hi");
  assert.deepEqual(calls, ["starter.edit Hi"]);
  editing.copySource();
  assert.deepEqual(calls, ["starter.edit Hi", "starter.copy"]);
}
{
  const { editing, calls, selection } = createEditing();
  editing.editResourceValue("Hi");
  await settle();
  assert.deepEqual(calls, ["transform de.rmf2 greeting Hi", "record transform de.rmf2", "preview de.rmf2 greeting de"]);
  editing.applySuggestion("Servus");
  assert.equal(selection.mode, "translation");
  assert.equal(selection.editorText, "Servus");

}
// The preview follows only the message that is still selected when the draft changed.
{
  const { editing, calls } = createEditing({ beforeDone: (selection) => { selection.selectedKey = "farewell"; } });
  editing.editResourceValue("Hi");
  await settle();
  assert.deepEqual(calls, ["transform de.rmf2 greeting Hi", "record transform de.rmf2"]);
}
{
  const rejected = { kind: "rejected", result: { diagnostics: [{ message: "invalid MF2" }] } };
  const { editing, feedback } = createEditing({ transform: rejected });
  editing.editResourceValue("{");
  await settle();
  assert.equal(feedback.clientError, "invalid MF2");
}

// A quick fix changes the draft and the shown text; for a file in repair it changes the repair text.
{
  const { editing, calls, selection } = createEditing();
  await editing.applyDiagnosticFix({ path: "de.rmf2", key: "greeting" }, { title: "Fix" });
  assert.deepEqual(calls, ["fix de.rmf2 draft", "record quick-fix de.rmf2", "preview de.rmf2 greeting de"]);
  assert.equal(selection.editorText, "Fixed");
}
{
  const { editing, calls, repair } = createEditing();
  repair.document = document;
  await editing.applyDiagnosticFix({ path: "de.rmf2" }, { title: "Fix" });
  assert.deepEqual(calls, ["fix de.rmf2 repair"]);
  assert.equal(repair.text, "fixed");
}

// A diagnostic of the current text selects its message and locale and its span in the file view.
{
  const { editing, calls, selection } = createEditing();
  selection.selectedLocale = "en";
  const revision = await sha256(document.content);
  await editing.selectDiagnostic({ path: "de.rmf2", id: "RTR0001", message: "x", key: "greeting", locale: "de", sourceRevision: revision, span: { startUtf16: 11, lengthUtf16: 5 } });
  assert.equal(selection.selectedLocale, "de");
  assert.equal(selection.mode, "raw");
  assert.equal(selection.editorText, document.content);
  assert.deepEqual(calls, ["reveal 11-16"]);
}
// A diagnostic of older text is located in the current text first; one that is gone does not navigate.
{
  const { editing, calls, selection } = createEditing({
    transformDocument: async () => ({ diagnostics: [], content: document.content, entries: [] }),
  });
  await editing.selectDiagnostic({ path: "de.rmf2", id: "RTR0001", message: "x", sourceRevision: "old", span: { startUtf16: 0, lengthUtf16: 1 } });
  assert.deepEqual(calls, []);
  assert.equal(selection.mode, "translation");
}

console.log("PASS: message editing routes file-view and composer text to the starter or the document draft, previews only the selected message, seals formatting, applies quick fixes to drafts or the repair, and navigates to a diagnostic's span.");
