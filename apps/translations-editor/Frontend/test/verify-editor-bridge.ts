// Document routes after a workspace command. Since Runic SDK 0.11 (read-your-writes, runic-sdk #585) a command's reply
// reaches the page after the state frames it produced on every View, so the bridge maps a result's documents onto the
// root snapshot's references at once instead of waiting for a later root frame.
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createMockBridge, installMockBridge } from "@runic-artifex/views/mock";
import { mockEditor, type EditorMock } from "../src/generated/editor.mock.ts";
import { mockEditorDocument } from "../src/generated/editorDocument.mock.ts";
import { mockEditorWorkspace } from "../src/generated/editorWorkspace.mock.ts";
import { mockEditorDocumentTools } from "../src/generated/editorDocumentTools.mock.ts";
import type { EditorDocumentDraft, EditorMessageDraft, EditorMessageEntry, EditorMessagePreview, WorkspaceSnapshot } from "../src/lib/contracts.ts";
import { mockExecute as executeMock } from "../src/lib/mock-bridge.ts";

const source = await readFile(new URL("../src/lib/editor-bridge.ts", import.meta.url), "utf8");
assert.doesNotMatch(source, /setTimeout|\.subscribe\(/,
  "The editor bridge must rely on read-your-writes instead of waiting for root state.");

const bridge = installMockBridge(createMockBridge());
const paths = ["de.json", "en.json"];
const documents = paths.map((path, index) => mockEditorDocument(bridge, {
  id: `document${index}`,
  state: { path, content: "{}", fileRevision: "r1", isMalformed: false },
}));
const unused = (kind: string) => ({ kind, id: `unused-${kind}` }) as never;

let root!: EditorMock;
let publishWithReply = true;
const workspace = mockEditorWorkspace(bridge, {
  id: "workspace",
  state: {
    loadResultJson: "null", checkExternalChangesResultJson: "null", pickWorkspaceResultJson: "null",
    previewMutationResultJson: "null", applyMutationResultJson: "null", recoverTransactionResultJson: "null",
    undoResultJson: "null", redoResultJson: "null", listLocalesResultJson: "null", describeLocaleResultJson: "null",
  },
  commands: {
    load: (_state, input) => {
      const { requestId } = JSON.parse(input) as { requestId: string };
      // As EditorFeatureViewModel does: the root's documents change in a model turn before the command replies.
      if (publishWithReply) root.update({ documents: documents.map(document => document.reference) });
      return {
        loadResultJson: JSON.stringify({
          requestId,
          result: { documents: paths.map(path => ({ path })) },
        }),
      };
    },
  },
});
// The draft commands of a missing translation (issue #225, P2) route to DocumentTools with their typed arguments; the
// mock answers them as the mock bridge does.
const draftRequests: Array<{ command: string; argument: unknown }> = [];
const documentTools = mockEditorDocumentTools(bridge, {
  id: "documentTools",
  state: {
    applyAuthoringOperationResultJson: "null", applyDiagnosticFixResultJson: "null", transformDocumentResultJson: "null",
    previewMessageResultJson: "null", createMessageDraftResultJson: "null", applyMessageDraftOperationResultJson: "null",
  },
  commands: {
    createMessageDraft: async (_state, input) => {
      const { requestId, argument } = JSON.parse(input) as { requestId: string; argument: unknown };
      draftRequests.push({ command: "createMessageDraft", argument });
      return { createMessageDraftResultJson: JSON.stringify({ requestId, result: await executeMock("CreateMessageDraft", argument) }) };
    },
    applyMessageDraftOperation: async (_state, input) => {
      const { requestId, argument } = JSON.parse(input) as { requestId: string; argument: unknown };
      draftRequests.push({ command: "applyMessageDraftOperation", argument });
      return { applyMessageDraftOperationResultJson: JSON.stringify({ requestId, result: await executeMock("ApplyMessageDraftOperation", argument) }) };
    },
  },
});
root = mockEditor(bridge, {
  state: {
    workspace: workspace.reference,
    documentTools: documentTools.reference,
    review: unused("editorReview"),
    interchange: unused("editorInterchange"),
    diagnostics: unused("editorDiagnostics"),
    localState: unused("editorLocalState"),
    project: unused("editorProject"),
    libraries: unused("editorLibraries"),
    documents: [],
  },
});

const { createEditorBridge } = await import("../src/lib/editor-bridge.ts");
const editor = createEditorBridge();

// Resolves within a bound shorter than the former 5 s wait, so a reintroduced wait fails here.
async function promptly<T>(promise: Promise<T>, what: string): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const late = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new Error(`${what} waited for state.`)), 1000);
  });
  try {
    return await Promise.race([promise, late]);
  } finally {
    clearTimeout(timer);
  }
}

// The reply arrives after the root's documents frame: every path resolves to its document route at once.
await promptly(editor.load(), "load");
for (const path of paths) {
  const state = await promptly(editor.openDocument(path), `openDocument(${path})`);
  assert.equal(state?.path, path, `openDocument(${path}) must open the document's own route.`);
}

// A reply that the host sent without its frames (the 5 s bound expired) leaves the paths unmapped without waiting;
// once the root frame arrives, the next call maps them.
publishWithReply = false;
root.update({ documents: [] });
await promptly(editor.load(), "load without frames");
assert.equal(await promptly(editor.openDocument("de.json"), "openDocument without frames"), undefined,
  "A document whose route the root does not list yet must not resolve.");
root.update({ documents: documents.map(document => document.reference) });
await bridge.flush();
assert.equal((await promptly(editor.openDocument("en.json"), "openDocument after frames"))?.path, "en.json",
  "The root's later frame must map the remembered documents.");

// Message identity (issue #225, P9): an entry and a per-message diagnostic carry the logical path besides the canonical key.
const { mockExecute } = await import("../src/lib/mock-bridge.ts");
const loadedSnapshot = await mockExecute("LoadWorkspace", {}) as WorkspaceSnapshot;
const stale = loadedSnapshot.diagnostics.find(diagnostic => diagnostic.id === "RTR0090");
assert.deepEqual(stale?.messagePath, ["common_save"], "A per-message diagnostic must carry its message path.");
// The recorded base text of a stale review state (issue #225, P20): by key, then by the entry's source fingerprint.
const staleEntry = loadedSnapshot.review?.entries.find(item => item.stale === true);
assert.equal(staleEntry?.sourceFingerprint, "sha256:outdated");
assert.equal(loadedSnapshot.review?.recordedSources?.[staleEntry!.key]?.[staleEntry!.sourceFingerprint!], "Save all changes",
  "A stale entry's recorded base text must be addressed by its key and source fingerprint.");
const transformed = await mockExecute("TransformDocument", { path: "de/common_save.mf2", content: "Speichern\n" }) as EditorDocumentDraft;
assert.deepEqual(transformed.entries.map(entry => [entry.key, entry.path]), [["common_save", ["common_save"]]],
  "A message entry must carry its logical path.");
const entry: EditorMessageEntry = { key: "common_cancel", path: ["common", "cancel"], content: "Cancel", valueStartByte: 0, valueLengthBytes: 6 };
assert.equal(entry.path.join("."), "common.cancel");

// A missing translation's draft (issue #225, P2): the base's structure with the target locale's categories, empty,
// supported by the composer; an operation changes only the draft text, and a stale revision is refused.
const draft = await promptly(editor.createMessageDraft("files_selected", "fr"), "createMessageDraft");
assert.deepEqual(draftRequests.at(-1), { command: "createMessageDraft", argument: { key: "files_selected", locale: "fr" } });
assert.equal(draft.success, true);
assert.equal(draft.text, ".input {$count :integer select=plural}\n.match $count\none {{}}\nmany {{}}\n* {{}}",
  "A plural draft must take the target locale's categories, with empty patterns.");
assert.deepEqual(draft.projection?.variants.map(variant => variant.keys), [["one"], ["many"], ["*"]]);
assert.equal(draft.projection?.supported, true);
assert.equal(draft.projection?.empty, true, "An untouched draft must be empty.");
const operation = { kind: "set-pattern", variantId: "1", pattern: "{$count} de fichiers" } as const;
const typed: EditorMessageDraft = await promptly(editor.applyMessageDraftOperation("files_selected", "fr", draft.text, draft.projection!.revision, operation), "applyMessageDraftOperation");
assert.deepEqual(draftRequests.at(-1), { command: "applyMessageDraftOperation",
  argument: { key: "files_selected", locale: "fr", text: draft.text, expectedRevision: draft.projection!.revision, operation } });
assert.equal(typed.success, true);
assert.match(typed.text, /^many \{\{\{\$count\} de fichiers\}\}$/m);
assert.equal(typed.projection?.empty, false, "A draft with text must not be empty.");
const staleDraft = await mockExecute("ApplyMessageDraftOperation", { key: "files_selected", locale: "fr", text: typed.text, expectedRevision: draft.projection!.revision, operation }) as EditorMessageDraft;
assert.equal(staleDraft.success, false);
assert.equal(staleDraft.text, typed.text, "A refused operation must keep the draft text.");
const plainDraft = await mockExecute("CreateMessageDraft", { key: "common_save", locale: "en" }) as EditorMessageDraft;
assert.equal(plainDraft.text, "", "A plain message's draft must be the empty simple pattern.");
assert.equal(plainDraft.projection?.empty, true);
const missingDraft = await mockExecute("CreateMessageDraft", { key: "missing_key", locale: "en" }) as EditorMessageDraft;
assert.equal(missingDraft.success, false);
assert.equal(missingDraft.diagnostics[0]?.notice?.code, "ui_backend_preview_key_missing");

// The preview result carries the previewed message's primary diagnostic besides the catalog's (issue #225, P5).
const preview = await mockExecute("PreviewMessage", { path: "de/common_save.mf2", content: "Speichern\n", locale: "de", key: "common_save" }) as EditorMessagePreview;
assert.equal(preview.primaryDiagnostic, undefined, "A preview without a diagnostic of its message has no primary diagnostic.");

// The close question (issue #225, P19): the page's handler answers the host's question on the root client, and stops
// answering when the page unmounts; Close window runs the root's command, whose failure (the window closed) is the end.
let closeQuestions = 0;
let closeAnswer = false;
const stopCloseQuestion = editor.handleCloseQuestion(() => { closeQuestions++; return closeAnswer; });
await bridge.flush();
assert.equal(root.interactions.confirmClose.handled, true, "The page did not register its close question handler.");
assert.deepEqual(await root.interactions.confirmClose.request({ answerWithinMilliseconds: 30_000 }), { kind: "answered", output: false });
closeAnswer = true;
assert.deepEqual(await root.interactions.confirmClose.request({ answerWithinMilliseconds: 30_000 }), { kind: "answered", output: true });
assert.equal(closeQuestions, 2);
stopCloseQuestion();
await bridge.flush();
assert.equal(root.interactions.confirmClose.handled, false, "The close question handler outlived the page.");
await promptly(editor.closeWindow(), "closeWindow");

console.log("Editor bridge read-your-writes, message identity, message draft and close question checks passed.");
