import { errorNotice, notice, type UiMessage } from "../ui-text";
import type { RepairController } from "./repair-controller.svelte";
import type { SaveCoordinator, SaveOutcome } from "./save-coordinator.svelte";
import type { UnsavedWork } from "./unsaved-work.svelte";

/** What the close question names as unsaved: the one save's summary, and the repair the one save does not include. */
export type CloseSummary = { documents: string[]; review: boolean; repair?: string };

export interface CloseGuardOptions {
  unsaved: Pick<UnsavedWork, "hasUnsavedWork" | "discard">;
  save: Pick<SaveCoordinator, "unsavedSummary">;
  repair: Pick<RepairController, "hasUnsaved" | "document">;
  /** The one save with its notices (`WorkspaceController.saveAll`); `undefined` while another save or a history step runs. */
  saveAll: () => Promise<SaveOutcome | undefined>;
  /** Writes the native editor state (preferences, the recovery copies) before the window closes. */
  flush: () => Promise<void>;
  /** Closes the window as its close button does (`EditorBridge.closeWindow`); the host asks `answer` again. */
  closeWindow: () => Promise<void>;
  /** How long the window waits for `flush` before it closes anyway; 3 s by default. */
  flushTimeoutMs?: number;
}

/**
 * The question before the window closes (#225 P19). The host asks `answer` before the close button, Alt+F4, the window
 * manager or the Close window command closes the window. The answer never waits for a person: with nothing unsaved the
 * window closes at once; with unsaved work it stays open, and the page asks Save, Discard or Cancel in its own dialog.
 * Save and Discard then close the window through the same guard, which finds nothing unsaved; Cancel, a blocked or failed
 * save, and a host that stops waiting keep it open.
 */
export class CloseGuard {
  /** Whether the Save, Discard and Cancel question is shown. */
  open = $state(false);
  /** A save, or the close after Save or Discard, is running. */
  busy = $state(false);
  /** Why the window is still open after Save: the document that blocks the save, or the save's failure. */
  message = $state<UiMessage>();
  summary = $derived.by((): CloseSummary => {
    const { documents, review } = this.#options.save.unsavedSummary;
    const repair = this.#options.repair.hasUnsaved ? this.#options.repair.document?.path : undefined;
    return repair === undefined ? { documents, review } : { documents, review, repair };
  });

  readonly #options: CloseGuardOptions;

  constructor(options: CloseGuardOptions) {
    this.#options = options;
  }

  /**
   * The host's question: may the window close now? Without unsaved work it may, once the native editor state is written.
   * With unsaved work it stays open and the question is shown; while it is shown, or a save it started runs, the window
   * stays open too.
   */
  async answer(): Promise<boolean> {
    if (this.open || this.busy) return false;
    if (this.#options.unsaved.hasUnsavedWork) {
      this.message = undefined;
      this.open = true;
      return false;
    }
    await this.#flush();
    return true;
  }

  /** Save: the one save, then the close. A blocked or failed save, or work it leaves unsaved, keeps the window open. */
  async save(): Promise<void> {
    if (this.busy) return;
    this.busy = true;
    this.message = undefined;
    let saved = false;
    try {
      saved = this.#saved(await this.#options.saveAll());
    } catch (error) {
      this.message = errorNotice(error);
    } finally {
      this.busy = false;
    }
    if (saved) await this.#close();
  }

  /**
   * Discard: drops the unsaved work and its recovery copy, as every confirmed discard does, then closes the window. The
   * next start offers nothing to recover.
   */
  async discard(): Promise<void> {
    if (this.busy) return;
    this.#options.unsaved.discard();
    await this.#close();
  }

  /** Cancel: the window stays open with the work as it is. */
  cancel(): void {
    if (this.busy) return;
    this.open = false;
    this.message = undefined;
  }

  /** The page's own Close window command: the same close as the window's close button. */
  async requestClose(): Promise<void> {
    if (this.open || this.busy) return;
    await this.#options.closeWindow();
  }

  #saved(outcome: SaveOutcome | undefined): boolean {
    if (outcome === undefined) {
      this.message = notice("ui_close_save_busy");
      return false;
    }
    switch (outcome.kind) {
      case "invalid":
        this.message = notice("ui_close_save_invalid", { path: outcome.path });
        return false;
      case "refused":
      case "review-failed":
        this.message = outcome.message;
        return false;
      case "failed":
        this.message = errorNotice(outcome.error);
        return false;
    }
    // A newer draft typed during the save, or a repair, which the one save does not include, is still unsaved.
    if (!this.#options.unsaved.hasUnsavedWork) return true;
    const repair = this.summary.repair;
    this.message = repair === undefined ? notice("ui_close_still_unsaved") : notice("ui_close_repair_unsaved", { path: repair });
    return false;
  }

  async #close(): Promise<void> {
    this.busy = true;
    try {
      await this.#flush();
    } finally {
      this.busy = false;
    }
    // The host asks `answer` again, which finds nothing unsaved; new work typed meanwhile shows the question again.
    this.open = false;
    this.message = undefined;
    await this.#options.closeWindow();
  }

  // The recovery copies follow the work: a written copy of discarded or saved work would be offered at the next start.
  async #flush(): Promise<void> {
    let timer: ReturnType<typeof setTimeout> | undefined;
    try {
      await Promise.race([
        this.#options.flush(),
        new Promise<void>((resolve) => { timer = setTimeout(resolve, this.#options.flushTimeoutMs ?? 3_000); }),
      ]);
    } catch {
      // A failed write leaves the record as it was; it does not keep the window open.
    } finally {
      clearTimeout(timer);
    }
  }
}
