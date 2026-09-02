// Canonical Application Bridge contract for the editor frontend, mirroring
// Frontend/contract/editor-contract.mjs one-to-one. The manifest under
// Contract/bridge.manifest.json is generated from that authoring source; its
// fingerprint below pins both runtimes to the same wire surface.
import { Schema } from "effect";
import { defineApplicationContract } from "@runic-artifex/application-bridge";

const EditorLocale = Schema.Struct({
  tag: Schema.String,
  fallback: Schema.optional(Schema.String),
});

const EditorLayer = Schema.Struct({
  name: Schema.String,
  priority: Schema.Int,
});

const EditorCatalog = Schema.Struct({
  id: Schema.String,
  schemaVersion: Schema.Int,
  defaultLocale: Schema.String,
  locales: Schema.Array(EditorLocale),
  layers: Schema.Array(EditorLayer),
});

const EditorCatalogSummary = Schema.Struct({
  id: Schema.String,
  manifestPaths: Schema.Array(Schema.String),
  documentCount: Schema.Int,
  localeCount: Schema.Int,
  messageCount: Schema.Int,
  errorCount: Schema.Int,
  warningCount: Schema.Int,
  success: Schema.Boolean,
});

const EditorDocument = Schema.Struct({
  path: Schema.String,
  content: Schema.String,
  revision: Schema.String,
  isManifest: Schema.Boolean,
  isMalformed: Schema.Boolean,
  locale: Schema.optional(Schema.String),
  layer: Schema.optional(Schema.String),
});

const EditorDiagnostic = Schema.Struct({
  id: Schema.String,
  severity: Schema.Literal("error", "warning"),
  message: Schema.String,
  path: Schema.String,
  line: Schema.Int,
  column: Schema.Int,
  endLine: Schema.Int,
  endColumn: Schema.Int,
});

const EditorPendingTransaction = Schema.Struct({
  catalogId: Schema.String,
  paths: Schema.Array(Schema.String),
});

const EditorSampleEntry = Schema.Struct({
  key: Schema.String,
  value: Schema.String,
});

const EditorReviewEntry = Schema.Struct({
  key: Schema.String,
  locale: Schema.String,
  state: Schema.Literal("draft", "translated", "needs-review", "approved"),
  note: Schema.optional(Schema.String),
  sourceFingerprint: Schema.optional(Schema.String),
  samples: Schema.Array(EditorSampleEntry),
});

const EditorTerminologyEntry = Schema.Struct({
  source: Schema.String,
  preferred: Schema.String,
  locale: Schema.optional(Schema.String),
  note: Schema.optional(Schema.String),
});

const EditorReviewSnapshot = Schema.Struct({
  path: Schema.String,
  revision: Schema.optional(Schema.String),
  error: Schema.optional(Schema.String),
  entries: Schema.Array(EditorReviewEntry),
  terminology: Schema.Array(EditorTerminologyEntry),
});

const EditorHistoryState = Schema.Struct({
  canUndo: Schema.Boolean,
  canRedo: Schema.Boolean,
  undoLabel: Schema.optional(Schema.String),
  redoLabel: Schema.optional(Schema.String),
});

export const WorkspaceSnapshot = Schema.Struct({
  root: Schema.String,
  catalog: Schema.optional(EditorCatalog),
  catalogs: Schema.Array(EditorCatalogSummary),
  documents: Schema.Array(EditorDocument),
  diagnostics: Schema.Array(EditorDiagnostic),
  success: Schema.Boolean,
  pendingTransaction: Schema.optional(EditorPendingTransaction),
  review: Schema.optional(EditorReviewSnapshot),
  history: Schema.optional(EditorHistoryState),
});

const ValidationResult = Schema.Struct({
  success: Schema.Boolean,
  diagnostics: Schema.Array(EditorDiagnostic),
});

const EditorMessagePreview = Schema.Struct({
  success: Schema.Boolean,
  locale: Schema.optional(Schema.String),
  astJson: Schema.optional(Schema.String),
  diagnostics: Schema.Array(EditorDiagnostic),
});

const EditorOperationResult = Schema.Struct({
  ok: Schema.Boolean,
  kind: Schema.String,
  message: Schema.optional(Schema.String),
  snapshot: Schema.optional(WorkspaceSnapshot),
  validation: Schema.optional(ValidationResult),
  history: Schema.optional(EditorHistoryState),
});

const EditorReviewSaveRequest = Schema.Struct({
  expectedRevision: Schema.optional(Schema.String),
  entries: Schema.Array(EditorReviewEntry),
  terminology: Schema.Array(EditorTerminologyEntry),
});

const EditorReviewOperationResult = Schema.Struct({
  ok: Schema.Boolean,
  message: Schema.optional(Schema.String),
  review: Schema.optional(EditorReviewSnapshot),
  history: Schema.optional(EditorHistoryState),
});

const EditorAbout = Schema.Struct({
  product: Schema.String,
  version: Schema.String,
  updateChannel: Schema.String,
  commit: Schema.optional(Schema.String),
  runtime: Schema.String,
  runtimeIdentifier: Schema.String,
  operatingSystem: Schema.String,
  architecture: Schema.String,
});

const EditorDiagnosticBundleResult = Schema.Struct({
  ok: Schema.Boolean,
  path: Schema.optional(Schema.String),
  message: Schema.optional(Schema.String),
});

const EditorDiagnosticBundleActionResult = Schema.Struct({
  ok: Schema.Boolean,
  message: Schema.optional(Schema.String),
});

const EditorLocalStateEntry = Schema.Struct({
  key: Schema.String,
  value: Schema.String,
});

const EditorLocalStateSnapshot = Schema.Struct({
  entries: Schema.Array(EditorLocalStateEntry),
  recovered: Schema.Boolean,
});

const EditorLocalStateClearResult = Schema.Struct({
  removedEntries: Schema.Int,
  recovered: Schema.Boolean,
});

const EditorProjectLocaleRequest = Schema.Struct({
  tag: Schema.String,
  fallback: Schema.optional(Schema.String),
});

const EditorProjectCreationRequest = Schema.Struct({
  directory: Schema.String,
  catalogId: Schema.String,
  defaultLocale: Schema.String,
  additionalLocales: Schema.Array(EditorProjectLocaleRequest),
  codeNamespace: Schema.String,
  className: Schema.String,
  includeStarterMessage: Schema.Boolean,
});

const EditorProjectPlan = Schema.Struct({
  ok: Schema.Boolean,
  message: Schema.optional(Schema.String),
  directory: Schema.String,
  catalogId: Schema.String,
  locales: Schema.Array(EditorLocale),
  files: Schema.Array(Schema.String),
});

const EditorOpenWorkspaceRequest = Schema.Struct({
  directory: Schema.String,
  catalogId: Schema.optional(Schema.String),
});

const EditorExternalFileChange = Schema.Struct({
  path: Schema.String,
  exists: Schema.Boolean,
  content: Schema.optional(Schema.String),
  revision: Schema.optional(Schema.String),
});

const EditorExternalChanges = Schema.Struct({
  overflowed: Schema.Boolean,
  paths: Schema.Array(Schema.String),
  changes: Schema.Array(EditorExternalFileChange),
});

const EditorWorkspacePickerResult = Schema.Struct({
  ok: Schema.Boolean,
  cancelled: Schema.Boolean,
  directory: Schema.optional(Schema.String),
  message: Schema.optional(Schema.String),
});

const EditorMutationRequest = Schema.Struct({
  kind: Schema.Literal(
    "add-locale",
    "remove-locale",
    "set-fallback",
    "create-key",
    "rename-key",
    "duplicate-key",
    "delete-key",
  ),
  locale: Schema.optional(Schema.String),
  fallback: Schema.optional(Schema.String),
  replacementFallback: Schema.optional(Schema.String),
  copyFromLocale: Schema.optional(Schema.String),
  sourceKey: Schema.optional(Schema.String),
  targetKey: Schema.optional(Schema.String),
  initialValue: Schema.optional(Schema.String),
  confirmationToken: Schema.optional(Schema.String),
});

const EditorMutationFile = Schema.Struct({
  path: Schema.String,
  kind: Schema.String,
  beforeBytes: Schema.Int,
  afterBytes: Schema.Int,
});

const EditorMutationPreview = Schema.Struct({
  ok: Schema.Boolean,
  message: Schema.optional(Schema.String),
  files: Schema.Array(EditorMutationFile),
  requiresIrreversibleConfirmation: Schema.Boolean,
  confirmationToken: Schema.optional(Schema.String),
});

const EditorInterchangeLoss = Schema.Struct({
  code: Schema.String,
  location: Schema.String,
  message: Schema.String,
  semanticLoss: Schema.Boolean,
});

const EditorInterchangeRefusal = Schema.Struct({
  code: Schema.String,
  message: Schema.String,
});

const EditorInterchangeFile = Schema.Struct({
  path: Schema.String,
  locale: Schema.String,
  byteCount: Schema.Int,
});

const EditorXliffExportResult = Schema.Struct({
  ok: Schema.Boolean,
  message: Schema.optional(Schema.String),
  catalogId: Schema.optional(Schema.String),
  documents: Schema.Array(EditorInterchangeFile),
  losses: Schema.Array(EditorInterchangeLoss),
  lossless: Schema.Boolean,
});

const EditorReviewFileResult = Schema.Struct({
  ok: Schema.Boolean,
  message: Schema.optional(Schema.String),
  path: Schema.optional(Schema.String),
  entryCount: Schema.Int,
});

const EditorKeyChange = Schema.Struct({
  key: Schema.String,
  kind: Schema.Literal("added", "changed", "removed", "state-change"),
  before: Schema.optional(Schema.String),
  after: Schema.optional(Schema.String),
  stateBefore: Schema.optional(Schema.String),
  stateAfter: Schema.optional(Schema.String),
});

const EditorXliffImportPreview = Schema.Struct({
  ok: Schema.Boolean,
  message: Schema.optional(Schema.String),
  requiresIrreversibleConfirmation: Schema.Boolean,
  confirmationToken: Schema.optional(Schema.String),
  catalogId: Schema.optional(Schema.String),
  sourceLocale: Schema.optional(Schema.String),
  targetLocale: Schema.optional(Schema.String),
  layer: Schema.optional(Schema.String),
  changes: Schema.Array(EditorKeyChange),
  addedCount: Schema.Int,
  changedCount: Schema.Int,
  removedCount: Schema.Int,
  unchangedCount: Schema.Int,
  reviewUpdateCount: Schema.Int,
  changesOverflowed: Schema.Boolean,
  refusals: Schema.Array(EditorInterchangeRefusal),
});

const EditorReviewChange = Schema.Struct({
  key: Schema.String,
  locale: Schema.String,
  kind: Schema.Literal("added", "changed", "removed"),
  stateBefore: Schema.optional(Schema.String),
  stateAfter: Schema.optional(Schema.String),
});

const EditorReviewImportPreview = Schema.Struct({
  ok: Schema.Boolean,
  message: Schema.optional(Schema.String),
  requiresIrreversibleConfirmation: Schema.Boolean,
  confirmationToken: Schema.optional(Schema.String),
  catalogId: Schema.optional(Schema.String),
  changes: Schema.Array(EditorReviewChange),
  addedCount: Schema.Int,
  changedCount: Schema.Int,
  removedCount: Schema.Int,
  changesOverflowed: Schema.Boolean,
  refusals: Schema.Array(EditorInterchangeRefusal),
});

export const EditorCommand = Schema.Union(
  Schema.TaggedStruct("InitializeApplication", {}),
  Schema.TaggedStruct("LoadWorkspace", {}),
  Schema.TaggedStruct("CheckExternalChanges", {}),
  Schema.TaggedStruct("PickWorkspace", {}),
  Schema.TaggedStruct("PreviewMutation", { request: EditorMutationRequest }),
  Schema.TaggedStruct("ApplyMutation", { request: EditorMutationRequest }),
  Schema.TaggedStruct("RecoverTransaction", { mode: Schema.Literal("complete", "rollback") }),
  Schema.TaggedStruct("Undo", {}),
  Schema.TaggedStruct("Redo", {}),
  Schema.TaggedStruct("ValidateDocument", { path: Schema.String, content: Schema.String }),
  Schema.TaggedStruct("PreviewMessage", {
    path: Schema.String,
    content: Schema.String,
    locale: Schema.String,
    key: Schema.String,
  }),
  Schema.TaggedStruct("SaveDocument", {
    path: Schema.String,
    content: Schema.String,
    revision: Schema.String,
  }),
  Schema.TaggedStruct("SaveReview", { request: EditorReviewSaveRequest }),
  Schema.TaggedStruct("About", {}),
  Schema.TaggedStruct("CreateDiagnosticBundle", {}),
  Schema.TaggedStruct("RevealDiagnosticBundle", { path: Schema.String }),
  Schema.TaggedStruct("DeleteDiagnosticBundle", { path: Schema.String }),
  Schema.TaggedStruct("LoadLocalState", {}),
  Schema.TaggedStruct("SaveLocalState", { entries: Schema.Array(EditorLocalStateEntry) }),
  Schema.TaggedStruct("ClearLocalState", {}),
  Schema.TaggedStruct("PreviewProject", { request: EditorProjectCreationRequest }),
  Schema.TaggedStruct("CreateProject", { request: EditorProjectCreationRequest }),
  Schema.TaggedStruct("OpenWorkspace", { request: EditorOpenWorkspaceRequest }),
  Schema.TaggedStruct("ExportXliff", { directory: Schema.optional(Schema.String) }),
  Schema.TaggedStruct("PreviewXliffImport", { path: Schema.String }),
  Schema.TaggedStruct("ApplyXliffImport", { confirmationToken: Schema.String }),
  Schema.TaggedStruct("ExportReviewJson", { path: Schema.optional(Schema.String) }),
  Schema.TaggedStruct("PreviewReviewJsonImport", { path: Schema.String }),
  Schema.TaggedStruct("ApplyReviewJsonImport", { confirmationToken: Schema.String }),
);

export const EditorReceipt = Schema.Union(
  Schema.TaggedStruct("ApplicationInitialized", { snapshot: WorkspaceSnapshot }),
  Schema.TaggedStruct("WorkspaceLoaded", { snapshot: WorkspaceSnapshot }),
  Schema.TaggedStruct("ExternalChangesChecked", { changes: EditorExternalChanges }),
  Schema.TaggedStruct("WorkspacePicked", { result: EditorWorkspacePickerResult }),
  Schema.TaggedStruct("MutationPreviewed", { preview: EditorMutationPreview }),
  Schema.TaggedStruct("MutationApplied", { result: EditorOperationResult }),
  Schema.TaggedStruct("TransactionRecovered", { result: EditorOperationResult }),
  Schema.TaggedStruct("UndoApplied", { result: EditorOperationResult }),
  Schema.TaggedStruct("RedoApplied", { result: EditorOperationResult }),
  Schema.TaggedStruct("DocumentValidated", { result: ValidationResult }),
  Schema.TaggedStruct("MessagePreviewed", { preview: EditorMessagePreview }),
  Schema.TaggedStruct("DocumentSaved", { result: EditorOperationResult }),
  Schema.TaggedStruct("ReviewSaved", { result: EditorReviewOperationResult }),
  Schema.TaggedStruct("AboutLoaded", { about: EditorAbout }),
  Schema.TaggedStruct("DiagnosticBundleCreated", { result: EditorDiagnosticBundleResult }),
  Schema.TaggedStruct("DiagnosticBundleRevealed", { result: EditorDiagnosticBundleActionResult }),
  Schema.TaggedStruct("DiagnosticBundleDeleted", { result: EditorDiagnosticBundleActionResult }),
  Schema.TaggedStruct("LocalStateLoaded", { state: EditorLocalStateSnapshot }),
  Schema.TaggedStruct("LocalStateSaved", { state: EditorLocalStateSnapshot }),
  Schema.TaggedStruct("LocalStateCleared", { result: EditorLocalStateClearResult }),
  Schema.TaggedStruct("ProjectPreviewed", { plan: EditorProjectPlan }),
  Schema.TaggedStruct("ProjectCreated", { result: EditorOperationResult }),
  Schema.TaggedStruct("WorkspaceOpened", { result: EditorOperationResult }),
  Schema.TaggedStruct("XliffExported", { result: EditorXliffExportResult }),
  Schema.TaggedStruct("XliffImportPreviewed", { preview: EditorXliffImportPreview }),
  Schema.TaggedStruct("XliffImportApplied", { result: EditorOperationResult }),
  Schema.TaggedStruct("ReviewJsonExported", { result: EditorReviewFileResult }),
  Schema.TaggedStruct("ReviewJsonImportPreviewed", { preview: EditorReviewImportPreview }),
  Schema.TaggedStruct("ReviewJsonImportApplied", { result: EditorReviewOperationResult }),
);

// The editor publishes no host events today; the stream stays empty by design,
// so the event union contains a single marker that the host never emits.
export const EditorEvent = Schema.TaggedStruct("EditorNoEvents", {});

export const EditorContract = defineApplicationContract({
  identity: "runic.translations.editor",
  version: 1,
  fingerprint: "45e9039e5f53ab97d6c57556a7001b97db527625a4f520592a2cb032c279d439",
  command: EditorCommand,
  receipt: EditorReceipt,
  event: EditorEvent,
  snapshot: WorkspaceSnapshot,
  initialize: { _tag: "InitializeApplication" } as const,
});

export type EditorCommand = typeof EditorCommand.Type;
export type EditorReceipt = typeof EditorReceipt.Type;
export type EditorEvent = Schema.Schema.Type<typeof EditorEvent>;
