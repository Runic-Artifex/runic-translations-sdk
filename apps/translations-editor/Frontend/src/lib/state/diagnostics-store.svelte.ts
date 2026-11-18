import type { EditorDiagnostic, ValidationResult, WorkspaceSnapshot } from "../contracts";
import { rowDiagnostics } from "../review-model";

/** The request that produced a validation result. */
export type DiagnosticSource =
  | "transform"
  | "authoring"
  | "validation"
  | "quick-fix"
  | "save"
  | "navigation"
  | "edit";

const severityRank = (diagnostic: EditorDiagnostic): number => diagnostic.severity === "error" ? 0 : diagnostic.severity === "warning" ? 1 : 2;

/**
 * The compiler's validation results of the open workspace, kept per request and document. The panel shows the result
 * that finished last, or the workspace's diagnostics before any; `clear` returns to the latter.
 */
export class DiagnosticsStore {
  #results = $state.raw<ReadonlyMap<string, ValidationResult>>(new Map());
  #latest = $state.raw<string>();
  readonly #snapshot: () => WorkspaceSnapshot | undefined;

  /** The result that finished last. */
  latest = $derived(this.#latest === undefined ? undefined : this.#results.get(this.#latest));
  /** The diagnostics the panel lists. */
  displayed = $derived.by(() => this.latest?.diagnostics ?? this.#snapshot()?.diagnostics ?? []);
  errorCount = $derived(this.displayed.filter((item) => item.severity === "error").length);
  warningCount = $derived(this.displayed.filter((item) => item.severity === "warning").length);

  constructor(snapshot: () => WorkspaceSnapshot | undefined) {
    this.#snapshot = snapshot;
  }

  record(source: DiagnosticSource, path: string, result: ValidationResult): void {
    const key = `${source}\u0000${path}`;
    const results = new Map(this.#results);
    results.set(key, result);
    this.#results = results;
    this.#latest = key;
  }

  /** The latest result of one request kind for one document. */
  result(source: DiagnosticSource, path: string): ValidationResult | undefined {
    return this.#results.get(`${source}\u0000${path}`);
  }

  /** Forgets every result: the panel shows the workspace's diagnostics again. */
  clear(): void {
    this.#results = new Map();
    this.#latest = undefined;
  }

  /** The displayed diagnostics about one message in one locale. */
  forMessage(key: string, locale: string): EditorDiagnostic[] {
    return rowDiagnostics(this.displayed, key, locale);
  }

  /** The displayed diagnostics, errors before warnings, otherwise in their reported order. */
  sortedErrorsFirst(): EditorDiagnostic[] {
    return this.displayed
      .map((diagnostic, index) => ({ diagnostic, index }))
      .sort((left, right) => severityRank(left.diagnostic) - severityRank(right.diagnostic) || left.index - right.index)
      .map((item) => item.diagnostic);
  }
}
