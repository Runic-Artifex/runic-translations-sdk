import type { EditorProjectCreationRequest, EditorProjectPlan, WorkspaceSnapshot } from "../contracts";
import type { EditorBridge } from "../editor-bridge";
import { errorNotice, notice, type UiMessage } from "../ui-text";
import type { EditorFeedback } from "./feedback.svelte";
import type { InterchangeController } from "./interchange-controller.svelte";
import type { UnsavedWork } from "./unsaved-work.svelte";

export type ProjectLocaleDraft = { id: number; tag: string; fallback: string };
export type WizardImportFormat = "" | "resx" | "i18next";

/** The wizard's steps: project, languages, generated API, import, review. */
export const projectWizardSteps = 5;

export interface ProjectWizardOptions {
  bridge: Pick<EditorBridge, "previewProject" | "createProject">;
  unsaved: Pick<UnsavedWork, "hasUnsavedWork" | "confirmDiscard" | "discard">;
  feedback: EditorFeedback;
  /** Opens the created project as the workspace. */
  install: (next: WorkspaceSnapshot) => void;
  /** The import step's dry run, opened in the interchange dialog. */
  interchange: Pick<InterchangeController, "startCatalogImport" | "previewCatalogImport">;
  historyBusy: () => boolean;
}

/**
 * The new-project wizard: where the project lives, its languages, the generated API, an optional import, and the
 * host's plan of the files it creates, which the last step confirms.
 */
export class ProjectWizardController {
  open = $state(false);
  step = $state(1);
  // The wizard's optional import step: after creating the project, the import's dry run opens for confirmation.
  importFrom = $state<WizardImportFormat>("");
  importSource = $state("");
  directory = $state("");
  catalog = $state("product");
  defaultLocale = $state("en");
  locales = $state<ProjectLocaleDraft[]>([]);
  namespace = $state("Customer.Product");
  className = $state("ProductText");
  includeStarter = $state(true);
  plan = $state.raw<EditorProjectPlan>();
  error = $state<UiMessage>();
  busy = $state(false);

  #nextLocaleId = 1;
  readonly #options: ProjectWizardOptions;

  constructor(options: ProjectWizardOptions) {
    this.#options = options;
  }

  /** Opens the wizard on its first step with the default answers. */
  start(): void {
    if (this.#options.historyBusy()) return;
    this.step = 1;
    this.importFrom = "";
    this.importSource = "";
    this.directory = "";
    this.catalog = "product";
    this.defaultLocale = "en";
    this.locales = [];
    this.namespace = "Customer.Product";
    this.className = "ProductText";
    this.includeStarter = true;
    this.plan = undefined;
    this.error = undefined;
    this.busy = false;
    this.open = true;
  }

  close(): void {
    if (!this.busy) this.open = false;
  }

  addLocale(): void {
    this.locales.push({ id: this.#nextLocaleId++, tag: "", fallback: "" });
  }

  removeLocale(id: number): void {
    this.locales = this.locales.filter((locale) => locale.id !== id);
  }

  back(): void {
    this.step -= 1;
    this.error = undefined;
  }

  /** The project the answers describe. */
  request(): EditorProjectCreationRequest {
    return {
      directory: this.directory.trim(),
      catalogId: this.catalog.trim(),
      defaultLocale: this.defaultLocale.trim(),
      additionalLocales: this.locales.map((locale) => ({
        tag: locale.tag.trim(),
        fallback: locale.fallback.trim() || undefined,
      })),
      codeNamespace: this.namespace.trim(),
      className: this.className.trim(),
      includeStarterMessage: this.includeStarter,
    };
  }

  /** Whether the current step's answers are complete; else the step's error says what is missing. */
  validateStep(): boolean {
    if (this.step === 1 && (this.directory.trim() === "" || this.catalog.trim() === "")) {
      this.error = notice("ui_feedback_project_directory_required");
      return false;
    }
    if (this.step === 2) {
      const tags = [this.defaultLocale.trim(), ...this.locales.map((locale) => locale.tag.trim())];
      if (tags.some((tag) => tag === "")) {
        this.error = notice("ui_feedback_locale_required");
        return false;
      }
      if (new Set(tags.map((tag) => tag.toLocaleLowerCase())).size !== tags.length) {
        this.error = notice("ui_feedback_locale_duplicate");
        return false;
      }
    }
    if (this.step === 3 && [this.namespace, this.className].some((value) => value.trim() === "")) {
      this.error = notice("ui_feedback_namespace_required");
      return false;
    }
    if (this.step === 4 && this.importFrom !== "" && this.importSource.trim() === "") {
      this.error = notice("ui_backend_import_source_required");
      return false;
    }
    this.error = undefined;
    return true;
  }

  /** Continues to the next step; after the import step the host plans the project, which the review step shows. */
  async advance(): Promise<void> {
    if (this.#options.historyBusy()) return;
    if (!this.validateStep()) return;
    if (this.step < 4) {
      this.step += 1;
      return;
    }
    this.busy = true;
    try {
      const plan = await this.#options.bridge.previewProject(this.request());
      this.plan = plan;
      if (!plan.ok) {
        this.error = plan.message ?? notice("ui_feedback_project_invalid");
        return;
      }
      this.step = projectWizardSteps;
    } catch (error) {
      this.error = errorNotice(error);
    } finally {
      this.busy = false;
    }
  }

  /** Creates the planned project and opens it; with an import, its dry run opens for confirmation. */
  async create(): Promise<void> {
    const { unsaved, interchange } = this.#options;
    if (this.plan?.ok !== true || this.busy || this.#options.historyBusy()) return;
    if (!unsaved.confirmDiscard("ui_confirm_create_project")) return;
    if (unsaved.hasUnsavedWork) unsaved.discard();
    this.busy = true;
    this.error = undefined;
    try {
      const result = await this.#options.bridge.createProject(this.request());
      if (!result.ok || result.snapshot === undefined) {
        this.error = result.message ?? notice("ui_feedback_project_failed");
        return;
      }
      this.#options.install(result.snapshot);
      this.#options.feedback.operationMessage = notice("ui_feedback_project_created");
      this.open = false;
      if (this.importFrom !== "" && this.importSource.trim() !== "") {
        // The import step: its dry run opens in the interchange dialog, applied only after confirmation.
        interchange.startCatalogImport(this.importFrom, this.importSource.trim());
        this.busy = false;
        await interchange.previewCatalogImport();
      }
    } catch (error) {
      this.error = errorNotice(error);
    } finally {
      this.busy = false;
    }
  }
}
