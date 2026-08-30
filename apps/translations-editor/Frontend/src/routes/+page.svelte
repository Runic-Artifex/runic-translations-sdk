<script lang="ts">
  import { onMount } from "svelte";
  import { m } from "virtual:runic-translations/editor";

  // ESM ABI v2 exposes messages as a namespace with catalog-key bindings.
  const m$App$Advanced = m["App.Advanced"];
  const m$App$All = m["App.All"];
  const m$App$AddMessage = m["App.AddMessage"];
  const m$App$ApproveTranslations = m["App.ApproveTranslations"];
  const m$App$DefaultLocale = m["App.DefaultLocale"];
  const m$App$Diagnostics = m["App.Diagnostics"];
  const m$App$Eyebrow = m["App.Eyebrow"];
  const m$App$Invalid = m["App.Invalid"];
  const m$App$Missing = m["App.Missing"];
  const m$App$MissingTranslation = m["App.MissingTranslation"];
  const m$App$MarkForReview = m["App.MarkForReview"];
  const m$App$MessageBulkActions = m["App.MessageBulkActions"];
  const m$App$MessageFilters = m["App.MessageFilters"];
  const m$App$Messages = m["App.Messages"];
  const m$App$NoResults = m["App.NoResults"];
  const m$App$NoMatchingMessages = m["App.NoMatchingMessages"];
  const m$App$NoSelection = m["App.NoSelection"];
  const m$App$Raw = m["App.Raw"];
  const m$App$Reload = m["App.Reload"];
  const m$App$Review = m["App.Review"];
  const m$App$Save = m["App.Save"];
  const m$App$Saved = m["App.Saved"];
  const m$App$Saving = m["App.Saving"];
  const m$App$Search = m["App.Search"];
  const m$App$Simple = m["App.Simple"];
  const m$App$Structured = m["App.Structured"];
  const m$App$Stale = m["App.Stale"];
  const m$App$Title = m["App.Title"];
  const m$App$Unsaved = m["App.Unsaved"];
  const m$App$Translated = m["App.Translated"];
  const m$App$Valid = m["App.Valid"];
  const m$App$Workspace = m["App.Workspace"];
  const m$App$VisibleMessages = m["App.VisibleMessages"];
  import type {
    EditorAbout,
    EditorDiagnostic,
    EditorDocument,
    EditorExternalFileChange,
    EditorMutationPreview,
    EditorMutationRequest,
    EditorProjectCreationRequest,
    EditorProjectPlan,
    EditorReviewFileResult,
    EditorReviewEntry,
    EditorReviewImportPreview,
    EditorReviewState,
    EditorTerminologyEntry,
    EditorXliffExportResult,
    EditorXliffImportPreview,
    ValidationResult,
    WorkspaceSnapshot,
  } from "$lib/contracts";
  import {
    applyAppearance,
    readAppearance,
    saveAppearance,
    type ThemeMode,
    type ThemePalette,
  } from "$lib/appearance";
  import AppDialog from "$lib/AppDialog.svelte";
  import ArtifactPreviewPanel from "$lib/ArtifactPreviewPanel.svelte";
  import CommandPalette from "$lib/CommandPalette.svelte";
  import { buildEditorCommandPalette } from "$lib/command-palette";
  import { createEditorBridge } from "$lib/editor-bridge";
  import { createUiText, setUiText } from "$lib/ui-text";
  import { editorShortcut } from "$lib/editor-keyboard";
  import EditorModeSwitcher, { type EditorMode } from "$lib/EditorModeSwitcher.svelte";
  import InterchangeDialog from "$lib/InterchangeDialog.svelte";
  import EditorSettingsFooter from "$lib/EditorSettingsFooter.svelte";
  import EditorSidebarHeader from "$lib/EditorSidebarHeader.svelte";
  import EditorToolbar from "$lib/EditorToolbar.svelte";
  import LocaleSwitcher from "$lib/LocaleSwitcher.svelte";
  import MessageHeading from "$lib/MessageHeading.svelte";
  import MessageList, { type MessageListItem } from "$lib/MessageList.svelte";
  import MessageToolbar, { type MessageFilter } from "$lib/MessageToolbar.svelte";
  import ReviewWorkflow from "$lib/ReviewWorkflow.svelte";
  import SidebarSectionPanels from "$lib/SidebarSectionPanels.svelte";
  import TranslationEditor from "$lib/TranslationEditor.svelte";
  import ValidationPanel from "$lib/ValidationPanel.svelte";
  import WorkspacePanel from "$lib/WorkspacePanel.svelte";
  import MessageSquareTextIcon from "@lucide/svelte/icons/message-square-text";
  import PlusIcon from "@lucide/svelte/icons/plus";
  import Trash2Icon from "@lucide/svelte/icons/trash-2";
  import * as Alert from "$lib/components/ui/alert/index.js";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import { Button } from "$lib/components/ui/button/index.js";
  import { Checkbox } from "$lib/components/ui/checkbox/index.js";
  import * as Empty from "$lib/components/ui/empty/index.js";
  import * as Field from "$lib/components/ui/field/index.js";
  import { Input } from "$lib/components/ui/input/index.js";
  import * as Select from "$lib/components/ui/select/index.js";
  import * as Sidebar from "$lib/components/ui/sidebar/index.js";
  import { Spinner } from "$lib/components/ui/spinner/index.js";
  import { Textarea } from "$lib/components/ui/textarea/index.js";
  import type { MessageArtifact } from "$lib/message-composer";
  import { executeMessagePreview } from "$lib/message-preview.js";
  import {
    clearLocalEditorState,
    configureLocalEditorState,
    getLocalEditorState,
    inspectLocalEditorState,
    loadLocalEditorState,
    removeLocalEditorState,
    setLocalEditorState,
    type LocalStateSummary,
  } from "$lib/local-state";
  import {
    buildRows,
    coverage,
    formatJson,
    preview,
    updateResourceValue,
    type ResourceValue,
    type TranslationRow,
  } from "$lib/resource-model";
  import { createMessageSearchIndex } from "$lib/message-search";
  import {
    effectiveReviewState,
    bidiIssues,
    isStale,
    qualityIssues,
    qualityReportCsv,
    reviewIdentity,
    reviewMap,
    sourceFingerprint,
    translationSuggestions,
  } from "$lib/review-model";
  import {
    readUiSimulation,
    saveUiSimulation,
    simulatePreviewResult,
    type UiDirection,
  } from "$lib/simulation";

  type ProjectLocaleDraft = { id: number; tag: string; fallback: string };
  type StoredDraft = { content: string; baseRevision: string };
  type RecentProject = { root: string; catalogId: string; openedAt: string };
  type MutationKind = EditorMutationRequest["kind"];
  type MessagePreviewResult = ReturnType<typeof executeMessagePreview>;
  type PreviewNode = Extract<MessagePreviewResult, { kind: "content" }>["nodes"][number];

  const bridge = createEditorBridge();
  configureLocalEditorState(bridge);
  let snapshot = $state.raw<WorkspaceSnapshot>();
  let drafts = $state<Record<string, string>>({});
  let draftGenerations = $state<Record<string, number>>({});
  let selectedKey = $state("");
  let selectedLocale = $state("");
  let selectedDocumentPath = $state("");
  let filter = $state<MessageFilter>("all");
  let query = $state("");
  let mode = $state<EditorMode>("translation");
  let editorText = $state("");
  let uiLocale = $state("en");
  const ui = createUiText(() => uiLocale);
  setUiText(ui);
  let themeMode = $state<ThemeMode>("dark");
  let themePalette = $state<ThemePalette>("runic");
  let loading = $state(true);
  let saving = $state(false);
  let validationBusy = $state(false);
  let validation = $state.raw<ValidationResult>();
  let clientError = $state<string>();
  let operationMessage = $state<string>();
  let searchInput = $state<HTMLInputElement | null>(null);
  let validationTimer: number | undefined;
  let validationEpoch = 0;
  let projectDialogOpen = $state(false);
  let projectStep = $state(1);
  let projectDirectory = $state("");
  let projectCatalog = $state("product");
  let projectDefaultLocale = $state("en");
  let projectLocales = $state<ProjectLocaleDraft[]>([]);
  let projectNamespace = $state("Customer.Product");
  let projectClassName = $state("ProductText");
  let projectLayer = $state("base");
  let projectGenerateEsm = $state(true);
  let projectIncludeStarter = $state(true);
  let projectPlan = $state.raw<EditorProjectPlan>();
  let projectError = $state<string>();
  let projectBusy = $state(false);
  let nextProjectLocaleId = 1;
  let openDirectory = $state("");
  let openingWorkspace = $state(false);
  let pickingWorkspace = $state(false);
  let openDialogOpen = $state(false);
  let commandPaletteOpen = $state(false);
  let repairDocument = $state.raw<EditorDocument>();
  let repairText = $state("");
  let repairBusy = $state(false);
  let repairMessage = $state<string>();
  let externalChanges = $state<string[]>([]);
  let externalFileChanges = $state<EditorExternalFileChange[]>([]);
  let comparedExternalChange = $state.raw<EditorExternalFileChange>();
  let mergedExternalText = $state("");
  let recoveredDrafts = $state<Record<string, StoredDraft>>({});
  let recentProjects = $state<RecentProject[]>([]);
  let mutationDialogOpen = $state(false);
  let mutationKind = $state<MutationKind>("add-locale");
  let mutationLocale = $state("");
  let mutationFallback = $state("");
  let mutationReplacementFallback = $state("");
  let mutationLayer = $state("base");
  let mutationCopyFrom = $state("");
  let mutationSourceKey = $state("");
  let mutationTargetKey = $state("");
  let mutationInitialValue = $state("");
  let mutationPreview = $state.raw<EditorMutationPreview>();
  let mutationError = $state<string>();
  let mutationBusy = $state(false);
  let mutationIrreversibleConfirmed = $state(false);
  let recoveryBusy = $state(false);
  let recoveryReloadRequired = $state(false);
  let recoveryReloadMessage = $state<string>();
  let historyBusy = $state(false);
  let previewBusy = $state(false);
  let previewError = $state<string>();
  let previewAst = $state.raw<MessageArtifact>();
  let previewSamples = $state<Record<string, string>>({});
  let previewResult = $state.raw<MessagePreviewResult>();
  let previewTimer: number | undefined;
  let previewEpoch = 0;
  let reviewEntries = $state<EditorReviewEntry[]>([]);
  let terminology = $state<EditorTerminologyEntry[]>([]);
  let reviewRevision = $state<string>();
  let reviewDirty = $state(false);
  let reviewSaving = $state(false);
  let reviewMessage = $state<string>();
  let terminologyDialogOpen = $state(false);
  let termSource = $state("");
  let termPreferred = $state("");
  let termLocale = $state("");
  let termNote = $state("");
  let reportDialogOpen = $state(false);
  let aboutDialogOpen = $state(false);
  let aboutInfo = $state.raw<EditorAbout>();
  let aboutBusy = $state(false);
  let diagnosticBusy = $state(false);
  let diagnosticMessage = $state<string>();
  let diagnosticBundlePath = $state<string>();
  let localStateSummary = $state.raw<LocalStateSummary>();
  let localStateMessage = $state<string>();
  let languagesOpen = $state(true);
  let messagesOpen = $state(true);
  let pseudoLocalization = $state(false);
  let uiDirection = $state<UiDirection>("ltr");
  let artifactPreviewOpen = $state(false);
  let interchangeOpen = $state(false);
  let interchangeBusy = $state(false);
  let xliffDirectory = $state("");
  let xliffImportPath = $state("");
  let reviewExportPath = $state("");
  let reviewImportPath = $state("");
  let xliffExport = $state.raw<EditorXliffExportResult>();
  let xliffPreview = $state.raw<EditorXliffImportPreview>();
  let reviewExport = $state.raw<EditorReviewFileResult>();
  let reviewPreview = $state.raw<EditorReviewImportPreview>();

  let labels = $derived(labelsFor(uiLocale));
  $effect(() => {
    document.documentElement.lang = uiLocale;
    document.documentElement.dir = uiDirection;
  });
  let hasUnsavedRepair = $derived(repairDocument !== undefined && repairText !== repairDocument.content);
  let hasUnsavedWork = $derived(reviewDirty || hasUnsavedRepair || Object.keys(drafts).length > 0);
  let historyBlocked = $derived(
    historyBusy || loading || saving || reviewSaving || validationBusy || mutationBusy || recoveryBusy ||
    previewBusy || projectBusy || openingWorkspace || pickingWorkspace || repairBusy || diagnosticBusy || aboutBusy || interchangeBusy ||
    hasUnsavedWork || mutationDialogOpen || projectDialogOpen || openDialogOpen ||
    comparedExternalChange !== undefined || repairDocument !== undefined || terminologyDialogOpen || reportDialogOpen || aboutDialogOpen,
  );
  let editorMutationBlocked = $derived(historyBusy || saving || reviewSaving || loading || recoveryBusy || openingWorkspace);
  let reviewMutationBlocked = $derived(editorMutationBlocked || reviewSaving);
  let rows = $derived(buildRows(snapshot, drafts));
  let messageSearch = $derived(createMessageSearchIndex(rows));
  let localeSummaries = $derived.by(() => (snapshot?.catalog?.locales ?? []).map((locale) => {
    const state = coverage(rows, locale.tag);
    return {
      tag: locale.tag,
      name: localeName(locale.tag),
      fallback: locale.fallback,
      translated: state.translated,
      total: state.total,
      percent: state.total === 0 ? 100 : Math.round((state.translated / state.total) * 100),
      isSource: locale.tag === snapshot?.catalog?.defaultLocale,
    };
  }));
  let reviewIndex = $derived(reviewMap(reviewEntries));
  let localeQuality = $derived(qualityIssues(
    rows,
    snapshot?.catalog?.defaultLocale ?? "",
    selectedLocale,
    reviewEntries,
    terminology,
  ));
  let localeQualityFindings = $derived.by(() => {
    const bidi = bidiIssues(
      rows.map((row) => ({
        key: row.key,
        locale: selectedLocale,
        text: preview(row.cells[selectedLocale]?.entry),
      })),
      uiDirection,
    );
    if (bidi.length === 0) return localeQuality;
    return [...localeQuality, ...bidi].sort((left, right) =>
      left.key.localeCompare(right.key) || left.kind.localeCompare(right.kind));
  });
  let qualityKeySet = $derived(new Set(localeQualityFindings.map((issue) => issue.key)));
  let filterOptions = $derived([
    { value: "all" as const, label: labels.all, count: rows.length },
    { value: "missing" as const, label: labels.missing, count: rows.filter((row) => row.cells[selectedLocale]?.entry === undefined).length },
    { value: "structured" as const, label: labels.structured, count: rows.filter((row) => row.structured).length },
    { value: "needs-review" as const, label: labels.review, count: rows.filter((row) => effectiveReviewState(reviewIndex.get(reviewIdentity(row.key, selectedLocale)), row.cells[selectedLocale]?.entry !== undefined) === "needs-review").length },
    { value: "stale" as const, label: labels.stale, count: rows.filter((row) => isStale(reviewIndex.get(reviewIdentity(row.key, selectedLocale)), row.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry?.value)).length },
    { value: "quality" as const, label: ui.text("Ui.Page.Quality.Title"), count: qualityKeySet.size },
  ]);
  let visibleRows = $derived.by(() => {
    const searchMatches = messageSearch.matchingRows(query);
    return rows.filter((row) => {
      const cell = row.cells[selectedLocale];
      if (filter === "missing" && cell?.entry !== undefined) return false;
      if (filter === "structured" && !row.structured) return false;
      const review = reviewIndex.get(reviewIdentity(row.key, selectedLocale));
      if (filter === "needs-review" && effectiveReviewState(review, cell?.entry !== undefined) !== "needs-review") return false;
      if (filter === "stale" && !isStale(review, row.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry?.value)) return false;
      if (filter === "quality" && !qualityKeySet.has(row.key)) return false;
      return searchMatches.has(row);
    });
  });
  let simulatedPreviewResult = $derived(
    previewResult === undefined ? undefined : simulatePreviewResult(previewResult, { pseudoLocalization }),
  );
  let messageListItems = $derived.by((): MessageListItem[] => visibleRows.map((row) => {
    const cell = row.cells[selectedLocale];
    const rowReview = reviewIndex.get(reviewIdentity(row.key, selectedLocale));
    return {
      key: row.key,
      preview: preview(cell?.entry),
      missing: cell?.entry === undefined,
      structured: row.structured,
      stale: isStale(rowReview, row.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry?.value),
      needsReview: effectiveReviewState(rowReview, cell?.entry !== undefined) === "needs-review",
    };
  }));
  let selectedRow = $derived.by(() => rows.find((row) => row.key === selectedKey));
  let currentCell = $derived(selectedRow?.cells[selectedLocale]);
  let currentSourceValue = $derived(
    selectedRow?.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry?.value,
  );
  let currentReview = $derived(reviewIndex.get(reviewIdentity(selectedKey, selectedLocale)));
  let currentReviewState = $derived(effectiveReviewState(currentReview, currentCell?.entry !== undefined));
  let currentIsStale = $derived(isStale(currentReview, currentSourceValue));
  let currentQuality = $derived(localeQualityFindings.filter((issue) => issue.key === selectedKey));
  let memorySuggestions = $derived(translationSuggestions(
    rows,
    snapshot?.catalog?.defaultLocale ?? "",
    selectedLocale,
    selectedKey,
  ));
  let currentDocument = $derived.by(() =>
    snapshot?.documents.find((document) => document.path === selectedDocumentPath),
  );
  let currentContent = $derived(
    currentDocument === undefined
      ? undefined
      : (drafts[currentDocument.path] ?? currentDocument.content),
  );
  let isDirty = $derived(
    currentDocument !== undefined && drafts[currentDocument.path] !== undefined,
  );
  let diagnostics = $derived(validation?.diagnostics ?? snapshot?.diagnostics ?? []);
  let errorCount = $derived(diagnostics.filter((item) => item.severity === "error").length);
  let warningCount = $derived(diagnostics.filter((item) => item.severity === "warning").length);
  let malformedDocuments = $derived(
    snapshot?.documents.filter((document) => document.isMalformed) ?? [],
  );
  let paletteCommands = $derived.by(() =>
    buildEditorCommandPalette(
      {
        reloadWorkspace: () => void loadWorkspace(true),
        openWorkspaceDialog: showOpenWorkspaceDialog,
        createProject: openProjectWizard,
        openInterchange: () => interchangeOpen = true,
        saveDocument: () => void save(),
        undo: () => void undoHistory(),
        redo: () => void redoHistory(),
        focusMessageSearch: () => searchInput?.focus(),
        saveReview: () => void saveReview(),
        discardReview,
        setMessageReviewState: setCurrentReviewState,
        markVisibleMessages: markVisible,
        openTerminology: () => terminologyDialogOpen = true,
        openQualityReport: () => reportDialogOpen = true,
        showAbout: () => void showAbout(),
        createDiagnosticBundle: () => void createDiagnosticBundle(),
        setEditorMode: chooseMode,
        selectLocale,
        setUiLocale: (locale) => uiLocale = locale,
        setThemeMode: changeThemeMode,
        toggleLanguagesSection: () => languagesOpen = !languagesOpen,
        toggleMessagesSection: () => messagesOpen = !messagesOpen,
        togglePseudoLocalization,
        toggleUiDirection,
        toggleArtifactPreview,
      },
      {
        locales: localeSummaries.map((locale) => ({ tag: locale.tag, name: locale.name })),
        selectedLocale,
        editorMode: mode,
        uiLocale,
        themeMode,
        workspaceReady: snapshot !== undefined,
        searchAvailable: searchInput !== null,
        documentDirty: isDirty && !editorMutationBlocked,
        canUndo: snapshot?.history?.canUndo === true && !historyBlocked,
        canRedo: snapshot?.history?.canRedo === true && !historyBlocked,
        reviewEditable: selectedRow !== undefined && !reviewMutationBlocked &&
          snapshot?.review?.error === undefined,
        reviewDirty,
        reviewError: snapshot?.review?.error !== undefined,
        pseudoLocalization,
        uiDirection,
        artifactPreviewOpen,
      },
    ));

  onMount(() => {
    let disposed = false;
    const appearance = readAppearance(getLocalEditorState);
    themeMode = appearance.mode;
    themePalette = appearance.palette;
    applyAppearance(themeMode, themePalette);
    const simulation = readUiSimulation(getLocalEditorState);
    pseudoLocalization = simulation.pseudoLocalization;
    uiDirection = simulation.direction;
    const colorScheme = window.matchMedia("(prefers-color-scheme: dark)");
    const updateSystemTheme = (): void => {
      if (themeMode === "system") applyAppearance(themeMode, themePalette);
    };
    colorScheme.addEventListener("change", updateSystemTheme);
    void (async () => {
      try {
        const nativeStateRecovered = await loadLocalEditorState();
        if (disposed) return;
        const loadedAppearance = readAppearance(getLocalEditorState);
        themeMode = loadedAppearance.mode;
        themePalette = loadedAppearance.palette;
        applyAppearance(themeMode, themePalette);
        const loadedSimulation = readUiSimulation(getLocalEditorState);
        pseudoLocalization = loadedSimulation.pseudoLocalization;
        uiDirection = loadedSimulation.direction;
        recentProjects = readRecentProjects();
        if (nativeStateRecovered) {
          operationMessage = "Recovered from an unreadable local editor-state record; saved preferences and recovery drafts were reset.";
        }
      } catch (error) {
        clientError = `The per-user editor state could not be loaded. ${errorMessage(error)}`;
      }
      if (!disposed) await loadWorkspace(false);
    })();
    const interval = window.setInterval(() => void checkExternalChanges(), 2_000);
    return () => {
      disposed = true;
      colorScheme.removeEventListener("change", updateSystemTheme);
      window.clearInterval(interval);
    };
  });

  function changeThemeMode(mode: ThemeMode): void {
    themeMode = mode;
    saveAppearance(themeMode, themePalette, setLocalEditorState);
  }

  function changeThemePalette(palette: ThemePalette): void {
    themePalette = palette;
    saveAppearance(themeMode, themePalette, setLocalEditorState);
  }

  function togglePseudoLocalization(): void {
    pseudoLocalization = !pseudoLocalization;
    saveUiSimulation(pseudoLocalization, uiDirection, setLocalEditorState);
  }

  function toggleUiDirection(): void {
    uiDirection = uiDirection === "rtl" ? "ltr" : "rtl";
    saveUiSimulation(pseudoLocalization, uiDirection, setLocalEditorState);
  }

  function toggleArtifactPreview(): void {
    artifactPreviewOpen = !artifactPreviewOpen;
  }

  async function checkExternalChanges(): Promise<void> {
    if (loading || openingWorkspace || saving || snapshot === undefined) return;
    try {
      const result = await bridge.checkExternalChanges();
      if (result.paths.length > 0) {
        externalChanges = [...new Set([...externalChanges, ...result.paths])].sort();
        externalFileChanges = [
          ...externalFileChanges.filter((existing) => !result.changes.some((change) => change.path === existing.path)),
          ...result.changes,
        ].sort((left, right) => left.path.localeCompare(right.path));
      }
    } catch {
      // The next poll retries; editing and saving remain available.
    }
  }

  async function loadWorkspace(confirmDiscard: boolean): Promise<void> {
    if (hasUnsavedWork) {
      if (!confirmDiscard || !confirmDiscardUnsavedWork("reload the workspace")) return;
      discardUnsavedWork();
    }
    loading = true;
    operationMessage = undefined;
    clientError = undefined;
    try {
      const next = await bridge.load();
      installSnapshot(next, true);
      recoveryReloadRequired = false;
      recoveryReloadMessage = undefined;
      externalChanges = [];
      externalFileChanges = [];
    } catch (error) {
      clientError = errorMessage(error);
    } finally {
      loading = false;
    }
  }

  function installSnapshot(next: WorkspaceSnapshot, resetSelection: boolean): void {
    snapshot = next;
    if (resetSelection) {
      drafts = {};
      draftGenerations = {};
      recoveredDrafts = readStoredDrafts(next);
    }
    if (!reviewDirty) installReview(next);
    validation = undefined;
    const nextRows = buildRows(next, {});
    if (resetSelection || !nextRows.some((row) => row.key === selectedKey)) {
      selectedKey = nextRows[0]?.key ?? "";
    }
    if (resetSelection || !next.catalog?.locales.some((locale) => locale.tag === selectedLocale)) {
      selectedLocale = next.catalog?.defaultLocale ?? next.catalog?.locales[0]?.tag ?? "";
    }
    configureEditor();
    rememberProject(next);
  }

  function installReview(next: WorkspaceSnapshot): void {
    reviewEntries = structuredClone(next.review?.entries ?? []);
    terminology = structuredClone(next.review?.terminology ?? []);
    reviewRevision = next.review?.revision;
    reviewDirty = false;
    reviewMessage = next.review?.error;
  }

  function confirmDiscardUnsavedWork(action: string): boolean {
    if (!hasUnsavedWork) return true;
    return confirm(`Discard unsaved document drafts, repair text, and workflow/terminology changes to ${action}?`);
  }

  function discardUnsavedWork(): void {
    clearStoredDrafts(snapshot);
    drafts = {};
    draftGenerations = {};
    if (snapshot !== undefined) installReview(snapshot);
    if (hasUnsavedRepair) {
      repairDocument = undefined;
      repairText = "";
      repairMessage = undefined;
    }
  }

  function confirmDiscardNonDocumentWork(action: string): boolean {
    if (!reviewDirty && !hasUnsavedRepair) return true;
    if (!confirm(`Discard unsaved repair text and workflow/terminology changes to ${action}?`)) return false;
    if (snapshot !== undefined) installReview(snapshot);
    if (hasUnsavedRepair) {
      repairDocument = undefined;
      repairText = "";
      repairMessage = undefined;
    }
    return true;
  }

  function closeRepair(): void {
    if (hasUnsavedRepair && !confirm("Discard unsaved repair text?")) return;
    repairDocument = undefined;
    repairText = "";
    repairMessage = undefined;
  }

  function selectRow(row: TranslationRow): void {
    const nextKey = row.key;
    validation = undefined;
    clientError = undefined;
    operationMessage = undefined;
    configureEditor(undefined, nextKey, selectedLocale);
    selectedKey = nextKey;
  }

  function selectLocale(locale: string): void {
    validation = undefined;
    clientError = undefined;
    operationMessage = undefined;
    configureEditor(undefined, selectedKey, locale);
    selectedLocale = locale;
  }

  function chooseMode(nextMode: EditorMode): void {
    if (editorMutationBlocked) return;
    mode = nextMode;
    clientError = undefined;
    configureEditor(nextMode);
  }

  function configureEditor(preferredMode?: EditorMode, key = selectedKey, locale = selectedLocale): void {
    const row = buildRows(snapshot, drafts).find((candidate) => candidate.key === key);
    const cell = row?.cells[locale];
    const document = cell?.document;
    selectedDocumentPath = document?.path ?? "";
    const sourceEntry = row?.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry;
    previewSamples = {
      ...(reviewIndex.get(reviewIdentity(key, locale))?.samples ?? {}),
    };
    const nextMode = preferredMode ?? "translation";
    mode = nextMode;
    if (nextMode === "raw") {
      editorText = document === undefined ? "" : (drafts[document.path] ?? document.content);
    } else {
      const resourceValue = cell?.entry?.value ?? sourceEntry?.value ?? "";
      editorText = typeof resourceValue === "string" ? resourceValue : JSON.stringify(resourceValue, null, 2);
    }
    previewAst = undefined;
    previewResult = undefined;
    previewError = undefined;
    if (nextMode === "translation" && document !== undefined) {
      schedulePreview(document.path, drafts[document.path] ?? document.content);
    }
  }

  function edit(value: string): void {
    if (editorMutationBlocked) return;
    if (mode !== "raw") {
      editResourceValue(value);
      return;
    }
    editorText = value;
    clientError = undefined;
    operationMessage = undefined;
    const document = currentDocument;
    if (document === undefined) {
      clientError = "This locale has no resource document to edit.";
      return;
    }
    try {
      setDraft(document.path, value);
      persistDrafts();
      scheduleValidation(document.path, value);
    } catch (error) {
      clientError = errorMessage(error);
      validation = { success: false, diagnostics: [] };
    }
  }

  function editResourceValue(resourceValue: ResourceValue): void {
    if (editorMutationBlocked) return;
    editorText = typeof resourceValue === "string" ? resourceValue : JSON.stringify(resourceValue, null, 2);
    clientError = undefined;
    operationMessage = undefined;
    const document = currentDocument;
    if (document === undefined) {
      clientError = "This locale has no resource document to edit.";
      return;
    }
    try {
      const sourceEntry = selectedRow?.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry;
      const content = updateResourceValue(
        drafts[document.path] ?? document.content,
        selectedKey,
        resourceValue,
        sourceEntry,
      );
      setDraft(document.path, content);
      persistDrafts();
      scheduleValidation(document.path, content);
      schedulePreview(document.path, content);
    } catch (error) {
      clientError = errorMessage(error);
      validation = { success: false, diagnostics: [] };
    }
  }

  function schedulePreview(path: string, content: string): void {
    if (previewTimer !== undefined) window.clearTimeout(previewTimer);
    const epoch = ++previewEpoch;
    previewBusy = true;
    previewTimer = window.setTimeout(() => {
      void bridge.previewMessage(path, content, selectedLocale, selectedKey).then((result) => {
        if (epoch !== previewEpoch) return;
        if (!result.success || result.astJson === undefined || result.locale === undefined) {
          previewAst = undefined;
          previewResult = undefined;
          previewError = result.diagnostics[0]?.message ?? "The compiler could not build a preview.";
          return;
        }
        const ast = JSON.parse(result.astJson) as MessageArtifact;
        previewAst = ast;
        const samples: Record<string, string> = {};
        for (const [name, descriptor] of Object.entries(ast.inputs)) {
          samples[name] = previewSamples[name] ?? defaultSample(descriptor.type);
        }
        previewSamples = samples;
        previewError = undefined;
        renderPreview(result.locale);
      }).catch((error) => {
        if (epoch === previewEpoch) previewError = errorMessage(error);
      }).finally(() => {
        if (epoch === previewEpoch) previewBusy = false;
      });
    }, 450);
  }

  function updatePreviewSample(name: string, value: string): void {
    previewSamples = { ...previewSamples, [name]: value };
    renderPreview(selectedLocale);
  }

  function updateReview(
    key: string,
    locale: string,
    patch: Partial<EditorReviewEntry>,
  ): void {
    if (reviewMutationBlocked) return;
    const identity = reviewIdentity(key, locale);
    const index = reviewEntries.findIndex((entry) => reviewIdentity(entry.key, entry.locale) === identity);
    const row = rows.find((candidate) => candidate.key === key);
    const existing = index < 0 ? undefined : reviewEntries[index];
    const next: EditorReviewEntry = {
      key,
      locale,
      state: patch.state ?? existing?.state ?? effectiveReviewState(undefined, row?.cells[locale]?.entry !== undefined),
      note: patch.note ?? existing?.note,
      sourceFingerprint: patch.sourceFingerprint ?? existing?.sourceFingerprint,
      samples: patch.samples ?? existing?.samples ?? {},
    };
    reviewEntries = index < 0
      ? [...reviewEntries, next]
      : reviewEntries.map((entry, candidate) => candidate === index ? next : entry);
    reviewDirty = true;
    reviewMessage = undefined;
  }

  function setCurrentReviewState(state: EditorReviewState): void {
    updateReview(selectedKey, selectedLocale, {
      state,
      sourceFingerprint: sourceFingerprint(currentSourceValue),
      samples: { ...previewSamples },
    });
  }

  function setCurrentReviewNote(note: string): void {
    updateReview(selectedKey, selectedLocale, { note });
  }

  function markVisible(state: EditorReviewState): void {
    if (reviewMutationBlocked) return;
    let next = [...reviewEntries];
    const byIdentity = reviewMap(next);
    for (const row of visibleRows) {
      const identity = reviewIdentity(row.key, selectedLocale);
      const existing = byIdentity.get(identity);
      const entry: EditorReviewEntry = {
        key: row.key,
        locale: selectedLocale,
        state,
        note: existing?.note,
        sourceFingerprint: sourceFingerprint(row.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry?.value),
        samples: { ...(existing?.samples ?? {}) },
      };
      const index = next.findIndex((candidate) => reviewIdentity(candidate.key, candidate.locale) === identity);
      if (index < 0) next.push(entry);
      else next[index] = entry;
      byIdentity.set(identity, entry);
    }
    reviewEntries = next;
    reviewDirty = true;
    reviewMessage = visibleRows.length + " visible messages marked " + state + ". Save workflow changes to commit.";
  }

  async function saveReview(): Promise<void> {
    if (!reviewDirty || reviewSaving || historyBusy || snapshot?.review?.error !== undefined) return;
    reviewSaving = true;
    reviewMessage = undefined;
    try {
      const result = await bridge.saveReview({
        expectedRevision: reviewRevision,
        entries: reviewEntries.map((entry) => ({ ...entry, samples: { ...entry.samples } })),
        terminology: terminology.map((term) => ({ ...term })),
      });
      if (!result.ok || result.review === undefined) {
        reviewMessage = result.message ?? "Review data could not be saved.";
        return;
      }
      reviewEntries = structuredClone(result.review.entries);
      terminology = structuredClone(result.review.terminology);
      reviewRevision = result.review.revision;
      reviewDirty = false;
      reviewMessage = "Workflow sidecar saved";
      if (snapshot !== undefined) snapshot.review = result.review;
      if (snapshot !== undefined) snapshot.history = result.history;
    } catch (error) {
      reviewMessage = errorMessage(error);
    } finally {
      reviewSaving = false;
    }
  }

  function discardReview(): void {
    if (snapshot !== undefined) installReview(snapshot);
  }

  function addTerm(): void {
    if (reviewMutationBlocked) return;
    if (termSource.trim() === "" || termPreferred.trim() === "") return;
    terminology = [...terminology, {
      source: termSource.trim(),
      preferred: termPreferred.trim(),
      locale: termLocale.trim() || undefined,
      note: termNote.trim() || undefined,
    }];
    reviewDirty = true;
    termSource = "";
    termPreferred = "";
    termLocale = "";
    termNote = "";
  }

  function removeTerm(index: number): void {
    if (reviewMutationBlocked) return;
    terminology = terminology.filter((_, candidate) => candidate !== index);
    reviewDirty = true;
  }

  async function showAbout(): Promise<void> {
    if (historyBusy) return;
    aboutDialogOpen = true;
    diagnosticMessage = undefined;
    localStateSummary = inspectLocalEditorState();
    localStateMessage = undefined;
    if (aboutInfo !== undefined || aboutBusy) return;
    aboutBusy = true;
    try {
      aboutInfo = await bridge.about();
    } catch (error) {
      diagnosticMessage = errorMessage(error);
    } finally {
      aboutBusy = false;
    }
  }

  async function clearLocalState(): Promise<void> {
    const summary = localStateSummary ?? inspectLocalEditorState();
    if (summary.entries === 0) return;
    if (!confirm("Remove the editor's saved preferences, recent-project list, and crash-recovery drafts from this user profile? Workspace files and currently open in-memory work will not be changed.")) return;
    const removed = await clearLocalEditorState();
    recentProjects = [];
    recoveredDrafts = {};
    localStateSummary = inspectLocalEditorState();
    localStateMessage = removed === 1
      ? "Removed 1 local editor-state entry."
      : `Removed ${removed} local editor-state entries.`;
  }

  async function createDiagnosticBundle(): Promise<void> {
    if (diagnosticBusy || historyBusy) return;
    diagnosticBusy = true;
    diagnosticMessage = undefined;
    try {
      const result = await bridge.createDiagnosticBundle();
      diagnosticBundlePath = result.ok ? result.path : undefined;
      diagnosticMessage = result.ok
        ? "Sanitized diagnostics were saved in this user's application-data directory."
        : result.message ?? "The diagnostic bundle could not be created.";
    } catch (error) {
      diagnosticMessage = errorMessage(error);
    } finally {
      diagnosticBusy = false;
    }
  }

  async function revealDiagnosticBundle(): Promise<void> {
    if (diagnosticBundlePath === undefined || diagnosticBusy) return;
    diagnosticBusy = true;
    try {
      const result = await bridge.revealDiagnosticBundle(diagnosticBundlePath);
      diagnosticMessage = result.ok ? "Opened the diagnostic bundle location." : result.message ?? "The diagnostic bundle location could not be opened.";
    } catch (error) {
      diagnosticMessage = errorMessage(error);
    } finally {
      diagnosticBusy = false;
    }
  }

  async function copyDiagnosticBundlePath(): Promise<void> {
    if (diagnosticBundlePath === undefined) return;
    try {
      await navigator.clipboard.writeText(diagnosticBundlePath);
      diagnosticMessage = "Copied the diagnostic bundle path.";
    } catch {
      diagnosticMessage = "The diagnostic bundle path could not be copied. Select it below and copy it manually.";
    }
  }

  async function deleteDiagnosticBundle(): Promise<void> {
    if (diagnosticBundlePath === undefined || diagnosticBusy) return;
    if (!confirm("Delete this sanitized diagnostic bundle? This does not affect workspace files or editor state.")) return;
    diagnosticBusy = true;
    try {
      const result = await bridge.deleteDiagnosticBundle(diagnosticBundlePath);
      if (result.ok) diagnosticBundlePath = undefined;
      diagnosticMessage = result.ok ? "Deleted the diagnostic bundle." : result.message ?? "The diagnostic bundle could not be deleted.";
    } catch (error) {
      diagnosticMessage = errorMessage(error);
    } finally {
      diagnosticBusy = false;
    }
  }

  async function exportXliff(): Promise<void> {
    if (interchangeBusy) return;
    interchangeBusy = true;
    try {
      xliffExport = await bridge.exportXliff(xliffDirectory.trim() || undefined);
    } catch (error) {
      xliffExport = { ok: false, message: errorMessage(error), documents: [], losses: [], lossless: false };
    } finally {
      interchangeBusy = false;
    }
  }

  async function previewXliffImport(): Promise<void> {
    if (interchangeBusy || xliffImportPath.trim() === "") return;
    interchangeBusy = true;
    try {
      xliffPreview = await bridge.previewXliffImport(xliffImportPath.trim());
    } catch (error) {
      xliffPreview = {
        ok: false, message: errorMessage(error), requiresIrreversibleConfirmation: false,
        changes: [], addedCount: 0, changedCount: 0, removedCount: 0, unchangedCount: 0,
        reviewUpdateCount: 0, changesOverflowed: false, refusals: [],
      };
    } finally {
      interchangeBusy = false;
    }
  }

  async function applyXliffImport(): Promise<void> {
    if (interchangeBusy || xliffPreview?.confirmationToken === undefined || !confirmDiscardUnsavedWork("apply this XLIFF import")) return;
    interchangeBusy = true;
    try {
      const result = await bridge.applyXliffImport(xliffPreview.confirmationToken);
      operationMessage = result.message ?? (result.ok ? "XLIFF import applied." : "The XLIFF import could not be applied.");
      if (result.snapshot !== undefined) installSnapshot(result.snapshot, true);
      if (result.ok) xliffPreview = undefined;
    } catch (error) {
      operationMessage = errorMessage(error);
    } finally {
      interchangeBusy = false;
    }
  }

  async function exportReviewJson(): Promise<void> {
    if (interchangeBusy) return;
    interchangeBusy = true;
    try {
      reviewExport = await bridge.exportReviewJson(reviewExportPath.trim() || undefined);
    } catch (error) {
      reviewExport = { ok: false, message: errorMessage(error), entryCount: 0 };
    } finally {
      interchangeBusy = false;
    }
  }

  async function previewReviewJsonImport(): Promise<void> {
    if (interchangeBusy || reviewImportPath.trim() === "") return;
    interchangeBusy = true;
    try {
      reviewPreview = await bridge.previewReviewJsonImport(reviewImportPath.trim());
    } catch (error) {
      reviewPreview = {
        ok: false, message: errorMessage(error), requiresIrreversibleConfirmation: false,
        changes: [], addedCount: 0, changedCount: 0, removedCount: 0, changesOverflowed: false, refusals: [],
      };
    } finally {
      interchangeBusy = false;
    }
  }

  async function applyReviewJsonImport(): Promise<void> {
    if (interchangeBusy || reviewPreview?.confirmationToken === undefined || !confirmDiscardUnsavedWork("apply this review import")) return;
    interchangeBusy = true;
    try {
      const result = await bridge.applyReviewJsonImport(reviewPreview.confirmationToken);
      operationMessage = result.message ?? (result.ok ? "Review import applied." : "The review import could not be applied.");
      if (result.ok && result.review !== undefined && snapshot !== undefined) {
        snapshot = { ...snapshot, review: result.review, history: result.history ?? snapshot.history };
        installReview(snapshot);
        reviewPreview = undefined;
      }
    } catch (error) {
      operationMessage = errorMessage(error);
    } finally {
      interchangeBusy = false;
    }
  }

  function applySuggestion(value: string): void {
    mode = "translation";
    editResourceValue(value);
  }

  function renderPreview(locale: string): void {
    if (previewAst === undefined) return;
    try {
      previewResult = executeMessagePreview(previewAst, locale, previewSamples);
      previewError = undefined;
    } catch (error) {
      previewResult = undefined;
      previewError = errorMessage(error);
    }
  }

  function defaultSample(type: string): string {
    if (type === "int" || type === "number") return "1";
    if (type === "bool") return "true";
    if (type === "date") return "2026-08-08";
    if (type === "time") return "12:30:00";
    if (type === "datetime") return "2026-08-08T12:30:00Z";
    if (type === "guid") return "12345678-1234-1234-1234-123456789abc";
    return "Sample";
  }

  function formatRaw(): void {
    if (editorMutationBlocked) return;
    if (mode !== "raw") return;
    try {
      const formatted = formatJson(editorText);
      edit(formatted);
    } catch (error) {
      clientError = errorMessage(error);
    }
  }

  function scheduleValidation(path: string, content: string): void {
    if (validationTimer !== undefined) window.clearTimeout(validationTimer);
    const epoch = ++validationEpoch;
    validationBusy = true;
    validationTimer = window.setTimeout(() => {
      void bridge.validate(path, content).then((result) => {
        if (epoch !== validationEpoch) return;
        validation = result;
        validationBusy = false;
      }).catch((error) => {
        if (epoch !== validationEpoch) return;
        clientError = errorMessage(error);
        validationBusy = false;
      });
    }, 350);
  }

  async function save(): Promise<void> {
    const document = currentDocument;
    const content = currentContent;
    if (document === undefined || content === undefined || !isDirty || saving || historyBusy) return;
    const generation = draftGenerations[document.path] ?? 0;
    saving = true;
    operationMessage = undefined;
    clientError = undefined;
    try {
      const checked = await bridge.validate(document.path, content);
      validation = checked;
      if (!checked.success) return;
      const result = await bridge.save(document.path, content, document.revision);
      if (!result.ok) {
        if (result.validation !== undefined) validation = result.validation;
        clientError = result.message ?? `Save failed (${result.kind}).`;
        return;
      }
      if (result.snapshot === undefined) {
        await reloadAfterSave(document.path, content, generation, result.message ?? labels.saved);
        return;
      }
      const key = selectedKey;
      const locale = selectedLocale;
      installSnapshot(result.snapshot, false);
      if (!hasNewerDraft(document.path, content, generation)) {
        delete drafts[document.path];
        delete draftGenerations[document.path];
        persistDrafts();
      }
      selectedKey = key;
      selectedLocale = locale;
      configureEditor();
      operationMessage = hasNewerDraft(document.path, content, generation)
        ? "Saved the earlier draft; your newer edit is still open."
        : labels.saved;
    } catch (error) {
      clientError = errorMessage(error);
    } finally {
      saving = false;
    }
  }

  async function reloadAfterSave(path: string, content: string, generation: number, message: string): Promise<void> {
    const next = await bridge.load();
    const newer = hasNewerDraft(path, content, generation);
    installSnapshot(next, false);
    if (!newer) {
      delete drafts[path];
      delete draftGenerations[path];
      persistDrafts();
    }
    configureEditor();
    operationMessage = newer ? "Saved the earlier draft; your newer edit is still open." : message;
  }

  function handleKeyboard(event: KeyboardEvent): void {
    const shortcut = editorShortcut(event, isTextEditingTarget(event.target));
    if (shortcut === undefined) return;
    event.preventDefault();
    if (shortcut === "undo") void undoHistory();
    else if (shortcut === "redo") void redoHistory();
    else if (shortcut === "save") void save();
    else if (shortcut === "toggle-command-search") commandPaletteOpen = !commandPaletteOpen;
    else if (shortcut === "toggle-pseudo-localization") togglePseudoLocalization();
    else if (shortcut === "toggle-right-to-left") toggleUiDirection();
    else toggleArtifactPreview();
  }

  async function undoHistory(): Promise<void> {
    if (historyBlocked) return;
    historyBusy = true;
    clientError = undefined;
    try {
      const result = await bridge.undo();
      if (!result.ok) {
        if (snapshot !== undefined && result.history !== undefined) snapshot.history = result.history;
        clientError = result.message ?? "The saved change could not be undone.";
        return;
      }
      if (result.snapshot === undefined) {
        await loadWorkspace(false);
        operationMessage = result.message ?? "Saved change undone";
        return;
      }
      installSnapshot(result.snapshot, false);
      operationMessage = "Saved change undone";
    } catch (error) {
      clientError = errorMessage(error);
    } finally {
      historyBusy = false;
    }
  }

  async function redoHistory(): Promise<void> {
    if (historyBlocked) return;
    historyBusy = true;
    clientError = undefined;
    try {
      const result = await bridge.redo();
      if (!result.ok) {
        if (snapshot !== undefined && result.history !== undefined) snapshot.history = result.history;
        clientError = result.message ?? "The saved change could not be redone.";
        return;
      }
      if (result.snapshot === undefined) {
        await loadWorkspace(false);
        operationMessage = result.message ?? "Saved change redone";
        return;
      }
      installSnapshot(result.snapshot, false);
      operationMessage = "Saved change redone";
    } catch (error) {
      clientError = errorMessage(error);
    } finally {
      historyBusy = false;
    }
  }

  function isTextEditingTarget(target: EventTarget | null): boolean {
    return target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement ||
      (target instanceof HTMLElement && target.isContentEditable);
  }

  function selectDiagnostic(diagnostic: EditorDiagnostic): void {
    const document = snapshot?.documents.find((candidate) => candidate.path === diagnostic.path);
    if (document?.locale !== undefined) selectedLocale = document.locale;
    if (document !== undefined) {
      selectedDocumentPath = document.path;
      mode = "raw";
      editorText = drafts[document.path] ?? document.content;
    }
  }

  function protectDraft(event: BeforeUnloadEvent): void {
    if (!hasUnsavedWork) return;
    event.preventDefault();
  }

  function localeName(tag: string): string {
    try {
      return new Intl.DisplayNames([uiLocale], { type: "language" }).of(tag) ?? tag;
    } catch {
      return tag;
    }
  }

  function errorMessage(error: unknown): string {
    return error instanceof Error ? error.message : String(error);
  }

  function openProjectWizard(): void {
    if (historyBusy) return;
    projectStep = 1;
    projectDirectory = "";
    projectCatalog = "product";
    projectDefaultLocale = "en";
    projectLocales = [];
    projectNamespace = "Customer.Product";
    projectClassName = "ProductText";
    projectLayer = "base";
    projectGenerateEsm = true;
    projectIncludeStarter = true;
    projectPlan = undefined;
    projectError = undefined;
    projectBusy = false;
    projectDialogOpen = true;
  }

  function closeProjectWizard(): void {
    if (!projectBusy) projectDialogOpen = false;
  }

  function addProjectLocale(): void {
    projectLocales.push({ id: nextProjectLocaleId++, tag: "", fallback: "" });
  }

  function removeProjectLocale(id: number): void {
    projectLocales = projectLocales.filter((locale) => locale.id !== id);
  }

  function projectRequest(): EditorProjectCreationRequest {
    return {
      directory: projectDirectory.trim(),
      catalogId: projectCatalog.trim(),
      defaultLocale: projectDefaultLocale.trim(),
      additionalLocales: projectLocales.map((locale) => ({
        tag: locale.tag.trim(),
        fallback: locale.fallback.trim() || undefined,
      })),
      codeNamespace: projectNamespace.trim(),
      className: projectClassName.trim(),
      layerName: projectLayer.trim(),
      generateEsm: projectGenerateEsm,
      includeStarterMessage: projectIncludeStarter,
    };
  }

  function validateProjectStep(): boolean {
    if (projectStep === 1 && (projectDirectory.trim() === "" || projectCatalog.trim() === "")) {
      projectError = "Choose a new directory and enter a catalog ID.";
      return false;
    }
    if (projectStep === 2) {
      const tags = [projectDefaultLocale.trim(), ...projectLocales.map((locale) => locale.tag.trim())];
      if (tags.some((tag) => tag === "")) {
        projectError = "Every language needs a locale tag.";
        return false;
      }
      if (new Set(tags.map((tag) => tag.toLocaleLowerCase())).size !== tags.length) {
        projectError = "Each language must use a different locale tag.";
        return false;
      }
    }
    if (projectStep === 3 && [projectNamespace, projectClassName, projectLayer].some((value) => value.trim() === "")) {
      projectError = "Namespace, class name, and layer are required.";
      return false;
    }
    projectError = undefined;
    return true;
  }

  async function advanceProjectWizard(): Promise<void> {
    if (historyBusy) return;
    if (!validateProjectStep()) return;
    if (projectStep < 3) {
      projectStep += 1;
      return;
    }
    projectBusy = true;
    try {
      const plan = await bridge.previewProject(projectRequest());
      projectPlan = plan;
      if (!plan.ok) {
        projectError = plan.message ?? "The proposed project is invalid.";
        return;
      }
      projectStep = 4;
    } catch (error) {
      projectError = errorMessage(error);
    } finally {
      projectBusy = false;
    }
  }

  async function createProject(): Promise<void> {
    if (projectPlan?.ok !== true || projectBusy || historyBusy) return;
    if (!confirmDiscardUnsavedWork("create a new project")) return;
    if (hasUnsavedWork) discardUnsavedWork();
    projectBusy = true;
    projectError = undefined;
    try {
      const result = await bridge.createProject(projectRequest());
      if (!result.ok || result.snapshot === undefined) {
        projectError = result.message ?? "The project could not be created.";
        return;
      }
      installSnapshot(result.snapshot, true);
      operationMessage = "Project created";
      projectDialogOpen = false;
    } catch (error) {
      projectError = errorMessage(error);
    } finally {
      projectBusy = false;
    }
  }

  async function openWorkspace(catalogId?: string, directoryOverride?: string): Promise<void> {
    if (openingWorkspace || historyBusy) return;
    if (!confirmDiscardUnsavedWork("open another workspace")) return;
    if (hasUnsavedWork) discardUnsavedWork();
    const directory = directoryOverride ?? (catalogId === undefined ? openDirectory.trim() : snapshot?.root ?? "");
    if (directory === "") {
      clientError = "Enter a workspace directory.";
      return;
    }
    openingWorkspace = true;
    clientError = undefined;
    try {
      const result = await bridge.openWorkspace({ directory, catalogId });
      if (!result.ok || result.snapshot === undefined) {
        clientError = result.message ?? "The workspace could not be opened.";
        return;
      }
      installSnapshot(result.snapshot, true);
      externalChanges = [];
      externalFileChanges = [];
      openDirectory = result.snapshot.root;
      openDialogOpen = false;
    } catch (error) {
      clientError = errorMessage(error);
    } finally {
      openingWorkspace = false;
    }
  }

  async function pickWorkspace(): Promise<void> {
    if (pickingWorkspace || openingWorkspace || historyBusy) return;
    pickingWorkspace = true;
    clientError = undefined;
    try {
      const result = await bridge.pickWorkspace();
      if (result.ok && result.directory !== undefined) {
        openDirectory = result.directory;
      } else if (!result.cancelled && result.message !== undefined) {
        clientError = result.message;
      }
    } catch (error) {
      clientError = errorMessage(error);
    } finally {
      pickingWorkspace = false;
    }
  }

  function showOpenWorkspaceDialog(): void {
    if (historyBusy) return;
    const current = snapshot;
    if (current === undefined) return;
    openDirectory = current.root;
    openDialogOpen = true;
  }

  function prepareMutation(kind: MutationKind): boolean {
    if (historyBusy) return false;
    const current = snapshot;
    if (current?.catalog === undefined) return false;
    if (!confirmDiscardUnsavedWork("make this structural workspace change")) return false;
    if (hasUnsavedWork) discardUnsavedWork();
    mutationKind = kind;
    const firstNonDefault = current.catalog.locales.find((locale) => locale.tag !== current.catalog?.defaultLocale)?.tag ?? "";
    mutationLocale = kind === "remove-locale" || kind === "set-fallback"
      ? (selectedLocale === current.catalog.defaultLocale ? firstNonDefault : selectedLocale)
      : "";
    mutationFallback = current.catalog.defaultLocale;
    mutationReplacementFallback = current.catalog.defaultLocale;
    mutationLayer = current.catalog.layers[0]?.name ?? "base";
    mutationCopyFrom = current.catalog.defaultLocale;
    mutationSourceKey = selectedKey;
    mutationTargetKey = kind === "duplicate-key" ? `${selectedKey}Copy` : kind === "create-key" ? "" : selectedKey;
    mutationInitialValue = "";
    mutationPreview = undefined;
    mutationError = undefined;
    mutationBusy = false;
    mutationIrreversibleConfirmed = false;
    mutationDialogOpen = true;
    return true;
  }

  function mutationRequest(): EditorMutationRequest {
    return {
      kind: mutationKind,
      locale: mutationLocale.trim() || undefined,
      fallback: mutationFallback.trim() || undefined,
      replacementFallback: mutationReplacementFallback.trim() || undefined,
      layer: mutationLayer,
      copyFromLocale: mutationCopyFrom,
      sourceKey: mutationSourceKey.trim() || undefined,
      targetKey: mutationTargetKey.trim() || undefined,
      initialValue: mutationInitialValue,
      confirmationToken: mutationIrreversibleConfirmed ? mutationPreview?.confirmationToken : undefined,
    };
  }

  function invalidateMutationPreview(): void {
    mutationPreview = undefined;
    mutationError = undefined;
    mutationIrreversibleConfirmed = false;
  }

  function changeMutationKind(value: string): void {
    const next = value as MutationKind;
    mutationKind = next;
    const locales = snapshot?.catalog?.locales ?? [];
    const firstTarget = locales.find((locale) => locale.tag !== snapshot?.catalog?.defaultLocale)?.tag ?? "";
    mutationLocale = next === "add-locale" ? "" : firstTarget;
    mutationFallback = snapshot?.catalog?.defaultLocale ?? "";
    mutationReplacementFallback = snapshot?.catalog?.defaultLocale ?? "";
    invalidateMutationPreview();
  }

  async function previewMutation(): Promise<void> {
    if (mutationBusy || historyBusy) return;
    mutationBusy = true;
    mutationError = undefined;
    try {
      const result = await bridge.previewMutation(mutationRequest());
      mutationPreview = result;
      if (!result.ok) mutationError = result.message ?? "The change is not valid.";
    } catch (error) {
      mutationError = errorMessage(error);
    } finally {
      mutationBusy = false;
    }
  }

  async function applyMutation(): Promise<void> {
    if (mutationBusy || historyBusy || mutationPreview?.ok !== true ||
      (mutationPreview.requiresIrreversibleConfirmation && !mutationIrreversibleConfirmed)) return;
    mutationBusy = true;
    mutationError = undefined;
    try {
      const result = await bridge.applyMutation(mutationRequest());
      if (!result.ok) {
        mutationError = result.message ?? "The workspace change could not be committed.";
        mutationPreview = undefined;
        return;
      }
      if (result.snapshot === undefined) {
        await loadWorkspace(false);
        operationMessage = result.message ?? "Workspace updated";
        mutationDialogOpen = false;
        return;
      }
      const preferredKey = mutationKind === "rename-key" || mutationKind === "duplicate-key"
        ? mutationTargetKey
        : selectedKey;
      installSnapshot(result.snapshot, true);
      if (buildRows(result.snapshot, {}).some((row) => row.key === preferredKey)) selectedKey = preferredKey;
      mutationDialogOpen = false;
      operationMessage = "Workspace updated";
      configureEditor();
    } catch (error) {
      mutationError = errorMessage(error);
      mutationPreview = undefined;
    } finally {
      mutationBusy = false;
    }
  }

  async function recoverTransaction(mode: "complete" | "rollback"): Promise<void> {
    if (recoveryBusy || historyBusy) return;
    recoveryBusy = true;
    clientError = undefined;
    try {
      const result = await bridge.recoverTransaction(mode);
      if (!result.ok) {
        clientError = result.message ?? "Workspace recovery failed.";
        return;
      }
      if (result.snapshot === undefined) {
        openDirectory = snapshot?.root ?? openDirectory;
        snapshot = undefined;
        recoveryReloadRequired = true;
        recoveryReloadMessage = result.message ?? "Recovery completed; reload the workspace to refresh it.";
        await loadWorkspace(false);
        return;
      }
      installSnapshot(result.snapshot, true);
      recoveryReloadRequired = false;
      recoveryReloadMessage = undefined;
      operationMessage = mode === "complete" ? "Interrupted change completed" : "Interrupted change rolled back";
    } catch (error) {
      clientError = errorMessage(error);
    } finally {
      recoveryBusy = false;
    }
  }

  function recoverSavedDrafts(): void {
    drafts = Object.fromEntries(Object.entries(recoveredDrafts).map(([path, draft]) => [path, draft.content]));
    recoveredDrafts = {};
    persistDrafts();
    configureEditor();
  }

  function reviewExternalChanges(): void {
    const change = externalFileChanges[0];
    if (change === undefined) return;
    comparedExternalChange = change;
    const base = snapshot?.documents.find((document) => document.path === change.path);
    mergedExternalText = drafts[change.path] ?? base?.content ?? change.content ?? "";
  }

  async function applyExternalMerge(): Promise<void> {
    const change = comparedExternalChange;
    if (change === undefined || historyBusy) return;
    if (!confirmDiscardNonDocumentWork("reload the external file and keep the merged document draft")) return;
    const retainedDrafts = { ...drafts, [change.path]: mergedExternalText };
    loading = true;
    clientError = undefined;
    try {
      const next = await bridge.load();
      installSnapshot(next, true);
      drafts = Object.fromEntries(Object.entries(retainedDrafts).filter(([path]) =>
        next.documents.some((document) => document.path === path)));
      if (drafts[change.path] === undefined) {
        clientError = `The externally deleted file '${change.path}' cannot receive a merged draft.`;
      }
      persistDrafts();
      externalChanges = [];
      externalFileChanges = [];
      comparedExternalChange = undefined;
      configureEditor();
    } catch (error) {
      clientError = errorMessage(error);
    } finally {
      loading = false;
    }
  }

  function discardSavedDrafts(): void {
    recoveredDrafts = {};
    clearStoredDrafts(snapshot);
  }

  function draftStorageKey(value: WorkspaceSnapshot): string {
    return `runic-translations:drafts:1:${value.root}\n${value.catalog?.id ?? ""}`;
  }

  function persistDrafts(): void {
    const current = snapshot;
    if (current === undefined) return;
    if (Object.keys(drafts).length === 0) {
      clearStoredDrafts(current);
      return;
    }
    const stored: Record<string, StoredDraft> = {};
    for (const [path, content] of Object.entries(drafts)) {
      const document = current.documents.find((candidate) => candidate.path === path);
      if (document !== undefined) stored[path] = { content, baseRevision: document.revision };
    }
    setLocalEditorState(draftStorageKey(current), JSON.stringify({ version: 1, documents: stored }));
  }

  function setDraft(path: string, content: string): void {
    drafts[path] = content;
    draftGenerations[path] = (draftGenerations[path] ?? 0) + 1;
  }

  function hasNewerDraft(path: string, content: string, generation: number): boolean {
    return draftGenerations[path] !== generation || drafts[path] !== content;
  }

  function readStoredDrafts(value: WorkspaceSnapshot): Record<string, StoredDraft> {
    try {
      const raw = getLocalEditorState(draftStorageKey(value));
      if (raw === null) return {};
      const parsed = JSON.parse(raw) as { version?: unknown; documents?: unknown };
      if (parsed.version !== 1 || typeof parsed.documents !== "object" || parsed.documents === null) return {};
      const recovered: Record<string, StoredDraft> = {};
      for (const [path, candidate] of Object.entries(parsed.documents as Record<string, unknown>)) {
        const document = value.documents.find((item) => item.path === path);
        if (document === undefined || typeof candidate !== "object" || candidate === null) continue;
        const draft = candidate as Partial<StoredDraft>;
        if (typeof draft.content === "string" && typeof draft.baseRevision === "string") recovered[path] = draft as StoredDraft;
      }
      return recovered;
    } catch {
      return {};
    }
  }

  function clearStoredDrafts(value: WorkspaceSnapshot | undefined): void {
    if (value !== undefined) removeLocalEditorState(draftStorageKey(value));
  }

  function readRecentProjects(): RecentProject[] {
    try {
      const value = JSON.parse(getLocalEditorState("runic-translations:recent:1") ?? "[]") as unknown;
      if (!Array.isArray(value)) return [];
      return value.filter((item): item is RecentProject =>
        typeof item === "object" && item !== null &&
        typeof (item as RecentProject).root === "string" &&
        typeof (item as RecentProject).catalogId === "string" &&
        typeof (item as RecentProject).openedAt === "string").slice(0, 8);
    } catch {
      return [];
    }
  }

  function rememberProject(value: WorkspaceSnapshot): void {
    const catalogId = value.catalog?.id;
    if (catalogId === undefined) return;
    const entry = { root: value.root, catalogId, openedAt: new Date().toISOString() };
    recentProjects = [entry, ...recentProjects.filter((item) => item.root !== entry.root || item.catalogId !== catalogId)].slice(0, 8);
    setLocalEditorState("runic-translations:recent:1", JSON.stringify(recentProjects));
  }

  function beginRepair(document: EditorDocument): void {
    if (hasUnsavedRepair && !confirm("Discard unsaved repair text and open another document?")) return;
    repairDocument = document;
    repairText = document.content;
    repairMessage = undefined;
  }

  async function saveRepair(): Promise<void> {
    const document = repairDocument;
    if (document === undefined || repairBusy || historyBusy) return;
    repairBusy = true;
    repairMessage = undefined;
    try {
      const checked = await bridge.validate(document.path, repairText);
      if (!checked.success) {
        repairMessage = checked.diagnostics[0]?.message ?? "The document is still invalid.";
        return;
      }
      const result = await bridge.save(document.path, repairText, document.revision);
      if (!result.ok) {
        repairMessage = result.message ?? "The repaired document could not be saved.";
        return;
      }
      if (result.snapshot === undefined) {
        await reloadAfterSave(document.path, repairText, draftGenerations[document.path] ?? 0, result.message ?? labels.saved);
        repairDocument = undefined;
        return;
      }
      repairDocument = undefined;
      installSnapshot(result.snapshot, false);
    } catch (error) {
      repairMessage = errorMessage(error);
    } finally {
      repairBusy = false;
    }
  }

  function mutationTitle(kind: MutationKind): string {
    return {
      "add-locale": ui.text("Ui.Page.Mutation.AddLanguage"),
      "remove-locale": ui.text("Ui.Page.Mutation.RemoveLanguage"),
      "set-fallback": ui.text("Ui.Page.Mutation.ChangeFallbackRelationships"),
      "create-key": labels.addMessage,
      "rename-key": ui.text("Ui.Page.Mutation.RenameMoveMessage"),
      "duplicate-key": ui.text("Ui.Page.Mutation.DuplicateMessage"),
      "delete-key": ui.text("Ui.Page.Mutation.DeleteMessage"),
    }[kind];
  }

  function labelsFor(locale: string) {
    const options = { locale };
    return {
      title: m$App$Title(options),
      eyebrow: m$App$Eyebrow(options),
      search: m$App$Search(options),
      all: m$App$All(options),
      missing: m$App$Missing(options),
      structured: m$App$Structured(options),
      save: m$App$Save(options),
      saving: m$App$Saving(options),
      reload: m$App$Reload(options),
      simple: m$App$Simple(options),
      advanced: m$App$Advanced(options),
      raw: m$App$Raw(options),
      noSelection: m$App$NoSelection(options),
      noResults: m$App$NoResults(options),
      valid: m$App$Valid(options),
      invalid: m$App$Invalid(options),
      unsaved: m$App$Unsaved(options),
      saved: m$App$Saved(options),
      defaultLocale: m$App$DefaultLocale(options),
      workspace: m$App$Workspace(options),
      diagnostics: m$App$Diagnostics(options),
      messages: m$App$Messages(options),
      messageBulkActions: m$App$MessageBulkActions(options),
      visibleMessages: m$App$VisibleMessages(options),
      markForReview: m$App$MarkForReview(options),
      approveTranslations: m$App$ApproveTranslations(options),
      addMessage: m$App$AddMessage(options),
      noMatchingMessages: m$App$NoMatchingMessages(options),
      missingTranslation: m$App$MissingTranslation(options),
      translated: m$App$Translated(options),
      stale: m$App$Stale(options),
      review: m$App$Review(options),
      messageFilters: m$App$MessageFilters(options),
    };
  }
</script>

<svelte:head>
  <title>{labels.title}</title>
    <meta name="description" content={ui.text("Ui.Page.MetaDescription")} />
</svelte:head>
<svelte:window onkeydown={handleKeyboard} onbeforeunload={protectDraft} />

{#snippet previewNodes(nodes: PreviewNode[])}
  {#each nodes as node, index (index)}
    {#if node.kind === "text"}
      <span class="preview-text">{node.value}</span>
    {:else}
      <span class="preview-element">
        <span class="preview-element-label">{node.name}</span>
        {#if Object.keys(node.attributes).length > 0}
          <span class="preview-attributes">{Object.entries(node.attributes).map(([name, value]) => name + "=" + value).join(" · ")}</span>
        {/if}
        <span class="preview-children">{@render previewNodes(node.children)}</span>
      </span>
    {/if}
  {/each}
{/snippet}

{#if externalChanges.length > 0}
  <div class="pointer-events-none fixed inset-x-2 bottom-2 z-50 mx-auto max-w-[calc(100vw-1rem)] sm:inset-x-4 sm:bottom-4 sm:max-w-4xl">
    <Alert.Root class="pointer-events-auto pr-4 shadow-xl" aria-live="polite">
      <Alert.Title>{ui.text("Ui.Page.ExternalChanges.Title")}</Alert.Title>
      <Alert.Description class="min-w-0">
        <p class="truncate font-mono text-xs">{externalChanges.join(", ")}</p>
        <p>{hasUnsavedWork ? ui.text("Ui.Page.ExternalChanges.UnsavedIntact") : ui.text("Ui.Page.ExternalChanges.ReloadLatest")}</p>
      </Alert.Description>
      <Alert.Action class="static col-span-full mt-2 flex flex-wrap justify-end gap-2">
        <Button variant="ghost" size="xs" onclick={() => { externalChanges = []; externalFileChanges = []; }}>{ui.text("Ui.Page.KeepCurrentView")}</Button>
        <Button variant="outline" size="xs" onclick={reviewExternalChanges}>{ui.text("Ui.Page.ExternalChanges.CompareMerge")}</Button>
        <Button size="xs" onclick={() => void loadWorkspace(true)}>{ui.text("Ui.Page.ExternalChanges.ReloadFiles")}</Button>
      </Alert.Action>
    </Alert.Root>
  </div>
{/if}

{#if Object.keys(recoveredDrafts).length > 0}
  <div class="pointer-events-none fixed inset-x-2 bottom-2 z-50 mx-auto max-w-[calc(100vw-1rem)] sm:inset-x-4 sm:bottom-4 sm:max-w-2xl">
    <Alert.Root class="pointer-events-auto pr-4 shadow-xl" aria-live="polite">
      <Alert.Title>{ui.text("Ui.Page.RecoveredDrafts.Title")}</Alert.Title>
      <Alert.Description>
        {Object.keys(recoveredDrafts).length === 1
          ? ui.text("Ui.Page.RecoveredDrafts.One")
          : `${Object.keys(recoveredDrafts).length} ${ui.text("Ui.Page.RecoveredDrafts.Many")}`}
      </Alert.Description>
      <Alert.Action class="static col-span-full mt-2 flex flex-col gap-2 min-[360px]:flex-row min-[360px]:justify-end">
        <Button variant="ghost" size="xs" onclick={discardSavedDrafts}>{ui.text("Ui.Page.Discard")}</Button>
        <Button size="xs" onclick={recoverSavedDrafts}>{ui.text("Ui.Page.RecoveredDrafts.Restore")}</Button>
      </Alert.Action>
    </Alert.Root>
  </div>
{/if}

{#if comparedExternalChange !== undefined}
  <AppDialog
    open
    title={comparedExternalChange.path}
    description={ui.text("Ui.Page.ExternalCompare.Description")}
    class="sm:max-w-6xl"
    bodyClass="grid gap-4"
    onopenchange={(open) => { if (!open) comparedExternalChange = undefined; }}
  >
    <div class="grid gap-4 lg:grid-cols-2">
      <Field.Field>
        <Field.Label for="external-editor-base">{ui.text("Ui.Page.ExternalCompare.EditorBase")}</Field.Label>
        <Textarea id="external-editor-base" class="min-h-64 font-mono text-xs" readonly value={snapshot?.documents.find((document) => document.path === comparedExternalChange?.path)?.content ?? ui.text("Ui.Page.ExternalCompare.NotPreviouslyLoaded")} />
      </Field.Field>
      <Field.Field>
        <Field.Label for="external-current-disk">{ui.text("Ui.Page.ExternalCompare.CurrentDisk")}</Field.Label>
        <Textarea id="external-current-disk" class="min-h-64 font-mono text-xs" readonly value={comparedExternalChange.content ?? ui.text("Ui.Page.ExternalCompare.DeletedExternally")} />
      </Field.Field>
    </div>
    <Field.Field>
      <Field.Label for="external-merged-draft">{ui.text("Ui.Page.ExternalCompare.MergedDraft")}</Field.Label>
      <Textarea id="external-merged-draft" class="min-h-64 font-mono text-xs" bind:value={mergedExternalText} spellcheck={false} />
    </Field.Field>
    {#snippet footer()}
      <Button variant="outline" onclick={() => comparedExternalChange = undefined}>{ui.text("Ui.Page.KeepCurrentView")}</Button>
      <Button onclick={() => void applyExternalMerge()}>{ui.text("Ui.Page.ExternalCompare.ReloadKeepMerged")}</Button>
    {/snippet}
  </AppDialog>
{/if}

{#if loading}
  <main class="loading-shell" aria-live="polite">
    <div class="mark" aria-hidden="true"><span></span></div>
    <p>{labels.eyebrow}</p>
    <div class="loading-line"></div>
  </main>
{:else if snapshot === undefined}
  <main class="fatal-shell">
    <div class="mark" aria-hidden="true"><span></span></div>
    <p class="eyebrow">{labels.eyebrow}</p>
    <h1>{recoveryReloadRequired ? ui.text("Ui.Page.Fatal.RecoveryReloadRequired") : ui.text("Ui.Page.Fatal.CouldNotOpen")}</h1>
    <p>{recoveryReloadRequired ? recoveryReloadMessage ?? ui.text("Ui.Page.Fatal.RecoveryRefresh") : clientError ?? ui.text("Ui.Page.Fatal.NoCatalog")}</p>
    <div class="recovery-actions">
      <button class="primary" onclick={() => void loadWorkspace(false)}>{labels.reload}</button>
      {#if recoveryReloadRequired}<button class="secondary" onclick={() => openDialogOpen = true}>{ui.text("Ui.Page.Fatal.OpenAnotherWorkspace")}</button>{/if}
    </div>
  </main>
{:else if snapshot.pendingTransaction !== undefined}
  <main class="recovery-shell">
    <div class="mark" aria-hidden="true"><span></span></div>
    <p class="eyebrow">{ui.text("Ui.Page.Recovery.Eyebrow")}</p>
    <h1>{ui.text("Ui.Page.Recovery.Title")}</h1>
    <p>{ui.text("Ui.Page.Recovery.JournalFor")} <strong>{snapshot.pendingTransaction.catalogId}</strong> {ui.text("Ui.Page.Recovery.Lists")} {snapshot.pendingTransaction.paths.length} {snapshot.pendingTransaction.paths.length === 1 ? ui.text("Ui.Page.File") : ui.text("Ui.Page.Files")}. {ui.text("Ui.Page.Recovery.NoFurtherEditing")}</p>
    <div class="recovery-paths">
      {#each snapshot.pendingTransaction.paths as path (path)}<code>{path}</code>{/each}
    </div>
    {#if clientError}<p class="project-error" aria-live="polite">{clientError}</p>{/if}
    <div class="recovery-actions">
      <button class="secondary" disabled={recoveryBusy} onclick={() => void recoverTransaction("rollback")}>{ui.text("Ui.Page.Recovery.RestoreBefore")}</button>
      <button class="primary" disabled={recoveryBusy} onclick={() => void recoverTransaction("complete")}>{recoveryBusy ? ui.text("Ui.Page.Recovery.Recovering") : ui.text("Ui.Page.Recovery.Complete")}</button>
    </div>
    <small>{ui.text("Ui.Page.Recovery.JournalNote")}</small>
  </main>
{:else if snapshot.catalog === undefined}
  <main class="welcome-shell">
    <header class="welcome-brand">
      <div class="mark small" aria-hidden="true"><span></span></div>
      <div><p class="eyebrow">{labels.eyebrow}</p><h1>{labels.title}</h1></div>
      <select aria-label={ui.text("Ui.Page.InterfaceLanguage")} value={uiLocale} onchange={(event) => uiLocale = event.currentTarget.value}>
        <option value="en">EN</option><option value="de">DE</option>
      </select>
    </header>
    <section class="welcome-content">
      <div class="welcome-heading">
        <p class="eyebrow">{ui.text("Ui.Page.Welcome.Eyebrow")}</p>
        <h2>{snapshot.catalogs.length > 1 ? ui.text("Ui.Page.Welcome.ChooseCatalog") : ui.text("Ui.Page.Welcome.OpenProject")}</h2>
        <p>{snapshot.catalogs.length > 1
          ? `${ui.text("Ui.Page.Welcome.Found")} ${snapshot.catalogs.length} ${ui.text("Ui.Page.Welcome.CatalogsBelow")}`
          : ui.text("Ui.Page.Welcome.OpenOrCreate")}</p>
      </div>

      {#if snapshot.catalogs.length > 0}
        <div class="catalog-choices">
          {#each snapshot.catalogs as catalog (catalog.id)}
            <button class="catalog-choice" onclick={() => void openWorkspace(catalog.id)} disabled={openingWorkspace}>
              <span class={{ "status-dot": true, warning: !catalog.success }}></span>
              <span><strong>{catalog.id}</strong><small>{catalog.manifestPaths.join(", ")}</small></span>
              <span class="catalog-metrics">{catalog.localeCount} {ui.text("Ui.Page.Locales")}<br />{catalog.messageCount} {ui.text("Ui.Page.Messages")}</span>
              <span class={catalog.errorCount > 0 ? "health error" : "health"}>{catalog.errorCount > 0 ? `${catalog.errorCount} ${ui.text("Ui.Page.Errors")}` : ui.text("Ui.Page.Healthy")}</span>
            </button>
          {/each}
        </div>
      {/if}

      <div class="open-workspace-card">
        <label for="open-directory">{ui.text("Ui.Page.WorkspaceDirectory")}</label>
        <div><input id="open-directory" bind:value={openDirectory} placeholder="/projects/customer-app" autocomplete="off" />
          <button class="secondary" disabled={pickingWorkspace || openingWorkspace} onclick={() => void pickWorkspace()}>{pickingWorkspace ? ui.text("Ui.Page.Choosing") : ui.text("Ui.Page.Browse")}</button>
          <button class="primary" disabled={openingWorkspace} onclick={() => void openWorkspace()}>{openingWorkspace ? ui.text("Ui.Page.Opening") : ui.text("Ui.Page.Open")}</button></div>
        <small>{ui.text("Ui.Page.Welcome.TraversalNote")}</small>
      </div>

      <div class="welcome-actions">
        <button class="secondary" onclick={openProjectWizard}>＋ {ui.text("Ui.Page.Welcome.CreateProject")}</button>
        <button class="secondary" onclick={() => void loadWorkspace(true)}>↻ {ui.text("Ui.Page.Welcome.Scan")} {snapshot.root}</button>
      </div>

      {#if recentProjects.length > 0}
        <section class="recent-projects">
          <header><strong>{ui.text("Ui.Page.Welcome.RecentProjects")}</strong><span>{ui.text("Ui.Page.Welcome.StoredLocalProfile")}</span></header>
          {#each recentProjects as project (`${project.root}\n${project.catalogId}`)}
            <button onclick={() => void openWorkspace(project.catalogId, project.root)} disabled={openingWorkspace}>
              <span><strong>{project.catalogId}</strong><code>{project.root}</code></span>
              <small>{new Date(project.openedAt).toLocaleDateString(uiLocale)}</small>
            </button>
          {/each}
        </section>
      {/if}

      {#if malformedDocuments.length > 0}
        <section class="repair-list">
          <header><div><strong>{ui.text("Ui.Page.Welcome.RepairMalformedJson")}</strong><span>{malformedDocuments.length} {ui.text("Ui.Page.Welcome.FilesNeedAttention")}</span></div></header>
          {#each malformedDocuments as document (document.path)}
            <button onclick={() => beginRepair(document)}><span>!</span><code>{document.path}</code><small>{ui.text("Ui.Page.Welcome.OpenRepairEditor")} →</small></button>
          {/each}
        </section>
      {/if}
      {#if clientError}<p class="project-error" aria-live="polite">{clientError}</p>{/if}
    </section>
  </main>
{:else}
  <Sidebar.Provider style="--sidebar-width: 21rem; --sidebar-width-mobile: min(20rem, calc(100vw - 1rem));" class="h-svh min-h-0 overflow-hidden">
    <Sidebar.Root collapsible="offcanvas">
      <EditorSidebarHeader
        catalogId={snapshot.catalog.id}
        localeCount={snapshot.catalog.locales.length}
        schemaVersion={snapshot.catalog.schemaVersion}
        root={snapshot.root}
        success={snapshot.success}
        reloadLabel={labels.reload}
        {recentProjects}
        onreload={() => void loadWorkspace(true)}
        onopenworkspace={showOpenWorkspaceDialog}
        onnewproject={openProjectWizard}
        onopenrecent={(project) => void openWorkspace(project.catalogId, project.root)}
      />

      <Sidebar.Content class="gap-0 overflow-hidden">

      <WorkspacePanel
        {malformedDocuments}
        reviewError={snapshot.review?.error}
        onrepair={beginRepair}
      />

      <SidebarSectionPanels bind:languagesOpen bind:messagesOpen>
        {#snippet languages()}
          <LocaleSwitcher
            locales={localeSummaries}
            {selectedLocale}
            onselect={selectLocale}
            onmanage={() => prepareMutation("add-locale")}
            bind:open={languagesOpen}
          />
        {/snippet}

        {#snippet messages()}
          <MessageList
            items={messageListItems}
            {selectedKey}
            visibleCount={visibleRows.length}
            reviewActionsDisabled={reviewMutationBlocked}
            noResultsLabel={labels.noResults}
            labels={{
              messages: labels.messages,
              bulkActions: labels.messageBulkActions,
              visibleMessages: labels.visibleMessages,
              markForReview: labels.markForReview,
              approveTranslations: labels.approveTranslations,
              addMessage: labels.addMessage,
              noMatchingMessages: labels.noMatchingMessages,
              missingTranslation: labels.missingTranslation,
              translated: labels.translated,
              structured: labels.structured,
              stale: labels.stale,
              review: labels.review,
            }}
            onselect={(key) => {
              const row = visibleRows.find((candidate) => candidate.key === key);
              if (row !== undefined) selectRow(row);
            }}
            onadd={() => prepareMutation("create-key")}
            onmarkreview={() => markVisible("needs-review")}
            onapprove={() => markVisible("approved")}
            bind:open={messagesOpen}
          >
            {#snippet toolbar()}
              <MessageToolbar
                bind:query
                bind:filter
                bind:inputRef={searchInput}
                placeholder={labels.search}
                options={filterOptions}
                filterLabel={labels.messageFilters}
              />
            {/snippet}
          </MessageList>
        {/snippet}
      </SidebarSectionPanels>
      </Sidebar.Content>
      <EditorSettingsFooter
        locale={uiLocale}
        {themeMode}
        {themePalette}
        {pseudoLocalization}
        {uiDirection}
        onlocalechange={(locale) => uiLocale = locale}
        onthememodechange={changeThemeMode}
        onthemepalettechange={changeThemePalette}
        ontogglepseudo={togglePseudoLocalization}
        ontoggledirection={toggleUiDirection}
        onabout={() => void showAbout()}
      />
      <Sidebar.Rail />
    </Sidebar.Root>

    <Sidebar.Inset class="editor-shell min-h-0 min-w-0 overflow-hidden">
      <EditorToolbar
        {reviewDirty}
        {reviewSaving}
        reviewDisabled={snapshot.review?.error !== undefined}
        saveDisabled={!isDirty || saving || validationBusy || validation?.success === false || clientError !== undefined}
        {saving}
        saveLabel={labels.save}
        savingLabel={labels.saving}
        saveState={isDirty ? labels.unsaved : operationMessage ?? labels.saved}
        {isDirty}
        canUndo={snapshot.history?.canUndo === true && !historyBlocked}
        canRedo={snapshot.history?.canRedo === true && !historyBlocked}
        {historyBusy}
        undoLabel={snapshot.history?.undoLabel}
        redoLabel={snapshot.history?.redoLabel}
        ondiscardreview={discardReview}
        onsavereview={() => void saveReview()}
        onsave={() => void save()}
        onundo={() => void undoHistory()}
        onredo={() => void redoHistory()}
      />

      <div class="flex justify-end border-b px-4 py-2">
        <Button variant="outline" size="sm" disabled={loading || interchangeBusy} onclick={() => interchangeOpen = true}>{ui.text("Ui.Page.Interchange")}…</Button>
      </div>

      {#if selectedRow === undefined}
        <Empty.Root>
          <Empty.Header>
            <Empty.Media variant="icon" class="text-primary">
              <MessageSquareTextIcon aria-hidden="true" />
            </Empty.Media>
            <Empty.Title class="font-serif font-medium">{labels.noSelection}</Empty.Title>
            <Empty.Description>{ui.text("Ui.Page.EmptySelection.Description")}</Empty.Description>
          </Empty.Header>
        </Empty.Root>
      {:else}
        <div class="editor-content" lang={selectedLocale} dir={uiDirection}>
          <MessageHeading
            messageKey={selectedRow.key}
            description={selectedRow.description}
            tags={selectedRow.tags}
            locale={selectedLocale}
            layer={currentDocument?.layer ?? ui.text("Ui.Page.NoDocument")}
            inheritedFrom={currentCell?.inheritedFrom}
            onrename={() => prepareMutation("rename-key")}
            onduplicate={() => prepareMutation("duplicate-key")}
            ondelete={() => prepareMutation("delete-key")}
          />

          <ReviewWorkflow
            state={currentReviewState}
            dirty={reviewDirty}
            message={reviewMessage ?? ui.text("Ui.Page.ProjectNotes")}
            disabled={snapshot.review?.error !== undefined || reviewMutationBlocked}
            stale={currentIsStale}
            terminologyCount={terminology.length}
            qualityCount={localeQualityFindings.length}
            note={currentReview?.note ?? ""}
            qualityIssues={currentQuality}
            suggestions={memorySuggestions}
            onstatechange={setCurrentReviewState}
            onnotechange={setCurrentReviewNote}
            onterminology={() => terminologyDialogOpen = true}
            onreport={() => reportDialogOpen = true}
            onqualityfilter={() => filter = "quality"}
            onsuggestion={applySuggestion}
          />

          <EditorModeSwitcher
            {mode}
            simpleLabel={labels.simple}
            rawLabel={labels.raw}
            onchange={chooseMode}
          />

          <TranslationEditor
            {mode}
            locale={selectedLocale}
            label={mode === "translation" ? localeName(selectedLocale) : currentDocument?.path ?? ui.text("Ui.Page.ResourceDocument")}
            value={editorText}
            resourceValue={currentCell?.entry?.value ?? selectedRow.cells[snapshot.catalog.defaultLocale]?.entry?.value}
            missing={currentCell?.entry === undefined}
            invalid={clientError !== undefined || validation?.success === false}
            disabled={editorMutationBlocked}
            onresourcechange={editResourceValue}
            onrawchange={edit}
            onformatraw={formatRaw}
          />

          {#if mode === "translation"}
            <section class="message-preview" aria-live="polite">
              <header>
                <div><strong>{ui.text("Ui.Page.Preview.Title")}</strong><span>{ui.text("Ui.Page.Preview.Description")}</span></div>
                <span class="preview-state">{previewBusy ? ui.text("Ui.Page.Preview.Compiling") : previewAst === undefined ? ui.text("Ui.Page.Preview.Unavailable") : selectedLocale}</span>
              </header>
              {#if previewAst !== undefined && Object.keys(previewAst.inputs).length > 0}
                <div class="sample-inputs">
                  {#each Object.entries(previewAst.inputs) as [name, descriptor] (name)}
                    <label><span>{name}<small>{descriptor.type}</small></span><input value={previewSamples[name] ?? ""} oninput={(event) => updatePreviewSample(name, event.currentTarget.value)} /></label>
                  {/each}
                </div>
              {/if}
              <div class="preview-canvas">
                {#if previewBusy}
                  <span class="preview-placeholder">{ui.text("Ui.Page.Preview.CompilingDraft")}</span>
                {:else if previewError}
                  <span class="preview-error">{previewError}</span>
                {:else if simulatedPreviewResult?.kind === "text"}
                  <p>{simulatedPreviewResult.value}</p>
                {:else if simulatedPreviewResult?.kind === "content"}
                  <div class="safe-content">{@render previewNodes(simulatedPreviewResult.nodes)}</div>
                {:else}
                  <span class="preview-placeholder">{ui.text("Ui.Page.Preview.EditToBuild")}</span>
                {/if}
              </div>
              <p class="safe-note">{ui.text("Ui.Page.Preview.SafeMarkup")}{pseudoLocalization ? ` ${ui.text("Ui.Page.Preview.PseudoActive")}` : ""}{uiDirection === "rtl" ? ` ${ui.text("Ui.Page.Preview.RtlActive")}` : ""}</p>
            </section>

            <ArtifactPreviewPanel
              open={artifactPreviewOpen}
              baseResult={previewResult}
              simulatedResult={simulatedPreviewResult}
              {pseudoLocalization}
              direction={uiDirection}
              busy={previewBusy}
              onclose={() => artifactPreviewOpen = false}
            />
          {/if}

          <ValidationPanel
            busy={validationBusy}
            {diagnostics}
            {clientError}
            {errorCount}
            {warningCount}
            validLabel={labels.valid}
            invalidLabel={labels.invalid}
            diagnosticsLabel={labels.diagnostics}
            schemaVersion={snapshot.catalog.schemaVersion}
            onselect={selectDiagnostic}
          />
        </div>
      {/if}
    </Sidebar.Inset>
  </Sidebar.Provider>
{/if}

{#if aboutDialogOpen}
  <AppDialog
    open
    title={aboutInfo?.product ?? ui.text("Ui.Page.About.Product")}
    description={ui.text("Ui.Page.About.Description")}
    onopenchange={(open) => aboutDialogOpen = open}
  >
    <div class="grid gap-4">
      {#if aboutBusy}
        <div class="flex items-center gap-2 text-muted-foreground"><Spinner />{ui.text("Ui.Page.About.Reading")}</div>
      {:else if aboutInfo !== undefined}
        <dl class="grid overflow-hidden rounded-xl border">
          {#each [
            [ui.text("Ui.Page.About.Version"), aboutInfo.version],
            [ui.text("Ui.Page.About.UpdateChannel"), aboutInfo.updateChannel],
            [ui.text("Ui.Page.About.SourceRevision"), aboutInfo.commit ?? ui.text("Ui.Page.About.DevelopmentBuild")],
            [ui.text("Ui.Page.About.Runtime"), aboutInfo.runtime],
            [ui.text("Ui.Page.About.RuntimeIdentifier"), aboutInfo.runtimeIdentifier],
            [ui.text("Ui.Page.About.System"), `${aboutInfo.operatingSystem} · ${aboutInfo.architecture}`],
          ] as item (item[0])}
            <div class="grid gap-1 border-b px-4 py-3 last:border-b-0 sm:grid-cols-[9rem_1fr] sm:gap-4">
              <dt class="text-muted-foreground">{item[0]}</dt><dd class="m-0 overflow-wrap-anywhere font-mono text-xs">{item[1]}</dd>
            </div>
          {/each}
        </dl>
      {/if}
      <Alert.Root>
        <Alert.Title>{ui.text("Ui.Page.About.DiagnosticBundleTitle")}</Alert.Title>
        <Alert.Description>{ui.text("Ui.Page.About.DiagnosticBundleDescription")}</Alert.Description>
        {#if diagnosticMessage}<p class="text-sm text-primary" aria-live="polite">{diagnosticMessage}</p>{/if}
      </Alert.Root>
      {#if diagnosticBundlePath !== undefined}
        <section class="grid gap-2 rounded-xl border p-4" aria-labelledby="diagnostic-bundle-actions-title">
          <h3 id="diagnostic-bundle-actions-title" class="font-medium">{ui.text("Ui.Page.About.DiagnosticBundle")}</h3>
          <Input aria-label={ui.text("Ui.Page.About.DiagnosticBundlePath")} readonly value={diagnosticBundlePath} class="font-mono text-xs" />
          <div class="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" disabled={diagnosticBusy} onclick={() => void revealDiagnosticBundle()}>{ui.text("Ui.Page.About.RevealLocation")}</Button>
            <Button variant="outline" size="sm" onclick={() => void copyDiagnosticBundlePath()}>{ui.text("Ui.Page.About.CopyPath")}</Button>
            <Button variant="destructive" size="sm" disabled={diagnosticBusy} onclick={() => void deleteDiagnosticBundle()}>{ui.text("Ui.Page.About.DeleteBundle")}</Button>
          </div>
          <p class="text-xs text-muted-foreground">{ui.text("Ui.Page.About.BundleNoUpload")}</p>
        </section>
      {/if}
      <section class="grid gap-3 rounded-xl border p-4" aria-labelledby="local-state-title">
        <div class="grid gap-1 sm:grid-cols-[1fr_auto] sm:items-center sm:gap-4">
          <div><h3 id="local-state-title" class="font-medium">{ui.text("Ui.Page.About.LocalState")}</h3><p class="text-sm text-muted-foreground">{ui.text("Ui.Page.About.LocalStateDescription")}</p></div>
          <Button variant="outline" size="sm" disabled={(localStateSummary?.entries ?? 0) === 0} onclick={clearLocalState}>{ui.text("Ui.Page.About.ClearLocalState")}</Button>
        </div>
        {#if localStateSummary !== undefined}
          <p class="text-sm text-muted-foreground">{localStateSummary.entries} {ui.text("Ui.Page.About.Entries")} · {localStateSummary.bytes.toLocaleString()} {ui.text("Ui.Page.About.Bytes")} · {localStateSummary.preferenceEntries} {ui.text("Ui.Page.About.Preferences")} · {localStateSummary.recentProjectEntries} {ui.text("Ui.Page.About.RecentProjectRecords")} · {localStateSummary.draftEntries} {ui.text("Ui.Page.About.RecoveryDraftRecords")}{localStateSummary.recovered ? ` · ${ui.text("Ui.Page.About.RecoveredUnreadable")}` : ""}</p>
        {/if}
        <p class="text-xs text-muted-foreground">{ui.text("Ui.Page.About.ClearLocalStateNote")}</p>
        {#if localStateMessage}<p class="text-sm text-primary" aria-live="polite">{localStateMessage}</p>{/if}
      </section>
      <p class="text-sm text-muted-foreground">{ui.text("Ui.Page.About.LicenseNote")}</p>
    </div>
    {#snippet footer()}
      <Button variant="outline" onclick={() => aboutDialogOpen = false}>{ui.text("Ui.Page.Close")}</Button>
      <Button disabled={diagnosticBusy || aboutBusy} onclick={() => void createDiagnosticBundle()}>
        {#if diagnosticBusy}<Spinner data-icon="inline-start" />{/if}
        {diagnosticBusy ? ui.text("Ui.Page.About.CreatingBundle") : ui.text("Ui.Page.About.CreateBundle")}
      </Button>
    {/snippet}
  </AppDialog>
{/if}

{#if terminologyDialogOpen}
  <AppDialog
    open
    title={ui.text("Ui.Page.Terminology.Title")}
    description={ui.text("Ui.Page.Terminology.Description")}
    class="sm:max-w-4xl"
    onopenchange={(open) => terminologyDialogOpen = open}
  >
    <div inert={reviewMutationBlocked} aria-disabled={reviewMutationBlocked} class:opacity-60={reviewMutationBlocked}>
    <Field.FieldGroup class="grid gap-3 sm:grid-cols-2">
      <Field.Field><Field.Label for="term-source">{ui.text("Ui.Page.Terminology.SourceTerm")}</Field.Label><Input id="term-source" bind:value={termSource} placeholder={ui.text("Ui.Page.Terminology.SourcePlaceholder")} /></Field.Field>
      <Field.Field><Field.Label for="term-preferred">{ui.text("Ui.Page.Terminology.Preferred")}</Field.Label><Input id="term-preferred" bind:value={termPreferred} placeholder={ui.text("Ui.Page.Terminology.PreferredPlaceholder")} /></Field.Field>
      <Field.Field><Field.Label for="term-locale">{ui.text("Ui.Page.Terminology.Locale")}</Field.Label><Input id="term-locale" bind:value={termLocale} placeholder={ui.text("Ui.Page.Terminology.LocalePlaceholder")} /></Field.Field>
      <Field.Field><Field.Label for="term-note">{ui.text("Ui.Page.Terminology.Note")}</Field.Label><Input id="term-note" bind:value={termNote} placeholder={ui.text("Ui.Page.Terminology.NotePlaceholder")} /></Field.Field>
      <Button class="justify-self-start sm:col-span-2" variant="outline" disabled={reviewMutationBlocked || termSource.trim() === "" || termPreferred.trim() === ""} onclick={addTerm}>
        <PlusIcon data-icon="inline-start" />{ui.text("Ui.Page.Terminology.AddTerm")}
      </Button>
    </Field.FieldGroup>
    <div class="mt-5 grid overflow-hidden rounded-xl border">
      {#each terminology as term, index (term)}
        <div class="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-3 border-b px-4 py-3 last:border-b-0">
          <div class="min-w-0">
            <div class="flex min-w-0 flex-wrap items-center gap-2"><strong>{term.source}</strong><span class="text-muted-foreground">→</span><strong>{term.preferred}</strong>{#if term.locale}<Badge variant="outline">{term.locale}</Badge>{/if}</div>
            <p class="truncate text-xs text-muted-foreground">{term.note ?? ui.text("Ui.Page.Terminology.NoNote")}</p>
          </div>
          <Button variant="ghost" size="icon-xs" aria-label={`${ui.text("Ui.Page.Terminology.RemoveTerm")} ${term.source}`} onclick={() => removeTerm(index)}><Trash2Icon /></Button>
        </div>
      {:else}
        <p class="p-6 text-center text-sm text-muted-foreground">{ui.text("Ui.Page.Terminology.Empty")}</p>
      {/each}
    </div>
    </div>
    {#snippet footer()}
      <Button variant="outline" onclick={() => terminologyDialogOpen = false}>{ui.text("Ui.Page.Done")}</Button>
      <Button disabled={!reviewDirty || reviewMutationBlocked} onclick={() => void saveReview()}>
        {#if reviewSaving}<Spinner data-icon="inline-start" />{/if}{ui.text("Ui.Page.Terminology.SaveWorkflow")}
      </Button>
    {/snippet}
  </AppDialog>
{/if}

{#if reportDialogOpen}
  <AppDialog
    open
    title={`${selectedLocale} ${ui.text("Ui.Page.Quality.Report")}`}
    description={`${localeQualityFindings.length} ${ui.text("Ui.Page.Quality.FindingsAcross")} ${qualityKeySet.size} ${ui.text("Ui.Page.Messages")}. ${ui.text("Ui.Page.Quality.CsvOrder")}`}
    class="sm:max-w-4xl"
    onopenchange={(open) => reportDialogOpen = open}
  >
    <Textarea class="min-h-[26rem] font-mono text-xs" aria-label={ui.text("Ui.Page.Quality.CsvAriaLabel")} readonly value={qualityReportCsv(localeQualityFindings)} />
    {#snippet footer()}<Button variant="outline" onclick={() => reportDialogOpen = false}>{ui.text("Ui.Page.Close")}</Button>{/snippet}
  </AppDialog>
{/if}

<InterchangeDialog
  bind:open={interchangeOpen}
  busy={interchangeBusy}
  bind:xliffDirectory
  bind:xliffImportPath
  bind:reviewPath={reviewExportPath}
  bind:reviewImportPath
  {xliffExport}
  {xliffPreview}
  {reviewExport}
  {reviewPreview}
  onexportxliff={() => void exportXliff()}
  onpreviewxliff={() => void previewXliffImport()}
  onapplyxliff={() => void applyXliffImport()}
  onexportreview={() => void exportReviewJson()}
  onpreviewreview={() => void previewReviewJsonImport()}
  onapplyreview={() => void applyReviewJsonImport()}
/>

{#if repairDocument !== undefined}
  <AppDialog
    open
    title={repairDocument.path}
    description={ui.text("Ui.Page.Repair.Description")}
    class="sm:max-w-4xl"
    showCloseButton={!repairBusy}
    onopenchange={(open) => { if (!open && !repairBusy) closeRepair(); }}
  >
    <Textarea class="min-h-[26rem] font-mono text-xs" aria-label={ui.text("Ui.Page.Repair.DocumentAriaLabel")} bind:value={repairText} spellcheck={false} disabled={repairBusy || editorMutationBlocked} />
    {#if repairMessage}<Alert.Root variant="destructive" class="mt-4"><Alert.Title>{ui.text("Ui.Page.Repair.Failed")}</Alert.Title><Alert.Description>{repairMessage}</Alert.Description></Alert.Root>{/if}
    {#snippet footer()}
      <Button variant="outline" disabled={repairBusy} onclick={closeRepair}>{ui.text("Ui.Page.Cancel")}</Button>
      <Button disabled={repairBusy} onclick={() => void saveRepair()}>{#if repairBusy}<Spinner data-icon="inline-start" />{/if}{repairBusy ? ui.text("Ui.Page.Validating") : ui.text("Ui.Page.Repair.ValidateSave")}</Button>
    {/snippet}
  </AppDialog>
{/if}

{#if openDialogOpen}
  <AppDialog
    open
    title={ui.text("Ui.Page.OpenProject.Title")}
    description={ui.text("Ui.Page.OpenProject.Description")}
    showCloseButton={!openingWorkspace && !pickingWorkspace}
    onopenchange={(open) => { if (!openingWorkspace && !pickingWorkspace) openDialogOpen = open; }}
  >
    <Field.Field>
      <Field.Label for="dialog-open-directory">{ui.text("Ui.Page.WorkspaceDirectory")}</Field.Label>
      <div class="flex flex-col gap-2 sm:flex-row">
        <Input id="dialog-open-directory" class="min-w-0 flex-1" bind:value={openDirectory} autocomplete="off" />
        <Button variant="outline" disabled={pickingWorkspace || openingWorkspace} onclick={() => void pickWorkspace()}>{pickingWorkspace ? ui.text("Ui.Page.Choosing") : ui.text("Ui.Page.Browse")}</Button>
      </div>
    </Field.Field>
    {#if clientError}<Alert.Root variant="destructive" class="mt-4"><Alert.Title>{ui.text("Ui.Page.Fatal.CouldNotOpen")}</Alert.Title><Alert.Description>{clientError}</Alert.Description></Alert.Root>{/if}
    {#snippet footer()}
      <Button variant="outline" disabled={openingWorkspace} onclick={() => openDialogOpen = false}>{ui.text("Ui.Page.Cancel")}</Button>
      <Button disabled={openingWorkspace || openDirectory.trim() === ""} onclick={() => void openWorkspace()}>{#if openingWorkspace}<Spinner data-icon="inline-start" />{/if}{openingWorkspace ? ui.text("Ui.Page.Opening") : ui.text("Ui.Page.OpenProject.Action")}</Button>
    {/snippet}
  </AppDialog>
{/if}

{#if mutationDialogOpen && snapshot?.catalog !== undefined}
  <AppDialog
    open
    title={mutationTitle(mutationKind)}
    description={ui.text("Ui.Page.Mutation.Description")}
    class="sm:max-w-3xl"
    showCloseButton={!mutationBusy}
    onopenchange={(open) => { if (!mutationBusy) mutationDialogOpen = open; }}
  >
    <Field.FieldGroup class="gap-4">
      {#if mutationKind === "add-locale" || mutationKind === "remove-locale" || mutationKind === "set-fallback"}
        <Field.Field>
          <Field.Label for="language-operation">{ui.text("Ui.Page.Mutation.LanguageOperation")}</Field.Label>
          <Select.Root type="single" value={mutationKind} onValueChange={changeMutationKind}>
            <Select.Trigger id="language-operation" class="w-full">{mutationTitle(mutationKind)}</Select.Trigger>
            <Select.Content><Select.Group><Select.Label>{ui.text("Ui.Page.Mutation.LanguageOperation")}</Select.Label>
              <Select.Item value="add-locale" label={ui.text("Ui.Page.Mutation.AddLanguage")}>{ui.text("Ui.Page.Mutation.AddLanguage")}</Select.Item>
              <Select.Item value="remove-locale" label={ui.text("Ui.Page.Mutation.RemoveLanguage")}>{ui.text("Ui.Page.Mutation.RemoveLanguage")}</Select.Item>
              <Select.Item value="set-fallback" label={ui.text("Ui.Page.Mutation.ChangeFallback")}>{ui.text("Ui.Page.Mutation.ChangeFallback")}</Select.Item>
            </Select.Group></Select.Content>
          </Select.Root>
        </Field.Field>
      {/if}

      {#if mutationKind === "add-locale"}
        <div class="grid gap-4 sm:grid-cols-2">
          <Field.Field><Field.Label for="mutation-locale">{ui.text("Ui.Page.Mutation.NewLocaleTag")}</Field.Label><Input id="mutation-locale" bind:value={mutationLocale} oninput={invalidateMutationPreview} placeholder="fr-FR" autocomplete="off" /></Field.Field>
          <Field.Field><Field.Label for="mutation-fallback">{ui.text("Ui.Page.Mutation.Fallback")}</Field.Label>
            <Select.Root type="single" value={mutationFallback} onValueChange={(value) => { mutationFallback = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="mutation-fallback" class="w-full">{mutationFallback} · {localeName(mutationFallback)}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.locales as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
          <Field.Field><Field.Label for="mutation-copy-from">{ui.text("Ui.Page.Mutation.CopyStarterValues")}</Field.Label>
            <Select.Root type="single" value={mutationCopyFrom} onValueChange={(value) => { mutationCopyFrom = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="mutation-copy-from" class="w-full">{mutationCopyFrom} · {localeName(mutationCopyFrom)}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.locales as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
            <Field.Description>{ui.text("Ui.Page.Mutation.CopyStarterValuesDescription")}</Field.Description>
          </Field.Field>
          <Field.Field><Field.Label for="mutation-layer">{ui.text("Ui.Page.Mutation.Layer")}</Field.Label>
            <Select.Root type="single" value={mutationLayer} onValueChange={(value) => { mutationLayer = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="mutation-layer" class="w-full">{mutationLayer}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.layers as layer (layer.name)}<Select.Item value={layer.name} label={layer.name}>{layer.name}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
        </div>
      {:else if mutationKind === "remove-locale"}
        <div class="grid gap-4 sm:grid-cols-2">
          <Field.Field><Field.Label for="remove-locale">{ui.text("Ui.Page.Mutation.LanguageToRemove")}</Field.Label>
            <Select.Root type="single" value={mutationLocale} onValueChange={(value) => { mutationLocale = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="remove-locale" class="w-full">{mutationLocale} · {localeName(mutationLocale)}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.locales.filter((locale) => locale.tag !== snapshot?.catalog?.defaultLocale) as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
          <Field.Field><Field.Label for="replacement-fallback">{ui.text("Ui.Page.Mutation.RedirectFallbacks")}</Field.Label>
            <Select.Root type="single" value={mutationReplacementFallback} onValueChange={(value) => { mutationReplacementFallback = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="replacement-fallback" class="w-full">{mutationReplacementFallback}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.locales.filter((locale) => locale.tag !== mutationLocale) as locale (locale.tag)}<Select.Item value={locale.tag} label={locale.tag}>{locale.tag}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
        </div>
        <Alert.Root variant="destructive"><Alert.Title>{ui.text("Ui.Page.Mutation.FilesDeleted")}</Alert.Title><Alert.Description>{ui.text("Ui.Page.Mutation.FilesDeletedDescription")}</Alert.Description></Alert.Root>
      {:else if mutationKind === "set-fallback"}
        <div class="grid gap-4 sm:grid-cols-2">
          <Field.Field><Field.Label for="fallback-locale">{ui.text("Ui.Page.Mutation.Language")}</Field.Label>
            <Select.Root type="single" value={mutationLocale} onValueChange={(value) => { mutationLocale = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="fallback-locale" class="w-full">{mutationLocale} · {localeName(mutationLocale)}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.locales.filter((locale) => locale.tag !== snapshot?.catalog?.defaultLocale) as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
          <Field.Field><Field.Label for="fallback-target">{ui.text("Ui.Page.Mutation.Fallback")}</Field.Label>
            <Select.Root type="single" value={mutationFallback} onValueChange={(value) => { mutationFallback = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="fallback-target" class="w-full">{mutationFallback} · {localeName(mutationFallback)}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.locales.filter((locale) => locale.tag !== mutationLocale) as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
        </div>
        <div class="flex flex-wrap gap-2">{#each snapshot.catalog.locales as locale (locale.tag)}<Badge variant="outline"><strong>{locale.tag}</strong>{locale.tag === mutationLocale ? ` → ${mutationFallback}` : locale.fallback ? ` → ${locale.fallback}` : ` · ${ui.text("Ui.Page.ProjectWizard.Source")}`}</Badge>{/each}</div>
      {:else if mutationKind === "create-key"}
        <Field.Field><Field.Label for="mutation-target-key">{ui.text("Ui.Page.Mutation.MessageKey")}</Field.Label><Input id="mutation-target-key" bind:value={mutationTargetKey} oninput={invalidateMutationPreview} placeholder="Checkout.Actions.Pay" autocomplete="off" /><Field.Description>{ui.text("Ui.Page.Mutation.MessageKeyDescription")}</Field.Description></Field.Field>
        <Field.Field><Field.Label for="mutation-initial-value">{ui.text("Ui.Page.Mutation.InitialText")}</Field.Label><Textarea id="mutation-initial-value" class="min-h-28" bind:value={mutationInitialValue} oninput={invalidateMutationPreview} placeholder={ui.text("Ui.Page.Mutation.InitialTextPlaceholder")} /><Field.Description>{ui.text("Ui.Page.Mutation.InitialTextDescription")}</Field.Description></Field.Field>
        <Field.Field><Field.Label for="message-layer">{ui.text("Ui.Page.Mutation.Layer")}</Field.Label>
          <Select.Root type="single" value={mutationLayer} onValueChange={(value) => { mutationLayer = value; invalidateMutationPreview(); }}><Select.Trigger id="message-layer" class="w-full">{mutationLayer}</Select.Trigger><Select.Content><Select.Group>{#each snapshot.catalog.layers as layer (layer.name)}<Select.Item value={layer.name} label={layer.name}>{layer.name}</Select.Item>{/each}</Select.Group></Select.Content></Select.Root>
        </Field.Field>
      {:else if mutationKind === "rename-key" || mutationKind === "duplicate-key"}
        <Field.Field><Field.Label for="mutation-source-key">{ui.text("Ui.Page.Mutation.ExistingKey")}</Field.Label><Input id="mutation-source-key" value={mutationSourceKey} readonly /></Field.Field>
        <Field.Field><Field.Label for="mutation-new-key">{mutationKind === "rename-key" ? ui.text("Ui.Page.Mutation.NewKeyGroupPath") : ui.text("Ui.Page.Mutation.DuplicateKey")}</Field.Label><Input id="mutation-new-key" bind:value={mutationTargetKey} oninput={invalidateMutationPreview} autocomplete="off" /><Field.Description>{ui.text("Ui.Page.Mutation.RenameDescription")}</Field.Description></Field.Field>
      {:else}
        <Alert.Root variant="destructive"><Alert.Title>{ui.text("Ui.Page.Mutation.Delete")} {mutationSourceKey}?</Alert.Title><Alert.Description>{ui.text("Ui.Page.Mutation.DeleteDescription")}</Alert.Description></Alert.Root>
      {/if}
    </Field.FieldGroup>

    {#if mutationError}<Alert.Root variant="destructive" class="mt-4" aria-live="polite"><Alert.Title>{ui.text("Ui.Page.Mutation.Invalid")}</Alert.Title><Alert.Description>{mutationError}</Alert.Description></Alert.Root>{/if}
    {#if mutationPreview?.ok}
      <section class="mt-5 overflow-hidden rounded-xl border" aria-label={ui.text("Ui.Page.Mutation.OperationPreview")}>
        <header class="flex items-center justify-between gap-3 border-b px-4 py-3"><strong>{ui.text("Ui.Page.Mutation.OperationPreview")}</strong><Badge variant="secondary">{mutationPreview.files.length} {ui.text("Ui.Page.Mutation.Affected")} {mutationPreview.files.length === 1 ? ui.text("Ui.Page.File") : ui.text("Ui.Page.Files")}</Badge></header>
        {#each mutationPreview.files as file (file.path)}
          <div class="grid grid-cols-[auto_minmax(0,1fr)] items-center gap-3 border-b px-4 py-3 last:border-b-0 sm:grid-cols-[auto_minmax(0,1fr)_auto]"><Badge variant={file.kind === "delete" ? "destructive" : file.kind === "create" ? "default" : "secondary"}>{file.kind}</Badge><code class="truncate text-xs">{file.path}</code><small class="col-start-2 text-muted-foreground sm:col-start-auto">{file.beforeBytes.toLocaleString()} → {file.afterBytes.toLocaleString()} bytes</small></div>
        {/each}
      </section>
      {#if mutationPreview.requiresIrreversibleConfirmation}
        <Field.Field class="mt-4 rounded-lg border border-destructive/40 bg-destructive/5 p-4">
          <div class="flex items-start gap-3">
            <Checkbox id="confirm-irreversible" bind:checked={mutationIrreversibleConfirmed} />
            <div class="grid gap-1.5 leading-none">
              <Field.Label for="confirm-irreversible">{ui.text("Ui.Page.Mutation.IrreversibleConfirm")}</Field.Label>
              <Field.Description>{ui.text("Ui.Page.Mutation.IrreversibleDescription")}</Field.Description>
            </div>
          </div>
        </Field.Field>
      {/if}
    {/if}
    {#snippet footer()}
      <Button variant="outline" disabled={mutationBusy} onclick={() => mutationDialogOpen = false}>{ui.text("Ui.Page.Cancel")}</Button>
      {#if mutationPreview?.ok}
        <Button variant={mutationKind === "remove-locale" || mutationKind === "delete-key" ? "destructive" : "default"} disabled={mutationBusy || (mutationPreview.requiresIrreversibleConfirmation && !mutationIrreversibleConfirmed)} onclick={() => void applyMutation()}>{#if mutationBusy}<Spinner data-icon="inline-start" />{/if}{mutationBusy ? ui.text("Ui.Page.Mutation.Committing") : ui.text("Ui.Page.Mutation.Commit")}</Button>
      {:else}
        <Button disabled={mutationBusy} onclick={() => void previewMutation()}>{#if mutationBusy}<Spinner data-icon="inline-start" />{/if}{mutationBusy ? ui.text("Ui.Page.Mutation.Checking") : ui.text("Ui.Page.Mutation.Preview")}</Button>
      {/if}
    {/snippet}
  </AppDialog>
{/if}

{#if projectDialogOpen}
  <AppDialog
    open
    title={ui.text("Ui.Page.ProjectWizard.Title")}
    description={ui.text("Ui.Page.ProjectWizard.Description")}
    class="sm:max-w-3xl"
    showCloseButton={!projectBusy}
    onopenchange={(open) => { if (!open && !projectBusy) closeProjectWizard(); }}
  >
    <ol class="mb-6 grid grid-cols-2 gap-2 sm:grid-cols-4" aria-label={ui.text("Ui.Page.ProjectWizard.StepsAriaLabel")}>
      {#each [ui.text("Ui.Page.ProjectWizard.StepProject"), ui.text("Ui.Page.ProjectWizard.StepLanguages"), ui.text("Ui.Page.ProjectWizard.StepSettings"), ui.text("Ui.Page.ProjectWizard.StepReview")] as title, index (title)}
        <li class="flex items-center gap-2 text-sm" aria-current={projectStep === index + 1 ? "step" : undefined}>
          <Badge variant={projectStep === index + 1 ? "default" : projectStep > index + 1 ? "secondary" : "outline"}>{projectStep > index + 1 ? "✓" : index + 1}</Badge>
          <span class={projectStep === index + 1 ? "font-medium" : "text-muted-foreground"}>{title}</span>
        </li>
      {/each}
    </ol>

    {#if projectStep === 1}
      <div class="mb-5"><h3 class="font-medium">{ui.text("Ui.Page.ProjectWizard.WhereLive")}</h3><p class="text-sm text-muted-foreground">{ui.text("Ui.Page.ProjectWizard.NoOverwrite")}</p></div>
      <Field.FieldGroup>
        <Field.Field><Field.Label for="project-directory">{ui.text("Ui.Page.ProjectWizard.NewDirectory")}</Field.Label><Input id="project-directory" bind:value={projectDirectory} placeholder="/projects/customer-app/Resources" autocomplete="off" /><Field.Description>{ui.text("Ui.Page.ProjectWizard.DirectoryDescription")}</Field.Description></Field.Field>
        <Field.Field><Field.Label for="project-catalog">{ui.text("Ui.Page.ProjectWizard.CatalogId")}</Field.Label><Input id="project-catalog" bind:value={projectCatalog} placeholder="product" autocomplete="off" /><Field.Description>{ui.text("Ui.Page.ProjectWizard.CatalogIdDescription")}</Field.Description></Field.Field>
      </Field.FieldGroup>
    {:else if projectStep === 2}
      <div class="mb-5"><h3 class="font-medium">{ui.text("Ui.Page.ProjectWizard.WhichLanguages")}</h3><p class="text-sm text-muted-foreground">{ui.text("Ui.Page.ProjectWizard.LanguageDescription")}</p></div>
      <div class="grid gap-3">
        <div class="grid items-end gap-3 rounded-xl border p-4 sm:grid-cols-[1fr_auto]">
          <Field.Field><Field.Label for="project-default-locale">{ui.text("Ui.Page.ProjectWizard.SourceDefault")}</Field.Label><Input id="project-default-locale" bind:value={projectDefaultLocale} placeholder="de" autocomplete="off" /></Field.Field>
          <Badge variant="secondary" class="mb-2">{ui.text("Ui.Page.ProjectWizard.CanonicalSource")}</Badge>
        </div>
        {#each projectLocales as locale (locale.id)}
          <div class="grid items-end gap-3 rounded-xl border p-4 sm:grid-cols-[1fr_1fr_auto]">
            <Field.Field><Field.Label for={`project-locale-${locale.id}`}>{ui.text("Ui.Page.ProjectWizard.AdditionalLanguage")}</Field.Label><Input id={`project-locale-${locale.id}`} bind:value={locale.tag} placeholder="en" autocomplete="off" /></Field.Field>
            <Field.Field><Field.Label for={`project-fallback-${locale.id}`}>{ui.text("Ui.Page.Mutation.Fallback")}</Field.Label>
              <Select.Root type="single" value={locale.fallback} onValueChange={(value) => locale.fallback = value}>
                <Select.Trigger id={`project-fallback-${locale.id}`} class="w-full">{locale.fallback || `${ui.text("Ui.Page.ProjectWizard.Default")} (${projectDefaultLocale || ui.text("Ui.Page.ProjectWizard.Source")})`}</Select.Trigger>
                <Select.Content><Select.Group><Select.Item value="" label={`${ui.text("Ui.Page.ProjectWizard.Default")} (${projectDefaultLocale || ui.text("Ui.Page.ProjectWizard.Source")})`}>{ui.text("Ui.Page.ProjectWizard.Default")} ({projectDefaultLocale || ui.text("Ui.Page.ProjectWizard.Source")})</Select.Item>{#each projectLocales.filter((candidate) => candidate.id !== locale.id && candidate.tag.trim() !== "") as candidate (candidate.id)}<Select.Item value={candidate.tag} label={candidate.tag}>{candidate.tag}</Select.Item>{/each}</Select.Group></Select.Content>
              </Select.Root>
            </Field.Field>
            <Button variant="ghost" size="icon-sm" aria-label={`${ui.text("Ui.Page.ProjectWizard.RemoveLocale")} ${locale.tag || ui.text("Ui.Page.ProjectWizard.Row")}`} onclick={() => removeProjectLocale(locale.id)}><Trash2Icon /></Button>
          </div>
        {/each}
        <Button variant="outline" class="justify-self-start" onclick={addProjectLocale}><PlusIcon data-icon="inline-start" />{ui.text("Ui.Page.ProjectWizard.AddLanguage")}</Button>
      </div>
    {:else if projectStep === 3}
      <div class="mb-5"><h3 class="font-medium">{ui.text("Ui.Page.ProjectWizard.GeneratedApi")}</h3><p class="text-sm text-muted-foreground">{ui.text("Ui.Page.ProjectWizard.GeneratedApiDescription")}</p></div>
      <Field.FieldGroup>
        <div class="grid gap-4 sm:grid-cols-2">
          <Field.Field><Field.Label for="project-namespace">{ui.text("Ui.Page.ProjectWizard.CodeNamespace")}</Field.Label><Input id="project-namespace" bind:value={projectNamespace} autocomplete="off" /></Field.Field>
          <Field.Field><Field.Label for="project-class">{ui.text("Ui.Page.ProjectWizard.GeneratedClass")}</Field.Label><Input id="project-class" bind:value={projectClassName} autocomplete="off" /></Field.Field>
          <Field.Field><Field.Label for="project-layer">{ui.text("Ui.Page.ProjectWizard.InitialLayer")}</Field.Label><Input id="project-layer" bind:value={projectLayer} autocomplete="off" /></Field.Field>
        </div>
        <Field.Field orientation="horizontal"><Checkbox id="project-esm" bind:checked={projectGenerateEsm} /><Field.Content><Field.Label for="project-esm">{ui.text("Ui.Page.ProjectWizard.EnableEsm")}</Field.Label><Field.Description>{ui.text("Ui.Page.ProjectWizard.EnableEsmDescription")}</Field.Description></Field.Content></Field.Field>
        <Field.Field orientation="horizontal"><Checkbox id="project-starter" bind:checked={projectIncludeStarter} /><Field.Content><Field.Label for="project-starter">{ui.text("Ui.Page.ProjectWizard.AddStarter")}</Field.Label><Field.Description>{ui.text("Ui.Page.ProjectWizard.AddStarterDescription")}</Field.Description></Field.Content></Field.Field>
      </Field.FieldGroup>
    {:else if projectStep === 4 && projectPlan !== undefined}
      <Alert.Root><Alert.Title>{ui.text("Ui.Page.ProjectWizard.ReadyCreate")} {projectPlan.catalogId}</Alert.Title><Alert.Description>{projectPlan.locales.length} {projectPlan.locales.length === 1 ? ui.text("Ui.Page.ProjectWizard.Language") : ui.text("Ui.Page.ProjectWizard.Languages")} · {projectPlan.files.length} {ui.text("Ui.Page.Files")} · {ui.text("Ui.Page.ProjectWizard.CompilerValidated")}</Alert.Description></Alert.Root>
      <dl class="mt-4 grid gap-3 rounded-xl border p-4"><div class="grid gap-1 sm:grid-cols-[7rem_1fr]"><dt class="text-muted-foreground">{ui.text("Ui.Page.ProjectWizard.Directory")}</dt><dd class="m-0 truncate font-mono text-xs">{projectPlan.directory}</dd></div><div class="grid gap-1 sm:grid-cols-[7rem_1fr]"><dt class="text-muted-foreground">{ui.text("Ui.Page.ProjectWizard.Languages")}</dt><dd class="m-0 font-medium">{projectPlan.locales.map((locale) => locale.tag).join(", ")}</dd></div></dl>
      <section class="mt-4 overflow-hidden rounded-xl border" aria-label={ui.text("Ui.Page.ProjectWizard.FilesToCreate")}><h4 class="border-b px-4 py-3 font-medium">{ui.text("Ui.Page.ProjectWizard.FilesToCreate")}</h4>{#each projectPlan.files as file (file)}<div class="border-b px-4 py-3 last:border-b-0"><code class="text-xs">{file}</code></div>{/each}</section>
    {/if}

    {#if projectError}<Alert.Root variant="destructive" class="mt-4" aria-live="polite"><Alert.Title>{ui.text("Ui.Page.ProjectWizard.Invalid")}</Alert.Title><Alert.Description>{projectError}</Alert.Description></Alert.Root>{/if}
    {#snippet footer()}
      <Button variant="outline" disabled={projectBusy} onclick={closeProjectWizard}>{ui.text("Ui.Page.Cancel")}</Button>
      {#if projectStep > 1}<Button variant="ghost" disabled={projectBusy} onclick={() => { projectStep -= 1; projectError = undefined; }}>{ui.text("Ui.Page.Back")}</Button>{/if}
      {#if projectStep < 4}
        <Button disabled={projectBusy} onclick={() => void advanceProjectWizard()}>{#if projectBusy}<Spinner data-icon="inline-start" />{/if}{projectBusy ? ui.text("Ui.Page.Validating") : ui.text("Ui.Page.Continue")}</Button>
      {:else}
        <Button disabled={projectBusy || projectPlan?.ok !== true} onclick={() => void createProject()}>{#if projectBusy}<Spinner data-icon="inline-start" />{/if}{projectBusy ? ui.text("Ui.Page.Creating") : ui.text("Ui.Page.ProjectWizard.CreateProject")}</Button>
      {/if}
    {/snippet}
  </AppDialog>
{/if}

<CommandPalette open={commandPaletteOpen} commands={paletteCommands} onopenchange={(open) => commandPaletteOpen = open} />

<style>
  :global(*) { box-sizing: border-box; }
  :global(:root) {
    font-family: Inter, "Segoe UI", system-ui, sans-serif;
    color: var(--foreground);
    background: var(--background);
    font-synthesis: none;
  }
  :global(body) { margin: 0; min-width: 0; min-height: 100vh; overflow: hidden; }
  :global(button), :global(input), :global(textarea), :global(select) { font: inherit; }
  :global(button), :global(select) { color: inherit; }
  :global(button:focus-visible), :global(input:focus-visible), :global(textarea:focus-visible), :global(select:focus-visible) { outline: 2px solid var(--ring); outline-offset: 2px; }
  :global(::selection) { color: var(--primary-foreground); background: var(--primary); }

  .eyebrow { margin: 0; color: var(--primary); font-size: .64rem; font-weight: 700; letter-spacing: .15em; text-transform: uppercase; }
  .mark { width: 4rem; height: 4rem; border-radius: 1rem; background: url("/brand/icon.png") center / cover no-repeat; box-shadow: 0 1rem 3rem color-mix(in oklch, var(--foreground) 22%, transparent); }
  .mark span { display: none; }
  .mark.small { width: 2.25rem; height: 2.25rem; border-radius: .65rem; box-shadow: 0 .35rem 1rem color-mix(in oklch, var(--foreground) 16%, transparent); }

  .status-dot { width: .5rem; height: .5rem; border-radius: 50%; background: var(--chart-2); box-shadow: 0 0 .6rem color-mix(in oklch, var(--chart-2) 55%, transparent); }
  .status-dot.warning { background: var(--primary); box-shadow: 0 0 .6rem color-mix(in oklch, var(--primary) 55%, transparent); }
  .primary { border: 1px solid var(--primary); border-radius: .45rem; padding: .58rem .85rem; color: var(--primary-foreground); background: var(--primary); font-weight: 750; cursor: pointer; box-shadow: 0 .35rem 1rem color-mix(in oklch, var(--foreground) 16%, transparent); }
  .primary:hover:not(:disabled) { filter: brightness(1.08); }
  .primary:disabled { cursor: not-allowed; filter: grayscale(.5); opacity: .42; }

  .editor-content { flex: 1; min-width: 0; min-height: 0; padding: 1.25rem 1rem 2rem; overflow-x: hidden; overflow-y: auto; scrollbar-color: var(--border) transparent; }
  .message-preview { max-width: 1000px; margin: 1.2rem auto 0; border: 1px solid var(--border); border-radius: .65rem; color: var(--card-foreground); background: color-mix(in oklch, var(--card) 94%, var(--primary)); overflow: hidden; }
  .message-preview > header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: .75rem .9rem; background: color-mix(in oklch, var(--muted) 82%, var(--primary)); }
  .message-preview > header > div { display: grid; gap: .15rem; }
  .message-preview header strong { color: var(--foreground); font-size: .68rem; }
  .message-preview header span { color: var(--muted-foreground); font-size: .56rem; }
  .preview-state { border: 1px solid color-mix(in oklch, var(--primary) 45%, var(--border)); border-radius: 1rem; padding: .23rem .5rem; color: var(--primary) !important; background: color-mix(in oklch, var(--primary) 12%, transparent); font: .53rem ui-monospace, monospace !important; }
  .sample-inputs { display: grid; grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr)); gap: .55rem; border-top: 1px solid var(--border); padding: .7rem .9rem; }
  .sample-inputs label { display: grid; gap: .3rem; color: var(--muted-foreground); font-size: .58rem; }
  .sample-inputs label > span { display: flex; justify-content: space-between; }
  .sample-inputs small { color: var(--muted-foreground); font: .52rem ui-monospace, monospace; }
  .sample-inputs input { min-width: 0; border: 1px solid var(--input); border-radius: .35rem; padding: .46rem .5rem; color: var(--foreground); background: var(--background); font: .62rem ui-monospace, monospace; }
  .preview-canvas { min-height: 5rem; border-top: 1px solid var(--border); padding: 1rem; color: var(--foreground); background: radial-gradient(circle at 90% 0, color-mix(in oklch, var(--primary) 13%, transparent), transparent 45%), var(--background); font-size: .9rem; line-height: 1.7; }
  .preview-canvas p { margin: 0; white-space: pre-wrap; }
  .preview-placeholder { color: var(--muted-foreground); font-size: .65rem; }
  .preview-error { color: var(--destructive); font-size: .65rem; }
  .safe-content, .preview-children { display: inline-flex; flex-wrap: wrap; align-items: baseline; gap: .2rem; }
  .preview-element { display: inline-flex; flex-wrap: wrap; align-items: baseline; gap: .25rem; border: 1px solid color-mix(in oklch, var(--primary) 40%, var(--border)); border-radius: .35rem; padding: .24rem .35rem; background: color-mix(in oklch, var(--primary) 10%, var(--card)); }
  .preview-element-label { color: var(--primary); font: .52rem ui-monospace, monospace; }
  .preview-attributes { color: var(--muted-foreground); font: .48rem ui-monospace, monospace; }
  .safe-note { margin: 0; border-top: 1px solid var(--border); padding: .55rem .9rem; color: var(--muted-foreground); font-size: .55rem; }
  .loading-shell, .fatal-shell, .recovery-shell { display: grid; place-content: center; place-items: center; height: 100vh; padding: 2rem; color: var(--muted-foreground); text-align: center; background: radial-gradient(circle at center, color-mix(in oklch, var(--primary) 10%, var(--background)), var(--background) 65%); }
  .loading-shell { gap: 1.5rem; }
  .loading-shell p { margin: 0; color: var(--primary); font-size: .65rem; font-weight: 700; letter-spacing: .18em; text-transform: uppercase; }
  .loading-line { width: 12rem; height: 1px; background: linear-gradient(90deg, transparent, var(--primary), transparent); animation: pulse .8s infinite alternate; }
  .fatal-shell { max-width: none; }
  .fatal-shell .mark { margin-bottom: 2rem; }
  .fatal-shell h1 { max-width: 36rem; margin: .8rem 0; color: var(--foreground); font-family: Georgia, serif; font-size: 2.6rem; font-weight: 500; }
  .fatal-shell > p:not(.eyebrow) { max-width: 44rem; margin: 0 0 1.5rem; }
  .recovery-shell { gap: .9rem; }
  .recovery-shell h1 { max-width: 42rem; margin: .4rem 0; color: var(--foreground); font-family: Georgia, serif; font-size: 2.3rem; font-weight: 500; }
  .recovery-shell > p:not(.eyebrow) { max-width: 44rem; margin: 0; line-height: 1.6; }
  .recovery-paths { display: grid; gap: .3rem; width: min(36rem, 100%); max-height: 12rem; margin: .5rem 0; overflow-y: auto; }
  .recovery-paths code { border: 1px solid var(--border); border-radius: .3rem; padding: .45rem .6rem; color: var(--foreground); text-align: left; background: var(--card); font: .6rem ui-monospace, monospace; }
  .recovery-actions { display: flex; gap: .7rem; }
  .recovery-shell > small { color: var(--muted-foreground); font-size: .58rem; }
  .welcome-shell { width: 100vw; min-height: 100vh; overflow-y: auto; background: radial-gradient(circle at 70% -10%, color-mix(in oklch, var(--primary) 12%, transparent), transparent 42%), var(--background); }
  .welcome-brand { display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: .8rem; border-bottom: 1px solid var(--border); padding: 1rem 1.5rem; background: color-mix(in oklch, var(--card) 94%, transparent); }
  .welcome-brand h1 { margin: .1rem 0 0; color: var(--foreground); font-family: Georgia, serif; font-size: 1.35rem; font-weight: 500; }
  .welcome-brand select { border: 1px solid var(--input); border-radius: .4rem; padding: .35rem .5rem; background: var(--secondary); }
  .welcome-content { width: min(880px, calc(100% - 3rem)); margin: 0 auto; padding: 4rem 0; }
  .welcome-heading { margin-bottom: 1.6rem; }
  .welcome-heading h2 { margin: .55rem 0; color: var(--foreground); font-family: Georgia, serif; font-size: clamp(2rem, 4vw, 3.2rem); font-weight: 500; letter-spacing: -.035em; }
  .welcome-heading > p:last-child { max-width: 42rem; margin: 0; color: var(--muted-foreground); font-size: .78rem; line-height: 1.6; }
  .catalog-choices { display: grid; gap: .65rem; margin: 1.4rem 0; }
  .catalog-choice { display: grid; grid-template-columns: auto minmax(0, 1fr) auto auto; align-items: center; gap: .9rem; width: 100%; border: 1px solid var(--border); border-radius: .65rem; padding: 1rem; color: var(--card-foreground); text-align: left; background: var(--card); cursor: pointer; }
  .catalog-choice:hover { border-color: var(--ring); background: var(--accent); }
  .catalog-choice > span:nth-child(2) { display: grid; gap: .2rem; min-width: 0; }
  .catalog-choice strong { color: var(--foreground); font-size: .82rem; }
  .catalog-choice small { overflow: hidden; color: var(--muted-foreground); font: .58rem ui-monospace, monospace; text-overflow: ellipsis; white-space: nowrap; }
  .catalog-metrics { color: var(--muted-foreground); font-size: .6rem; line-height: 1.55; text-align: right; }
  .health { min-width: 4.5rem; border-radius: 1rem; padding: .25rem .45rem; color: var(--chart-2); background: color-mix(in oklch, var(--chart-2) 13%, transparent); font-size: .56rem; text-align: center; }
  .health.error { color: var(--destructive); background: color-mix(in oklch, var(--destructive) 13%, transparent); }
  .open-workspace-card { display: grid; gap: .45rem; margin-top: 1.2rem; border: 1px solid var(--border); border-radius: .65rem; padding: 1rem; background: var(--card); }
  .open-workspace-card label { color: var(--foreground); font-size: .66rem; font-weight: 650; }
  .open-workspace-card > div { display: grid; grid-template-columns: 1fr auto auto; gap: .6rem; }
  .open-workspace-card input { min-width: 0; border: 1px solid var(--input); border-radius: .45rem; outline: 0; padding: .68rem .75rem; color: var(--foreground); background: var(--background); font-size: .75rem; }
  .open-workspace-card input:focus { border-color: var(--ring); }
  .open-workspace-card small { color: var(--muted-foreground); font-size: .58rem; }
  .welcome-actions { display: flex; gap: .65rem; margin-top: .8rem; }
  .repair-list { margin-top: 1.5rem; border: 1px solid color-mix(in oklch, var(--destructive) 42%, var(--border)); border-radius: .65rem; background: color-mix(in oklch, var(--destructive) 5%, var(--card)); overflow: hidden; }
  .repair-list > header { padding: .8rem 1rem; background: color-mix(in oklch, var(--destructive) 10%, var(--card)); }
  .repair-list > header > div { display: flex; justify-content: space-between; }
  .repair-list strong { color: var(--destructive); font-size: .7rem; }
  .repair-list header span { color: color-mix(in oklch, var(--destructive) 70%, var(--muted-foreground)); font-size: .6rem; }
  .repair-list > button { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: .7rem; width: 100%; border: 0; border-top: 1px solid color-mix(in oklch, var(--destructive) 28%, var(--border)); padding: .7rem 1rem; text-align: left; background: transparent; cursor: pointer; }
  .repair-list > button:hover { background: color-mix(in oklch, var(--destructive) 9%, var(--card)); }
  .repair-list > button > span { display: grid; place-items: center; width: 1.3rem; height: 1.3rem; border-radius: 50%; color: var(--destructive); background: color-mix(in oklch, var(--destructive) 16%, transparent); font-weight: 800; }
  .repair-list code { color: var(--foreground); font: .62rem ui-monospace, monospace; }
  .repair-list small { color: var(--muted-foreground); font-size: .58rem; }
  .recent-projects { margin-top: 1.5rem; border: 1px solid var(--border); border-radius: .65rem; background: var(--card); overflow: hidden; }
  .recent-projects > header { display: flex; justify-content: space-between; padding: .75rem 1rem; background: var(--muted); }
  .recent-projects header strong { color: var(--foreground); font-size: .68rem; }
  .recent-projects header span { color: var(--muted-foreground); font-size: .56rem; }
  .recent-projects > button { display: grid; grid-template-columns: 1fr auto; align-items: center; width: 100%; border: 0; border-top: 1px solid var(--border); padding: .7rem 1rem; color: var(--foreground); text-align: left; background: transparent; cursor: pointer; }
  .recent-projects > button:hover { background: var(--accent); }
  .recent-projects button > span { display: grid; gap: .2rem; min-width: 0; }
  .recent-projects code { overflow: hidden; color: var(--muted-foreground); font: .57rem ui-monospace, monospace; text-overflow: ellipsis; white-space: nowrap; }
  .recent-projects small { color: var(--muted-foreground); font-size: .55rem; }
  .secondary { border: 1px solid var(--border); border-radius: .45rem; padding: .58rem .85rem; color: var(--secondary-foreground); background: var(--secondary); cursor: pointer; }
  .secondary:hover:not(:disabled) { border-color: var(--ring); color: var(--accent-foreground); background: var(--accent); }
  .project-error { margin: 1rem 0 0; border: 1px solid color-mix(in oklch, var(--destructive) 45%, var(--border)); border-radius: .45rem; padding: .7rem .8rem; color: var(--destructive); background: color-mix(in oklch, var(--destructive) 10%, transparent); font-size: .67rem; }
  @keyframes pulse { to { opacity: .35; } }
  @media (min-width: 640px) { .editor-content { padding: 1.5rem; } }
  @media (min-width: 1100px) { .editor-content { padding: 2rem clamp(2rem, 5vw, 5rem) 3rem; } }
</style>
