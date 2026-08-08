import type {
  EditorExternalChanges,
  EditorMutationPreview,
  EditorMutationRequest,
  EditorOperationResult,
  EditorOpenWorkspaceRequest,
  EditorProjectCreationRequest,
  EditorProjectPlan,
  EditorWorkspacePickerResult,
  ValidationResult,
  WorkspaceSnapshot,
} from "./contracts";
import { mockBridge } from "./mock-bridge";

declare global {
  var runicEditorLoad: (() => Promise<string>) | undefined;
  var runicEditorCheckExternalChanges: (() => Promise<string>) | undefined;
  var runicEditorPickWorkspace: (() => Promise<string>) | undefined;
  var runicEditorPreviewMutation: ((request: string) => Promise<string>) | undefined;
  var runicEditorApplyMutation: ((request: string) => Promise<string>) | undefined;
  var runicEditorRecoverTransaction: ((request: string) => Promise<string>) | undefined;
  var runicEditorValidate: ((path: string, content: string) => Promise<string>) | undefined;
  var runicEditorSave:
    | ((path: string, content: string, revision: string) => Promise<string>)
    | undefined;
  var runicEditorPreviewProject: ((request: string) => Promise<string>) | undefined;
  var runicEditorCreateProject: ((request: string) => Promise<string>) | undefined;
  var runicEditorOpenWorkspace: ((request: string) => Promise<string>) | undefined;
}

export interface EditorBridge {
  load(): Promise<WorkspaceSnapshot>;
  checkExternalChanges(): Promise<EditorExternalChanges>;
  pickWorkspace(): Promise<EditorWorkspacePickerResult>;
  previewMutation(request: EditorMutationRequest): Promise<EditorMutationPreview>;
  applyMutation(request: EditorMutationRequest): Promise<EditorOperationResult>;
  recoverTransaction(mode: "complete" | "rollback"): Promise<EditorOperationResult>;
  validate(path: string, content: string): Promise<ValidationResult>;
  save(path: string, content: string, revision: string): Promise<EditorOperationResult>;
  previewProject(request: EditorProjectCreationRequest): Promise<EditorProjectPlan>;
  createProject(request: EditorProjectCreationRequest): Promise<EditorOperationResult>;
  openWorkspace(request: EditorOpenWorkspaceRequest): Promise<EditorOperationResult>;
}

export function createEditorBridge(): EditorBridge {
  if (import.meta.env.MODE === "mock") return mockBridge;
  return {
    async load() {
      return parse(await binding("runicEditorLoad", globalThis.runicEditorLoad)());
    },
    async checkExternalChanges() {
      return parse(await binding(
        "runicEditorCheckExternalChanges",
        globalThis.runicEditorCheckExternalChanges,
      )());
    },
    async pickWorkspace() {
      return parse(await binding(
        "runicEditorPickWorkspace",
        globalThis.runicEditorPickWorkspace,
      )());
    },
    async previewMutation(request) {
      return parse(await binding(
        "runicEditorPreviewMutation",
        globalThis.runicEditorPreviewMutation,
      )(JSON.stringify(request)));
    },
    async applyMutation(request) {
      return parse(await binding(
        "runicEditorApplyMutation",
        globalThis.runicEditorApplyMutation,
      )(JSON.stringify(request)));
    },
    async recoverTransaction(mode) {
      return parse(await binding(
        "runicEditorRecoverTransaction",
        globalThis.runicEditorRecoverTransaction,
      )(JSON.stringify({ mode })));
    },
    async validate(path, content) {
      return parse(await binding("runicEditorValidate", globalThis.runicEditorValidate)(path, content));
    },
    async save(path, content, revision) {
      return parse(await binding("runicEditorSave", globalThis.runicEditorSave)(path, content, revision));
    },
    async previewProject(request) {
      return parse(await binding(
        "runicEditorPreviewProject",
        globalThis.runicEditorPreviewProject,
      )(JSON.stringify(request)));
    },
    async createProject(request) {
      return parse(await binding(
        "runicEditorCreateProject",
        globalThis.runicEditorCreateProject,
      )(JSON.stringify(request)));
    },
    async openWorkspace(request) {
      return parse(await binding(
        "runicEditorOpenWorkspace",
        globalThis.runicEditorOpenWorkspace,
      )(JSON.stringify(request)));
    },
  };
}

function binding<T>(name: string, value: T | undefined): T {
  if (value === undefined) {
    throw new Error(`${name} is unavailable. Start the native editor, or use npm run dev:mock.`);
  }
  return value;
}

function parse<T>(value: string): T {
  return JSON.parse(value) as T;
}
