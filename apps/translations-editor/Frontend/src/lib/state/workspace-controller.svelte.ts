import type { WorkspaceSnapshot } from "../contracts";
import type { EditorBridge } from "../editor-bridge";
import { errorNotice, notice } from "../ui-text";
import type { DiagnosticsStore } from "./diagnostics-store.svelte";
import type { DraftStore } from "./draft-store.svelte";
import type { EditorFeedback } from "./feedback.svelte";
import type { MessageDraftStore } from "./message-draft-store.svelte";
import type { ReviewStore } from "./review-store.svelte";
import type { SaveCoordinator, SaveOutcome } from "./save-coordinator.svelte";
import type { SelectionController } from "./selection-controller.svelte";
import type { UnsavedWork } from "./unsaved-work.svelte";

export type RecentProject = { root: string; catalogId: string; openedAt: string };

export const recentProjectsStateKey = "runic-translations:recent:1";
const recentProjectLimit = 8;

/** The recent projects kept in the native editor state; an unreadable record is none. */
export function readRecentProjects(raw: string | null): RecentProject[] {
  try {
    const value = JSON.parse(raw ?? "[]") as unknown;
    if (!Array.isArray(value)) return [];
    return value.filter((item): item is RecentProject =>
      typeof item === "object" && item !== null &&
      typeof (item as RecentProject).root === "string" &&
      typeof (item as RecentProject).catalogId === "string" &&
      typeof (item as RecentProject).openedAt === "string").slice(0, recentProjectLimit);
  } catch {
    return [];
  }
}

export interface WorkspaceControllerOptions {
  bridge: Pick<EditorBridge, "load" | "openWorkspace" | "pickWorkspace">;
  drafts: DraftStore;
  messageDrafts: Pick<MessageDraftStore, "reset" | "retain">;
  review: Pick<ReviewStore, "dirty" | "reset" | "install">;
  diagnostics: Pick<DiagnosticsStore, "clear">;
  save: SaveCoordinator;
  selection: Pick<SelectionController, "install" | "rememberCatalogView" | "configureEditor">;
  unsaved: Pick<UnsavedWork, "hasUnsavedWork" | "confirmDiscard" | "discard">;
  feedback: EditorFeedback;
  historyBusy: () => boolean;
  /** Called when a workspace was loaded or opened anew: the notices of the previous one no longer apply. */
  reloaded: (kind: "load" | "open") => void;
  storage: { get(key: string): string | null; set(key: string, value: string): void };
  now?: () => Date;
}

/**
 * The open workspace: its snapshot, loading, opening another project or catalog, the recent projects, and the one
 * save of all unsaved work. Installing a snapshot hands it to the stores and places the selection in it.
 */
export class WorkspaceController {
  snapshot = $state.raw<WorkspaceSnapshot>();
  loading = $state(true);
  openDirectory = $state("");
  openingWorkspace = $state(false);
  pickingWorkspace = $state(false);
  openDialogOpen = $state(false);
  recentProjects = $state<RecentProject[]>([]);
  /**
   * runic.json changed through a library operation (the catalog joined "overrides") while there was unsaved work, so
   * the workspace was not reloaded: the page says so and offers the reload once the work is saved.
   */
  projectReloadPending = $state(false);

  readonly #options: WorkspaceControllerOptions;

  constructor(options: WorkspaceControllerOptions) {
    this.#options = options;
  }

  /** Reloads the workspace from disk; unsaved work is dropped only after the user confirms it (`confirmDiscard`). */
  async load(confirmDiscard: boolean): Promise<void> {
    const { unsaved, feedback } = this.#options;
    if (unsaved.hasUnsavedWork) {
      if (!confirmDiscard || !unsaved.confirmDiscard("ui_confirm_reload_workspace")) return;
      unsaved.discard();
    }
    this.loading = true;
    feedback.clear();
    try {
      const next = await this.#options.bridge.load();
      this.install(next, true);
      this.#options.reloaded("load");
      this.projectReloadPending = false;
    } catch (error) {
      feedback.clientError = errorNotice(error);
    } finally {
      this.loading = false;
    }
  }

  /**
   * Makes `next` the open workspace. With `resetSelection` the stores start over (a newly opened or reloaded
   * workspace); without it they keep their unsaved work that still applies (a save, an undone saved change).
   */
  install(next: WorkspaceSnapshot, resetSelection: boolean): void {
    const { drafts, messageDrafts, review } = this.#options;
    drafts.nextWorkspace();
    this.snapshot = next;
    if (resetSelection) drafts.reset(next);
    else drafts.prune();
    // A starter whose message the saved files now have (it was saved) is done.
    if (resetSelection) messageDrafts.reset();
    else messageDrafts.retain((target) => !this.savedHasMessage(target.key, target.locale));
    // Unsaved review changes stay on top of the review data they started from.
    if (!review.dirty) {
      if (resetSelection) review.reset(next);
      else review.install(next);
    }
    this.#options.diagnostics.clear();
    this.#options.selection.install(next, resetSelection);
    this.#rememberProject(next);
  }

  /** Whether the saved files have `key` in `locale`: a missing translation's starter is kept until they do. */
  savedHasMessage(key: string, locale: string): boolean {
    return this.snapshot?.documents.some((document) => document.locale === locale && !document.isManifest &&
      (document.entries?.some((entry) => entry.key === key) ?? document.path.endsWith(`/${key}.mf2`))) === true;
  }

  /**
   * Opens a workspace: the catalog `catalogId` of the open workspace's root, the directory `directoryOverride`, or
   * else the directory typed in the open dialog.
   */
  async open(catalogId?: string, directoryOverride?: string): Promise<void> {
    const { unsaved, feedback } = this.#options;
    if (this.openingWorkspace || this.#options.historyBusy()) return;
    if (!unsaved.confirmDiscard("ui_confirm_open_workspace")) return;
    this.#options.selection.rememberCatalogView();
    if (unsaved.hasUnsavedWork) unsaved.discard();
    const directory = directoryOverride ?? (catalogId === undefined ? this.openDirectory.trim() : this.snapshot?.root ?? "");
    if (directory === "") {
      feedback.clientError = notice("ui_feedback_workspace_directory_required");
      return;
    }
    this.openingWorkspace = true;
    feedback.clientError = undefined;
    try {
      const result = await this.#options.bridge.openWorkspace({ directory, catalogId });
      if (!result.ok || result.snapshot === undefined) {
        feedback.clientError = result.message ?? notice("ui_feedback_workspace_open_failed");
        return;
      }
      this.install(result.snapshot, true);
      this.#options.reloaded("open");
      this.openDirectory = result.snapshot.root;
      this.openDialogOpen = false;
    } catch (error) {
      feedback.clientError = errorNotice(error);
    } finally {
      this.openingWorkspace = false;
    }
  }

  /** Lets the user choose the workspace directory in the system's folder picker. */
  async pick(): Promise<void> {
    const { feedback } = this.#options;
    if (this.pickingWorkspace || this.openingWorkspace || this.#options.historyBusy()) return;
    this.pickingWorkspace = true;
    feedback.clientError = undefined;
    try {
      const result = await this.#options.bridge.pickWorkspace();
      if (result.ok && result.directory !== undefined) {
        this.openDirectory = result.directory;
      } else if (!result.cancelled && result.message !== undefined) {
        feedback.clientError = result.message;
      }
    } catch (error) {
      feedback.clientError = errorNotice(error);
    } finally {
      this.pickingWorkspace = false;
    }
  }

  /** Opens the dialog that opens another workspace, starting from the open one's root. */
  showOpenDialog(): void {
    if (this.#options.historyBusy()) return;
    const current = this.snapshot;
    if (current === undefined) return;
    this.openDirectory = current.root;
    this.openDialogOpen = true;
  }

  /**
   * Ctrl+S and the toolbar's Save: every unsaved document (each validated; an invalid one blocks the save and its
   * diagnostics are shown), then the review changes. Says how the save ended; nothing when another save or a history
   * step is running.
   */
  async saveAll(): Promise<SaveOutcome | undefined> {
    const { save, feedback } = this.#options;
    if (save.saving || this.#options.historyBusy()) return undefined;
    feedback.clear();
    const outcome = await save.saveAll();
    if (outcome.kind === "nothing" || outcome.kind === "invalid") return outcome;
    if (outcome.saved.length > 0) this.#options.selection.configureEditor();
    const newer = outcome.kind !== "failed" && outcome.kind !== "refused" && outcome.newer;
    if (outcome.kind === "saved") feedback.operationMessage = newer ? notice("ui_feedback_newer_draft") : outcome.message;
    else if (outcome.kind === "failed") feedback.clientError = errorNotice(outcome.error);
    else {
      feedback.clientError = outcome.message;
      if (newer) feedback.operationMessage = notice("ui_feedback_newer_draft");
    }
    return outcome;
  }

  /** Reads the recent projects from the native editor state, once it has loaded. */
  loadRecentProjects(): void {
    this.recentProjects = readRecentProjects(this.#options.storage.get(recentProjectsStateKey));
  }

  #rememberProject(value: WorkspaceSnapshot): void {
    const catalogId = value.catalog?.id;
    if (catalogId === undefined) return;
    const entry = { root: value.root, catalogId, openedAt: (this.#options.now?.() ?? new Date()).toISOString() };
    this.recentProjects = [entry, ...this.recentProjects.filter((item) => item.root !== entry.root || item.catalogId !== catalogId)].slice(0, recentProjectLimit);
    this.#options.storage.set(recentProjectsStateKey, JSON.stringify(this.recentProjects));
  }
}
