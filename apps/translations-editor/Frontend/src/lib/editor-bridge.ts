import { mockExecute } from "./mock-bridge";
import { BridgeError, connectEditor, type EditorView } from "../generated/editor";
import type { EditorDocumentPageReference, EditorDocumentState } from "../generated/editorDocument";
import type {
  EditorAbout,
  EditorDiagnosticBundleActionResult,
  EditorDiagnosticBundleResult,
  EditorDocument,
  EditorDocumentDraft,
  EditorLocalStateClearResult,
  EditorLocalStateEntry,
  EditorLocalStateSnapshot,
  EditorExternalChanges,
  EditorReviewFileResult,
  EditorReviewImportPreview,
  EditorMessagePreview,
  EditorMutationPreview,
  EditorMutationRequest,
  EditorOperationResult,
  EditorOpenWorkspaceRequest,
  EditorProjectCreationRequest,
  EditorProjectPlan,
  EditorReviewOperationResult,
  EditorReviewSaveRequest,
  EditorXliffExportResult,
  EditorXliffImportPreview,
  EditorWorkspacePickerResult,
  ValidationResult,
  WorkspaceSnapshot,
} from "./contracts";

let connection: Promise<EditorView> | undefined;
let tail: Promise<void> = Promise.resolve();
const documentRefs = new Map<string, EditorDocumentPageReference>();

function rememberDocuments(result: unknown, view: EditorView): void {
  if (typeof result !== "object" || result === null) return;
  const value = result as { documents?: EditorDocument[]; snapshot?: WorkspaceSnapshot };
  const documents = value.documents ?? value.snapshot?.documents;
  if (!documents || documents.length !== view.snapshot.documents.length) return;
  documentRefs.clear();
  documents.forEach((document, index) => documentRefs.set(document.path, view.snapshot.documents[index]));
}

async function rootView(): Promise<EditorView> {
  return await (connection ??= connectEditor().catch(error => {
    connection = undefined;
    throw error;
  }));
}

async function serialized<T>(operation: () => Promise<T>): Promise<T> {
  const previous = tail;
  let release!: () => void;
  tail = new Promise<void>(resolve => { release = resolve; });
  await previous;
  try {
    return await operation();
  } finally {
    release();
  }
}

function disconnectOnFailure(error: unknown, root?: EditorView): void {
  if (error instanceof BridgeError && error.kind === "disconnected") {
    root?.dispose();
    connection = undefined;
    documentRefs.clear();
  }
}

interface RoutedReference<View> { connect(): Promise<View> }
interface DisposableRoute { dispose(): void }

async function invokeRouted<View extends DisposableRoute, State>(
  operation: string,
  select: (root: EditorView) => RoutedReference<View>,
  execute: (view: View, request: string) => Promise<State>,
  resultJson: (state: State) => string,
  argument: unknown,
): Promise<unknown> {
  if (import.meta.env.MODE === "mock") return await mockExecute(operation, argument);
  return serialized(async () => {
    let root: EditorView | undefined;
    let route: View | undefined;
    try {
      root = await rootView();
      route = await select(root).connect();
      const requestId = globalThis.crypto.randomUUID();
      const state = await execute(route, JSON.stringify({ requestId, argument }));
      const envelope = JSON.parse(resultJson(state)) as { requestId: string; result: unknown };
      if (envelope.requestId !== requestId) throw new Error("The editor response did not match its request.");
      rememberDocuments(envelope.result, root);
      return envelope.result;
    } catch (error) {
      disconnectOnFailure(error, root);
      throw error;
    } finally {
      route?.dispose();
    }
  });
}

async function invokeDocument<T>(path: string, command: "validate" | "save", request: object): Promise<T> {
  if (import.meta.env.MODE === "mock")
    return await mockExecute(command === "validate" ? "ValidateDocument" : "SaveDocument", { path, ...request }) as T;
  return serialized(async () => {
    const reference = documentRefs.get(path);
    if (!reference) throw new Error("The selected document route is unavailable. Reload the workspace.");
    let root: EditorView | undefined;
    let view: Awaited<ReturnType<EditorDocumentPageReference["connect"]>> | undefined;
    try {
      root = await rootView();
      view = await reference.connect();
      if (view.snapshot.path !== path) throw new Error("The selected document route changed.");
      const requestId = globalThis.crypto.randomUUID();
      const state = await view[command](JSON.stringify({ requestId, ...request }));
      const envelope = JSON.parse(command === "validate" ? state.validationResultJson : state.saveResultJson) as { requestId: string; result: T };
      if (envelope.requestId !== requestId) throw new Error("The document response did not match its request.");
      if (command === "save") rememberDocuments(envelope.result, root);
      return envelope.result;
    } catch (error) {
      disconnectOnFailure(error, root);
      throw error;
    } finally {
      view?.dispose();
    }
  });
}

export interface EditorBridge {
  load(): Promise<WorkspaceSnapshot>;
  checkExternalChanges(): Promise<EditorExternalChanges>;
  pickWorkspace(): Promise<EditorWorkspacePickerResult>;
  previewMutation(request: EditorMutationRequest): Promise<EditorMutationPreview>;
  applyMutation(request: EditorMutationRequest): Promise<EditorOperationResult>;
  recoverTransaction(mode: "complete" | "rollback"): Promise<EditorOperationResult>;
  undo(): Promise<EditorOperationResult>;
  redo(): Promise<EditorOperationResult>;
  transformDocument(path: string, content: string, key?: string, value?: string): Promise<EditorDocumentDraft>;
  previewMessage(path: string, content: string, locale: string, key: string, samplesJson?: string): Promise<EditorMessagePreview>;
  saveReview(request: EditorReviewSaveRequest): Promise<EditorReviewOperationResult>;
  about(): Promise<EditorAbout>;
  createDiagnosticBundle(): Promise<EditorDiagnosticBundleResult>;
  revealDiagnosticBundle(path: string): Promise<EditorDiagnosticBundleActionResult>;
  deleteDiagnosticBundle(path: string): Promise<EditorDiagnosticBundleActionResult>;
  loadLocalState(): Promise<EditorLocalStateSnapshot>;
  saveLocalState(entries: EditorLocalStateEntry[]): Promise<EditorLocalStateSnapshot>;
  clearLocalState(): Promise<EditorLocalStateClearResult>;
  previewProject(request: EditorProjectCreationRequest): Promise<EditorProjectPlan>;
  createProject(request: EditorProjectCreationRequest): Promise<EditorOperationResult>;
  openWorkspace(request: EditorOpenWorkspaceRequest): Promise<EditorOperationResult>;
  exportXliff(directory?: string): Promise<EditorXliffExportResult>;
  previewXliffImport(path: string): Promise<EditorXliffImportPreview>;
  applyXliffImport(confirmationToken: string): Promise<EditorOperationResult>;
  exportReviewJson(path?: string): Promise<EditorReviewFileResult>;
  previewReviewJsonImport(path: string): Promise<EditorReviewImportPreview>;
  applyReviewJsonImport(confirmationToken: string): Promise<EditorReviewOperationResult>;
  openDocument(path: string): Promise<EditorDocumentState | undefined>;
  validate(path: string, content: string): Promise<ValidationResult>;
  save(path: string, content: string, revision: string): Promise<EditorOperationResult>;
}

export function createEditorBridge(): EditorBridge {
  return {
    load: () => invokeRouted("LoadWorkspace", root => root.snapshot.workspace, (view, request) => view.load(request), state => state.loadResultJson, {}) as Promise<WorkspaceSnapshot>,
    checkExternalChanges: () => invokeRouted("CheckExternalChanges", root => root.snapshot.workspace, (view, request) => view.checkExternalChanges(request), state => state.checkExternalChangesResultJson, {}) as Promise<EditorExternalChanges>,
    pickWorkspace: () => invokeRouted("PickWorkspace", root => root.snapshot.workspace, (view, request) => view.pickWorkspace(request), state => state.pickWorkspaceResultJson, {}) as Promise<EditorWorkspacePickerResult>,
    previewMutation: (request) => invokeRouted("PreviewMutation", root => root.snapshot.workspace, (view, request) => view.previewMutation(request), state => state.previewMutationResultJson, request) as Promise<EditorMutationPreview>,
    applyMutation: (request) => invokeRouted("ApplyMutation", root => root.snapshot.workspace, (view, request) => view.applyMutation(request), state => state.applyMutationResultJson, request) as Promise<EditorOperationResult>,
    recoverTransaction: (mode) => invokeRouted("RecoverTransaction", root => root.snapshot.workspace, (view, request) => view.recoverTransaction(request), state => state.recoverTransactionResultJson, { mode }) as Promise<EditorOperationResult>,
    undo: () => invokeRouted("Undo", root => root.snapshot.workspace, (view, request) => view.undo(request), state => state.undoResultJson, {}) as Promise<EditorOperationResult>,
    redo: () => invokeRouted("Redo", root => root.snapshot.workspace, (view, request) => view.redo(request), state => state.redoResultJson, {}) as Promise<EditorOperationResult>,
    transformDocument: (path, content, key, value) => invokeRouted("TransformDocument", root => root.snapshot.documentTools, (view, request) => view.transformDocument(request), state => state.transformDocumentResultJson, { path, content, key, value }) as Promise<EditorDocumentDraft>,
    previewMessage: (path, content, locale, key, samplesJson) => invokeRouted("PreviewMessage", root => root.snapshot.documentTools, (view, request) => view.previewMessage(request), state => state.previewMessageResultJson, { path, content, locale, key, samplesJson }) as Promise<EditorMessagePreview>,
    saveReview: (request) => invokeRouted("SaveReview", root => root.snapshot.review, (view, request) => view.saveReview(request), state => state.saveReviewResultJson, request) as Promise<EditorReviewOperationResult>,
    about: () => invokeRouted("About", root => root.snapshot.diagnostics, (view, request) => view.about(request), state => state.aboutResultJson, {}) as Promise<EditorAbout>,
    createDiagnosticBundle: () => invokeRouted("CreateDiagnosticBundle", root => root.snapshot.diagnostics, (view, request) => view.createDiagnosticBundle(request), state => state.createDiagnosticBundleResultJson, {}) as Promise<EditorDiagnosticBundleResult>,
    revealDiagnosticBundle: (path) => invokeRouted("RevealDiagnosticBundle", root => root.snapshot.diagnostics, (view, request) => view.revealDiagnosticBundle(request), state => state.revealDiagnosticBundleResultJson, { path }) as Promise<EditorDiagnosticBundleActionResult>,
    deleteDiagnosticBundle: (path) => invokeRouted("DeleteDiagnosticBundle", root => root.snapshot.diagnostics, (view, request) => view.deleteDiagnosticBundle(request), state => state.deleteDiagnosticBundleResultJson, { path }) as Promise<EditorDiagnosticBundleActionResult>,
    loadLocalState: () => invokeRouted("LoadLocalState", root => root.snapshot.localState, (view, request) => view.loadLocalState(request), state => state.loadLocalStateResultJson, {}) as Promise<EditorLocalStateSnapshot>,
    saveLocalState: (entries) => invokeRouted("SaveLocalState", root => root.snapshot.localState, (view, request) => view.saveLocalState(request), state => state.saveLocalStateResultJson, entries) as Promise<EditorLocalStateSnapshot>,
    clearLocalState: () => invokeRouted("ClearLocalState", root => root.snapshot.localState, (view, request) => view.clearLocalState(request), state => state.clearLocalStateResultJson, {}) as Promise<EditorLocalStateClearResult>,
    previewProject: (request) => invokeRouted("PreviewProject", root => root.snapshot.project, (view, request) => view.previewProject(request), state => state.previewProjectResultJson, request) as Promise<EditorProjectPlan>,
    createProject: (request) => invokeRouted("CreateProject", root => root.snapshot.project, (view, request) => view.createProject(request), state => state.createProjectResultJson, request) as Promise<EditorOperationResult>,
    openWorkspace: (request) => invokeRouted("OpenWorkspace", root => root.snapshot.project, (view, request) => view.openWorkspace(request), state => state.openWorkspaceResultJson, request) as Promise<EditorOperationResult>,
    exportXliff: (directory) => invokeRouted("ExportXliff", root => root.snapshot.interchange, (view, request) => view.exportXliff(request), state => state.exportXliffResultJson, { directory }) as Promise<EditorXliffExportResult>,
    previewXliffImport: (path) => invokeRouted("PreviewXliffImport", root => root.snapshot.interchange, (view, request) => view.previewXliffImport(request), state => state.previewXliffImportResultJson, { path }) as Promise<EditorXliffImportPreview>,
    applyXliffImport: (confirmationToken) => invokeRouted("ApplyXliffImport", root => root.snapshot.interchange, (view, request) => view.applyXliffImport(request), state => state.applyXliffImportResultJson, { confirmationToken }) as Promise<EditorOperationResult>,
    exportReviewJson: (path) => invokeRouted("ExportReviewJson", root => root.snapshot.interchange, (view, request) => view.exportReviewJson(request), state => state.exportReviewJsonResultJson, { path }) as Promise<EditorReviewFileResult>,
    previewReviewJsonImport: (path) => invokeRouted("PreviewReviewJsonImport", root => root.snapshot.interchange, (view, request) => view.previewReviewJsonImport(request), state => state.previewReviewJsonImportResultJson, { path }) as Promise<EditorReviewImportPreview>,
    applyReviewJsonImport: (confirmationToken) => invokeRouted("ApplyReviewJsonImport", root => root.snapshot.interchange, (view, request) => view.applyReviewJsonImport(request), state => state.applyReviewJsonImportResultJson, { confirmationToken }) as Promise<EditorReviewOperationResult>,
    openDocument: async (path) => {
      const reference = documentRefs.get(path);
      if (import.meta.env.MODE === "mock" || !reference) return undefined;
      const view = await reference.connect();
      try {
        if (view.snapshot.path !== path) throw new Error("The selected document route changed.");
        return view.snapshot;
      } finally {
        view.dispose();
      }
    },
    validate: (path, content) => invokeDocument<ValidationResult>(path, "validate", { content }),
    save: (path, content, revision) => invokeDocument<EditorOperationResult>(path, "save", { content, revision }),
  };
}
