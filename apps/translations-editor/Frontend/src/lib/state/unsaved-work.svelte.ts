import type { DraftStore } from "./draft-store.svelte";
import type { MessageDraftStore } from "./message-draft-store.svelte";
import type { RepairController } from "./repair-controller.svelte";
import type { ReviewStore } from "./review-store.svelte";
import type { SaveCoordinator } from "./save-coordinator.svelte";

export interface UnsavedWorkOptions {
  save: Pick<SaveCoordinator, "hasUnsaved">;
  drafts: Pick<DraftStore, "discard">;
  messageDrafts: Pick<MessageDraftStore, "reset">;
  review: Pick<ReviewStore, "dirty" | "discard">;
  repair: Pick<RepairController, "hasUnsaved" | "reset">;
  /** Asks the user to confirm the UI message `messageKey`. */
  confirm: (messageKey: string) => boolean;
}

/**
 * Every kind of unsaved work (document text, review changes, the repair of a malformed file) and the confirmation
 * before an operation that replaces the workspace drops it.
 */
export class UnsavedWork {
  /** Whether anything is unsaved: what the title marks and what closing the window warns about. */
  hasUnsavedWork = $derived.by(() => this.#options.save.hasUnsaved || this.#options.repair.hasUnsaved);

  readonly #options: UnsavedWorkOptions;

  constructor(options: UnsavedWorkOptions) {
    this.#options = options;
  }

  /** Whether the operation may go on: nothing is unsaved, or the user agreed to lose it. */
  confirmDiscard(messageKey: string): boolean {
    if (!this.hasUnsavedWork) return true;
    return this.#options.confirm(messageKey);
  }

  /** Drops all unsaved work. */
  discard(): void {
    const { drafts, messageDrafts, review, repair } = this.#options;
    drafts.discard();
    messageDrafts.reset();
    review.discard();
    if (repair.hasUnsaved) repair.reset();
  }

  /** Confirms dropping the unsaved work that is not document text (review changes, a repair), and drops it. */
  confirmDiscardNonDocumentWork(messageKey: string): boolean {
    const { review, repair } = this.#options;
    if (!review.dirty && !repair.hasUnsaved) return true;
    if (!this.#options.confirm(messageKey)) return false;
    review.discard();
    if (repair.hasUnsaved) repair.reset();
    return true;
  }
}
