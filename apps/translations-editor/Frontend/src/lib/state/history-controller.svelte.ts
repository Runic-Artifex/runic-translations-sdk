import { tick } from "svelte";
import type { WorkspaceSnapshot } from "../contracts";
import { draftField, restoreCaret, type DraftField } from "../draft-fields";
import type { EditorBridge } from "../editor-bridge";
import { displayNotice, errorNotice, notice, type UiText } from "../ui-text";
import type { DraftStep } from "./draft-history.svelte";
import type { DraftStore } from "./draft-store.svelte";
import type { DiagnosticsStore } from "./diagnostics-store.svelte";
import type { EditorFeedback } from "./feedback.svelte";
import type { SelectionController } from "./selection-controller.svelte";

export interface HistoryControllerOptions {
  bridge: Pick<EditorBridge, "undo" | "redo">;
  snapshot: () => WorkspaceSnapshot | undefined;
  drafts: DraftStore;
  diagnostics: Pick<DiagnosticsStore, "record">;
  selection: Pick<SelectionController, "currentDocument" | "showStep">;
  feedback: EditorFeedback;
  /** Reloads the workspace after a saved change was undone without a new snapshot. */
  load: () => Promise<void>;
  /** Installs the workspace after a saved change was undone or redone, keeping the selection where it can. */
  install: (next: WorkspaceSnapshot) => void;
  /** Whether the saved-change history must wait: an operation runs, a dialog is open, or work is unsaved. */
  historyBlocked: () => boolean;
  /** Whether the unsaved-edit history must wait: an operation that replaces drafts runs, or a dialog is open. */
  draftHistoryBlocked: () => boolean;
  ui: UiText;
  /** The draft field that has the focus, whose caret an undo keeps. */
  focusedField?: () => DraftField | undefined;
}

/**
 * Undo and redo. Unsaved edits have their own undo history (#225 P4), per document: Undo and Redo take the selected
 * document's latest step, or else the latest of another document. Without an unsaved step they act on the saved
 * changes, through the host's history.
 */
export class HistoryController {
  /** Whether a saved change is being undone or redone. */
  busy = $state(false);
  /** Advanced by each undo or redo of an unsaved edit: the composer's fields show the draft again. */
  revision = $state(0);
  undoStep = $derived.by(() => this.#history().peekUndo(this.#history().undoTarget(this.#options.selection.currentDocument?.path)));
  redoStep = $derived.by(() => this.#history().peekRedo(this.#history().redoTarget(this.#options.selection.currentDocument?.path)));
  canUndo = $derived.by(() => this.undoStep !== undefined
    ? !this.#options.draftHistoryBlocked()
    : this.#options.snapshot()?.history?.canUndo === true && !this.#options.historyBlocked());
  canRedo = $derived.by(() => this.redoStep !== undefined
    ? !this.#options.draftHistoryBlocked()
    : this.#options.snapshot()?.history?.canRedo === true && !this.#options.historyBlocked());
  undoLabel = $derived.by(() => {
    const { ui } = this.#options;
    const step = this.undoStep;
    if (step !== undefined) {
      return ui.text("ui_toolbar_undo_unsaved", { change: this.changeName(step), message: step.message ?? step.path });
    }
    const saved = this.#options.snapshot()?.history?.undoLabel;
    return saved === undefined ? ui.text("ui_toolbar_undo_saved_change") : ui.text("ui_toolbar_undo_saved_change_named", { change: displayNotice(saved, ui) });
  });
  redoLabel = $derived.by(() => {
    const { ui } = this.#options;
    const step = this.redoStep;
    if (step !== undefined) {
      return ui.text("ui_toolbar_redo_unsaved", { change: this.changeName(step), message: step.message ?? step.path });
    }
    const saved = this.#options.snapshot()?.history?.redoLabel;
    return saved === undefined ? ui.text("ui_toolbar_redo_saved_change") : ui.text("ui_toolbar_redo_saved_change_named", { change: displayNotice(saved, ui) });
  });

  // Undo and redo run one after another, each after the edits typed before it reached the draft.
  #chain = Promise.resolve();
  readonly #options: HistoryControllerOptions;

  constructor(options: HistoryControllerOptions) {
    this.#options = options;
  }

  #history() {
    return this.#options.drafts.history;
  }

  /** Ctrl+Z, the toolbar's and the palette's Undo: the latest unsaved edit, or without one the latest saved change. */
  undo(): void {
    this.#queue("undo");
  }

  /** Ctrl+Shift+Z, Ctrl+Y, the toolbar's and the palette's Redo. */
  redo(): void {
    this.#queue("redo");
  }

  /** The name of an unsaved step's kind of change, for the Undo and Redo titles. */
  changeName(step: DraftStep): string {
    const { ui } = this.#options;
    switch (step.kind) {
      case "structure": return ui.text("ui_history_change_structure");
      case "quick-fix": return ui.text("ui_history_change_quick_fix");
      case "copy-source": return ui.text("ui_history_change_copy_source");
      case "file-edit": return ui.text("ui_history_change_file_edit");
      case "recovery": return ui.text("ui_history_change_recovery");
      default: return ui.text("ui_history_change_edit");
    }
  }

  #queue(direction: "undo" | "redo"): void {
    const field = this.#options.focusedField ? this.#options.focusedField() : draftField(document.activeElement);
    const shown = field?.value;
    this.#chain = this.#chain
      .then(() => this.#stepHistory(direction, field, shown))
      .catch((error: unknown) => { this.#options.feedback.clientError = errorNotice(error); });
  }

  async #stepHistory(direction: "undo" | "redo", field: DraftField | undefined, shown: string | undefined): Promise<void> {
    const { drafts, feedback, selection } = this.#options;
    // The edits typed before the key press reach the draft first, so the step they made is the one taken back.
    await drafts.settledAll();
    const history = drafts.history;
    const path = direction === "undo" ? history.undoTarget(selection.currentDocument?.path) : history.redoTarget(selection.currentDocument?.path);
    if (path === undefined) {
      // Text a field shows that never reached the draft (a refused edit) gives way to the draft.
      this.revision += 1;
      await this.#stepSaved(direction);
      return;
    }
    if (this.#options.draftHistoryBlocked()) return;
    const step = direction === "undo" ? drafts.undo(path) : drafts.redo(path);
    if (step === undefined) return;
    feedback.clear();
    selection.showStep(step);
    this.revision += 1;
    // The restored text's entries and diagnostics: an error the undone edit caused leaves the panel.
    void drafts.refresh(path, (outcome) => {
      if (outcome.kind === "failed") feedback.clientError = errorNotice(outcome.error);
      else this.#options.diagnostics.record("validation", path, outcome.result);
    });
    await tick();
    restoreCaret(field, shown);
  }

  // Undoes or redoes the latest saved change through the host's history.
  async #stepSaved(direction: "undo" | "redo"): Promise<void> {
    const { feedback } = this.#options;
    if (this.#options.historyBlocked()) return;
    this.busy = true;
    feedback.clientError = undefined;
    try {
      const result = direction === "undo" ? await this.#options.bridge.undo() : await this.#options.bridge.redo();
      if (!result.ok) {
        const snapshot = this.#options.snapshot();
        if (snapshot !== undefined && result.history !== undefined) snapshot.history = result.history;
        feedback.clientError = result.message ?? notice(direction === "undo" ? "ui_feedback_undo_failed" : "ui_feedback_redo_failed");
        return;
      }
      if (result.snapshot === undefined) {
        await this.#options.load();
        feedback.operationMessage = result.message ?? notice(direction === "undo" ? "ui_feedback_undo_saved" : "ui_feedback_redo_saved");
        return;
      }
      this.#options.install(result.snapshot);
      feedback.operationMessage = notice(direction === "undo" ? "ui_feedback_undo_saved" : "ui_feedback_redo_saved");
    } catch (error) {
      feedback.clientError = errorNotice(error);
    } finally {
      this.busy = false;
    }
  }
}
