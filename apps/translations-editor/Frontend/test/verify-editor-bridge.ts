// Document routes after a workspace command. Since Runic SDK 0.11 (read-your-writes, runic-sdk #585) a command's reply
// reaches the page after the state frames it produced on every View, so the bridge maps a result's documents onto the
// root snapshot's references at once instead of waiting for a later root frame.
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createMockBridge, installMockBridge } from "@runic-artifex/views/mock";
import { mockEditor, type EditorMock } from "../src/generated/editor.mock.ts";
import { mockEditorDocument } from "../src/generated/editorDocument.mock.ts";
import { mockEditorWorkspace } from "../src/generated/editorWorkspace.mock.ts";

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
    undoResultJson: "null", redoResultJson: "null",
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
root = mockEditor(bridge, {
  state: {
    workspace: workspace.reference,
    documentTools: unused("editorDocumentTools"),
    review: unused("editorReview"),
    interchange: unused("editorInterchange"),
    diagnostics: unused("editorDiagnostics"),
    localState: unused("editorLocalState"),
    project: unused("editorProject"),
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

console.log("Editor bridge read-your-writes checks passed.");
