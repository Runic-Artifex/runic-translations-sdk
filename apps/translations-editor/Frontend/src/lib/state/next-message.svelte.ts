import type { NextMessageDirection, NextMessageKind } from "../next-message";
import { notice, type UiMessage } from "../ui-text";
import type { SaveOutcome } from "./save-coordinator.svelte";
import type { SelectionController } from "./selection-controller.svelte";
import type { WorkspaceView } from "./workspace-view.svelte";

export interface NextMessageControllerOptions {
  selection: Pick<SelectionController, "selectedKey" | "selectedLocale" | "currentStarterTarget" | "selectVisible">;
  view: Pick<WorkspaceView, "stepKind" | "adjacent" | "visibleRows" | "currentIsStale">;
  /** The one save (SaveCoordinator through the workspace's notices); nothing when it did not run. */
  saveAll: () => Promise<SaveOutcome | undefined>;
  /** Whether a save or another operation keeps Save and next from starting. */
  busy: () => boolean;
  localeName: (tag: string) => string;
}

/** The selected message's state when it was selected, kept while it stays selected. */
type EntryState = { key: string; locale: string; missing: boolean; stale: boolean };

/**
 * "Next missing" and "Save and next" (D074.1): Next and Previous select the next message of the step kind in tree
 * order among those the list shows, wrapping at the end; Save and next saves all unsaved work through the one save and
 * then selects the next message of the kind the selected one had, or stays and says why.
 */
export class NextMessageController {
  /** Missing, or the outdated or to-review messages while the list's filter shows only those. */
  kind = $derived.by((): NextMessageKind => this.#options.view.stepKind);
  /** Where Next and Previous go; nothing when no other message is of the kind. */
  nextTarget = $derived.by(() => this.#options.view.adjacent(this.kind, "next"));
  previousTarget = $derived.by(() => this.#options.view.adjacent(this.kind, "previous"));
  /**
   * The kind Save and next steps through: the filter's outdated or to-review messages, else the kind the selected
   * message had when it was selected (missing or outdated), else missing.
   */
  saveAndNextKind = $derived.by((): NextMessageKind => {
    const entry = this.#entryState();
    if (this.kind !== "missing") return this.kind;
    return entry.missing ? "missing" : entry.stale ? "stale" : "missing";
  });
  /**
   * Whether the composer offers Save and next: the selected message was missing or outdated when it was selected; in a
   * content-editing project always, to the next text in tree order (D074.2).
   */
  saveAndNextOffered = $derived.by(() => {
    if (this.kind === "text") return true;
    const entry = this.#entryState();
    return entry.missing || entry.stale;
  });
  /**
   * What the latest step or Save and next reported: why it stayed, or that it wrapped. It holds while the same message,
   * locale and step kind stay selected.
   */
  notice = $derived.by((): UiMessage | undefined => {
    const reported = this.#notice;
    const { selectedKey, selectedLocale } = this.#options.selection;
    return reported !== undefined && reported.key === selectedKey && reported.locale === selectedLocale && reported.kind === this.kind
      ? reported.message
      : undefined;
  });
  running = $state(false);

  #notice = $state.raw<{ key: string; locale: string; kind: NextMessageKind; message: UiMessage }>();
  #entry: EntryState | undefined;
  readonly #options: NextMessageControllerOptions;

  constructor(options: NextMessageControllerOptions) {
    this.#options = options;
  }

  // Missing means the saved files lack it (its starter is open); outdated, that its review state is stale. Both stay
  // set while the message stays selected, so a save or a confirmation does not change what Save and next steps to.
  #entryState(): EntryState {
    const { selection, view } = this.#options;
    const key = selection.selectedKey;
    const locale = selection.selectedLocale;
    const missing = selection.currentStarterTarget !== undefined;
    const stale = view.currentIsStale;
    if (this.#entry === undefined || this.#entry.key !== key || this.#entry.locale !== locale) {
      this.#entry = { key, locale, missing, stale };
    } else {
      this.#entry = { key, locale, missing: this.#entry.missing || missing, stale: this.#entry.stale || stale };
    }
    return this.#entry;
  }

  #report(message: UiMessage | undefined): void {
    const { selectedKey: key, selectedLocale: locale } = this.#options.selection;
    this.#notice = message === undefined ? undefined : { key, locale, kind: this.kind, message };
  }

  /** Selects the next (or previous) message of `kind`; says so when there is no other one, or when it wrapped. */
  step(direction: NextMessageDirection, kind: NextMessageKind = this.kind): boolean {
    const { selection, view } = this.#options;
    const target = view.adjacent(kind, direction);
    if (target === undefined) {
      this.#report(noneNotice(kind, this.#options.localeName(selection.selectedLocale)));
      return false;
    }
    selection.selectVisible(target.key, view.visibleRows);
    this.#report(target.wrapped ? notice(direction === "next" ? "ui_workbench_wrapped_next" : "ui_workbench_wrapped_previous") : undefined);
    return true;
  }

  /**
   * Ctrl+Enter (⌘Enter): saves all unsaved work, then selects the next message of the kind the selected one had. An
   * invalid text, a refused or failed save keeps the selection and says why; the issues list the errors.
   */
  async saveAndNext(): Promise<boolean> {
    if (this.running || this.#options.busy()) return false;
    const kind = this.saveAndNextKind;
    this.running = true;
    this.#report(undefined);
    try {
      const outcome = await this.#options.saveAll();
      if (outcome === undefined) return false;
      if (outcome.kind === "invalid") {
        this.#report(notice("ui_workbench_save_and_next_invalid"));
        return false;
      }
      if (outcome.kind !== "saved" && outcome.kind !== "nothing") {
        this.#report(notice("ui_workbench_save_and_next_failed"));
        return false;
      }
      return this.step("next", kind);
    } finally {
      this.running = false;
    }
  }
}

/** "No other message is missing in German.", the same for outdated and to-review messages, and "There is no other text." */
export function noneNotice(kind: NextMessageKind, locale: string): UiMessage {
  if (kind === "text") return notice("ui_workbench_none_text");
  return notice(kind === "missing" ? "ui_workbench_none_missing" : kind === "stale" ? "ui_workbench_none_stale" : "ui_workbench_none_needs_review", { locale });
}
