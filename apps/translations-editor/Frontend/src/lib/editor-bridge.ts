import { mockExecute } from "./mock-bridge";
import { BridgeError } from "@runic-artifex/views";
import { connectEditor, type EditorClient, type EditorState } from "../generated/editor";
import type { EditorDocumentPageReference, EditorDocumentState } from "../generated/editorDocument";
import type {
  EditorAbout,
  EditorMessageOperation,
  EditorDiagnosticQuickFix,
  EditorDiagnosticBundleActionResult,
  EditorDiagnosticBundleResult,
  EditorDocument,
  EditorDocumentDraft,
  EditorMessageDraft,
  EditorLocalStateClearResult,
  EditorLocalStateEntry,
  EditorLocalStateSnapshot,
  EditorSpellingCheckResult,
  EditorSpellingLanguage,
  EditorSpellingStatus,
  EditorSpellingSuggestions,
  EditorSpellingText,
  EditorSpellingWordResult,
  EditorExternalChanges,
  EditorReviewFileResult,
  EditorReviewImportPreview,
  EditorMessagePreview,
  EditorLocaleCatalog,
  EditorLocaleDescription,
  EditorCatalogImportPlan,
  EditorCatalogImportRequest,
  EditorMutationPreview,
  EditorMutationRequest,
  EditorNotice,
  EditorOperationResult,
  EditorOpenWorkspaceRequest,
  EditorProjectCreationRequest,
  EditorProjectPlan,
  EditorReviewOperationResult,
  EditorReviewSaveRequest,
  EditorXliffExportResult,
  EditorXliffImportPreview,
  EditorWorkspacePickerResult,
  EditorLibraryOverrideRequest,
  EditorLibraryResult,
  ValidationResult,
  WorkspaceSnapshot,
} from "./contracts";

let connection: Promise<EditorClient> | undefined;
let tail: Promise<void> = Promise.resolve();
const documentRefs = new Map<string, EditorDocumentPageReference>();
let rememberedDocuments: readonly EditorDocument[] = [];

// A result lists the documents in the order of the root snapshot's `documents` references. The editor updates the
// root's documents in a model turn that runs before the command replies, and since Runic SDK 0.11 (read-your-writes,
// runic-sdk #585) the reply reaches the page only after that turn's state frames on every View of the window. The
// root snapshot is therefore current when the result arrives, and the paths are mapped onto its references at once.
function rememberDocuments(result: unknown, view: EditorClient): void {
  if (typeof result !== "object" || result === null) return;
  const value = result as { documents?: EditorDocument[]; snapshot?: WorkspaceSnapshot };
  const documents = value.documents ?? value.snapshot?.documents;
  if (!documents) return;
  rememberedDocuments = documents;
  documentRefs.clear();
  mapDocuments(view.snapshot);
}

function mapDocuments(state: EditorState): boolean {
  if (rememberedDocuments.length !== state.documents.length) return false;
  documentRefs.clear();
  rememberedDocuments.forEach((document, index) => documentRefs.set(document.path, state.documents[index]));
  return true;
}

// Reads the mapping without waiting for state. It is retried against the current root snapshot only for the rare reply
// that the host sent without its frames (a page that took no frames for 5 s); those frames arrive as ordinary updates
// before a later call.
async function documentReference(path: string): Promise<EditorDocumentPageReference | undefined> {
  const known = documentRefs.get(path);
  if (known || !rememberedDocuments.some(document => document.path === path)) return known;
  mapDocuments((await rootView()).snapshot);
  return documentRefs.get(path);
}

async function rootView(): Promise<EditorClient> {
  return await (connection ??= connectEditor().then(root => {
    registerCloseQuestion(root);
    return root;
  }, error => {
    connection = undefined;
    throw error;
  }));
}

// The page's answer to the host's question before the window closes (EditorViewModel.ConfirmClose). It is registered on
// each root client the page connects, so a reconnected page answers again; the host closes the window without asking
// while no page has a handler.
type CloseQuestionHandler = (signal: AbortSignal) => boolean | Promise<boolean>;
let closeQuestionHandler: CloseQuestionHandler | undefined;
let closeQuestionRegistration: { root: EditorClient; stop: () => void } | undefined;

function registerCloseQuestion(root: EditorClient): void {
  if (closeQuestionRegistration?.root === root) return;
  closeQuestionRegistration?.stop();
  closeQuestionRegistration = undefined;
  const handler = closeQuestionHandler;
  if (handler === undefined) return;
  closeQuestionRegistration = { root, stop: root.interactions.confirmClose.handle((_, { signal }) => handler(signal)) };
}

function handleCloseQuestion(handler: CloseQuestionHandler): () => void {
  if (import.meta.env.MODE === "mock") return () => undefined;
  closeQuestionHandler = handler;
  closeQuestionRegistration?.stop();
  closeQuestionRegistration = undefined;
  // Connects the root now, so the handler is registered before the first command that needs the root.
  void rootView().then(registerCloseQuestion, () => undefined);
  return () => {
    if (closeQuestionHandler !== handler) return;
    closeQuestionHandler = undefined;
    closeQuestionRegistration?.stop();
    closeQuestionRegistration = undefined;
  };
}

// Closes the window as its close button does; the host asks the close question first. A window that closes cancels the
// command, so its failure is the expected end.
async function closeWindow(): Promise<void> {
  if (import.meta.env.MODE === "mock") return;
  let root: EditorClient | undefined;
  try {
    root = await rootView();
    await root.closeWindow();
  } catch (error) {
    disconnectOnFailure(error, root);
  }
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

// Only a lost Bridge (`disconnected`) invalidates the cached root and document references. A `released` routed View
// (Views protocol 3) is reconnected by the next call through its retained page reference.
function disconnectOnFailure(error: unknown, root?: EditorClient): void {
  if (error instanceof BridgeError && error.kind === "disconnected") {
    root?.dispose();
    connection = undefined;
    documentRefs.clear();
  }
}

interface RoutedReference<View> { connect(): Promise<View> }
interface DisposableRoute { dispose(): void }

interface EditorDocumentSaveResult {
  readonly ok: boolean;
  readonly kind: string;
  readonly message?: EditorNotice;
}

async function invokeRouted<View extends DisposableRoute, State>(
  operation: string,
  select: (root: EditorClient) => RoutedReference<View>,
  execute: (view: View, request: string) => Promise<State>,
  resultJson: (state: State) => string,
  argument: unknown,
): Promise<unknown> {
  if (import.meta.env.MODE === "mock") return await mockExecute(operation, argument);
  return serialized(async () => {
    let root: EditorClient | undefined;
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
    const reference = await documentReference(path);
    if (!reference) throw new Error("The selected document route is unavailable. Reload the workspace.");
    let root: EditorClient | undefined;
    let view: Awaited<ReturnType<EditorDocumentPageReference["connect"]>> | undefined;
    try {
      root = await rootView();
      view = await reference.connect();
      if (view.snapshot.path !== path) throw new Error("The selected document route changed.");
      const operation = command === "validate"
        ? await view.startValidate({ content: (request as { content: string }).content })
        : await view.startSave({
            content: (request as { content: string }).content,
            revision: (request as { revision: string }).revision,
          });
      const terminal = await operation.completion;
      if (terminal.kind !== "succeeded")
        throw new BridgeError(terminal.kind === "cancelled" ? "cancelled" : "failed",
          terminal.kind === "failed" ? terminal.error.message : "The document operation did not complete.");
      if (terminal.delivery !== undefined)
        throw new BridgeError("failed", terminal.delivery.message);
      if (terminal.result === undefined)
        throw new BridgeError("failed", "The document operation completed without its typed result.");
      return terminal.result as T;
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
  applyAuthoringOperation(path: string, content: string, key: string, expectedRevision: string, operation: EditorMessageOperation): Promise<EditorDocumentDraft>;
  applyDiagnosticFix(path: string, content: string, fix: EditorDiagnosticQuickFix): Promise<EditorDocumentDraft>;
  transformDocument(path: string, content: string, key?: string, value?: string): Promise<EditorDocumentDraft>;
  /** The empty starter draft of `key` in `locale`, which has no translation yet, built from the base message. */
  createMessageDraft(key: string, locale: string): Promise<EditorMessageDraft>;
  /** An authoring operation on a draft's text (`expectedRevision`: its projection's revision); writes nothing. */
  applyMessageDraftOperation(key: string, locale: string, text: string, expectedRevision: string, operation: EditorMessageOperation): Promise<EditorMessageDraft>;
  /** `requestedLocale` (`de-CH` for `de` text) and `timeZone` (`utc`, an IANA zone; empty: the host's) format the preview. */
  previewMessage(path: string, content: string, locale: string, key: string, samplesJson?: string, requestedLocale?: string, timeZone?: string): Promise<EditorMessagePreview>;
  listLocales(): Promise<EditorLocaleCatalog>;
  /** The dry run of importing a .resx or i18next catalog; its confirmation token applies it once. */
  previewCatalogImport(request: EditorCatalogImportRequest): Promise<EditorCatalogImportPlan>;
  applyCatalogImport(confirmationToken: string): Promise<EditorOperationResult>;
  describeLocale(tag: string): Promise<EditorLocaleDescription>;
  saveReview(request: EditorReviewSaveRequest): Promise<EditorReviewOperationResult>;
  about(): Promise<EditorAbout>;
  createDiagnosticBundle(): Promise<EditorDiagnosticBundleResult>;
  revealDiagnosticBundle(path: string): Promise<EditorDiagnosticBundleActionResult>;
  deleteDiagnosticBundle(path: string): Promise<EditorDiagnosticBundleActionResult>;
  loadLocalState(): Promise<EditorLocalStateSnapshot>;
  saveLocalState(entries: EditorLocalStateEntry[]): Promise<EditorLocalStateSnapshot>;
  clearLocalState(): Promise<EditorLocalStateClearResult>;
  /** What checks each language; with `automaticDownload`, the system language's missing dictionary starts to download. */
  spellingStatus(locales: string[], automaticDownload: boolean): Promise<EditorSpellingStatus>;
  checkSpelling(locale: string, texts: EditorSpellingText[]): Promise<EditorSpellingCheckResult>;
  suggestSpelling(locale: string, word: string): Promise<EditorSpellingSuggestions>;
  /** Starts the download the user asked for; returns at once, and `spellingStatus` reports its progress. */
  downloadSpellingDictionary(locale: string): Promise<EditorSpellingLanguage>;
  addSpellingWord(word: string): Promise<EditorSpellingWordResult>;
  previewProject(request: EditorProjectCreationRequest): Promise<EditorProjectPlan>;
  createProject(request: EditorProjectCreationRequest): Promise<EditorOperationResult>;
  openWorkspace(request: EditorOpenWorkspaceRequest): Promise<EditorOperationResult>;
  exportXliff(directory?: string): Promise<EditorXliffExportResult>;
  previewXliffImport(path: string): Promise<EditorXliffImportPreview>;
  applyXliffImport(confirmationToken: string): Promise<EditorOperationResult>;
  exportReviewJson(path?: string): Promise<EditorReviewFileResult>;
  previewReviewJsonImport(path: string): Promise<EditorReviewImportPreview>;
  applyReviewJsonImport(confirmationToken: string): Promise<EditorReviewOperationResult>;
  /** Library catalogs and overrides (T5d): the library is layer 0 (read-only), the application's overrides layer 1. */
  loadLibrary(catalog: string): Promise<EditorLibraryResult>;
  overrideLibraryMessage(request: EditorLibraryOverrideRequest): Promise<EditorLibraryResult>;
  saveLibraryOverride(request: EditorLibraryOverrideRequest): Promise<EditorLibraryResult>;
  revertLibraryOverride(request: EditorLibraryOverrideRequest): Promise<EditorLibraryResult>;
  keepLibraryOverride(request: EditorLibraryOverrideRequest): Promise<EditorLibraryResult>;
  addLibraryLocale(catalog: string, locale: string): Promise<EditorLibraryResult>;
  openDocument(path: string): Promise<EditorDocumentState | undefined>;
  validate(path: string, content: string): Promise<ValidationResult>;
  save(path: string, content: string, revision: string): Promise<EditorDocumentSaveResult>;
  /** Sets the window's locale (`IRunicWindowLocale`), which the window's pages and native surfaces follow. */
  setUiLocale(locale: string): Promise<void>;
  /**
   * Answers the host's question whether the window may close now (the close button, Alt+F4, the window manager, and
   * `closeWindow`). Returns the function that stops answering; while no page answers, the host closes the window.
   */
  handleCloseQuestion(handler: (signal: AbortSignal) => boolean | Promise<boolean>): () => void;
  /** Closes the window as its close button does: the host asks the close question first. */
  closeWindow(): Promise<void>;
}

// The window's locale is set on the root View; the mock bridge has no window, so the page keeps its own locale there.
async function setWindowLocale(locale: string): Promise<void> {
  if (import.meta.env.MODE === "mock") return;
  let root: EditorClient | undefined;
  try {
    root = await rootView();
    await root.setUiLocale(locale);
  } catch (error) {
    disconnectOnFailure(error, root);
    throw error;
  }
}

export function createEditorBridge(): EditorBridge {
  return {
    setUiLocale: setWindowLocale,
    handleCloseQuestion,
    closeWindow,
    load: () => invokeRouted("LoadWorkspace", root => root.snapshot.workspace, (view, request) => view.load(request), state => state.loadResultJson, {}) as Promise<WorkspaceSnapshot>,
    checkExternalChanges: () => invokeRouted("CheckExternalChanges", root => root.snapshot.workspace, (view, request) => view.checkExternalChanges(request), state => state.checkExternalChangesResultJson, {}) as Promise<EditorExternalChanges>,
    pickWorkspace: () => invokeRouted("PickWorkspace", root => root.snapshot.workspace, (view, request) => view.pickWorkspace(request), state => state.pickWorkspaceResultJson, {}) as Promise<EditorWorkspacePickerResult>,
    previewMutation: (request) => invokeRouted("PreviewMutation", root => root.snapshot.workspace, (view, request) => view.previewMutation(request), state => state.previewMutationResultJson, request) as Promise<EditorMutationPreview>,
    applyMutation: (request) => invokeRouted("ApplyMutation", root => root.snapshot.workspace, (view, request) => view.applyMutation(request), state => state.applyMutationResultJson, request) as Promise<EditorOperationResult>,
    recoverTransaction: (mode) => invokeRouted("RecoverTransaction", root => root.snapshot.workspace, (view, request) => view.recoverTransaction(request), state => state.recoverTransactionResultJson, { mode }) as Promise<EditorOperationResult>,
    undo: () => invokeRouted("Undo", root => root.snapshot.workspace, (view, request) => view.undo(request), state => state.undoResultJson, {}) as Promise<EditorOperationResult>,
    redo: () => invokeRouted("Redo", root => root.snapshot.workspace, (view, request) => view.redo(request), state => state.redoResultJson, {}) as Promise<EditorOperationResult>,
    applyAuthoringOperation: (path, content, key, expectedRevision, operation) => invokeRouted("ApplyAuthoringOperation", root => root.snapshot.documentTools, (view, request) => view.applyAuthoringOperation(request), state => state.applyAuthoringOperationResultJson, { path, content, key, expectedRevision, operation }) as Promise<EditorDocumentDraft>,
    applyDiagnosticFix: (path, content, fix) => invokeRouted("ApplyDiagnosticFix", root => root.snapshot.documentTools, (view, request) => view.applyDiagnosticFix(request), state => state.applyDiagnosticFixResultJson, { path, content, fix }) as Promise<EditorDocumentDraft>,
    transformDocument: (path, content, key, value) => invokeRouted("TransformDocument", root => root.snapshot.documentTools, (view, request) => view.transformDocument(request), state => state.transformDocumentResultJson, { path, content, key, value }) as Promise<EditorDocumentDraft>,
    createMessageDraft: (key, locale) => invokeRouted("CreateMessageDraft", root => root.snapshot.documentTools, (view, request) => view.createMessageDraft(request), state => state.createMessageDraftResultJson, { key, locale }) as Promise<EditorMessageDraft>,
    applyMessageDraftOperation: (key, locale, text, expectedRevision, operation) => invokeRouted("ApplyMessageDraftOperation", root => root.snapshot.documentTools, (view, request) => view.applyMessageDraftOperation(request), state => state.applyMessageDraftOperationResultJson, { key, locale, text, expectedRevision, operation }) as Promise<EditorMessageDraft>,
    previewMessage: (path, content, locale, key, samplesJson, requestedLocale, timeZone) => invokeRouted("PreviewMessage", root => root.snapshot.documentTools, (view, request) => view.previewMessage(request), state => state.previewMessageResultJson, { path, content, locale, key, samplesJson, requestedLocale, timeZone }) as Promise<EditorMessagePreview>,
    previewCatalogImport: (request) => invokeRouted("PreviewCatalogImport", root => root.snapshot.interchange, (view, argument) => view.previewCatalogImport(argument), state => state.previewCatalogImportResultJson, request) as Promise<EditorCatalogImportPlan>,
    applyCatalogImport: (confirmationToken) => invokeRouted("ApplyCatalogImport", root => root.snapshot.interchange, (view, argument) => view.applyCatalogImport(argument), state => state.applyCatalogImportResultJson, { confirmationToken }) as Promise<EditorOperationResult>,
    listLocales: () => invokeRouted("ListLocales", root => root.snapshot.workspace, (view, request) => view.listLocales(request), state => state.listLocalesResultJson, {}) as Promise<EditorLocaleCatalog>,
    describeLocale: (tag) => invokeRouted("DescribeLocale", root => root.snapshot.workspace, (view, request) => view.describeLocale(request), state => state.describeLocaleResultJson, { tag }) as Promise<EditorLocaleDescription>,
    saveReview: (request) => invokeRouted("SaveReview", root => root.snapshot.review, (view, request) => view.saveReview(request), state => state.saveReviewResultJson, request) as Promise<EditorReviewOperationResult>,
    about: () => invokeRouted("About", root => root.snapshot.diagnostics, (view, request) => view.about(request), state => state.aboutResultJson, {}) as Promise<EditorAbout>,
    createDiagnosticBundle: () => invokeRouted("CreateDiagnosticBundle", root => root.snapshot.diagnostics, (view, request) => view.createDiagnosticBundle(request), state => state.createDiagnosticBundleResultJson, {}) as Promise<EditorDiagnosticBundleResult>,
    revealDiagnosticBundle: (path) => invokeRouted("RevealDiagnosticBundle", root => root.snapshot.diagnostics, (view, request) => view.revealDiagnosticBundle(request), state => state.revealDiagnosticBundleResultJson, { path }) as Promise<EditorDiagnosticBundleActionResult>,
    deleteDiagnosticBundle: (path) => invokeRouted("DeleteDiagnosticBundle", root => root.snapshot.diagnostics, (view, request) => view.deleteDiagnosticBundle(request), state => state.deleteDiagnosticBundleResultJson, { path }) as Promise<EditorDiagnosticBundleActionResult>,
    loadLocalState: () => invokeRouted("LoadLocalState", root => root.snapshot.localState, (view, request) => view.loadLocalState(request), state => state.loadLocalStateResultJson, {}) as Promise<EditorLocalStateSnapshot>,
    saveLocalState: (entries) => invokeRouted("SaveLocalState", root => root.snapshot.localState, (view, request) => view.saveLocalState(request), state => state.saveLocalStateResultJson, entries) as Promise<EditorLocalStateSnapshot>,
    clearLocalState: () => invokeRouted("ClearLocalState", root => root.snapshot.localState, (view, request) => view.clearLocalState(request), state => state.clearLocalStateResultJson, {}) as Promise<EditorLocalStateClearResult>,
    spellingStatus: (locales, automaticDownload) => invokeRouted("SpellingStatus", root => root.snapshot.spelling, (view, request) => view.spellingStatus(request), state => state.spellingStatusResultJson, { locales, automaticDownload }) as Promise<EditorSpellingStatus>,
    checkSpelling: (locale, texts) => invokeRouted("CheckSpelling", root => root.snapshot.spelling, (view, request) => view.checkSpelling(request), state => state.checkSpellingResultJson, { locale, texts }) as Promise<EditorSpellingCheckResult>,
    suggestSpelling: (locale, word) => invokeRouted("SuggestSpelling", root => root.snapshot.spelling, (view, request) => view.suggestSpelling(request), state => state.suggestSpellingResultJson, { locale, word }) as Promise<EditorSpellingSuggestions>,
    downloadSpellingDictionary: (locale) => invokeRouted("DownloadSpellingDictionary", root => root.snapshot.spelling, (view, request) => view.downloadSpellingDictionary(request), state => state.downloadSpellingDictionaryResultJson, { locale }) as Promise<EditorSpellingLanguage>,
    addSpellingWord: (word) => invokeRouted("AddSpellingWord", root => root.snapshot.spelling, (view, request) => view.addSpellingWord(request), state => state.addSpellingWordResultJson, { word }) as Promise<EditorSpellingWordResult>,
    previewProject: (request) => invokeRouted("PreviewProject", root => root.snapshot.project, (view, request) => view.previewProject(request), state => state.previewProjectResultJson, request) as Promise<EditorProjectPlan>,
    createProject: (request) => invokeRouted("CreateProject", root => root.snapshot.project, (view, request) => view.createProject(request), state => state.createProjectResultJson, request) as Promise<EditorOperationResult>,
    openWorkspace: (request) => invokeRouted("OpenWorkspace", root => root.snapshot.project, (view, request) => view.openWorkspace(request), state => state.openWorkspaceResultJson, request) as Promise<EditorOperationResult>,
    exportXliff: (directory) => invokeRouted("ExportXliff", root => root.snapshot.interchange, (view, request) => view.exportXliff(request), state => state.exportXliffResultJson, { directory }) as Promise<EditorXliffExportResult>,
    previewXliffImport: (path) => invokeRouted("PreviewXliffImport", root => root.snapshot.interchange, (view, request) => view.previewXliffImport(request), state => state.previewXliffImportResultJson, { path }) as Promise<EditorXliffImportPreview>,
    applyXliffImport: (confirmationToken) => invokeRouted("ApplyXliffImport", root => root.snapshot.interchange, (view, request) => view.applyXliffImport(request), state => state.applyXliffImportResultJson, { confirmationToken }) as Promise<EditorOperationResult>,
    exportReviewJson: (path) => invokeRouted("ExportReviewJson", root => root.snapshot.interchange, (view, request) => view.exportReviewJson(request), state => state.exportReviewJsonResultJson, { path }) as Promise<EditorReviewFileResult>,
    previewReviewJsonImport: (path) => invokeRouted("PreviewReviewJsonImport", root => root.snapshot.interchange, (view, request) => view.previewReviewJsonImport(request), state => state.previewReviewJsonImportResultJson, { path }) as Promise<EditorReviewImportPreview>,
    applyReviewJsonImport: (confirmationToken) => invokeRouted("ApplyReviewJsonImport", root => root.snapshot.interchange, (view, request) => view.applyReviewJsonImport(request), state => state.applyReviewJsonImportResultJson, { confirmationToken }) as Promise<EditorReviewOperationResult>,
    loadLibrary: (catalog) => invokeRouted("LoadLibrary", root => root.snapshot.libraries, (view, request) => view.loadLibrary(request), state => state.loadLibraryResultJson, { catalog }) as Promise<EditorLibraryResult>,
    overrideLibraryMessage: (request) => invokeRouted("OverrideLibraryMessage", root => root.snapshot.libraries, (view, request) => view.overrideMessage(request), state => state.overrideMessageResultJson, request) as Promise<EditorLibraryResult>,
    saveLibraryOverride: (request) => invokeRouted("SaveLibraryOverride", root => root.snapshot.libraries, (view, request) => view.saveOverride(request), state => state.saveOverrideResultJson, request) as Promise<EditorLibraryResult>,
    revertLibraryOverride: (request) => invokeRouted("RevertLibraryOverride", root => root.snapshot.libraries, (view, request) => view.revertOverride(request), state => state.revertOverrideResultJson, request) as Promise<EditorLibraryResult>,
    keepLibraryOverride: (request) => invokeRouted("KeepLibraryOverride", root => root.snapshot.libraries, (view, request) => view.keepOverride(request), state => state.keepOverrideResultJson, request) as Promise<EditorLibraryResult>,
    addLibraryLocale: (catalog, locale) => invokeRouted("AddLibraryLocale", root => root.snapshot.libraries, (view, request) => view.addLocale(request), state => state.addLocaleResultJson, { catalog, locale }) as Promise<EditorLibraryResult>,
    openDocument: async (path) => {
      if (import.meta.env.MODE === "mock") return undefined;
      const reference = await documentReference(path);
      if (!reference) return undefined;
      const view = await reference.connect();
      try {
        if (view.snapshot.path !== path) throw new Error("The selected document route changed.");
        return view.snapshot;
      } finally {
        view.dispose();
      }
    },
    validate: (path, content) => invokeDocument<ValidationResult>(path, "validate", { content }),
    save: (path, content, revision) => invokeDocument<EditorDocumentSaveResult>(path, "save", { content, revision }),
  };
}
