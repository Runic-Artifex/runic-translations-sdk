import type {
  EditorOperationResult,
  ValidationResult,
  WorkspaceSnapshot,
} from "./contracts";
import { mockBridge } from "./mock-bridge";

declare global {
  var runicEditorLoad: (() => Promise<string>) | undefined;
  var runicEditorValidate: ((path: string, content: string) => Promise<string>) | undefined;
  var runicEditorSave:
    | ((path: string, content: string, revision: string) => Promise<string>)
    | undefined;
}

export interface EditorBridge {
  load(): Promise<WorkspaceSnapshot>;
  validate(path: string, content: string): Promise<ValidationResult>;
  save(path: string, content: string, revision: string): Promise<EditorOperationResult>;
}

export function createEditorBridge(): EditorBridge {
  if (import.meta.env.MODE === "mock") return mockBridge;
  return {
    async load() {
      return parse(await binding("runicEditorLoad", globalThis.runicEditorLoad)());
    },
    async validate(path, content) {
      return parse(await binding("runicEditorValidate", globalThis.runicEditorValidate)(path, content));
    },
    async save(path, content, revision) {
      return parse(await binding("runicEditorSave", globalThis.runicEditorSave)(path, content, revision));
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
