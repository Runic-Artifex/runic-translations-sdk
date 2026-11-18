import { untrack } from "svelte";
import type { EditorDocumentState } from "../../generated/editorDocument";
import { catalogSelectionStateKey, readCatalogSelection, writeCatalogSelection } from "../catalog-selection";
import type { WorkspaceSnapshot } from "../contracts";
import type { EditorBridge } from "../editor-bridge";
import type { EditorMode } from "../EditorModeSwitcher.svelte";
import type { MessageFilter } from "../MessageToolbar.svelte";
import { buildRows, type TranslationRow } from "../resource-model";
import { reviewIdentity } from "../review-model";
import { errorNotice } from "../ui-text";
import type { DraftStep } from "./draft-history.svelte";
import type { DraftStore } from "./draft-store.svelte";
import type { DiagnosticsStore } from "./diagnostics-store.svelte";
import type { EditorFeedback } from "./feedback.svelte";
import type { MessageDraftStore, StarterTarget } from "./message-draft-store.svelte";
import type { PreviewController } from "./preview-controller.svelte";
import type { ReviewStore } from "./review-store.svelte";

/** The view of a catalog restored when the user switches back to it: selection, list filter and editor mode. */
export type CatalogView = { key: string; locale: string; filter: MessageFilter; query: string; mode: EditorMode };

/** The open project's runic.json: the first catalog summary is the open one. */
export function currentManifest(value: WorkspaceSnapshot | undefined): string {
  return value?.catalogs[0]?.manifestPaths[0] ?? value?.catalog?.id ?? "";
}

/** A catalog's identity for its remembered view: its runic.json, since two projects can share a catalog ID. */
export function catalogViewKey(value: WorkspaceSnapshot): string {
  return `${value.root}\n${currentManifest(value)}`;
}

/** The part of the native editor state the selection reads and writes. */
export interface SelectionStorage {
  get(key: string): string | null;
  set(key: string, value: string): void;
}

export interface SelectionControllerOptions {
  bridge: Pick<EditorBridge, "openDocument">;
  snapshot: () => WorkspaceSnapshot | undefined;
  drafts: DraftStore;
  messageDrafts: MessageDraftStore;
  review: Pick<ReviewStore, "index">;
  preview: PreviewController;
  diagnostics: Pick<DiagnosticsStore, "clear">;
  feedback: EditorFeedback;
  /** Whether the saved files have `key` in `locale`: a missing translation's starter is kept until they do. */
  savedHasMessage: (key: string, locale: string) => boolean;
  /** Whether an operation in progress (a save, a reload, a recovery) keeps the editor from changing. */
  editingBlocked: () => boolean;
  storage: SelectionStorage;
}

/**
 * The selected message and locale, the editor mode and the text the editor shows, the message list's filter and
 * search, and the navigation between them. `configureEditor` shows a selection: it opens the message's document,
 * starts its preview and, for a missing translation, its starter.
 */
export class SelectionController {
  selectedKey = $state("");
  selectedLocale = $state("");
  selectedDocumentPath = $state("");
  selectedDocumentViewState = $state.raw<EditorDocumentState>();
  filter = $state<MessageFilter>("all");
  query = $state("");
  mode = $state<EditorMode>("translation");
  editorText = $state("");
  /** The span the file view selects, for a diagnostic chosen in the validation area; a new token selects it again. */
  sourceSelection = $state.raw<{ start: number; end: number; token: number }>();

  /** The workspace's messages with the unsaved drafts. */
  rows = $derived.by(() => buildRows(this.#options.snapshot(), this.#options.drafts.drafts, this.#options.drafts.parsed));
  selectedRow = $derived.by(() => this.rows.find((row) => row.key === this.selectedKey));
  currentCell = $derived(this.selectedRow?.cells[this.selectedLocale]);
  sourceContext = $derived(this.selectedRow?.cells[this.#defaultLocale()]?.entry?.context);
  /** The examples of the message: the translation's own, else the base text's. */
  currentExamples = $derived(this.currentCell?.entry?.context?.examples.length ? this.currentCell.entry.context.examples : this.sourceContext?.examples ?? []);
  currentDocument = $derived.by(() => {
    const document = this.#options.snapshot()?.documents.find((candidate) => candidate.path === this.selectedDocumentPath) ?? this.currentCell?.document;
    const opened = this.selectedDocumentViewState;
    return document && opened?.path === document.path && opened.fileRevision === document.revision
      ? { ...document, content: opened.content, revision: opened.fileRevision }
      : document;
  });
  currentContent = $derived.by(() => this.currentDocument === undefined
    ? undefined
    : this.#options.drafts.content(this.currentDocument.path, this.currentDocument.content));
  /**
   * A translation the saved files lack is written in the composer on an empty starter shaped for the target locale
   * (#225 P2): where it goes, the starter once it exists, and whether it is still being prepared.
   */
  currentStarterTarget = $derived.by((): StarterTarget | undefined => {
    if (this.currentDocument === undefined || this.selectedRow === undefined || this.selectedLocale === this.#defaultLocale()) return undefined;
    if (this.#options.savedHasMessage(this.selectedKey, this.selectedLocale)) return undefined;
    return { path: this.currentDocument.path, saved: this.currentDocument.content, key: this.selectedKey, locale: this.selectedLocale };
  });
  currentStarter = $derived.by(() => this.currentStarterTarget === undefined ? undefined : this.#options.messageDrafts.get(this.currentStarterTarget));
  starterLoading = $derived.by(() => this.currentStarterTarget !== undefined && this.currentStarter === undefined &&
    this.#options.messageDrafts.isLoading(this.currentStarterTarget));
  sourceAuthoring = $derived(this.selectedRow?.cells[this.#defaultLocale()]?.entry?.authoring);

  #selectionToken = 0;
  // The selection of each catalog of a workspace, restored when the user switches back to it (L5-4).
  readonly #catalogViews = new Map<string, CatalogView>();
  readonly #options: SelectionControllerOptions;

  constructor(options: SelectionControllerOptions) {
    this.#options = options;
  }

  #defaultLocale(): string {
    return this.#options.snapshot()?.catalog?.defaultLocale ?? "";
  }

  /**
   * Places the selection in a newly installed workspace: the first message and the default locale, unless the
   * selection still exists; on a reset, the view this session had of the catalog, or else the selection it had when
   * it was last open (#225 P24). Then shows it.
   */
  install(next: WorkspaceSnapshot, resetSelection: boolean): void {
    const nextRows = buildRows(next, {});
    if (resetSelection || !nextRows.some((row) => row.key === this.selectedKey)) {
      this.selectedKey = nextRows[0]?.key ?? "";
    }
    if (resetSelection || !next.catalog?.locales.some((locale) => locale.tag === this.selectedLocale)) {
      this.selectedLocale = next.catalog?.defaultLocale ?? next.catalog?.locales[0]?.tag ?? "";
    }
    const remembered = resetSelection && next.catalog ? this.#catalogViews.get(catalogViewKey(next)) : undefined;
    // Without a view of this session, the selection the catalog had when it was last open (#225 P24).
    const stored = resetSelection && next.catalog && remembered === undefined
      ? readCatalogSelection(this.#options.storage.get(catalogSelectionStateKey), catalogViewKey(next))
      : undefined;
    if (stored) {
      if (nextRows.some((row) => row.key === stored.key)) this.selectedKey = stored.key;
      if (next.catalog?.locales.some((locale) => locale.tag === stored.locale)) this.selectedLocale = stored.locale;
    }
    if (remembered) {
      if (nextRows.some((row) => row.key === remembered.key)) this.selectedKey = remembered.key;
      if (next.catalog?.locales.some((locale) => locale.tag === remembered.locale)) this.selectedLocale = remembered.locale;
      this.filter = remembered.filter;
      this.query = remembered.query;
      this.mode = remembered.mode;
    }
    this.configureEditor();
  }

  /** Keeps this session's view of the open catalog, restored when the user switches back to it. */
  rememberCatalogView(): void {
    const snapshot = this.#options.snapshot();
    if (snapshot?.catalog) {
      this.#catalogViews.set(catalogViewKey(snapshot), {
        key: this.selectedKey, locale: this.selectedLocale, filter: this.filter, query: this.query, mode: this.mode,
      });
    }
  }

  /**
   * Stores the selected locale and message of the open catalog for the next launch (#225 P24). Run in an effect: it
   * tracks the workspace and the selection only.
   */
  persistSelection(): void {
    const current = this.#options.snapshot();
    const key = this.selectedKey;
    const locale = this.selectedLocale;
    if (current?.catalog === undefined || key === "" || locale === "") return;
    untrack(() => {
      const catalog = catalogViewKey(current);
      const raw = this.#options.storage.get(catalogSelectionStateKey);
      const stored = readCatalogSelection(raw, catalog);
      if (stored?.key === key && stored.locale === locale) return;
      try {
        this.#options.storage.set(catalogSelectionStateKey, writeCatalogSelection(raw, catalog, { key, locale }));
      } catch {
        // Without the native state (a test page), the selection is not remembered.
      }
    });
  }

  selectRow(row: TranslationRow): void {
    const nextKey = row.key;
    this.#options.diagnostics.clear();
    this.#options.feedback.clear();
    this.selectedKey = nextKey;
    this.configureEditor(undefined, nextKey, this.selectedLocale);
  }

  /** Selects the visible message `key`; a message the list does not show is not selected. */
  selectVisible(key: string, visibleRows: readonly TranslationRow[]): void {
    const row = visibleRows.find((candidate) => candidate.key === key);
    if (row !== undefined) this.selectRow(row);
  }

  selectLocale(locale: string): void {
    this.#options.diagnostics.clear();
    this.#options.feedback.clear();
    this.selectedLocale = locale;
    this.configureEditor(undefined, this.selectedKey, locale);
  }

  chooseMode(nextMode: EditorMode): void {
    if (this.#options.editingBlocked()) return;
    this.mode = nextMode;
    this.#options.feedback.clientError = undefined;
    this.configureEditor(nextMode);
  }

  /** Shows the whole list again. */
  clearFilter(): void {
    this.filter = "all";
    this.query = "";
  }

  /**
   * Shows the selection `key` in `locale`: opens its document, sets the text the editor shows, takes the preview's
   * samples from its review or examples and starts the preview, and opens the starter of a missing translation.
   */
  configureEditor(preferredMode?: EditorMode, key = this.selectedKey, locale = this.selectedLocale): void {
    const { bridge, drafts, messageDrafts, preview, feedback } = this.#options;
    const snapshot = this.#options.snapshot();
    this.sourceSelection = undefined;
    const row = buildRows(snapshot, drafts.drafts, drafts.parsed).find((candidate) => candidate.key === key);
    const cell = row?.cells[locale];
    const document = cell?.document;
    this.selectedDocumentPath = document?.path ?? "";
    this.selectedDocumentViewState = undefined;
    if (document !== undefined) {
      const path = document.path;
      const generation = drafts.workspaceGeneration;
      void bridge.openDocument(path).then((opened) => {
        if (!opened || this.selectedDocumentPath !== path || drafts.workspaceGeneration !== generation) return;
        if (this.#options.snapshot()?.documents.find((candidate) => candidate.path === path)?.revision !== opened.fileRevision) return;
        this.selectedDocumentViewState = opened;
        if (this.mode === "raw" && drafts.drafts[path] === undefined) this.editorText = opened.content;
      }).catch((error) => { if (this.selectedDocumentPath === path) feedback.clientError = errorNotice(error); });
    }
    const sourceEntry = row?.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry;
    const savedSamples = this.#options.review.index.get(reviewIdentity(key, locale))?.samples;
    preview.useSamples(savedSamples !== undefined && Object.keys(savedSamples).length > 0
      ? savedSamples : cell?.entry?.context?.examples[0] ?? sourceEntry?.context?.examples[0]);
    const nextMode = preferredMode ?? "translation";
    this.mode = nextMode;
    if (nextMode === "raw") {
      this.editorText = document === undefined ? "" : drafts.content(document.path, document.content);
    } else {
      // A missing translation starts empty, never with the source text.
      const resourceValue = cell?.entry?.value ?? "";
      this.editorText = typeof resourceValue === "string" ? resourceValue : JSON.stringify(resourceValue, null, 2);
    }
    preview.reset();
    if (nextMode === "translation" && document !== undefined) {
      preview.schedule(document.path, drafts.content(document.path, document.content), key, locale);
    }
    if (nextMode === "translation" && document !== undefined && cell?.entry === undefined && sourceEntry !== undefined &&
      locale !== snapshot?.catalog?.defaultLocale && !this.#options.savedHasMessage(key, locale)) {
      void messageDrafts.open({ path: document.path, key, locale }).then((outcome) => {
        if (outcome === undefined || outcome.kind === "applied" || this.selectedKey !== key || this.selectedLocale !== locale) return;
        feedback.clientError = outcome.kind === "failed"
          ? errorNotice(outcome.error)
          : outcome.result.diagnostics[0]?.notice ?? outcome.result.diagnostics[0]?.message;
      });
    }
  }

  /** Shows what an undone or redone step changed: its message and locale, or the file for a step of the whole file. */
  showStep(step: DraftStep): void {
    this.#options.diagnostics.clear();
    const snapshot = this.#options.snapshot();
    const locale = snapshot?.documents.find((candidate) => candidate.path === step.path)?.locale;
    if (locale !== undefined && snapshot?.catalog?.locales.some((candidate) => candidate.tag === locale)) this.selectedLocale = locale;
    if (step.key !== undefined && this.rows.some((row) => row.key === step.key)) this.selectedKey = step.key;
    this.configureEditor(step.key === undefined ? "raw" : this.mode);
  }

  /** Selects a span of the file view (a diagnostic's); each call selects it again. */
  revealSource(start: number, end: number): void {
    this.sourceSelection = { start, end, token: ++this.#selectionToken };
  }

  /** Previews the selected message again, after its samples or formatting changed. */
  refreshPreview(): void {
    if (this.currentDocument !== undefined && this.currentContent !== undefined) {
      this.#options.preview.schedule(this.currentDocument.path, this.currentContent, this.selectedKey, this.selectedLocale);
    }
  }

  /** Previews the selected message with the example at `index`. */
  selectPreviewExample(index: number): void {
    const sample = this.currentExamples[index];
    if (sample === undefined) return;
    this.#options.preview.useSamples(sample);
    this.refreshPreview();
  }

  /** Previews the selected message with a requested formatting locale and time zone. */
  changePreviewFormatting(requestedLocale: string, timeZone: string): void {
    this.#options.preview.setFormatting(requestedLocale, timeZone);
    this.refreshPreview();
  }
}
