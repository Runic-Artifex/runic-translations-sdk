import { Schema } from "effect";
import { bridge, defineApplicationBridgeContract } from "@runic-artifex/application-bridge";

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

const EditorReviewState = Schema.Literal("draft", "translated", "needs-review", "approved");

const EditorReviewEntry = Schema.Struct({
  key: Schema.String,
  locale: Schema.String,
  state: EditorReviewState,
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

// The native host owns these records in the user's application-data directory.
// Keeping the values as an ordered key/value list avoids a browser-owned
// dictionary and keeps the generated C# contract portable across runtimes.
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

const MutationKind = Schema.Literal(
  "add-locale",
  "remove-locale",
  "set-fallback",
  "create-key",
  "rename-key",
  "duplicate-key",
  "delete-key",
);

const EditorMutationRequest = Schema.Struct({
  kind: MutationKind,
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

// XLIFF interchange (W03): losses are reported by the tooling exporter, while
// refusals are hard import dead-ends (e.g. structured messages) that must stay
// visible instead of being swallowed as generic failures.
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

// The editor keeps authoritative conflict detection inside EditorSession
// (file revisions, review sidecars, confirmation tokens), so no bridge command
// advances the transport revision; expectedRevision stays neutral by design.
const command = <C extends Schema.Schema.Any>(
  tag: string,
  schema: C,
  receipt: string,
) => ({
  tag,
  schema,
  receipt,
  startsOperation: false,
  cancellable: false,
  advancesRevision: false,
});

const commands = [
  command("InitializeApplication", Schema.TaggedStruct("InitializeApplication", {}), "ApplicationInitialized"),
  command("LoadWorkspace", Schema.TaggedStruct("LoadWorkspace", {}), "WorkspaceLoaded"),
  command("CheckExternalChanges", Schema.TaggedStruct("CheckExternalChanges", {}), "ExternalChangesChecked"),
  command("PickWorkspace", Schema.TaggedStruct("PickWorkspace", {}), "WorkspacePicked"),
  command("PreviewMutation", Schema.TaggedStruct("PreviewMutation", { request: EditorMutationRequest }), "MutationPreviewed"),
  command("ApplyMutation", Schema.TaggedStruct("ApplyMutation", { request: EditorMutationRequest }), "MutationApplied"),
  command(
    "RecoverTransaction",
    Schema.TaggedStruct("RecoverTransaction", { mode: Schema.Literal("complete", "rollback") }),
    "TransactionRecovered",
  ),
  command("Undo", Schema.TaggedStruct("Undo", {}), "UndoApplied"),
  command("Redo", Schema.TaggedStruct("Redo", {}), "RedoApplied"),
  command(
    "ValidateDocument",
    Schema.TaggedStruct("ValidateDocument", { path: Schema.String, content: Schema.String }),
    "DocumentValidated",
  ),
  command(
    "PreviewMessage",
    Schema.TaggedStruct("PreviewMessage", {
      path: Schema.String,
      content: Schema.String,
      locale: Schema.String,
      key: Schema.String,
    }),
    "MessagePreviewed",
  ),
  command(
    "SaveDocument",
    Schema.TaggedStruct("SaveDocument", {
      path: Schema.String,
      content: Schema.String,
      revision: Schema.String,
    }),
    "DocumentSaved",
  ),
  command("SaveReview", Schema.TaggedStruct("SaveReview", { request: EditorReviewSaveRequest }), "ReviewSaved"),
  command("About", Schema.TaggedStruct("About", {}), "AboutLoaded"),
  command("CreateDiagnosticBundle", Schema.TaggedStruct("CreateDiagnosticBundle", {}), "DiagnosticBundleCreated"),
  command("RevealDiagnosticBundle", Schema.TaggedStruct("RevealDiagnosticBundle", { path: Schema.String }), "DiagnosticBundleRevealed"),
  command("DeleteDiagnosticBundle", Schema.TaggedStruct("DeleteDiagnosticBundle", { path: Schema.String }), "DiagnosticBundleDeleted"),
  command("LoadLocalState", Schema.TaggedStruct("LoadLocalState", {}), "LocalStateLoaded"),
  command(
    "SaveLocalState",
    Schema.TaggedStruct("SaveLocalState", { entries: Schema.Array(EditorLocalStateEntry) }),
    "LocalStateSaved",
  ),
  command("ClearLocalState", Schema.TaggedStruct("ClearLocalState", {}), "LocalStateCleared"),
  command(
    "PreviewProject",
    Schema.TaggedStruct("PreviewProject", { request: EditorProjectCreationRequest }),
    "ProjectPreviewed",
  ),
  command("CreateProject", Schema.TaggedStruct("CreateProject", { request: EditorProjectCreationRequest }), "ProjectCreated"),
  command("OpenWorkspace", Schema.TaggedStruct("OpenWorkspace", { request: EditorOpenWorkspaceRequest }), "WorkspaceOpened"),
  command(
    "ExportXliff",
    Schema.TaggedStruct("ExportXliff", { directory: Schema.optional(Schema.String) }),
    "XliffExported",
  ),
  command(
    "PreviewXliffImport",
    Schema.TaggedStruct("PreviewXliffImport", { path: Schema.String }),
    "XliffImportPreviewed",
  ),
  command(
    "ApplyXliffImport",
    Schema.TaggedStruct("ApplyXliffImport", { confirmationToken: Schema.String }),
    "XliffImportApplied",
  ),
  command(
    "ExportReviewJson",
    Schema.TaggedStruct("ExportReviewJson", { path: Schema.optional(Schema.String) }),
    "ReviewJsonExported",
  ),
  command(
    "PreviewReviewJsonImport",
    Schema.TaggedStruct("PreviewReviewJsonImport", { path: Schema.String }),
    "ReviewJsonImportPreviewed",
  ),
  command(
    "ApplyReviewJsonImport",
    Schema.TaggedStruct("ApplyReviewJsonImport", { confirmationToken: Schema.String }),
    "ReviewJsonImportApplied",
  ),
];

const receipts = [
  { tag: "ApplicationInitialized", schema: Schema.TaggedStruct("ApplicationInitialized", { snapshot: WorkspaceSnapshot }) },
  { tag: "WorkspaceLoaded", schema: Schema.TaggedStruct("WorkspaceLoaded", { snapshot: WorkspaceSnapshot }) },
  { tag: "ExternalChangesChecked", schema: Schema.TaggedStruct("ExternalChangesChecked", { changes: EditorExternalChanges }) },
  { tag: "WorkspacePicked", schema: Schema.TaggedStruct("WorkspacePicked", { result: EditorWorkspacePickerResult }) },
  { tag: "MutationPreviewed", schema: Schema.TaggedStruct("MutationPreviewed", { preview: EditorMutationPreview }) },
  { tag: "MutationApplied", schema: Schema.TaggedStruct("MutationApplied", { result: EditorOperationResult }) },
  { tag: "TransactionRecovered", schema: Schema.TaggedStruct("TransactionRecovered", { result: EditorOperationResult }) },
  { tag: "UndoApplied", schema: Schema.TaggedStruct("UndoApplied", { result: EditorOperationResult }) },
  { tag: "RedoApplied", schema: Schema.TaggedStruct("RedoApplied", { result: EditorOperationResult }) },
  { tag: "DocumentValidated", schema: Schema.TaggedStruct("DocumentValidated", { result: ValidationResult }) },
  { tag: "MessagePreviewed", schema: Schema.TaggedStruct("MessagePreviewed", { preview: EditorMessagePreview }) },
  { tag: "DocumentSaved", schema: Schema.TaggedStruct("DocumentSaved", { result: EditorOperationResult }) },
  { tag: "ReviewSaved", schema: Schema.TaggedStruct("ReviewSaved", { result: EditorReviewOperationResult }) },
  { tag: "AboutLoaded", schema: Schema.TaggedStruct("AboutLoaded", { about: EditorAbout }) },
  { tag: "DiagnosticBundleCreated", schema: Schema.TaggedStruct("DiagnosticBundleCreated", { result: EditorDiagnosticBundleResult }) },
  { tag: "DiagnosticBundleRevealed", schema: Schema.TaggedStruct("DiagnosticBundleRevealed", { result: EditorDiagnosticBundleActionResult }) },
  { tag: "DiagnosticBundleDeleted", schema: Schema.TaggedStruct("DiagnosticBundleDeleted", { result: EditorDiagnosticBundleActionResult }) },
  { tag: "LocalStateLoaded", schema: Schema.TaggedStruct("LocalStateLoaded", { state: EditorLocalStateSnapshot }) },
  { tag: "LocalStateSaved", schema: Schema.TaggedStruct("LocalStateSaved", { state: EditorLocalStateSnapshot }) },
  { tag: "LocalStateCleared", schema: Schema.TaggedStruct("LocalStateCleared", { result: EditorLocalStateClearResult }) },
  { tag: "ProjectPreviewed", schema: Schema.TaggedStruct("ProjectPreviewed", { plan: EditorProjectPlan }) },
  { tag: "ProjectCreated", schema: Schema.TaggedStruct("ProjectCreated", { result: EditorOperationResult }) },
  { tag: "WorkspaceOpened", schema: Schema.TaggedStruct("WorkspaceOpened", { result: EditorOperationResult }) },
  { tag: "XliffExported", schema: Schema.TaggedStruct("XliffExported", { result: EditorXliffExportResult }) },
  { tag: "XliffImportPreviewed", schema: Schema.TaggedStruct("XliffImportPreviewed", { preview: EditorXliffImportPreview }) },
  { tag: "XliffImportApplied", schema: Schema.TaggedStruct("XliffImportApplied", { result: EditorOperationResult }) },
  { tag: "ReviewJsonExported", schema: Schema.TaggedStruct("ReviewJsonExported", { result: EditorReviewFileResult }) },
  { tag: "ReviewJsonImportPreviewed", schema: Schema.TaggedStruct("ReviewJsonImportPreviewed", { preview: EditorReviewImportPreview }) },
  { tag: "ReviewJsonImportApplied", schema: Schema.TaggedStruct("ReviewJsonImportApplied", { result: EditorReviewOperationResult }) },
];

const receiptByTag = new Map(receipts.map((receipt) => [receipt.tag, receipt.schema] as const));
const bridgeCommands = commands.map((item) => {
  const receipt = receiptByTag.get(item.receipt);
  if (receipt === undefined) throw new TypeError(`Receipt '${item.receipt}' is not declared.`);
  return bridge.command(item.schema, {
    receipt,
    startsOperation: item.startsOperation,
    cancellable: item.cancellable,
    advancesRevision: item.advancesRevision,
  });
});

export default defineApplicationBridgeContract({
  protocol: { identity: "runic.translations.editor", version: 1 },
  csharp: { namespace: "Runic.Translations.Editor.Contract", contractName: "Editor" },
  snapshot: WorkspaceSnapshot,
  commands: bridgeCommands,
  events: [],
  errors: [],
  initialize: { _tag: "InitializeApplication" },
});

export type EditorCommand = (typeof commands)[number]["schema"]["Type"];
export type EditorReceipt = (typeof receipts)[number]["schema"]["Type"];
export type EditorEvent = never;
