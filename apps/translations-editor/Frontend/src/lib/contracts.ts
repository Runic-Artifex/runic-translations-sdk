export interface EditorNotice {
  code: string;
  args: Array<{ name: string; value?: string; number?: number }>;
  detail?: string;
}

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

export interface EditorMessageEntry {
  key: string;
  content: string;
  valueStartByte: number;
  valueLengthBytes: number;
  authoring?: EditorMessageProjection;
  context?: EditorMessageContext;
  semantic?: EditorMessageSemantic;
}

export interface EditorDocumentDraft {
  success: boolean;
  content: string;
  entries: EditorMessageEntry[];
  diagnostics: EditorDiagnostic[];
}

export interface EditorDocument {
  path: string;
  content: string;
  revision: string;
  isManifest: boolean;
  isMalformed: boolean;
  entries?: EditorMessageEntry[];
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
  notice?: EditorNotice;
  span?: EditorDiagnosticSpan;
  sourceRevision?: string;
  quickFixes?: EditorDiagnosticQuickFix[];
  /** The message row a per-message check is about (RTR0010, RTR0090, RTR0094–RTR0098, or a diagnostic at a message name). */
  key?: string;
  locale?: string;
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
  /** The library catalogs the open project can override (T5d). */
  libraries?: EditorLibrarySummary[];
}

/** A library catalog an installed package packs; `overridden` when runic.json "overrides" lists it. */
export interface EditorLibrarySummary {
  catalog: string;
  package?: string;
  version?: string;
  origin?: "nuget" | "npm";
  overridden: boolean;
  problem?: string;
  overrideSources: number;
}

export interface EditorLibraryLocale {
  tag: string;
  fallback?: string;
  /** "library": the library declares it; "application": an override source adds it. */
  origin: "library" | "application";
  messages: number;
  libraryTranslated: number;
  overridden: number;
  covered: number;
  pending: boolean;
}

/** An override (layer 1) of a library message in one locale. */
export interface EditorLibraryOverride {
  locale: string;
  text: string;
  path: string;
  revision: string;
  /** The library's base text changed since the override was written; undefined when unknown. */
  changed?: boolean;
  known: boolean;
}

export interface EditorLibraryMessage {
  key: string;
  path: string;
  inputs: string[];
  content: string;
  structured: boolean;
  baseText: string;
  description?: string;
  /** The library's own text per locale (layer 0, read-only). */
  texts: Record<string, string>;
  overrides: EditorLibraryOverride[];
}

export interface EditorLibraryView {
  catalog: string;
  package?: string;
  version?: string;
  origin?: "nuget" | "npm";
  listed: boolean;
  problem?: string;
  baseLocale: string;
  layers: EditorLayer[];
  locales: EditorLibraryLocale[];
  messages: EditorLibraryMessage[];
  diagnostics: EditorDiagnostic[];
  libraryError?: string;
}

export interface EditorLibraryResult {
  ok: boolean;
  message?: EditorNotice;
  library?: EditorLibraryView;
  /** runic.json changed (the catalog joined "overrides"); reload the workspace. */
  projectChanged?: boolean;
}

export interface EditorLibraryOverrideRequest {
  catalog: string;
  key: string;
  locale: string;
  text?: string;
  expectedRevision?: string;
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
  /**
   * The SDK per-unit fingerprint of the source this state was recorded for. A state the user sets carries the
   * fingerprint of the base text the editor loaded (EditorReviewSnapshot.fingerprints), never the source on disk at save.
   */
  sourceFingerprint?: string;
  samples: Record<string, string>;
  /** The source changed since this state was recorded (RTR0090). Computed by the backend. */
  stale?: boolean;
  /** A note or samples without a review-file entry: the state is inferred, and saving writes no review-file entry. */
  implicit?: boolean;
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
  /** A one-time notice, such as the move of editor approvals into the SDK review file. */
  notice?: EditorNotice;
  /** The SDK per-unit fingerprint of each base message as loaded; absent when runic.json or the base locale has errors. */
  fingerprints?: Record<string, string>;
  /** Version 1 editor approvals wait to move into the review file: the entries are shown read-only. */
  migrationPending?: boolean;
}

export interface EditorReviewSaveRequest {
  expectedRevision?: string;
  entries: EditorReviewEntry[];
  terminology: EditorTerminologyEntry[];
  /** The entries and terminology the edits started from: a changed review file gets only the user's changes re-applied. */
  original?: EditorReviewEntry[];
  originalTerminology?: EditorTerminologyEntry[];
}

export interface EditorReviewOperationResult {
  ok: boolean;
  message?: EditorNotice;
  review?: EditorReviewSnapshot;
  history?: EditorHistoryState;
}

export interface EditorHistoryState {
  canUndo: boolean;
  canRedo: boolean;
  undoLabel?: EditorNotice;
  redoLabel?: EditorNotice;
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
  message?: EditorNotice;
}

export interface EditorDiagnosticBundleActionResult {
  ok: boolean;
  message?: EditorNotice;
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
  renderedJson?: string;
  diagnostics: EditorDiagnostic[];
}

export interface EditorOperationResult {
  ok: boolean;
  kind: string;
  message?: EditorNotice;
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
  message?: EditorNotice;
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
  /** The review data after another writer (the command line, the language server) changed the review file. */
  review?: EditorReviewSnapshot;
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
  message?: EditorNotice;
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
  message?: EditorNotice;
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
  message: EditorNotice;
}

export interface EditorInterchangeFile {
  path: string;
  locale: string;
  byteCount: number;
}

export interface EditorXliffExportResult {
  ok: boolean;
  message?: EditorNotice;
  catalogId?: string;
  documents: EditorInterchangeFile[];
  losses: EditorInterchangeLoss[];
  lossless: boolean;
}

export interface EditorReviewFileResult {
  ok: boolean;
  message?: EditorNotice;
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
  message?: EditorNotice;
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
  message?: EditorNotice;
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

export interface EditorMessageInput { name: string; type: string; function?: string; declared: boolean; }
export interface EditorMessageVariant { id: string; keys: string[]; pattern: string; startByte: number; lengthBytes: number; }
export interface EditorMessageProjection {
  revision: string; supported: boolean; reason?: string;
  inputs: EditorMessageInput[]; selectors: string[]; selectorFunctions: string[]; variants: EditorMessageVariant[];
  pluralCategories: string[]; selectorPluralCategories: string[][]; cldrVersion: string;
}
export interface EditorMessageOperation {
  kind: "set-pattern" | "rename-input" | "add-input" | "remove-input" | "set-input" | "set-selectors" | "add-variant" | "remove-variant";
  variantId?: string; pattern?: string; name?: string; newName?: string; function?: string;
  keys?: string[]; selectors?: string[];
}
export interface EditorMessageContext { comments: string[]; tags: string[]; examples: Record<string, string>[]; }
export interface EditorMessageSemantic { text: string[]; placeholders: string[]; slots: string[]; supported: boolean; hasBoundaryWhitespace: boolean; }

export interface EditorDiagnosticSpan { startByte: number; lengthBytes: number; startUtf16: number; lengthUtf16: number; startLine: number; startCharacter: number; endLine: number; endCharacter: number; }
export interface EditorDiagnosticQuickFix { id: string; title: string; diagnosticId: string; location: { path: string; startByte: number; lengthBytes: number; line: number; column: number; endLine: number; endColumn: number }; expectedRevision: string; startByte: number; lengthBytes: number; newText: string; }
