import type {
  EditorCatalogImportPlan,
  EditorReviewFileResult,
  EditorReviewImportPreview,
  EditorXliffExportResult,
  EditorXliffImportPreview,
  WorkspaceSnapshot,
} from "../contracts";
import type { EditorBridge } from "../editor-bridge";
import { errorNotice, notice } from "../ui-text";
import type { EditorFeedback } from "./feedback.svelte";
import type { ReviewStore } from "./review-store.svelte";
import type { UnsavedWork } from "./unsaved-work.svelte";

export type CatalogImportFormat = "resx" | "i18next";

export interface InterchangeControllerOptions {
  bridge: Pick<EditorBridge,
    | "exportXliff" | "previewXliffImport" | "applyXliffImport"
    | "previewCatalogImport" | "applyCatalogImport"
    | "exportReviewJson" | "previewReviewJsonImport" | "applyReviewJsonImport">;
  /** The open workspace, whose review data and history an applied review import updates. */
  workspace: { snapshot: WorkspaceSnapshot | undefined; install(next: WorkspaceSnapshot, resetSelection: boolean): void };
  review: Pick<ReviewStore, "install">;
  unsaved: Pick<UnsavedWork, "confirmDiscard">;
  feedback: EditorFeedback;
}

/**
 * The interchange dialog: XLIFF export and import, review-file export and import, and the import of .resx and
 * i18next catalogs. Every import is a dry run first and is applied once by its confirmation token.
 */
export class InterchangeController {
  open = $state(false);
  busy = $state(false);
  xliffDirectory = $state("");
  xliffImportPath = $state("");
  reviewExportPath = $state("");
  reviewImportPath = $state("");
  xliffExport = $state.raw<EditorXliffExportResult>();
  xliffPreview = $state.raw<EditorXliffImportPreview>();
  reviewExport = $state.raw<EditorReviewFileResult>();
  reviewPreview = $state.raw<EditorReviewImportPreview>();
  // Import from .resx or i18next (runic-translations import): a dry run, then once by its confirmation token.
  catalogImportFrom = $state<CatalogImportFormat>("resx");
  catalogImportSource = $state("");
  catalogImportOverwrite = $state(false);
  catalogImportPlanned = $state.raw<{ inputs: string; plan: EditorCatalogImportPlan }>();
  // The dry run of the current inputs only: changing the format, the source or "overwrite" invalidates it, so "Import once"
  // never applies a plan made for other inputs (its token stays unused and expires with the next dry run).
  catalogImportInputs = $derived(JSON.stringify([this.catalogImportFrom, this.catalogImportSource.trim(), this.catalogImportOverwrite]));
  catalogImportPlan = $derived(this.catalogImportPlanned?.inputs === this.catalogImportInputs ? this.catalogImportPlanned.plan : undefined);

  readonly #options: InterchangeControllerOptions;

  constructor(options: InterchangeControllerOptions) {
    this.#options = options;
  }

  /** Opens the dialog on a catalog import of `source` (the project wizard's import step). */
  startCatalogImport(from: CatalogImportFormat, source: string): void {
    this.catalogImportFrom = from;
    this.catalogImportSource = source;
    this.catalogImportOverwrite = false;
    this.catalogImportPlanned = undefined;
    this.open = true;
  }

  async exportXliff(): Promise<void> {
    if (this.busy) return;
    this.busy = true;
    try {
      this.xliffExport = await this.#options.bridge.exportXliff(this.xliffDirectory.trim() || undefined);
    } catch (error) {
      this.xliffExport = { ok: false, message: errorNotice(error), documents: [], losses: [], lossless: false };
    } finally {
      this.busy = false;
    }
  }

  async previewXliffImport(): Promise<void> {
    if (this.busy || this.xliffImportPath.trim() === "") return;
    this.busy = true;
    try {
      this.xliffPreview = await this.#options.bridge.previewXliffImport(this.xliffImportPath.trim());
    } catch (error) {
      this.xliffPreview = {
        ok: false, message: errorNotice(error), requiresIrreversibleConfirmation: false,
        changes: [], addedCount: 0, changedCount: 0, removedCount: 0, unchangedCount: 0,
        reviewUpdateCount: 0, changesOverflowed: false, refusals: [],
      };
    } finally {
      this.busy = false;
    }
  }

  async applyXliffImport(): Promise<void> {
    const { feedback } = this.#options;
    if (this.busy || this.xliffPreview?.confirmationToken === undefined || !this.#options.unsaved.confirmDiscard("ui_confirm_apply_xliff")) return;
    this.busy = true;
    try {
      const result = await this.#options.bridge.applyXliffImport(this.xliffPreview.confirmationToken);
      feedback.operationMessage = result.message ?? (result.ok ? notice("ui_feedback_xliff_applied") : notice("ui_feedback_xliff_failed"));
      if (result.snapshot !== undefined) this.#options.workspace.install(result.snapshot, true);
      if (result.ok) this.xliffPreview = undefined;
    } catch (error) {
      feedback.operationMessage = errorNotice(error);
    } finally {
      this.busy = false;
    }
  }

  async previewCatalogImport(): Promise<void> {
    if (this.busy || this.catalogImportSource.trim() === "") return;
    this.busy = true;
    try {
      const inputs = this.catalogImportInputs;
      const plan = await this.#options.bridge.previewCatalogImport({ from: this.catalogImportFrom, source: this.catalogImportSource.trim(), overwrite: this.catalogImportOverwrite });
      this.catalogImportPlanned = { inputs, plan };
    } catch (error) {
      this.#options.feedback.operationMessage = errorNotice(error);
    } finally {
      this.busy = false;
    }
  }

  async applyCatalogImport(): Promise<void> {
    const { feedback } = this.#options;
    if (this.busy || this.catalogImportPlan?.confirmationToken === undefined || !this.#options.unsaved.confirmDiscard("ui_confirm_apply_catalog_import")) return;
    this.busy = true;
    try {
      const token = this.catalogImportPlan.confirmationToken;
      // The token is one-use on the host whether or not the import commits: a failed apply needs a fresh dry run.
      this.catalogImportPlanned = undefined;
      const result = await this.#options.bridge.applyCatalogImport(token);
      feedback.operationMessage = result.message ?? (result.ok ? notice("ui_feedback_catalog_imported") : notice("ui_backend_import_messages_invalid"));
      if (result.snapshot !== undefined) this.#options.workspace.install(result.snapshot, true);
    } catch (error) {
      feedback.operationMessage = errorNotice(error);
    } finally {
      this.busy = false;
    }
  }

  async exportReviewJson(): Promise<void> {
    if (this.busy) return;
    this.busy = true;
    try {
      this.reviewExport = await this.#options.bridge.exportReviewJson(this.reviewExportPath.trim() || undefined);
    } catch (error) {
      this.reviewExport = { ok: false, message: errorNotice(error), entryCount: 0 };
    } finally {
      this.busy = false;
    }
  }

  async previewReviewJsonImport(): Promise<void> {
    if (this.busy || this.reviewImportPath.trim() === "") return;
    this.busy = true;
    try {
      this.reviewPreview = await this.#options.bridge.previewReviewJsonImport(this.reviewImportPath.trim());
    } catch (error) {
      this.reviewPreview = {
        ok: false, message: errorNotice(error), requiresIrreversibleConfirmation: false,
        changes: [], addedCount: 0, changedCount: 0, removedCount: 0, changesOverflowed: false, refusals: [],
      };
    } finally {
      this.busy = false;
    }
  }

  async applyReviewJsonImport(): Promise<void> {
    const { feedback, workspace } = this.#options;
    if (this.busy || this.reviewPreview?.confirmationToken === undefined || !this.#options.unsaved.confirmDiscard("ui_confirm_apply_review")) return;
    this.busy = true;
    try {
      const result = await this.#options.bridge.applyReviewJsonImport(this.reviewPreview.confirmationToken);
      feedback.operationMessage = result.message ?? (result.ok ? notice("ui_feedback_review_applied") : notice("ui_feedback_review_failed"));
      if (result.ok && result.review !== undefined && workspace.snapshot !== undefined) {
        workspace.snapshot = { ...workspace.snapshot, review: result.review, history: result.history ?? workspace.snapshot.history };
        this.#options.review.install(workspace.snapshot);
        this.reviewPreview = undefined;
      }
    } catch (error) {
      feedback.operationMessage = errorNotice(error);
    } finally {
      this.busy = false;
    }
  }
}
