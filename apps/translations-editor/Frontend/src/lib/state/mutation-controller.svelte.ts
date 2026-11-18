import type { EditorLocaleDescription, EditorMutationPreview, EditorMutationRequest, WorkspaceSnapshot } from "../contracts";
import type { EditorBridge } from "../editor-bridge";
import { buildRows } from "../resource-model";
import { errorNotice, notice, type UiMessage } from "../ui-text";
import type { EditorFeedback } from "./feedback.svelte";
import type { SelectionController } from "./selection-controller.svelte";
import type { UnsavedWork } from "./unsaved-work.svelte";

export type MutationKind = EditorMutationRequest["kind"];

export interface MutationControllerOptions {
  bridge: Pick<EditorBridge, "describeLocale" | "previewMutation" | "applyMutation">;
  snapshot: () => WorkspaceSnapshot | undefined;
  selection: Pick<SelectionController, "selectedKey" | "selectedLocale" | "configureEditor">;
  unsaved: Pick<UnsavedWork, "hasUnsavedWork" | "confirmDiscard" | "discard">;
  feedback: EditorFeedback;
  /** Reloads the workspace after a change the host did not return a snapshot for. */
  load: () => Promise<void>;
  /** Installs the changed workspace with a reset selection. */
  install: (next: WorkspaceSnapshot) => void;
  historyBusy: () => boolean;
  setTimer?: (callback: () => void, delay: number) => unknown;
  clearTimer?: (handle: unknown) => void;
}

/**
 * The structural changes of a catalog: adding and removing a language, changing fallbacks, and creating, renaming,
 * duplicating and deleting a message. Each is previewed by the host first (the affected files) and committed with
 * the preview's confirmation token where it cannot be undone.
 */
export class MutationController {
  open = $state(false);
  kind = $state<MutationKind>("add-locale");
  locale = $state("");
  fallback = $state("");
  replacementFallback = $state("");
  copyFrom = $state("");
  /** What the typed new locale means in CLDR (canonical form, data ID, direction, plural and ordinal categories). */
  localeDescription = $state.raw<EditorLocaleDescription>();
  sourceKey = $state("");
  targetKey = $state("");
  initialValue = $state("");
  preview = $state.raw<EditorMutationPreview>();
  error = $state<UiMessage>();
  busy = $state(false);
  irreversibleConfirmed = $state(false);

  #describeTimer: unknown;
  #describeGeneration = 0;
  readonly #options: MutationControllerOptions;

  constructor(options: MutationControllerOptions) {
    this.#options = options;
  }

  /** Opens the dialog for `kind` on the selected message and locale; unsaved work is confirmed and dropped first. */
  prepare(kind: MutationKind): boolean {
    const { selection, unsaved } = this.#options;
    if (this.#options.historyBusy()) return false;
    const current = this.#options.snapshot();
    if (current?.catalog === undefined) return false;
    if (!unsaved.confirmDiscard("ui_confirm_change_workspace")) return false;
    if (unsaved.hasUnsavedWork) unsaved.discard();
    this.kind = kind;
    const firstNonDefault = current.catalog.locales.find((locale) => locale.tag !== current.catalog?.defaultLocale)?.tag ?? "";
    this.locale = kind === "remove-locale" || kind === "set-fallback"
      ? (selection.selectedLocale === current.catalog.defaultLocale ? firstNonDefault : selection.selectedLocale)
      : "";
    this.fallback = current.catalog.defaultLocale;
    this.localeDescription = undefined;
    this.replacementFallback = current.catalog.defaultLocale;
    this.copyFrom = current.catalog.defaultLocale;
    this.sourceKey = selection.selectedKey;
    this.targetKey = kind === "duplicate-key" ? `${selection.selectedKey}Copy` : kind === "create-key" ? "" : selection.selectedKey;
    this.initialValue = "";
    this.preview = undefined;
    this.error = undefined;
    this.busy = false;
    this.irreversibleConfirmed = false;
    this.open = true;
    return true;
  }

  /** The change the dialog's fields describe; the confirmation token only once the user confirmed it. */
  request(): EditorMutationRequest {
    return {
      kind: this.kind,
      locale: this.locale.trim() || undefined,
      fallback: this.fallback.trim() || undefined,
      replacementFallback: this.replacementFallback.trim() || undefined,
      copyFromLocale: this.copyFrom,
      sourceKey: this.sourceKey.trim() || undefined,
      targetKey: this.targetKey.trim() || undefined,
      initialValue: this.initialValue,
      confirmationToken: this.irreversibleConfirmed ? this.preview?.confirmationToken : undefined,
    };
  }

  /** A field changed: the preview no longer describes the change. */
  invalidate(): void {
    this.preview = undefined;
    this.error = undefined;
    this.irreversibleConfirmed = false;
  }

  /** Switches between the language operations, each starting from its defaults. */
  changeKind(value: string): void {
    const next = value as MutationKind;
    const snapshot = this.#options.snapshot();
    this.kind = next;
    const locales = snapshot?.catalog?.locales ?? [];
    const firstTarget = locales.find((locale) => locale.tag !== snapshot?.catalog?.defaultLocale)?.tag ?? "";
    this.locale = next === "add-locale" ? "" : firstTarget;
    this.fallback = snapshot?.catalog?.defaultLocale ?? "";
    this.replacementFallback = snapshot?.catalog?.defaultLocale ?? "";
    this.invalidate();
  }

  /** Describes the typed new locale in CLDR after a pause; a newer typed tag supersedes an older answer. */
  describeLocale(): void {
    const generation = ++this.#describeGeneration;
    const setTimer = this.#options.setTimer ?? ((callback, delay) => setTimeout(callback, delay));
    const clearTimer = this.#options.clearTimer ?? ((handle) => clearTimeout(handle as ReturnType<typeof setTimeout>));
    if (this.#describeTimer !== undefined) clearTimer(this.#describeTimer);
    const tag = this.locale.trim();
    if (tag === "") { this.localeDescription = undefined; return; }
    this.#describeTimer = setTimer(() => {
      void this.#options.bridge.describeLocale(tag).then((description) => {
        if (generation === this.#describeGeneration) this.localeDescription = description;
      }, () => { if (generation === this.#describeGeneration) this.localeDescription = undefined; });
    }, 200);
  }

  /** Uses the locale CLDR suggests for the typed one. */
  useSuggestedLocale(): void {
    this.locale = this.localeDescription?.suggested ?? this.locale;
    this.invalidate();
    this.describeLocale();
  }

  async previewChange(): Promise<void> {
    if (this.busy || this.#options.historyBusy()) return;
    this.busy = true;
    this.error = undefined;
    try {
      const result = await this.#options.bridge.previewMutation(this.request());
      this.preview = result;
      if (!result.ok) this.error = result.message ?? notice("ui_feedback_change_invalid");
    } catch (error) {
      this.error = errorNotice(error);
    } finally {
      this.busy = false;
    }
  }

  /** Commits the previewed change; a renamed or duplicated message is selected afterwards. */
  async apply(): Promise<void> {
    const { selection, feedback } = this.#options;
    if (this.busy || this.#options.historyBusy() || this.preview?.ok !== true ||
      (this.preview.requiresIrreversibleConfirmation && !this.irreversibleConfirmed)) return;
    this.busy = true;
    this.error = undefined;
    try {
      const result = await this.#options.bridge.applyMutation(this.request());
      if (!result.ok) {
        this.error = result.message ?? notice("ui_feedback_change_failed");
        this.preview = undefined;
        return;
      }
      if (result.snapshot === undefined) {
        await this.#options.load();
        feedback.operationMessage = result.message ?? notice("ui_feedback_workspace_updated");
        this.open = false;
        return;
      }
      const preferredKey = this.kind === "rename-key" || this.kind === "duplicate-key"
        ? this.targetKey
        : selection.selectedKey;
      this.#options.install(result.snapshot);
      if (buildRows(result.snapshot, {}).some((row) => row.key === preferredKey)) selection.selectedKey = preferredKey;
      this.open = false;
      // A committed change can carry a warning, such as review entries a locale removal could not drop.
      feedback.operationMessage = result.message ?? notice("ui_feedback_workspace_updated");
      selection.configureEditor();
    } catch (error) {
      this.error = errorNotice(error);
      this.preview = undefined;
    } finally {
      this.busy = false;
    }
  }
}
