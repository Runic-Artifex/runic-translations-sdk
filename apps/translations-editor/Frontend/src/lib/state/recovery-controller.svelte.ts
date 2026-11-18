import type { EditorBridge } from "../editor-bridge";
import { errorNotice, notice, type UiMessage } from "../ui-text";
import type { DraftStore } from "./draft-store.svelte";
import type { EditorFeedback } from "./feedback.svelte";
import type { ReviewStore } from "./review-store.svelte";
import type { SelectionController } from "./selection-controller.svelte";
import type { WorkspaceController } from "./workspace-controller.svelte";

export interface RecoveryControllerOptions {
  bridge: Pick<EditorBridge, "recoverTransaction">;
  workspace: WorkspaceController;
  drafts: Pick<DraftStore, "restoreRecovered" | "discardRecovered" | "drafts" | "parse" | "recovered">;
  review: Pick<ReviewStore, "restoreRecovered" | "discardRecovered" | "recovered">;
  selection: Pick<SelectionController, "configureEditor" | "selectedDocumentPath">;
  feedback: EditorFeedback;
  historyBusy: () => boolean;
}

/**
 * Recovery of work an earlier session did not finish: a workspace transaction its journal left (completed or rolled
 * back), and the unsaved document drafts and review changes it left in the native editor state.
 */
export class RecoveryController {
  busy = $state(false);
  /** The recovery succeeded, but the workspace could not be loaded from its result: the page asks for a reload. */
  reloadRequired = $state(false);
  reloadMessage = $state<UiMessage>();
  /** Whether an earlier session left unsaved work to restore. */
  hasRecoveredWork = $derived.by(() => Object.keys(this.#options.drafts.recovered).length > 0 || this.#options.review.recovered !== undefined);

  readonly #options: RecoveryControllerOptions;

  constructor(options: RecoveryControllerOptions) {
    this.#options = options;
  }

  /** The workspace loaded: a reload the recovery asked for is done. */
  reloaded(): void {
    this.reloadRequired = false;
    this.reloadMessage = undefined;
  }

  /** Completes or rolls back the pending workspace transaction. */
  async recoverTransaction(mode: "complete" | "rollback"): Promise<void> {
    const { workspace, feedback } = this.#options;
    if (this.busy || this.#options.historyBusy()) return;
    this.busy = true;
    feedback.clientError = undefined;
    try {
      const result = await this.#options.bridge.recoverTransaction(mode);
      if (!result.ok) {
        feedback.clientError = result.message ?? notice("ui_feedback_recovery_failed");
        return;
      }
      if (result.snapshot === undefined) {
        workspace.openDirectory = workspace.snapshot?.root ?? workspace.openDirectory;
        workspace.snapshot = undefined;
        this.reloadRequired = true;
        this.reloadMessage = result.message ?? notice("ui_feedback_recovery_reload");
        await workspace.load(false);
        return;
      }
      workspace.install(result.snapshot, true);
      this.reloaded();
      feedback.operationMessage = mode === "complete" ? notice("ui_feedback_recovery_completed") : notice("ui_feedback_recovery_rollback");
    } catch (error) {
      feedback.clientError = errorNotice(error);
    } finally {
      this.busy = false;
    }
  }

  /** Restores the unsaved work an earlier session left: document drafts and review changes. */
  restoreRecoveredWork(): void {
    const { drafts, selection, feedback } = this.#options;
    drafts.restoreRecovered();
    this.#options.review.restoreRecovered();
    selection.configureEditor();
    for (const [path, content] of Object.entries(drafts.drafts)) {
      void drafts.parse(path, content).then((stored) => {
        if (stored && selection.selectedDocumentPath === path) selection.configureEditor();
      }).catch((error) => { feedback.clientError = errorNotice(error); });
    }
  }

  /** Drops the unsaved work an earlier session left. */
  discardRecoveredWork(): void {
    this.#options.drafts.discardRecovered();
    this.#options.review.discardRecovered();
  }
}
