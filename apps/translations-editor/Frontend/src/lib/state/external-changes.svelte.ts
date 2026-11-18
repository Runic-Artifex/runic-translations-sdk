import type { EditorExternalFileChange, WorkspaceSnapshot } from "../contracts";
import type { EditorBridge } from "../editor-bridge";
import { errorNotice, notice } from "../ui-text";
import type { DraftStore } from "./draft-store.svelte";
import type { EditorFeedback } from "./feedback.svelte";
import type { ReviewStore } from "./review-store.svelte";
import type { UnsavedWork } from "./unsaved-work.svelte";

export interface ExternalChangesOptions {
  bridge: Pick<EditorBridge, "checkExternalChanges" | "load">;
  snapshot: () => WorkspaceSnapshot | undefined;
  drafts: Pick<DraftStore, "drafts" | "replace" | "persist">;
  review: Pick<ReviewStore, "saving" | "applyExternal">;
  unsaved: Pick<UnsavedWork, "confirmDiscardNonDocumentWork">;
  feedback: EditorFeedback;
  /** Whether a poll may run: a workspace is open and none is loading, opening or saving (else it waits a round). */
  idle: () => boolean;
  historyBusy: () => boolean;
  /** Sets whether the workspace is loading, while the merged draft's workspace reloads. */
  setLoading: (loading: boolean) => void;
  /** Installs the reloaded workspace with a reset selection. */
  install: (next: WorkspaceSnapshot) => void;
  configureEditor: () => void;
}

/**
 * Changes another program made to the workspace's files: polled from the host, offered as a reload or as a
 * comparison of one file with the editor's text, whose merged text becomes the file's draft in the reloaded workspace.
 * A changed review file is merged into the review store directly.
 */
export class ExternalChangesController {
  /** The changed files, by path. */
  paths = $state<string[]>([]);
  /** The changed files with their text on disk. */
  changes = $state<EditorExternalFileChange[]>([]);
  /** The change being compared and merged. */
  compared = $state.raw<EditorExternalFileChange>();
  merged = $state("");

  readonly #options: ExternalChangesOptions;

  constructor(options: ExternalChangesOptions) {
    this.#options = options;
  }

  /** Asks the host which files changed since the workspace was loaded. */
  async check(): Promise<void> {
    if (!this.#options.idle()) return;
    try {
      const result = await this.#options.bridge.checkExternalChanges();
      if (result.review !== undefined && !this.#options.review.saving) this.#options.review.applyExternal(result.review);
      if (result.paths.length > 0) {
        this.paths = [...new Set([...this.paths, ...result.paths])].sort();
        this.changes = [
          ...this.changes.filter((existing) => !result.changes.some((change) => change.path === existing.path)),
          ...result.changes,
        ].sort((left, right) => left.path.localeCompare(right.path));
      }
    } catch {
      // The next poll retries; editing and saving remain available.
    }
  }

  /** Forgets the changes: the workspace was reloaded, or the user keeps the current view. */
  clear(): void {
    this.paths = [];
    this.changes = [];
  }

  /** Opens the comparison of the first changed file, its merge starting from the editor's text. */
  compare(): void {
    const change = this.changes[0];
    if (change === undefined) return;
    this.compared = change;
    const base = this.#options.snapshot()?.documents.find((document) => document.path === change.path);
    this.merged = this.#options.drafts.drafts[change.path] ?? base?.content ?? change.content ?? "";
  }

  /** Reloads the workspace and keeps the merged text as the compared file's draft, beside the other drafts. */
  async applyMerge(): Promise<void> {
    const change = this.compared;
    const { drafts, feedback } = this.#options;
    if (change === undefined || this.#options.historyBusy()) return;
    if (!this.#options.unsaved.confirmDiscardNonDocumentWork("ui_confirm_external_merge")) return;
    const retainedDrafts = { ...drafts.drafts, [change.path]: this.merged };
    this.#options.setLoading(true);
    feedback.clientError = undefined;
    try {
      const next = await this.#options.bridge.load();
      this.#options.install(next);
      drafts.replace(Object.fromEntries(Object.entries(retainedDrafts).filter(([path]) =>
        next.documents.some((document) => document.path === path))));
      if (drafts.drafts[change.path] === undefined) {
        feedback.clientError = notice("ui_feedback_deleted_merge", { path: change.path });
      }
      drafts.persist();
      this.clear();
      this.compared = undefined;
      this.#options.configureEditor();
    } catch (error) {
      feedback.clientError = errorNotice(error);
    } finally {
      this.#options.setLoading(false);
    }
  }
}
