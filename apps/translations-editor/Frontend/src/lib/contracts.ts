export interface EditorLocale {
  tag: string;
  fallback?: string;
}

export interface EditorLayer {
  name: string;
  priority: number;
}

export interface EditorCatalogSummary {
  id: string;
  manifestPaths: string[];
  documentCount: number;
  localeCount: number;
  messageCount: number;
  errorCount: number;
  warningCount: number;
  success: boolean;
}

export interface EditorCatalog {
  id: string;
  schemaVersion: number;
  defaultLocale: string;
  locales: EditorLocale[];
  layers: EditorLayer[];
}

export interface EditorDocument {
  path: string;
  content: string;
  revision: string;
  isManifest: boolean;
  isMalformed: boolean;
  locale?: string;
  layer?: string;
}

export interface EditorDiagnostic {
  id: string;
  severity: "error" | "warning";
  message: string;
  path: string;
  line: number;
  column: number;
  endLine: number;
  endColumn: number;
}

export interface WorkspaceSnapshot {
  root: string;
  catalog?: EditorCatalog;
  catalogs: EditorCatalogSummary[];
  documents: EditorDocument[];
  diagnostics: EditorDiagnostic[];
  success: boolean;
  pendingTransaction?: EditorPendingTransaction;
  review?: EditorReviewSnapshot;
  history?: EditorHistoryState;
}

export interface EditorPendingTransaction {
  catalogId: string;
  paths: string[];
}

export type EditorReviewState = "draft" | "translated" | "needs-review" | "approved";

export interface EditorReviewEntry {
  key: string;
  locale: string;
  state: EditorReviewState;
  note?: string;
  sourceFingerprint?: string;
  samples: Record<string, string>;
}

export interface EditorTerminologyEntry {
  source: string;
  preferred: string;
  locale?: string;
  note?: string;
}

export interface EditorReviewSnapshot {
  path: string;
  revision?: string;
  error?: string;
  entries: EditorReviewEntry[];
  terminology: EditorTerminologyEntry[];
}

export interface EditorReviewSaveRequest {
  expectedRevision?: string;
  entries: EditorReviewEntry[];
  terminology: EditorTerminologyEntry[];
}

export interface EditorReviewOperationResult {
  ok: boolean;
  message?: string;
  review?: EditorReviewSnapshot;
  history?: EditorHistoryState;
}

export interface EditorHistoryState {
  canUndo: boolean;
  canRedo: boolean;
  undoLabel?: string;
  redoLabel?: string;
}

export interface EditorAbout {
  product: string;
  version: string;
  updateChannel: string;
  commit?: string;
  runtime: string;
  runtimeIdentifier: string;
  operatingSystem: string;
  architecture: string;
}

export interface EditorDiagnosticBundleResult {
  ok: boolean;
  path?: string;
  message?: string;
}

export interface EditorDiagnosticBundleActionResult {
  ok: boolean;
  message?: string;
}

export interface EditorLocalStateEntry {
  key: string;
  value: string;
}

export interface EditorLocalStateSnapshot {
  entries: EditorLocalStateEntry[];
  recovered: boolean;
}

export interface EditorLocalStateClearResult {
  removedEntries: number;
  recovered: boolean;
}

export interface ValidationResult {
  success: boolean;
  diagnostics: EditorDiagnostic[];
}

export interface EditorMessagePreview {
  success: boolean;
  locale?: string;
  astJson?: string;
  diagnostics: EditorDiagnostic[];
}

export interface EditorOperationResult {
  ok: boolean;
  kind: string;
  message?: string;
  snapshot?: WorkspaceSnapshot;
  validation?: ValidationResult;
  history?: EditorHistoryState;
}

export interface EditorProjectLocaleRequest {
  tag: string;
  fallback?: string;
}

export interface EditorProjectCreationRequest {
  directory: string;
  catalogId: string;
  defaultLocale: string;
  additionalLocales: EditorProjectLocaleRequest[];
  codeNamespace: string;
  className: string;
  includeStarterMessage: boolean;
}

export interface EditorProjectPlan {
  ok: boolean;
  message?: string;
  directory: string;
  catalogId: string;
  locales: EditorLocale[];
  files: string[];
}

export interface EditorOpenWorkspaceRequest {
  directory: string;
  catalogId?: string;
}

export interface EditorExternalChanges {
  overflowed: boolean;
  paths: string[];
  changes: EditorExternalFileChange[];
}

export interface EditorExternalFileChange {
  path: string;
  exists: boolean;
  content?: string;
  revision?: string;
}

export interface EditorWorkspacePickerResult {
  ok: boolean;
  cancelled: boolean;
  directory?: string;
  message?: string;
}

export interface EditorMutationRequest {
  kind: "add-locale" | "remove-locale" | "set-fallback" | "create-key" | "rename-key" | "duplicate-key" | "delete-key";
  locale?: string;
  fallback?: string;
  replacementFallback?: string;
  copyFromLocale?: string;
  sourceKey?: string;
  targetKey?: string;
  initialValue?: string;
  confirmationToken?: string;
}

export interface EditorMutationFile {
  path: string;
  kind: string;
  beforeBytes: number;
  afterBytes: number;
}

export interface EditorMutationPreview {
  ok: boolean;
  message?: string;
  files: EditorMutationFile[];
  requiresIrreversibleConfirmation: boolean;
  confirmationToken?: string;
}

export interface EditorInterchangeLoss {
  code: string;
  location: string;
  message: string;
  semanticLoss: boolean;
}

export interface EditorInterchangeRefusal {
  code: string;
  message: string;
}

export interface EditorInterchangeFile {
  path: string;
  locale: string;
  byteCount: number;
}

export interface EditorXliffExportResult {
  ok: boolean;
  message?: string;
  catalogId?: string;
  documents: EditorInterchangeFile[];
  losses: EditorInterchangeLoss[];
  lossless: boolean;
}

export interface EditorReviewFileResult {
  ok: boolean;
  message?: string;
  path?: string;
  entryCount: number;
}

export type EditorKeyChangeKind = "added" | "changed" | "removed" | "state-change";

export interface EditorKeyChange {
  key: string;
  kind: EditorKeyChangeKind;
  before?: string;
  after?: string;
  stateBefore?: string;
  stateAfter?: string;
}

export interface EditorXliffImportPreview {
  ok: boolean;
  message?: string;
  requiresIrreversibleConfirmation: boolean;
  confirmationToken?: string;
  catalogId?: string;
  sourceLocale?: string;
  targetLocale?: string;
  layer?: string;
  changes: EditorKeyChange[];
  addedCount: number;
  changedCount: number;
  removedCount: number;
  unchangedCount: number;
  reviewUpdateCount: number;
  changesOverflowed: boolean;
  refusals: EditorInterchangeRefusal[];
}

export type EditorReviewChangeKind = "added" | "changed" | "removed";

export interface EditorReviewChange {
  key: string;
  locale: string;
  kind: EditorReviewChangeKind;
  stateBefore?: string;
  stateAfter?: string;
}

export interface EditorReviewImportPreview {
  ok: boolean;
  message?: string;
  requiresIrreversibleConfirmation: boolean;
  confirmationToken?: string;
  catalogId?: string;
  changes: EditorReviewChange[];
  addedCount: number;
  changedCount: number;
  removedCount: number;
  changesOverflowed: boolean;
  refusals: EditorInterchangeRefusal[];
}
