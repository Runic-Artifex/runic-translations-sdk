import type { EditorAbout } from "../contracts";
import type { EditorBridge } from "../editor-bridge";
import type { LocalStateSummary } from "../local-state";
import { errorNotice, notice, type UiMessage } from "../ui-text";

export interface AboutControllerOptions {
  bridge: Pick<EditorBridge, "about" | "createDiagnosticBundle" | "revealDiagnosticBundle" | "deleteDiagnosticBundle">;
  /** The user-owned native editor state: what it holds, and clearing it. */
  localState: { inspect(): LocalStateSummary; clear(): Promise<number> };
  /** The cleared state held recent projects and recovery copies: the page forgets them too. */
  localStateCleared: () => void;
  /** Asks the user to confirm the UI message `messageKey`. */
  confirm: (messageKey: string) => boolean;
  writeClipboard: (text: string) => Promise<void>;
  historyBusy: () => boolean;
}

/**
 * The About dialog: the build and runtime the editor runs on, the diagnostic bundle the user can create, reveal, copy
 * and delete (it is never uploaded), and the inspector of the user-owned local state.
 */
export class AboutController {
  open = $state(false);
  info = $state.raw<EditorAbout>();
  busy = $state(false);
  diagnosticBusy = $state(false);
  diagnosticMessage = $state<UiMessage>();
  diagnosticBundlePath = $state<string>();
  localStateSummary = $state.raw<LocalStateSummary>();
  localStateMessage = $state<UiMessage>();

  readonly #options: AboutControllerOptions;

  constructor(options: AboutControllerOptions) {
    this.#options = options;
  }

  /** Opens the dialog; the build information is read from the host once. */
  async show(): Promise<void> {
    if (this.#options.historyBusy()) return;
    this.open = true;
    this.diagnosticMessage = undefined;
    this.localStateSummary = this.#options.localState.inspect();
    this.localStateMessage = undefined;
    if (this.info !== undefined || this.busy) return;
    this.busy = true;
    try {
      this.info = await this.#options.bridge.about();
    } catch (error) {
      this.diagnosticMessage = errorNotice(error);
    } finally {
      this.busy = false;
    }
  }

  async clearLocalState(): Promise<void> {
    const summary = this.localStateSummary ?? this.#options.localState.inspect();
    if (summary.entries === 0) return;
    if (!this.#options.confirm("ui_feedback_clear_state_confirm")) return;
    // Called from a click handler: a failure, such as a host that went away, is reported here, never left unhandled.
    let removed: number;
    try {
      removed = await this.#options.localState.clear();
    } catch (error) {
      this.localStateMessage = errorNotice(error);
      return;
    }
    this.#options.localStateCleared();
    this.localStateSummary = this.#options.localState.inspect();
    this.localStateMessage = notice("ui_count_state_removed", { count: removed });
  }

  async createDiagnosticBundle(): Promise<void> {
    if (this.diagnosticBusy || this.#options.historyBusy()) return;
    this.diagnosticBusy = true;
    this.diagnosticMessage = undefined;
    try {
      const result = await this.#options.bridge.createDiagnosticBundle();
      this.diagnosticBundlePath = result.ok ? result.path : undefined;
      this.diagnosticMessage = result.ok
        ? notice("ui_feedback_diagnostics_saved")
        : result.message ?? notice("ui_feedback_diagnostics_failed");
    } catch (error) {
      this.diagnosticMessage = errorNotice(error);
    } finally {
      this.diagnosticBusy = false;
    }
  }

  async revealDiagnosticBundle(): Promise<void> {
    if (this.diagnosticBundlePath === undefined || this.diagnosticBusy) return;
    this.diagnosticBusy = true;
    try {
      const result = await this.#options.bridge.revealDiagnosticBundle(this.diagnosticBundlePath);
      this.diagnosticMessage = result.ok ? notice("ui_feedback_diagnostics_opened") : result.message ?? notice("ui_feedback_diagnostics_open_failed");
    } catch (error) {
      this.diagnosticMessage = errorNotice(error);
    } finally {
      this.diagnosticBusy = false;
    }
  }

  async copyDiagnosticBundlePath(): Promise<void> {
    if (this.diagnosticBundlePath === undefined) return;
    try {
      await this.#options.writeClipboard(this.diagnosticBundlePath);
      this.diagnosticMessage = notice("ui_feedback_diagnostics_copied");
    } catch {
      this.diagnosticMessage = notice("ui_feedback_diagnostics_copy_failed");
    }
  }

  async deleteDiagnosticBundle(): Promise<void> {
    if (this.diagnosticBundlePath === undefined || this.diagnosticBusy) return;
    if (!this.#options.confirm("ui_feedback_diagnostics_delete_confirm")) return;
    this.diagnosticBusy = true;
    try {
      const result = await this.#options.bridge.deleteDiagnosticBundle(this.diagnosticBundlePath);
      if (result.ok) this.diagnosticBundlePath = undefined;
      this.diagnosticMessage = result.ok ? notice("ui_feedback_diagnostics_deleted") : result.message ?? notice("ui_feedback_diagnostics_delete_failed");
    } catch (error) {
      this.diagnosticMessage = errorNotice(error);
    } finally {
      this.diagnosticBusy = false;
    }
  }
}
