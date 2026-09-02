import {
  ApplicationBridgeLive,
  createApplicationBridgeController,
} from "@runic-artifex/application-bridge";
import { createDesktopFrameChannel } from "@runic-artifex/desktop";
import { createSvelteApplicationBridge } from "@runic-artifex/svelte";
import {
  createRunicDevtoolsObserver,
  preserveRunicHmrResource,
} from "virtual:runic/client";
import EditorContract from "../application.bridge.generated";
import type { EditorCommand, EditorReceipt } from "../application.bridge";
import { mockApplicationBridgeLayer } from "./mock-bridge";
import type {
  EditorAbout,
  EditorDiagnosticBundleActionResult,
  EditorDiagnosticBundleResult,
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

// One neutral controller owns transport, session handshake, and command
// dispatch. Svelte projects that controller, while Runic Vite observes its
// state and preserves the projection across hot replacements.
const bridge = preserveRunicHmrResource("editor-bridge", () =>
  createSvelteApplicationBridge(
    createApplicationBridgeController(
      EditorContract,
      import.meta.env.MODE === "mock"
        ? mockApplicationBridgeLayer
        : ApplicationBridgeLive(EditorContract, createDesktopFrameChannel()),
    ),
    { observer: createRunicDevtoolsObserver() },
  ));

let initialization: Promise<unknown> | undefined;

function ready(): Promise<unknown> {
  // The bridge session must initialize before any command is admitted;
  // every later load goes through LoadWorkspace instead.
  return (initialization ??= bridge.start());
}

/** Each editor command correlates with exactly one generated receipt tag. */
const receiptTags = {
  InitializeApplication: "ApplicationInitialized",
  LoadWorkspace: "WorkspaceLoaded",
  CheckExternalChanges: "ExternalChangesChecked",
  PickWorkspace: "WorkspacePicked",
  PreviewMutation: "MutationPreviewed",
  ApplyMutation: "MutationApplied",
  RecoverTransaction: "TransactionRecovered",
  Undo: "UndoApplied",
  Redo: "RedoApplied",
  ValidateDocument: "DocumentValidated",
  PreviewMessage: "MessagePreviewed",
  SaveDocument: "DocumentSaved",
  SaveReview: "ReviewSaved",
  About: "AboutLoaded",
  CreateDiagnosticBundle: "DiagnosticBundleCreated",
  RevealDiagnosticBundle: "DiagnosticBundleRevealed",
  DeleteDiagnosticBundle: "DiagnosticBundleDeleted",
  LoadLocalState: "LocalStateLoaded",
  SaveLocalState: "LocalStateSaved",
  ClearLocalState: "LocalStateCleared",
  PreviewProject: "ProjectPreviewed",
  CreateProject: "ProjectCreated",
  OpenWorkspace: "WorkspaceOpened",
  ExportXliff: "XliffExported",
  PreviewXliffImport: "XliffImportPreviewed",
  ApplyXliffImport: "XliffImportApplied",
  ExportReviewJson: "ReviewJsonExported",
  PreviewReviewJsonImport: "ReviewJsonImportPreviewed",
  ApplyReviewJsonImport: "ReviewJsonImportApplied",
} as const;

type CommandTag = keyof typeof receiptTags;
type ReceiptFor<K extends CommandTag> = Extract<EditorReceipt, { _tag: (typeof receiptTags)[K] }>;

async function dispatch<K extends CommandTag>(
  command: { readonly _tag: K } & Record<string, unknown>,
): Promise<ReceiptFor<K>> {
  await ready();
  return (await bridge.dispatch(command as EditorCommand)) as ReceiptFor<K>;
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
  validate(path: string, content: string): Promise<ValidationResult>;
  previewMessage(path: string, content: string, locale: string, key: string): Promise<EditorMessagePreview>;
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
    async load() {
      const receipt = await dispatch({ _tag: "LoadWorkspace" });
      return domain<WorkspaceSnapshot>(revive(receipt.snapshot));
    },
    async checkExternalChanges() {
      const receipt = await dispatch({ _tag: "CheckExternalChanges" });
      return domain<EditorExternalChanges>(revive(receipt.changes));
    },
    async pickWorkspace() {
      const receipt = await dispatch({ _tag: "PickWorkspace" });
      return domain<EditorWorkspacePickerResult>(revive(receipt.result));
    },
    async previewMutation(request) {
      const receipt = await dispatch({ _tag: "PreviewMutation", ...encodeRequest({ request }) });
      return domain<EditorMutationPreview>(revive(receipt.preview));
    },
    async applyMutation(request) {
      const receipt = await dispatch({ _tag: "ApplyMutation", ...encodeRequest({ request }) });
      return domain<EditorOperationResult>(revive(receipt.result));
    },
    async recoverTransaction(mode) {
      const receipt = await dispatch({ _tag: "RecoverTransaction", mode });
      return domain<EditorOperationResult>(revive(receipt.result));
    },
    async undo() {
      const receipt = await dispatch({ _tag: "Undo" });
      return domain<EditorOperationResult>(revive(receipt.result));
    },
    async redo() {
      const receipt = await dispatch({ _tag: "Redo" });
      return domain<EditorOperationResult>(revive(receipt.result));
    },
    async validate(path, content) {
      const receipt = await dispatch({ _tag: "ValidateDocument", path, content });
      return domain<ValidationResult>(revive(receipt.result));
    },
    async previewMessage(path, content, locale, key) {
      const receipt = await dispatch({ _tag: "PreviewMessage", path, content, locale, key });
      return domain<EditorMessagePreview>(revive(receipt.preview));
    },
    async saveReview(request) {
      const receipt = await dispatch({ _tag: "SaveReview", ...encodeReviewRequest(request) });
      return domain<EditorReviewOperationResult>(revive(receipt.result));
    },
    async about() {
      const receipt = await dispatch({ _tag: "About" });
      return domain<EditorAbout>(revive(receipt.about));
    },
    async createDiagnosticBundle() {
      const receipt = await dispatch({ _tag: "CreateDiagnosticBundle" });
      return domain<EditorDiagnosticBundleResult>(revive(receipt.result));
    },
    async revealDiagnosticBundle(path) {
      const receipt = await dispatch({ _tag: "RevealDiagnosticBundle", path });
      return domain<EditorDiagnosticBundleActionResult>(revive(receipt.result));
    },
    async deleteDiagnosticBundle(path) {
      const receipt = await dispatch({ _tag: "DeleteDiagnosticBundle", path });
      return domain<EditorDiagnosticBundleActionResult>(revive(receipt.result));
    },
    async loadLocalState() {
      const receipt = await dispatch({ _tag: "LoadLocalState" });
      return domain<EditorLocalStateSnapshot>(revive(receipt.state));
    },
    async saveLocalState(entries) {
      const receipt = await dispatch({ _tag: "SaveLocalState", entries });
      return domain<EditorLocalStateSnapshot>(revive(receipt.state));
    },
    async clearLocalState() {
      const receipt = await dispatch({ _tag: "ClearLocalState" });
      return domain<EditorLocalStateClearResult>(revive(receipt.result));
    },
    async save(path, content, revision) {
      const receipt = await dispatch({ _tag: "SaveDocument", path, content, revision });
      return domain<EditorOperationResult>(revive(receipt.result));
    },
    async previewProject(request) {
      const receipt = await dispatch({ _tag: "PreviewProject", ...encodeRequest({ request }) });
      return domain<EditorProjectPlan>(revive(receipt.plan));
    },
    async createProject(request) {
      const receipt = await dispatch({ _tag: "CreateProject", ...encodeRequest({ request }) });
      return domain<EditorOperationResult>(revive(receipt.result));
    },
    async openWorkspace(request) {
      const receipt = await dispatch({ _tag: "OpenWorkspace", ...encodeRequest({ request }) });
      return domain<EditorOperationResult>(revive(receipt.result));
    },
    async exportXliff(directory) {
      const receipt = await dispatch({ _tag: "ExportXliff", ...encodeRequest({ directory }) });
      return domain<EditorXliffExportResult>(revive(receipt.result));
    },
    async previewXliffImport(path) {
      const receipt = await dispatch({ _tag: "PreviewXliffImport", path });
      return domain<EditorXliffImportPreview>(revive(receipt.preview));
    },
    async applyXliffImport(confirmationToken) {
      const receipt = await dispatch({ _tag: "ApplyXliffImport", confirmationToken });
      return domain<EditorOperationResult>(revive(receipt.result));
    },
    async exportReviewJson(path) {
      const receipt = await dispatch({ _tag: "ExportReviewJson", ...encodeRequest({ path }) });
      return domain<EditorReviewFileResult>(revive(receipt.result));
    },
    async previewReviewJsonImport(path) {
      const receipt = await dispatch({ _tag: "PreviewReviewJsonImport", path });
      return domain<EditorReviewImportPreview>(revive(receipt.preview));
    },
    async applyReviewJsonImport(confirmationToken) {
      const receipt = await dispatch({ _tag: "ApplyReviewJsonImport", confirmationToken });
      return domain<EditorReviewOperationResult>(revive(receipt.result));
    },
  };
}

// The wire view is deeply readonly; the UI consumes the mutable domain shapes.
function domain<T>(value: unknown): T {
  return value as T;
}

// Drop explicit-undefined keys so Schema encode never sees excess properties.
function encodeRequest<T extends object>(request: T): T {
  const output: Record<string, unknown> = {};
  for (const [key, item] of Object.entries(request)) {
    if (item !== undefined) output[key] = item;
  }
  return output as T;
}

// The wire encodes review-entry samples as { key, value } pairs (the C#
// generator has no dictionary construct); the UI consumes Record<string,string>.
function encodeReviewRequest(request: EditorReviewSaveRequest): Record<string, unknown> {
  return {
    request: {
      ...(request.expectedRevision === undefined ? {} : { expectedRevision: request.expectedRevision }),
      entries: request.entries.map((entry) => ({
        ...encodeRequest(entry),
        samples: Object.entries(entry.samples).map(([key, value]) => ({ key, value })),
      })),
      terminology: request.terminology,
    },
  };
}

function revive(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(revive);
  if (value === null || typeof value !== "object") return value;
  const source = value as Record<string, unknown>;
  const output: Record<string, unknown> = {};
  for (const [key, item] of Object.entries(source)) {
    output[key] = key === "samples" && isSampleEntries(item)
      ? Object.fromEntries(item.map((sample) => [sample.key, sample.value]))
      : revive(item);
  }
  return output;
}

function isSampleEntries(value: unknown): value is Array<{ key: string; value: string }> {
  return Array.isArray(value) &&
    value.every((entry) =>
      entry !== null &&
      typeof entry === "object" &&
      typeof (entry as Record<string, unknown>).key === "string" &&
      typeof (entry as Record<string, unknown>).value === "string"
    );
}
