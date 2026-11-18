import type { EditorDiagnostic, ValidationResult, WorkspaceSnapshot } from "../contracts";
import { errorsFirst } from "../diagnostic-scope";
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

/**
 * The compiler's validation results of the open workspace, kept per request and document. The panel shows the result
 * that finished last, or the workspace's diagnostics before any; `clear` returns to the latter.
 */
export class DiagnosticsStore {
  #results = $state.raw<ReadonlyMap<string, ValidationResult>>(new Map());
  #latest = $state.raw<string>();
  // The saved workspace's diagnostics requested again after the snapshot was loaded (a save of review data).
  #refreshed = $state.raw<{ snapshot: WorkspaceSnapshot; diagnostics: EditorDiagnostic[] }>();
  readonly #snapshot: () => WorkspaceSnapshot | undefined;

  /** The result that finished last. */
  latest = $derived(this.#latest === undefined ? undefined : this.#results.get(this.#latest));
  /**
   * The diagnostics of the saved workspace: the snapshot's, or those requested again for the same snapshot since. A new
   * snapshot (a reload, a save of documents) brings its own.
   */
  workspaceDiagnostics = $derived.by((): EditorDiagnostic[] => {
    const snapshot = this.#snapshot();
    const refreshed = this.#refreshed;
    return refreshed !== undefined && refreshed.snapshot === snapshot ? refreshed.diagnostics : snapshot?.diagnostics ?? [];
  });
  /** The diagnostics the panel lists. */
  displayed = $derived.by(() => this.latest?.diagnostics ?? this.workspaceDiagnostics);
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

  /**
   * The saved workspace's diagnostics, requested again for `snapshot` after it was loaded: they replace the snapshot's
   * while it stays the open one, and the panel shows them instead of the results of earlier requests, which were
   * computed against the review file as it was. A save of review data changes what RTR0090 reports (#225 P20).
   */
  refreshWorkspace(snapshot: WorkspaceSnapshot, diagnostics: EditorDiagnostic[]): void {
    if (this.#snapshot() !== snapshot) return;
    this.#refreshed = { snapshot, diagnostics };
    this.clear();
  }

  /** The displayed diagnostics about one message in one locale. */
  forMessage(key: string, locale: string): EditorDiagnostic[] {
    return rowDiagnostics(this.displayed, key, locale);
  }

  /** The displayed diagnostics, errors before warnings, otherwise in their reported order. */
  sortedErrorsFirst(): EditorDiagnostic[] {
    return errorsFirst(this.displayed, (diagnostic) => diagnostic);
  }
}
