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

async function invoke<T>(operation: string, argument: unknown): Promise<T> {
  if (import.meta.env.MODE === "mock") return await mockExecute(operation, argument) as T;
  const requestId = globalThis.crypto.randomUUID();
  const previous = tail;
  let release!: () => void;
  tail = new Promise<void>(resolve => { release = resolve; });
  await previous;
  try {
    const view = await (connection ??= connectEditor().catch(error => {
      connection = undefined;
      throw error;
    }));
    try {
      const state = await view.execute(JSON.stringify({ requestId, operation, argument }));
      const envelope = JSON.parse(state.resultJson) as { requestId: string; result: T };
      if (envelope.requestId !== requestId) throw new Error("The editor response did not match its request.");
      rememberDocuments(envelope.result, view);
      return envelope.result;
    } catch (error) {
      if (error instanceof BridgeError && error.kind === "disconnected") {
        view.dispose();
        connection = undefined;
      }
      throw error;
    }
  } finally {
    release();
  }
}

async function invokeDocument<T>(path: string, command: "validate" | "save", request: object): Promise<T> {
  const reference = documentRefs.get(path);
  if (import.meta.env.MODE === "mock" || !reference) {
    if (command === "validate") return invoke<T>("ValidateDocument", { path, ...request });
    return invoke<T>("SaveDocument", { path, ...request });
  }
  const requestId = globalThis.crypto.randomUUID();
  const previous = tail;
  let release!: () => void;
  tail = new Promise<void>(resolve => { release = resolve; });
  await previous;
  let view: Awaited<ReturnType<EditorDocumentPageReference["connect"]>> | undefined;
  try {
    view = await reference.connect();
    if (view.snapshot.path !== path) throw new Error("The selected document route changed.");
    const state = await view[command](JSON.stringify({ requestId, ...request }));
    const envelope = JSON.parse(command === "validate" ? state.validationResultJson : state.saveResultJson) as { requestId: string; result: T };
    if (envelope.requestId !== requestId) throw new Error("The document response did not match its request.");
    if (command === "save" && connection) rememberDocuments(envelope.result, await connection);
    return envelope.result;
  } finally {
    view?.dispose();
    release();
  }
}

export interface EditorBridge {
  load(): Promise<WorkspaceSnapshot>;
  openDocument(path: string): Promise<EditorDocumentState | undefined>;
  checkExternalChanges(): Promise<EditorExternalChanges>;
  pickWorkspace(): Promise<EditorWorkspacePickerResult>;
  previewMutation(request: EditorMutationRequest): Promise<EditorMutationPreview>;
  applyMutation(request: EditorMutationRequest): Promise<EditorOperationResult>;
  recoverTransaction(mode: "complete" | "rollback"): Promise<EditorOperationResult>;
  undo(): Promise<EditorOperationResult>;
  redo(): Promise<EditorOperationResult>;
  validate(path: string, content: string): Promise<ValidationResult>;
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
  save(path: string, content: string, revision: string): Promise<EditorOperationResult>;
  previewProject(request: EditorProjectCreationRequest): Promise<EditorProjectPlan>;
  createProject(request: EditorProjectCreationRequest): Promise<EditorOperationResult>;
  openWorkspace(request: EditorOpenWorkspaceRequest): Promise<EditorOperationResult>;
  exportXliff(directory?: string): Promise<EditorXliffExportResult>;
  previewXliffImport(path: string): Promise<EditorXliffImportPreview>;
  applyXliffImport(confirmationToken: string): Promise<EditorOperationResult>;
  exportReviewJson(path?: string): Promise<EditorReviewFileResult>;
  previewReviewJsonImport(path: string): Promise<EditorReviewImportPreview>;
  applyReviewJsonImport(confirmationToken: string): Promise<EditorReviewOperationResult>;
}

export function createEditorBridge(): EditorBridge {
  return {
    load: () => invoke<WorkspaceSnapshot>("LoadWorkspace", {}),
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
    checkExternalChanges: () => invoke<EditorExternalChanges>("CheckExternalChanges", {}),
    pickWorkspace: () => invoke<EditorWorkspacePickerResult>("PickWorkspace", {}),
    previewMutation: (request) => invoke<EditorMutationPreview>("PreviewMutation", request),
    applyMutation: (request) => invoke<EditorOperationResult>("ApplyMutation", request),
    recoverTransaction: (mode) => invoke<EditorOperationResult>("RecoverTransaction", { mode }),
    undo: () => invoke<EditorOperationResult>("Undo", {}),
    redo: () => invoke<EditorOperationResult>("Redo", {}),
    validate: (path, content) => invokeDocument<ValidationResult>(path, "validate", { content }),
    transformDocument: (path, content, key, value) => invoke<EditorDocumentDraft>("TransformDocument", { path, content, key, value }),
    previewMessage: (path, content, locale, key, samplesJson) => invoke<EditorMessagePreview>("PreviewMessage", { path, content, locale, key, samplesJson }),
    saveReview: (request) => invoke<EditorReviewOperationResult>("SaveReview", request),
    about: () => invoke<EditorAbout>("About", {}),
    createDiagnosticBundle: () => invoke<EditorDiagnosticBundleResult>("CreateDiagnosticBundle", {}),
    revealDiagnosticBundle: (path) => invoke<EditorDiagnosticBundleActionResult>("RevealDiagnosticBundle", { path }),
    deleteDiagnosticBundle: (path) => invoke<EditorDiagnosticBundleActionResult>("DeleteDiagnosticBundle", { path }),
    loadLocalState: () => invoke<EditorLocalStateSnapshot>("LoadLocalState", {}),
    saveLocalState: (entries) => invoke<EditorLocalStateSnapshot>("SaveLocalState", entries),
    clearLocalState: () => invoke<EditorLocalStateClearResult>("ClearLocalState", {}),
    save: (path, content, revision) => invokeDocument<EditorOperationResult>(path, "save", { content, revision }),
    previewProject: (request) => invoke<EditorProjectPlan>("PreviewProject", request),
    createProject: (request) => invoke<EditorOperationResult>("CreateProject", request),
    openWorkspace: (request) => invoke<EditorOperationResult>("OpenWorkspace", request),
    exportXliff: (directory) => invoke<EditorXliffExportResult>("ExportXliff", { directory }),
    previewXliffImport: (path) => invoke<EditorXliffImportPreview>("PreviewXliffImport", { path }),
    applyXliffImport: (confirmationToken) => invoke<EditorOperationResult>("ApplyXliffImport", { confirmationToken }),
    exportReviewJson: (path) => invoke<EditorReviewFileResult>("ExportReviewJson", { path }),
    previewReviewJsonImport: (path) => invoke<EditorReviewImportPreview>("PreviewReviewJsonImport", { path }),
    applyReviewJsonImport: (confirmationToken) => invoke<EditorReviewOperationResult>("ApplyReviewJsonImport", { confirmationToken }),
  };
}
