export interface EditorLocale {
  tag: string;
  fallback?: string;
}

export interface EditorLayer {
  name: string;
  priority: number;
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
  documents: EditorDocument[];
  diagnostics: EditorDiagnostic[];
  success: boolean;
}

export interface ValidationResult {
  success: boolean;
  diagnostics: EditorDiagnostic[];
}

export interface EditorOperationResult {
  ok: boolean;
  kind: string;
  message?: string;
  snapshot?: WorkspaceSnapshot;
  validation?: ValidationResult;
}
