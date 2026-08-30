import { Effect } from "effect";
import { MockApplicationBridge } from "@runic-artifex/application-bridge";
import type { EditorCommand, EditorReceipt } from "./editor-contract";
import type {
  EditorDocument,
  EditorProjectCreationRequest,
  EditorReviewSaveRequest,
  EditorReviewSnapshot,
  WorkspaceSnapshot,
} from "./contracts";
import { sourceMessageToArtifact, toStructuredMessage } from "./message-composer";

const manifest = document("product.catalog.json", undefined, undefined, {
  schemaVersion: 2,
  catalog: "customer-product",
  code: { namespace: "Customer.Product", className: "ProductText", visibility: "public" },
  defaultLocale: "de",
  locales: [{ tag: "de" }, { tag: "en", fallback: "de" }, { tag: "fr", fallback: "de" }],
  layers: [{ name: "base", priority: 0 }],
  validation: { translationCompleteness: "warning", extraLocaleKeys: "error", emptyValues: "error" },
});
manifest.isManifest = true;

let snapshot: WorkspaceSnapshot = {
  root: "/mock/customer-product/Resources",
  catalog: {
    id: "customer-product",
    schemaVersion: 2,
    defaultLocale: "de",
    locales: [{ tag: "de" }, { tag: "en", fallback: "de" }, { tag: "fr", fallback: "de" }],
    layers: [{ name: "base", priority: 0 }],
  },
  catalogs: [{
    id: "customer-product",
    manifestPaths: ["product.catalog.json"],
    documentCount: 3,
    localeCount: 3,
    messageCount: 4,
    errorCount: 0,
    warningCount: 0,
    success: true,
  }],
  documents: [
    manifest,
    document("product.de.json", "de", "base", resources("Speichern", "Abbrechen", "Willkommen zurück, {name}")),
    document("product.en.json", "en", "base", resources("Save", "Cancel", "Welcome back, {name}")),
    document("product.fr.json", "fr", "base", {
      schemaVersion: 2,
      catalog: "customer-product",
      locale: "fr",
      layer: "base",
      resources: { Common: { Save: "Enregistrer" } },
    }),
  ],
  diagnostics: [],
  success: true,
  review: {
    path: ".runic-translations/customer-product.editor-state.json",
    revision: "mock-review-1",
    entries: [
      { key: "Common.Save", locale: "de", state: "approved", sourceFingerprint: "outdated", samples: {} },
      { key: "Dashboard.Welcome", locale: "en", state: "needs-review", note: "Check the tone.", samples: { name: "Ada" } },
    ],
    terminology: [{ source: "Save", preferred: "Speichern", locale: "de", note: "Use for action buttons." }],
  },
  history: { canUndo: false, canRedo: false },
};
const MAXIMUM_HISTORY_ENTRIES = 64;
const MAXIMUM_HISTORY_BYTES = 32 * 1024 * 1024;

let destructiveConfirmation: { token: string; request: string; fingerprint: string } | undefined;
let preparedXliffImport: { token: string; fingerprint: string } | undefined;
let preparedReviewImport: { token: string; fingerprint: string } | undefined;
type MockHistoryEntry =
  | { kind: "document"; label: string; path: string; before: EditorDocument; after: EditorDocument; bytes: number }
  | { kind: "review"; label: string; before: EditorReviewSnapshot | undefined; after: EditorReviewSnapshot | undefined; bytes: number };
let undoStack: MockHistoryEntry[] = [];
let redoStack: MockHistoryEntry[] = [];
let historyBytes = 0;
let localState = new Map<string, string>();

function historySize(value: unknown): number {
  return new TextEncoder().encode(JSON.stringify(value)).byteLength;
}

function clearRedo(): void {
  for (const entry of redoStack) historyBytes -= entry.bytes;
  redoStack = [];
}

function clearHistory(): void {
  undoStack = [];
  redoStack = [];
  historyBytes = 0;
}

function recordHistory(entry: MockHistoryEntry | undefined): void {
  clearRedo();
  if (entry === undefined || entry.bytes > MAXIMUM_HISTORY_BYTES) {
    syncHistory();
    return;
  }
  undoStack = [...undoStack, entry];
  historyBytes += entry.bytes;
  while (undoStack.length > MAXIMUM_HISTORY_ENTRIES || historyBytes > MAXIMUM_HISTORY_BYTES) {
    const oldest = undoStack.shift();
    if (oldest !== undefined) historyBytes -= oldest.bytes;
  }
  syncHistory();
}

function recordDocumentHistory(before: EditorDocument, after: EditorDocument): void {
  const entry = {
    kind: "document" as const,
    label: `Save ${after.path}`,
    path: after.path,
    before: structuredClone(before),
    after: structuredClone(after),
  };
  recordHistory({ ...entry, bytes: historySize(entry) });
}

function recordReviewHistory(before: EditorReviewSnapshot | undefined, after: EditorReviewSnapshot | undefined): void {
  const entry = {
    kind: "review" as const,
    label: "Save workflow",
    before: structuredClone(before),
    after: structuredClone(after),
  };
  recordHistory({ ...entry, bytes: historySize(entry) });
}

function applyHistoryEntry(entry: MockHistoryEntry, undo: boolean): boolean {
  if (entry.kind === "document") {
    const current = snapshot.documents.find((document) => document.path === entry.path);
    const expected = undo ? entry.after : entry.before;
    const replacement = undo ? entry.before : entry.after;
    if (current?.revision !== expected.revision) return false;
    snapshot.documents = snapshot.documents.map((document) =>
      document.path === entry.path ? structuredClone(replacement) : document);
    return true;
  }

  const expected = undo ? entry.after : entry.before;
  const replacement = undo ? entry.before : entry.after;
  if (snapshot.review?.revision !== expected?.revision) return false;
  snapshot.review = structuredClone(replacement);
  return true;
}

function syncHistory(): void {
  snapshot.history = {
    canUndo: undoStack.length > 0,
    canRedo: redoStack.length > 0,
    undoLabel: undoStack.at(-1)?.label,
    redoLabel: redoStack.at(-1)?.label,
  };
}

function workspaceFingerprint(): string {
  return JSON.stringify({
    root: snapshot.root,
    catalog: snapshot.catalog,
    reviewRevision: snapshot.review?.revision,
    documents: snapshot.documents.map((document) => [document.path, document.revision]),
  });
}

function normalizeMockDocumentPath(path: string): string | undefined {
  const normalized = path.replaceAll("\\", "/").replace(/^\/+/, "");
  if (normalized === "" || normalized.split("/").includes("..")) return undefined;
  return normalized;
}

function syncMockManifest(): void {
  const catalog = snapshot.catalog;
  const manifest = snapshot.documents.find((document) => document.isManifest);
  if (catalog === undefined || manifest === undefined) return;
  const root = resourceRoot(manifest);
  root.defaultLocale = catalog.defaultLocale;
  root.locales = catalog.locales;
  root.layers = catalog.layers;
  manifest.content = JSON.stringify(root, null, 2) + "\n";
  manifest.revision = crypto.randomUUID();
  const summary = snapshot.catalogs.find((candidate) => candidate.id === catalog.id);
  if (summary !== undefined) {
    summary.localeCount = catalog.locales.length;
    summary.documentCount = snapshot.documents.length;
  }
}

function applyMockMutation(request: { kind: string; locale?: string; fallback?: string; replacementFallback?: string; copyFromLocale?: string; sourceKey?: string; targetKey?: string; initialValue?: string; layer?: string }): void {
  if (request.kind === "add-locale" && request.locale !== undefined && snapshot.catalog !== undefined) {
    const source = snapshot.documents.find((document) => document.locale === (request.copyFromLocale ?? snapshot.catalog?.defaultLocale) && document.layer === request.layer);
    if (source !== undefined) {
      const root = resourceRoot(source);
      root.locale = request.locale;
      const added = document(`product.${request.locale}.json`, request.locale, request.layer, root);
      snapshot.documents = [...snapshot.documents, added];
      snapshot.catalog.locales = [...snapshot.catalog.locales, { tag: request.locale, fallback: request.fallback }];
    }
    return;
  }
  if (request.kind === "remove-locale" && request.locale !== undefined && snapshot.catalog !== undefined) {
    snapshot.documents = snapshot.documents.filter((document) => document.locale !== request.locale);
    snapshot.catalog.locales = snapshot.catalog.locales
      .filter((locale) => locale.tag !== request.locale)
      .map((locale) => locale.fallback === request.locale ? { ...locale, fallback: request.replacementFallback } : locale);
    return;
  }
  if (request.kind === "set-fallback" && request.locale !== undefined && snapshot.catalog !== undefined) {
    snapshot.catalog.locales = snapshot.catalog.locales.map((locale) =>
      locale.tag === request.locale ? { ...locale, fallback: request.fallback } : locale);
    return;
  }
  const resources = snapshot.documents.filter((document) => !document.isManifest &&
    (request.layer === undefined || document.layer === request.layer));
  if (request.kind === "create-key" && request.targetKey !== undefined) {
    for (const document of resources) setResource(document, request.targetKey, request.initialValue ?? "");
    return;
  }
  if (request.sourceKey === undefined) return;
  for (const document of resources) {
    const value = getResource(document, request.sourceKey);
    if (value === undefined) continue;
    if (request.kind !== "duplicate-key") deleteResource(document, request.sourceKey);
    if (request.kind !== "delete-key" && request.targetKey !== undefined) setResource(document, request.targetKey, value);
  }
}

function resourceRoot(document: EditorDocument): Record<string, unknown> {
  return JSON.parse(document.content) as Record<string, unknown>;
}

function getResource(document: EditorDocument, key: string): unknown {
  let current: unknown = resourceRoot(document).resources;
  for (const segment of key.split(".")) {
    if (typeof current !== "object" || current === null || !(segment in current)) return undefined;
    current = (current as Record<string, unknown>)[segment];
  }
  return structuredClone(current);
}

function setResource(document: EditorDocument, key: string, value: unknown): void {
  const root = resourceRoot(document);
  let current = root.resources as Record<string, unknown>;
  const segments = key.split(".");
  for (const segment of segments.slice(0, -1)) {
    current = (current[segment] ??= {}) as Record<string, unknown>;
  }
  current[segments.at(-1)!] = structuredClone(value);
  document.content = JSON.stringify(root, null, 2) + "\n";
  document.revision = crypto.randomUUID();
}

function deleteResource(document: EditorDocument, key: string): void {
  const root = resourceRoot(document);
  let current = root.resources as Record<string, unknown>;
  const segments = key.split(".");
  for (const segment of segments.slice(0, -1)) {
    const next = current[segment];
    if (typeof next !== "object" || next === null) return;
    current = next as Record<string, unknown>;
  }
  delete current[segments.at(-1)!];
  document.content = JSON.stringify(root, null, 2) + "\n";
  document.revision = crypto.randomUUID();
}

async function load(): Promise<WorkspaceSnapshot> {
  return structuredClone(snapshot);
}

async function checkExternalChanges() {
  return { overflowed: false, paths: [], changes: [] };
}

async function pickWorkspace() {
  return { ok: true, cancelled: false, directory: "/mock/multi-catalog" };
}

async function previewMutation(request: MockMutationRequest) {
  const path = request.kind.includes("locale") || request.kind === "set-fallback"
    ? "product.catalog.json"
    : "product.de.json";
  const requiresIrreversibleConfirmation = true;
  const confirmationToken = crypto.randomUUID();
  destructiveConfirmation = {
    token: confirmationToken,
    request: JSON.stringify({ ...request, confirmationToken: undefined }),
    fingerprint: workspaceFingerprint(),
  };
  return {
    ok: true,
    files: [{ path, kind: "replace", beforeBytes: 512, afterBytes: 548 }],
    requiresIrreversibleConfirmation,
    confirmationToken,
  };
}

async function applyMutation(request: MockMutationRequest & { confirmationToken?: string | undefined }) {
  if (destructiveConfirmation === undefined ||
    destructiveConfirmation.token !== request.confirmationToken ||
    destructiveConfirmation.request !== JSON.stringify({ ...request, confirmationToken: undefined }) ||
    destructiveConfirmation.fingerprint !== workspaceFingerprint()) {
    destructiveConfirmation = undefined;
    return { ok: false, kind: "irreversible-confirmation", message: "Preview this destructive change again and confirm the exact affected files." };
  }
  // One-use after a validated attempt, including an operation that later fails.
  destructiveConfirmation = undefined;
  const before = structuredClone(snapshot);
  applyMockMutation(request as Parameters<typeof applyMockMutation>[0]);
  syncMockManifest();
  if (snapshot.documents.some((document) => new TextEncoder().encode(document.content).byteLength > 8 * 1024 * 1024)) {
    snapshot = before;
    return { ok: false, kind: "resource-limit", message: "Mock workspace exceeds the editor size limit." };
  }
  recordHistory(undefined);
  return { ok: true, kind: "mutated", snapshot: structuredClone(snapshot) };
}

async function recoverTransaction() {
  snapshot.pendingTransaction = undefined;
  destructiveConfirmation = undefined;
  clearHistory();
  syncHistory();
  return { ok: true, kind: "recovered", snapshot: structuredClone(snapshot) };
}

async function undo() {
  const entry = undoStack.pop();
  if (entry === undefined) return { ok: false, kind: "nothing-to-undo", message: "There is no saved change to undo.", history: snapshot.history };
  if (!applyHistoryEntry(entry, true)) {
    clearHistory();
    syncHistory();
    return { ok: false, kind: "conflict", message: "The saved target changed after this operation; history was cleared.", history: snapshot.history };
  }
  destructiveConfirmation = undefined;
  redoStack.push(entry);
  syncHistory();
  return { ok: true, kind: "undone", snapshot: structuredClone(snapshot), history: snapshot.history };
}

async function redo() {
  const entry = redoStack.pop();
  if (entry === undefined) return { ok: false, kind: "nothing-to-redo", message: "There is no saved change to redo.", history: snapshot.history };
  if (!applyHistoryEntry(entry, false)) {
    clearHistory();
    syncHistory();
    return { ok: false, kind: "conflict", message: "The saved target changed after this operation; history was cleared.", history: snapshot.history };
  }
  destructiveConfirmation = undefined;
  undoStack.push(entry);
  syncHistory();
  return { ok: true, kind: "redone", snapshot: structuredClone(snapshot), history: snapshot.history };
}

async function validate(path: string, content: string) {
  try {
    JSON.parse(content);
    return { success: true, diagnostics: [] };
  } catch (error) {
    return {
      success: false,
      diagnostics: [{
        id: "JSON",
        severity: "error",
        message: error instanceof Error ? error.message : "Invalid JSON",
        path,
        line: 1,
        column: 1,
        endLine: 1,
        endColumn: 1,
      }],
    };
  }
}

async function previewMessage(path: string, content: string, locale: string, key: string) {
  try {
    const root = JSON.parse(content) as Record<string, unknown>;
    let value: unknown = root.resources;
    for (const segment of key.split(".")) value = (value as Record<string, unknown>)[segment];
    if (typeof value === "object" && value !== null && "$value" in value) {
      value = (value as Record<string, unknown>).$value;
    }
    const artifact = sourceMessageToArtifact(toStructuredMessage(value as string | Record<string, unknown>));
    return { success: true, locale, astJson: JSON.stringify(artifact), diagnostics: [] };
  } catch (error) {
    return {
      success: false,
      diagnostics: [{
        id: "PREVIEW", severity: "error",
        message: error instanceof Error ? error.message : String(error),
        path, line: 1, column: 1, endLine: 1, endColumn: 1,
      }],
    };
  }
}

async function saveReview(request: EditorReviewSaveRequest) {
  if (request.expectedRevision !== snapshot.review?.revision) {
    return {
      ok: false,
      message: "The editor-state sidecar changed on disk. Reload before saving review data.",
      history: snapshot.history,
    };
  }
  const before = structuredClone(snapshot.review);
  snapshot.review = {
    path: snapshot.review?.path ?? ".runic-translations/customer-product.editor-state.json",
    revision: crypto.randomUUID(),
    entries: structuredClone(request.entries),
    terminology: structuredClone(request.terminology),
  };
  recordReviewHistory(before, snapshot.review);
  destructiveConfirmation = undefined;
  return { ok: true, review: structuredClone(snapshot.review), history: snapshot.history };
}

async function about() {
  return {
    product: "Runic Translations Editor",
    version: "1.0.0+mock",
    updateChannel: "preview",
    commit: "mock",
    runtime: ".NET 10 (mock bridge)",
    runtimeIdentifier: "browser-mock",
    operatingSystem: navigator.platform || "Browser",
    architecture: "unknown",
  };
}

async function createDiagnosticBundle() {
  return { ok: true, path: "/mock/state/RunicArtifex/Runic.Translations.Editor/Diagnostics/runic-translations-editor-diagnostics-mock.zip" };
}

async function revealDiagnosticBundle() {
  return { ok: true };
}

async function deleteDiagnosticBundle() {
  return { ok: true };
}

async function exportXliff(directory: string | undefined) {
  const catalog = snapshot.catalog;
  if (catalog === undefined) return { ok: false, message: "Choose a catalog before exporting XLIFF.", documents: [], losses: [], lossless: false };
  const outputDirectory = directory?.trim() || "interchange/xliff";
  return {
    ok: true,
    catalogId: catalog.id,
    documents: catalog.locales
      .filter((locale) => locale.tag !== catalog.defaultLocale)
      .map((locale) => ({ path: `${outputDirectory}/${catalog.id}.${locale.tag}.xliff`, locale: locale.tag, byteCount: 512 })),
    losses: [],
    lossless: true,
  };
}

async function previewXliffImport(path: string) {
  if (path.trim() === "") {
    return { ok: false, message: "Choose an XLIFF file to import.", requiresIrreversibleConfirmation: false, changes: [], addedCount: 0, changedCount: 0, removedCount: 0, unchangedCount: 0, reviewUpdateCount: 0, changesOverflowed: false, refusals: [{ code: "EDITOR-IO", message: "The import path is empty." }] };
  }
  if (path.includes("structured")) {
    return { ok: false, message: "This mock file contains a structured message that XLIFF 2.1 cannot safely import.", requiresIrreversibleConfirmation: false, changes: [], addedCount: 0, changedCount: 0, removedCount: 0, unchangedCount: 0, reviewUpdateCount: 0, changesOverflowed: false, refusals: [{ code: "XLIFF21-STRUCTURED-IMPORT", message: "Structured messages require a lossless interchange format and were not imported." }] };
  }
  const token = crypto.randomUUID();
  preparedXliffImport = { token, fingerprint: workspaceFingerprint() };
  return {
    ok: true, catalogId: snapshot.catalog?.id, sourceLocale: "de", targetLocale: "en", layer: "base",
    requiresIrreversibleConfirmation: true, confirmationToken: token,
    changes: [
      { key: "Dashboard.Welcome", kind: "changed", before: "Welcome back, {name}", after: "Welcome again, {name}" },
      { key: "Common.Save", kind: "state-change", stateBefore: "draft", stateAfter: "needs-review" },
    ],
    addedCount: 0, changedCount: 1, removedCount: 0, unchangedCount: 3, reviewUpdateCount: 1,
    changesOverflowed: false, refusals: [],
  };
}

async function applyXliffImport(confirmationToken: string) {
  if (preparedXliffImport?.token !== confirmationToken || preparedXliffImport.fingerprint !== workspaceFingerprint()) {
    preparedXliffImport = undefined;
    return { ok: false, kind: "irreversible-confirmation", message: "Preview this import again to obtain a valid confirmation token.", history: snapshot.history };
  }
  preparedXliffImport = undefined;
  const document = snapshot.documents.find((candidate) => candidate.locale === "en" && candidate.layer === "base");
  if (document !== undefined) {
    const before = structuredClone(document);
    setResource(document, "Dashboard.Welcome", "Welcome again, {name}");
    recordDocumentHistory(before, document);
  }
  return { ok: true, kind: "imported", snapshot: structuredClone(snapshot), history: snapshot.history };
}

async function exportReviewJson(path: string | undefined) {
  return {
    ok: true,
    path: path?.trim() || "interchange/review.json",
    entryCount: snapshot.review?.entries.length ?? 0,
  };
}

async function previewReviewJsonImport(path: string) {
  if (path.trim() === "") {
    return { ok: false, message: "Choose a review JSON file to import.", requiresIrreversibleConfirmation: false, changes: [], addedCount: 0, changedCount: 0, removedCount: 0, changesOverflowed: false, refusals: [{ code: "EDITOR-IO", message: "The import path is empty." }] };
  }
  const token = crypto.randomUUID();
  preparedReviewImport = { token, fingerprint: workspaceFingerprint() };
  return {
    ok: true, catalogId: snapshot.catalog?.id, requiresIrreversibleConfirmation: true, confirmationToken: token,
    changes: [{ key: "Dashboard.Welcome", locale: "en", kind: "changed", stateBefore: "needs-review", stateAfter: "approved" }],
    addedCount: 0, changedCount: 1, removedCount: 0, changesOverflowed: false, refusals: [],
  };
}

async function applyReviewJsonImport(confirmationToken: string) {
  if (preparedReviewImport?.token !== confirmationToken || preparedReviewImport.fingerprint !== workspaceFingerprint()) {
    preparedReviewImport = undefined;
    return { ok: false, message: "Preview this import again to obtain a valid confirmation token.", history: snapshot.history };
  }
  preparedReviewImport = undefined;
  const before = structuredClone(snapshot.review);
  const entries = structuredClone(snapshot.review?.entries ?? []).map((entry) =>
    entry.key === "Dashboard.Welcome" && entry.locale === "en" ? { ...entry, state: "approved" as const } : entry);
  snapshot.review = {
    path: snapshot.review?.path ?? ".runic-translations/customer-product.editor-state.json",
    revision: crypto.randomUUID(), entries, terminology: structuredClone(snapshot.review?.terminology ?? []),
  };
  recordReviewHistory(before, snapshot.review);
  return { ok: true, review: structuredClone(snapshot.review), history: snapshot.history };
}

async function save(path: string, content: string, revision: string) {
  const normalizedPath = normalizeMockDocumentPath(path);
  if (normalizedPath === undefined) return { ok: false, kind: "invalid-request", message: "Workspace paths must stay within the mock workspace." };
  const current = snapshot.documents.find((candidate) => candidate.path === normalizedPath);
  if (current === undefined) return { ok: false, kind: "not-found", message: "Document not found." };
  if (current.revision !== revision) return { ok: false, kind: "conflict", message: "Mock document changed." };
  if (new TextEncoder().encode(content).byteLength > 8 * 1024 * 1024) {
    return { ok: false, kind: "resource-limit", message: "Mock document exceeds the editor size limit." };
  }
  const before = structuredClone(current);
  current.content = content;
  current.revision = crypto.randomUUID();
  snapshot = structuredClone(snapshot);
  recordDocumentHistory(before, current);
  destructiveConfirmation = undefined;
  return { ok: true, kind: "saved", snapshot: structuredClone(snapshot) };
}

async function previewProject(request: EditorProjectCreationRequest) {
  const locales = projectLocales(request);
  return {
    ok: request.directory.trim().length > 0,
    message: request.directory.trim().length > 0 ? undefined : "Choose a project directory.",
    directory: request.directory,
    catalogId: request.catalogId,
    locales,
    files: [
      `${request.catalogId}.catalog.json`,
      ...locales.map((locale) => `${request.catalogId}.${locale.tag}.json`),
    ].sort(),
  };
}

async function createProject(request: EditorProjectCreationRequest) {
  const locales = projectLocales(request);
  const manifestValue = {
    schemaVersion: 2,
    catalog: request.catalogId,
    code: { namespace: request.codeNamespace, className: request.className, visibility: "public" },
    defaultLocale: request.defaultLocale,
    locales,
    layers: [{ name: request.layerName, priority: 0 }],
    validation: { translationCompleteness: "error", extraLocaleKeys: "error", emptyValues: "error" },
  };
  const nextManifest = document(`${request.catalogId}.catalog.json`, undefined, undefined, manifestValue);
  nextManifest.isManifest = true;
  snapshot = {
    root: request.directory,
    catalog: {
      id: request.catalogId,
      schemaVersion: 2,
      defaultLocale: request.defaultLocale,
      locales,
      layers: [{ name: request.layerName, priority: 0 }],
    },
    catalogs: [{
      id: request.catalogId,
      manifestPaths: [`${request.catalogId}.catalog.json`],
      documentCount: locales.length,
      localeCount: locales.length,
      messageCount: request.includeStarterMessage ? 1 : 0,
      errorCount: 0,
      warningCount: 0,
      success: true,
    }],
    documents: [
      nextManifest,
      ...locales.map((locale) => document(
        `${request.catalogId}.${locale.tag}.json`,
        locale.tag,
        request.layerName,
        {
          schemaVersion: 2,
          catalog: request.catalogId,
          locale: locale.tag,
          layer: request.layerName,
          resources: request.includeStarterMessage
            ? { Application: { Name: request.className } }
            : {},
        },
      )),
    ],
    diagnostics: [],
    success: true,
  };
  clearHistory();
  destructiveConfirmation = undefined;
  syncHistory();
  return { ok: true, kind: "created", snapshot: structuredClone(snapshot) };
}

async function openWorkspace(request: { readonly directory: string; readonly catalogId?: string | undefined }) {
  if (request.catalogId === undefined && request.directory.includes("multi")) {
    const choice = structuredClone(snapshot);
    choice.root = request.directory;
    choice.catalog = undefined;
    choice.catalogs = [
      { id: "storefront", manifestPaths: ["storefront/catalog.json"], documentCount: 2, localeCount: 2, messageCount: 18, errorCount: 0, warningCount: 0, success: true },
      { id: "backoffice", manifestPaths: ["admin/catalog.json"], documentCount: 1, localeCount: 1, messageCount: 9, errorCount: 1, warningCount: 0, success: false },
    ];
    snapshot = choice;
    clearHistory();
    destructiveConfirmation = undefined;
    syncHistory();
    return { ok: true, kind: "opened", snapshot: choice };
  }
  const opened = structuredClone(snapshot);
  opened.root = request.directory;
  if (request.catalogId !== undefined) {
    const catalogId = request.catalogId;
    opened.catalog = {
      ...(opened.catalog ?? {
        schemaVersion: 2,
        defaultLocale: "en",
        locales: [{ tag: "en" }],
        layers: [{ name: "base", priority: 0 }],
      }),
      id: catalogId,
    };
  }
  snapshot = opened;
  clearHistory();
  destructiveConfirmation = undefined;
  syncHistory();
  return { ok: true, kind: "opened", snapshot: opened };
}

// The dev:mock fixture speaks the same generated contract as the native host:
// tagged commands in, tagged receipts out, with review-entry samples encoded
// as { key, value } pairs exactly like the C# bridge codec emits them.
export const mockApplicationBridgeLayer = MockApplicationBridge<
  EditorCommand,
  EditorReceipt,
  never,
  unknown
>({
  initialize: () => Effect.succeed(receipt({
    _tag: "ApplicationInitialized",
    snapshot: wire(structuredClone(snapshot)),
  })),
  dispatch: (command) => Effect.promise(async () => handle(command)),
});

type MockMutationRequest = {
  kind: string;
  locale?: string | undefined;
  fallback?: string | undefined;
  replacementFallback?: string | undefined;
  layer?: string | undefined;
  copyFromLocale?: string | undefined;
  sourceKey?: string | undefined;
  targetKey?: string | undefined;
  initialValue?: string | undefined;
  confirmationToken?: string | undefined;
};

async function handle(command: EditorCommand): Promise<EditorReceipt> {
  switch (command._tag) {
    case "InitializeApplication":
      return receipt({ _tag: "ApplicationInitialized", snapshot: wire(structuredClone(snapshot)) });
    case "LoadWorkspace":
      return receipt({ _tag: "WorkspaceLoaded", snapshot: wire(await load()) });
    case "CheckExternalChanges":
      return receipt({ _tag: "ExternalChangesChecked", changes: await checkExternalChanges() });
    case "PickWorkspace":
      return receipt({ _tag: "WorkspacePicked", result: await pickWorkspace() });
    case "PreviewMutation":
      return receipt({
        _tag: "MutationPreviewed",
        preview: wire(await previewMutation(command.request as unknown as MockMutationRequest)),
      });
    case "ApplyMutation":
      return receipt({
        _tag: "MutationApplied",
        result: wire(await applyMutation(command.request as unknown as MockMutationRequest)),
      });
    case "RecoverTransaction":
      return receipt({ _tag: "TransactionRecovered", result: wire(await recoverTransaction()) });
    case "Undo":
      return receipt({ _tag: "UndoApplied", result: wire(await undo()) });
    case "Redo":
      return receipt({ _tag: "RedoApplied", result: wire(await redo()) });
    case "ValidateDocument":
      return receipt({
        _tag: "DocumentValidated",
        result: await validate(command.path, command.content),
      });
    case "PreviewMessage":
      return receipt({
        _tag: "MessagePreviewed",
        preview: await previewMessage(command.path, command.content, command.locale, command.key),
      });
    case "SaveDocument":
      return receipt({
        _tag: "DocumentSaved",
        result: wire(await save(command.path, command.content, command.revision)),
      });
    case "SaveReview":
      return receipt({ _tag: "ReviewSaved", result: wire(await saveReview(domainReviewRequest(command.request))) });
    case "About":
      return receipt({ _tag: "AboutLoaded", about: await about() });
    case "CreateDiagnosticBundle":
      return receipt({ _tag: "DiagnosticBundleCreated", result: await createDiagnosticBundle() });
    case "RevealDiagnosticBundle":
      return receipt({ _tag: "DiagnosticBundleRevealed", result: await revealDiagnosticBundle() });
    case "DeleteDiagnosticBundle":
      return receipt({ _tag: "DiagnosticBundleDeleted", result: await deleteDiagnosticBundle() });
    case "LoadLocalState":
      return receipt({ _tag: "LocalStateLoaded", state: localStateSnapshot(false) });
    case "SaveLocalState":
      localState = new Map(command.entries.map((entry) => [entry.key, entry.value]));
      return receipt({ _tag: "LocalStateSaved", state: localStateSnapshot(false) });
    case "ClearLocalState": {
      const removedEntries = localState.size;
      localState.clear();
      return receipt({ _tag: "LocalStateCleared", result: { removedEntries, recovered: false } });
    }
    case "PreviewProject":
      return receipt({
        _tag: "ProjectPreviewed",
        plan: await previewProject(command.request as unknown as EditorProjectCreationRequest),
      });
    case "CreateProject":
      return receipt({
        _tag: "ProjectCreated",
        result: wire(await createProject(command.request as unknown as EditorProjectCreationRequest)),
      });
    case "OpenWorkspace":
      return receipt({ _tag: "WorkspaceOpened", result: wire(await openWorkspace(command.request)) });
    case "ExportXliff":
      return receipt({ _tag: "XliffExported", result: await exportXliff(command.directory) });
    case "PreviewXliffImport":
      return receipt({ _tag: "XliffImportPreviewed", preview: await previewXliffImport(command.path) });
    case "ApplyXliffImport":
      return receipt({ _tag: "XliffImportApplied", result: wire(await applyXliffImport(command.confirmationToken)) });
    case "ExportReviewJson":
      return receipt({ _tag: "ReviewJsonExported", result: await exportReviewJson(command.path) });
    case "PreviewReviewJsonImport":
      return receipt({ _tag: "ReviewJsonImportPreviewed", preview: await previewReviewJsonImport(command.path) });
    case "ApplyReviewJsonImport":
      return receipt({ _tag: "ReviewJsonImportApplied", result: wire(await applyReviewJsonImport(command.confirmationToken)) });
  }
}

function receipt<T extends object>(value: T): EditorReceipt {
  return value as EditorReceipt;
}

function localStateSnapshot(recovered: boolean): { entries: { key: string; value: string }[]; recovered: boolean } {
  return { entries: [...localState].map(([key, value]) => ({ key, value })), recovered };
}

// Inbound: the wire carries samples as pairs; the mock state keeps Records.
type SaveReviewWireRequest = Extract<EditorCommand, { _tag: "SaveReview" }>["request"];

function domainReviewRequest(request: SaveReviewWireRequest): EditorReviewSaveRequest {
  return {
    expectedRevision: request.expectedRevision,
    entries: request.entries.map((entry) => ({
      key: entry.key,
      locale: entry.locale,
      state: entry.state,
      note: entry.note,
      sourceFingerprint: entry.sourceFingerprint,
      samples: Object.fromEntries(entry.samples.map((sample) => [sample.key, sample.value])),
    })),
    terminology: request.terminology.map((term) => ({
      source: term.source,
      preferred: term.preferred,
      locale: term.locale,
      note: term.note,
    })),
  };
}

// Outbound: encode samples Records back into the wire pair representation.
function wire<T>(value: T): T {
  if (Array.isArray(value)) return value.map((item) => wire(item)) as T;
  if (value === null || typeof value !== "object") return value;
  const source = value as Record<string, unknown>;
  const output: Record<string, unknown> = {};
  for (const [key, item] of Object.entries(source)) {
    output[key] = key === "samples" && item !== null && typeof item === "object" && !Array.isArray(item)
      ? Object.entries(item).map(([sampleKey, sampleValue]) => ({ key: sampleKey, value: sampleValue }))
      : wire(item);
  }
  return output as T;
}

function projectLocales(request: EditorProjectCreationRequest) {
  return [
    { tag: request.defaultLocale },
    ...request.additionalLocales.map((locale) => ({
      tag: locale.tag,
      fallback: locale.fallback ?? request.defaultLocale,
    })),
  ];
}

function document(
  path: string,
  locale: string | undefined,
  layer: string | undefined,
  value: Record<string, unknown>,
): EditorDocument {
  return {
    path,
    locale,
    layer,
    isManifest: false,
    isMalformed: false,
    revision: `mock-${path}`,
    content: `${JSON.stringify(value, null, 2)}\n`,
  };
}

function resources(save: string, cancel: string, welcome: string): Record<string, unknown> {
  return {
    schemaVersion: 2,
    catalog: "customer-product",
    locale: save === "Speichern" ? "de" : "en",
    layer: "base",
    resources: {
      Common: { Save: save, Cancel: cancel },
      Dashboard: {
        Welcome: {
          $value: welcome,
          $description: "Greeting on the dashboard",
          $tags: ["dashboard", "customer-facing"],
          $placeholders: { name: { type: "string" } },
        },
      },
      Files: {
        Selected: {
          $value: {
            inputs: { count: { type: "int64" } },
            selectors: [{ name: "quantity", input: "count", function: "plural" }],
            variants: [
              { match: { quantity: "one" }, value: save === "Speichern" ? "Eine Datei ausgewählt" : "One file selected" },
              { match: { quantity: "*" }, value: save === "Speichern" ? "{count} Dateien ausgewählt" : "{count} files selected" },
            ],
          },
        },
      },
    },
  };
}
