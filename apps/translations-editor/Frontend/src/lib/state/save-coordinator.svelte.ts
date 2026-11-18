import type { EditorBridge } from "../editor-bridge";
import type { ValidationResult, WorkspaceSnapshot } from "../contracts";
import { notice, type UiMessage } from "../ui-text";
import type { DraftStore } from "./draft-store.svelte";
import type { ReviewStore } from "./review-store.svelte";

/** What is unsaved: the documents with unsaved text, and whether review states, notes or terminology are. */
export type UnsavedSummary = { documents: string[]; review: boolean };

/**
 * How a save of all unsaved work ended. `saved` lists the documents written; `newer` says a draft typed during the
 * save is still open. Nothing was written when a document is invalid; a refused document stops the save, and the
 * review changes are saved only after every document was.
 */
export type SaveOutcome =
  | { kind: "nothing" }
  | { kind: "saved"; saved: string[]; newer: boolean; message: UiMessage }
  | { kind: "invalid"; path: string; result: ValidationResult }
  | { kind: "refused"; saved: string[]; path: string; message: UiMessage }
  | { kind: "review-failed"; saved: string[]; newer: boolean; message: UiMessage }
  | { kind: "failed"; saved: string[]; error: unknown };

export interface SaveCoordinatorOptions {
  bridge: Pick<EditorBridge, "validate" | "save" | "load">;
  drafts: DraftStore;
  review: ReviewStore;
  snapshot: () => WorkspaceSnapshot | undefined;
  /** Receives the validation of each document before it is saved. */
  validated?: (path: string, result: ValidationResult) => void;
  /** Installs the workspace reloaded after documents were saved. */
  install: (next: WorkspaceSnapshot) => void;
}

/**
 * The one save of the editor. It owns what counts as unsaved work (document text that differs from the saved file,
 * review data that differs from the review file) and saves all of it: every unsaved document first, each validated,
 * then the review changes, so a review state is recorded against saved text.
 */
export class SaveCoordinator {
  saving = $state(false);
  unsavedSummary = $derived.by((): UnsavedSummary => ({
    documents: this.#options.drafts.dirtyPaths,
    review: this.#options.review.dirty,
  }));
  hasUnsaved = $derived(this.unsavedSummary.documents.length > 0 || this.unsavedSummary.review);

  readonly #options: SaveCoordinatorOptions;

  constructor(options: SaveCoordinatorOptions) {
    this.#options = options;
  }

  async saveAll(): Promise<SaveOutcome> {
    if (this.saving) return { kind: "nothing" };
    this.saving = true;
    const saved: string[] = [];
    try {
      return await this.#saveAll(saved);
    } catch (error) {
      return { kind: "failed", saved, error };
    } finally {
      this.saving = false;
    }
  }

  async #saveAll(saved: string[]): Promise<SaveOutcome> {
    const { bridge, drafts, review } = this.#options;
    // Queued composer edits, authoring operations and fixes land in their drafts before the text is saved.
    await drafts.settledAll();
    const snapshot = this.#options.snapshot();
    const documents = drafts.dirtyPaths.flatMap((path) => {
      const document = snapshot?.documents.find((candidate) => candidate.path === path);
      const content = drafts.drafts[path];
      return document === undefined || content === undefined
        ? []
        : [{ path, content, revision: document.revision, generation: drafts.generation(path) }];
    });
    if (documents.length === 0 && !review.dirty) return { kind: "nothing" };

    // An invalid document blocks the whole save: nothing is written.
    for (const document of documents) {
      const checked = await bridge.validate(document.path, document.content);
      this.#options.validated?.(document.path, checked);
      if (!checked.success) return { kind: "invalid", path: document.path, result: checked };
    }

    let message: UiMessage | undefined;
    let refused: { path: string; message: UiMessage } | undefined;
    for (const document of documents) {
      const result = await bridge.save(document.path, document.content, document.revision);
      if (!result.ok) {
        refused = { path: document.path, message: result.message ?? notice("ui_feedback_save_failed", { kind: result.kind }) };
        break;
      }
      saved.push(document.path);
      message = result.message ?? message;
    }

    let newer = false;
    if (saved.length > 0) {
      const next = await bridge.load();
      const kept = documents.filter((document) => saved.includes(document.path) &&
        drafts.hasNewer(document.path, document.content, document.generation));
      this.#options.install(next);
      for (const path of saved) if (!kept.some((document) => document.path === path)) drafts.drop(path);
      newer = kept.length > 0;
    }
    if (refused !== undefined) return { kind: "refused", saved, ...refused };

    if (!(await review.save())) {
      return { kind: "review-failed", saved, newer, message: review.message ?? notice("ui_feedback_review_save_failed") };
    }
    return { kind: "saved", saved, newer, message: message ?? notice("app_saved") };
  }
}
