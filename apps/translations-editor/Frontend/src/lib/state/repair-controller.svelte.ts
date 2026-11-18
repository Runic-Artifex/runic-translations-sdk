import type { EditorDocument, ValidationResult, WorkspaceSnapshot } from "../contracts";
import type { EditorBridge } from "../editor-bridge";
import { errorNotice, notice, type UiMessage } from "../ui-text";
import type { DraftStore } from "./draft-store.svelte";
import type { EditorFeedback } from "./feedback.svelte";

export interface RepairControllerOptions {
  bridge: Pick<EditorBridge, "validate" | "save" | "load">;
  drafts: Pick<DraftStore, "generation" | "hasNewer" | "drop">;
  feedback: EditorFeedback;
  /** Installs the workspace reloaded after the repaired file was saved, keeping the selection where it can. */
  install: (next: WorkspaceSnapshot, resetSelection: boolean) => void;
  /** Shows the selected message again from the reloaded workspace. */
  configureEditor: () => void;
  historyBusy: () => boolean;
  /** Asks the user to confirm the UI message `messageKey`. */
  confirm: (messageKey: string) => boolean;
}

/**
 * The repair of a malformed document: its text edited as a whole in the repair dialog, validated, and saved against
 * the revision it was opened at.
 */
export class RepairController {
  document = $state.raw<EditorDocument>();
  text = $state("");
  input = $state<HTMLTextAreaElement | null>(null);
  validation = $state.raw<ValidationResult>();
  busy = $state(false);
  message = $state<UiMessage>();
  /** Whether the repair text differs from the file it was opened from. */
  hasUnsaved = $derived(this.document !== undefined && this.text !== this.document.content);

  readonly #options: RepairControllerOptions;

  constructor(options: RepairControllerOptions) {
    this.#options = options;
  }

  /** Opens `document` for repair and validates its text; an unsaved repair of another file is confirmed first. */
  begin(document: EditorDocument): void {
    if (this.hasUnsaved && !this.#options.confirm("ui_feedback_repair_open_confirm")) return;
    this.document = document;
    this.text = document.content;
    this.message = undefined;
    this.validation = undefined;
    const content = this.text;
    void this.#options.bridge.validate(document.path, content).then(result => {
      if (this.document?.path === document.path && this.text === content) this.validation = result;
    }).catch(error => { this.message = errorNotice(error); });
  }

  /** Closes the repair; an unsaved repair is confirmed first. */
  close(): void {
    if (this.hasUnsaved && !this.#options.confirm("ui_feedback_discard_repair")) return;
    this.reset();
  }

  /** Drops the repair without asking. */
  reset(): void {
    this.document = undefined;
    this.text = "";
    this.message = undefined;
  }

  /** Validates the repaired text, saves it and reloads the workspace. */
  async save(): Promise<void> {
    const document = this.document;
    const { bridge, drafts } = this.#options;
    if (document === undefined || this.busy || this.#options.historyBusy()) return;
    this.busy = true;
    this.message = undefined;
    try {
      const checked = await bridge.validate(document.path, this.text);
      if (!checked.success) {
        this.message = checked.diagnostics[0]?.notice ?? checked.diagnostics[0]?.message ?? notice("ui_feedback_repair_invalid");
        return;
      }
      const result = await bridge.save(document.path, this.text, document.revision);
      if (!result.ok) {
        this.message = result.message ?? notice("ui_feedback_repair_failed");
        return;
      }
      await this.#reloadAfterSave(document.path, this.text, drafts.generation(document.path), result.message ?? notice("app_saved"));
      this.document = undefined;
    } catch (error) {
      this.message = errorNotice(error);
    } finally {
      this.busy = false;
    }
  }

  async #reloadAfterSave(path: string, content: string, generation: number, message: UiMessage): Promise<void> {
    const { bridge, drafts, feedback } = this.#options;
    const next = await bridge.load();
    const newer = drafts.hasNewer(path, content, generation);
    this.#options.install(next, false);
    if (!newer) drafts.drop(path);
    this.#options.configureEditor();
    feedback.operationMessage = newer ? notice("ui_feedback_newer_draft") : message;
  }
}
