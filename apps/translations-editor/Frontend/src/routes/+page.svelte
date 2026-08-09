<script lang="ts">
  import { onMount } from "svelte";
  import {
    m$App$Advanced,
    m$App$All,
    m$App$DefaultLocale,
    m$App$Diagnostics,
    m$App$Eyebrow,
    m$App$Invalid,
    m$App$Missing,
    m$App$NoResults,
    m$App$NoSelection,
    m$App$Raw,
    m$App$Reload,
    m$App$Save,
    m$App$Saved,
    m$App$Saving,
    m$App$Search,
    m$App$Simple,
    m$App$Structured,
    m$App$Title,
    m$App$Unsaved,
    m$App$Valid,
    m$App$Workspace,
  } from "virtual:runic-text-resources/editor";
  import type {
    EditorAbout,
    EditorDiagnostic,
    EditorDocument,
    EditorExternalFileChange,
    EditorMutationPreview,
    EditorMutationRequest,
    EditorProjectCreationRequest,
    EditorProjectPlan,
    EditorReviewEntry,
    EditorReviewState,
    EditorTerminologyEntry,
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
  import { createEditorBridge } from "$lib/editor-bridge";
  import EditorModeSwitcher, { type EditorMode } from "$lib/EditorModeSwitcher.svelte";
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
    buildRows,
    coverage,
    formatJson,
    preview,
    updateResourceValue,
    type ResourceValue,
    type TranslationRow,
  } from "$lib/resource-model";
  import {
    effectiveReviewState,
    isStale,
    qualityIssues,
    qualityReportCsv,
    reviewIdentity,
    reviewMap,
    sourceFingerprint,
    translationSuggestions,
  } from "$lib/review-model";

  type ProjectLocaleDraft = { id: number; tag: string; fallback: string };
  type StoredDraft = { content: string; baseRevision: string };
  type RecentProject = { root: string; catalogId: string; openedAt: string };
  type MutationKind = EditorMutationRequest["kind"];
  type MessagePreviewResult = ReturnType<typeof executeMessagePreview>;
  type PreviewNode = Extract<MessagePreviewResult, { kind: "content" }>["nodes"][number];

  const bridge = createEditorBridge();
  let snapshot = $state.raw<WorkspaceSnapshot>();
  let drafts = $state<Record<string, string>>({});
  let selectedKey = $state("");
  let selectedLocale = $state("");
  let selectedDocumentPath = $state("");
  let filter = $state<MessageFilter>("all");
  let query = $state("");
  let mode = $state<EditorMode>("translation");
  let editorText = $state("");
  let uiLocale = $state("en");
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
  let recoveryBusy = $state(false);
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
  let rowLimit = $state(300);
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
  let languagesOpen = $state(true);
  let messagesOpen = $state(true);

  let labels = $derived(labelsFor(uiLocale));
  let rows = $derived(buildRows(snapshot, drafts));
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
  let qualityKeySet = $derived(new Set(localeQuality.map((issue) => issue.key)));
  let filterOptions = $derived([
    { value: "all" as const, label: labels.all, count: rows.length },
    { value: "missing" as const, label: labels.missing, count: rows.filter((row) => row.cells[selectedLocale]?.entry === undefined).length },
    { value: "structured" as const, label: labels.structured, count: rows.filter((row) => row.structured).length },
    { value: "needs-review" as const, label: "Review", count: rows.filter((row) => effectiveReviewState(reviewIndex.get(reviewIdentity(row.key, selectedLocale)), row.cells[selectedLocale]?.entry !== undefined) === "needs-review").length },
    { value: "stale" as const, label: "Stale", count: rows.filter((row) => isStale(reviewIndex.get(reviewIdentity(row.key, selectedLocale)), row.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry?.value)).length },
    { value: "quality" as const, label: "Quality", count: qualityKeySet.size },
  ]);
  let visibleRows = $derived.by(() => {
    const normalized = query.trim().toLocaleLowerCase();
    return rows.filter((row) => {
      const cell = row.cells[selectedLocale];
      if (filter === "missing" && cell?.entry !== undefined) return false;
      if (filter === "structured" && !row.structured) return false;
      const review = reviewIndex.get(reviewIdentity(row.key, selectedLocale));
      if (filter === "needs-review" && effectiveReviewState(review, cell?.entry !== undefined) !== "needs-review") return false;
      if (filter === "stale" && !isStale(review, row.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry?.value)) return false;
      if (filter === "quality" && !qualityKeySet.has(row.key)) return false;
      if (normalized.length === 0) return true;
      const searchable = [
        row.key,
        row.description ?? "",
        ...row.tags,
        ...Object.values(row.cells).map((candidate) => preview(candidate.entry)),
      ].join("\n").toLocaleLowerCase();
      return searchable.includes(normalized);
    });
  });
  let renderedRows = $derived(visibleRows.slice(0, rowLimit));
  let messageListItems = $derived.by((): MessageListItem[] => renderedRows.map((row) => {
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
  let currentQuality = $derived(localeQuality.filter((issue) => issue.key === selectedKey));
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

  onMount(() => {
    const appearance = readAppearance();
    themeMode = appearance.mode;
    themePalette = appearance.palette;
    applyAppearance(themeMode, themePalette);
    const colorScheme = window.matchMedia("(prefers-color-scheme: dark)");
    const updateSystemTheme = (): void => {
      if (themeMode === "system") applyAppearance(themeMode, themePalette);
    };
    colorScheme.addEventListener("change", updateSystemTheme);
    recentProjects = readRecentProjects();
    void loadWorkspace(false);
    const interval = window.setInterval(() => void checkExternalChanges(), 2_000);
    return () => {
      colorScheme.removeEventListener("change", updateSystemTheme);
      window.clearInterval(interval);
    };
  });

  function changeThemeMode(mode: ThemeMode): void {
    themeMode = mode;
    saveAppearance(themeMode, themePalette);
  }

  function changeThemePalette(palette: ThemePalette): void {
    themePalette = palette;
    saveAppearance(themeMode, themePalette);
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
    if (confirmDiscard && Object.keys(drafts).length > 0 && !confirm("Discard all unsaved changes?")) return;
    if (confirmDiscard) clearStoredDrafts(snapshot);
    loading = true;
    operationMessage = undefined;
    clientError = undefined;
    try {
      const next = await bridge.load();
      installSnapshot(next, true);
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
      recoveredDrafts = readStoredDrafts(next);
    }
    if (resetSelection || !reviewDirty) installReview(next);
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
      drafts[document.path] = value;
      persistDrafts();
      scheduleValidation(document.path, value);
    } catch (error) {
      clientError = errorMessage(error);
      validation = { success: false, diagnostics: [] };
    }
  }

  function editResourceValue(resourceValue: ResourceValue): void {
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
      drafts[document.path] = content;
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
    if (!reviewDirty || reviewSaving || snapshot?.review?.error !== undefined) return;
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
    terminology = terminology.filter((_, candidate) => candidate !== index);
    reviewDirty = true;
  }

  async function showAbout(): Promise<void> {
    aboutDialogOpen = true;
    diagnosticMessage = undefined;
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

  async function createDiagnosticBundle(): Promise<void> {
    if (diagnosticBusy) return;
    diagnosticBusy = true;
    diagnosticMessage = undefined;
    try {
      const result = await bridge.createDiagnosticBundle();
      diagnosticMessage = result.ok
        ? `Sanitized diagnostics saved to ${result.path ?? "the temporary diagnostics directory"}.`
        : result.message ?? "The diagnostic bundle could not be created.";
    } catch (error) {
      diagnosticMessage = errorMessage(error);
    } finally {
      diagnosticBusy = false;
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
    if (document === undefined || content === undefined || !isDirty || saving) return;
    saving = true;
    operationMessage = undefined;
    clientError = undefined;
    try {
      const checked = await bridge.validate(document.path, content);
      validation = checked;
      if (!checked.success) return;
      const result = await bridge.save(document.path, content, document.revision);
      if (!result.ok || result.snapshot === undefined) {
        if (result.validation !== undefined) validation = result.validation;
        clientError = result.message ?? `Save failed (${result.kind}).`;
        return;
      }
      const key = selectedKey;
      const locale = selectedLocale;
      delete drafts[document.path];
      persistDrafts();
      installSnapshot(result.snapshot, false);
      selectedKey = key;
      selectedLocale = locale;
      configureEditor();
      operationMessage = labels.saved;
    } catch (error) {
      clientError = errorMessage(error);
    } finally {
      saving = false;
    }
  }

  function handleKeyboard(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key.toLocaleLowerCase() === "s") {
      event.preventDefault();
      void save();
    }
    if ((event.ctrlKey || event.metaKey) && event.key.toLocaleLowerCase() === "k") {
      event.preventDefault();
      searchInput?.focus();
    }
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
    if (Object.keys(drafts).length === 0) return;
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
    if (projectPlan?.ok !== true || projectBusy) return;
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
    if (openingWorkspace) return;
    if (Object.keys(drafts).length > 0 && !confirm("Discard all unsaved changes?")) return;
    clearStoredDrafts(snapshot);
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
    if (pickingWorkspace || openingWorkspace) return;
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
    const current = snapshot;
    if (current === undefined) return;
    openDirectory = current.root;
    openDialogOpen = true;
  }

  function prepareMutation(kind: MutationKind): boolean {
    const current = snapshot;
    if (current?.catalog === undefined) return false;
    if (Object.keys(drafts).length > 0 && !confirm("Structural changes require a clean workspace. Discard unsaved drafts?")) return false;
    drafts = {};
    clearStoredDrafts(current);
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
    };
  }

  function invalidateMutationPreview(): void {
    mutationPreview = undefined;
    mutationError = undefined;
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
    if (mutationBusy) return;
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
    if (mutationBusy || mutationPreview?.ok !== true) return;
    mutationBusy = true;
    mutationError = undefined;
    try {
      const result = await bridge.applyMutation(mutationRequest());
      if (!result.ok || result.snapshot === undefined) {
        mutationError = result.message ?? "The workspace change could not be committed.";
        mutationPreview = undefined;
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
    if (recoveryBusy) return;
    recoveryBusy = true;
    clientError = undefined;
    try {
      const result = await bridge.recoverTransaction(mode);
      if (!result.ok || result.snapshot === undefined) {
        clientError = result.message ?? "Workspace recovery failed.";
        return;
      }
      installSnapshot(result.snapshot, true);
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
    if (change === undefined) return;
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
    return `runic-text-resources:drafts:1:${value.root}\n${value.catalog?.id ?? ""}`;
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
    localStorage.setItem(draftStorageKey(current), JSON.stringify({ version: 1, documents: stored }));
  }

  function readStoredDrafts(value: WorkspaceSnapshot): Record<string, StoredDraft> {
    try {
      const raw = localStorage.getItem(draftStorageKey(value));
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
    if (value !== undefined) localStorage.removeItem(draftStorageKey(value));
  }

  function readRecentProjects(): RecentProject[] {
    try {
      const value = JSON.parse(localStorage.getItem("runic-text-resources:recent:1") ?? "[]") as unknown;
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
    localStorage.setItem("runic-text-resources:recent:1", JSON.stringify(recentProjects));
  }

  function beginRepair(document: EditorDocument): void {
    repairDocument = document;
    repairText = document.content;
    repairMessage = undefined;
  }

  async function saveRepair(): Promise<void> {
    const document = repairDocument;
    if (document === undefined || repairBusy) return;
    repairBusy = true;
    repairMessage = undefined;
    try {
      const checked = await bridge.validate(document.path, repairText);
      if (!checked.success) {
        repairMessage = checked.diagnostics[0]?.message ?? "The document is still invalid.";
        return;
      }
      const result = await bridge.save(document.path, repairText, document.revision);
      if (!result.ok || result.snapshot === undefined) {
        repairMessage = result.message ?? "The repaired document could not be saved.";
        return;
      }
      repairDocument = undefined;
      installSnapshot(result.snapshot, true);
    } catch (error) {
      repairMessage = errorMessage(error);
    } finally {
      repairBusy = false;
    }
  }

  function mutationTitle(kind: MutationKind): string {
    return {
      "add-locale": "Add a language",
      "remove-locale": "Remove a language",
      "set-fallback": "Change fallback relationships",
      "create-key": "Add a message",
      "rename-key": "Rename or move a message",
      "duplicate-key": "Duplicate a message",
      "delete-key": "Delete a message",
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
    };
  }
</script>

<svelte:head>
  <title>{labels.title} · Runic Artifex</title>
  <meta name="description" content="A focused editor for Runic Text Resources" />
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
      <Alert.Title>Files changed outside the editor</Alert.Title>
      <Alert.Description class="min-w-0">
        <p class="truncate font-mono text-xs">{externalChanges.join(", ")}</p>
        <p>{Object.keys(drafts).length > 0 ? "Your local drafts are still intact." : "Reload to read the latest versions."}</p>
      </Alert.Description>
      <Alert.Action class="static col-span-full mt-2 flex flex-wrap justify-end gap-2">
        <Button variant="ghost" size="xs" onclick={() => { externalChanges = []; externalFileChanges = []; }}>Keep current view</Button>
        <Button variant="outline" size="xs" onclick={reviewExternalChanges}>Compare / merge</Button>
        <Button size="xs" onclick={() => void loadWorkspace(true)}>Reload files</Button>
      </Alert.Action>
    </Alert.Root>
  </div>
{/if}

{#if Object.keys(recoveredDrafts).length > 0}
  <div class="pointer-events-none fixed inset-x-2 bottom-2 z-50 mx-auto max-w-[calc(100vw-1rem)] sm:inset-x-4 sm:bottom-4 sm:max-w-2xl">
    <Alert.Root class="pointer-events-auto pr-4 shadow-xl" aria-live="polite">
      <Alert.Title>Unsaved work was recovered</Alert.Title>
      <Alert.Description>
        {Object.keys(recoveredDrafts).length === 1
          ? "One document draft was found in local application storage."
          : `${Object.keys(recoveredDrafts).length} document drafts were found in local application storage.`}
      </Alert.Description>
      <Alert.Action class="static col-span-full mt-2 flex flex-col gap-2 min-[360px]:flex-row min-[360px]:justify-end">
        <Button variant="ghost" size="xs" onclick={discardSavedDrafts}>Discard</Button>
        <Button size="xs" onclick={recoverSavedDrafts}>Restore drafts</Button>
      </Alert.Action>
    </Alert.Root>
  </div>
{/if}

{#if comparedExternalChange !== undefined}
  <AppDialog
    open
    title={comparedExternalChange.path}
    description="Compare the editor base with the current file, then keep or merge the change."
    class="sm:max-w-6xl"
    bodyClass="grid gap-4"
    onopenchange={(open) => { if (!open) comparedExternalChange = undefined; }}
  >
    <div class="grid gap-4 lg:grid-cols-2">
      <Field.Field>
        <Field.Label for="external-editor-base">Editor base</Field.Label>
        <Textarea id="external-editor-base" class="min-h-64 font-mono text-xs" readonly value={snapshot?.documents.find((document) => document.path === comparedExternalChange?.path)?.content ?? "File was not previously loaded."} />
      </Field.Field>
      <Field.Field>
        <Field.Label for="external-current-disk">Current disk</Field.Label>
        <Textarea id="external-current-disk" class="min-h-64 font-mono text-xs" readonly value={comparedExternalChange.content ?? "File was deleted externally."} />
      </Field.Field>
    </div>
    <Field.Field>
      <Field.Label for="external-merged-draft">Merged draft</Field.Label>
      <Textarea id="external-merged-draft" class="min-h-64 font-mono text-xs" bind:value={mergedExternalText} spellcheck={false} />
    </Field.Field>
    {#snippet footer()}
      <Button variant="outline" onclick={() => comparedExternalChange = undefined}>Keep current view</Button>
      <Button onclick={() => void applyExternalMerge()}>Reload base and keep merged draft</Button>
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
    <h1>Could not open this translation workspace</h1>
    <p>{clientError ?? "No Runic Text Resources catalog was found."}</p>
    <button class="primary" onclick={() => void loadWorkspace(false)}>{labels.reload}</button>
  </main>
{:else if snapshot.pendingTransaction !== undefined}
  <main class="recovery-shell">
    <div class="mark" aria-hidden="true"><span></span></div>
    <p class="eyebrow">Workspace recovery</p>
    <h1>An interrupted change needs your decision</h1>
    <p>The recovery journal for <strong>{snapshot.pendingTransaction.catalogId}</strong> lists {snapshot.pendingTransaction.paths.length} affected {snapshot.pendingTransaction.paths.length === 1 ? "file" : "files"}. No further editing is allowed until it is resolved.</p>
    <div class="recovery-paths">
      {#each snapshot.pendingTransaction.paths as path (path)}<code>{path}</code>{/each}
    </div>
    {#if clientError}<p class="project-error" aria-live="polite">{clientError}</p>{/if}
    <div class="recovery-actions">
      <button class="secondary" disabled={recoveryBusy} onclick={() => void recoverTransaction("rollback")}>Restore files from before the change</button>
      <button class="primary" disabled={recoveryBusy} onclick={() => void recoverTransaction("complete")}>{recoveryBusy ? "Recovering…" : "Complete the planned change"}</button>
    </div>
    <small>Both choices use the bounded local journal. The journal is removed only after recovery succeeds.</small>
  </main>
{:else if snapshot.catalog === undefined}
  <main class="welcome-shell">
    <header class="welcome-brand">
      <div class="mark small" aria-hidden="true"><span></span></div>
      <div><p class="eyebrow">{labels.eyebrow}</p><h1>{labels.title}</h1></div>
      <select aria-label="Interface language" value={uiLocale} onchange={(event) => uiLocale = event.currentTarget.value}>
        <option value="en">EN</option><option value="de">DE</option>
      </select>
    </header>
    <section class="welcome-content">
      <div class="welcome-heading">
        <p class="eyebrow">Workspace onboarding</p>
        <h2>{snapshot.catalogs.length > 1 ? "Choose a translation catalog" : "Open a translation project"}</h2>
        <p>{snapshot.catalogs.length > 1
          ? `We found ${snapshot.catalogs.length} catalogs below this workspace boundary.`
          : "Open an existing workspace or create a compiler-valid project from scratch."}</p>
      </div>

      {#if snapshot.catalogs.length > 0}
        <div class="catalog-choices">
          {#each snapshot.catalogs as catalog (catalog.id)}
            <button class="catalog-choice" onclick={() => void openWorkspace(catalog.id)} disabled={openingWorkspace}>
              <span class={{ "status-dot": true, warning: !catalog.success }}></span>
              <span><strong>{catalog.id}</strong><small>{catalog.manifestPaths.join(", ")}</small></span>
              <span class="catalog-metrics">{catalog.localeCount} locales<br />{catalog.messageCount} messages</span>
              <span class={catalog.errorCount > 0 ? "health error" : "health"}>{catalog.errorCount > 0 ? `${catalog.errorCount} errors` : "Healthy"}</span>
            </button>
          {/each}
        </div>
      {/if}

      <div class="open-workspace-card">
        <label for="open-directory">Workspace directory</label>
        <div><input id="open-directory" bind:value={openDirectory} placeholder="/projects/customer-app" autocomplete="off" />
          <button class="secondary" disabled={pickingWorkspace || openingWorkspace} onclick={() => void pickWorkspace()}>{pickingWorkspace ? "Choosing…" : "Browse…"}</button>
          <button class="primary" disabled={openingWorkspace} onclick={() => void openWorkspace()}>{openingWorkspace ? "Opening…" : "Open"}</button></div>
        <small>Traversal stays inside this boundary and ignores links, dependencies, and generated output.</small>
      </div>

      <div class="welcome-actions">
        <button class="secondary" onclick={openProjectWizard}>＋ Create new project</button>
        <button class="secondary" onclick={() => void loadWorkspace(false)}>↻ Scan {snapshot.root}</button>
      </div>

      {#if recentProjects.length > 0}
        <section class="recent-projects">
          <header><strong>Recent projects</strong><span>Stored only in your local application profile</span></header>
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
          <header><div><strong>Repair malformed JSON</strong><span>{malformedDocuments.length} files need attention</span></div></header>
          {#each malformedDocuments as document (document.path)}
            <button onclick={() => beginRepair(document)}><span>!</span><code>{document.path}</code><small>Open repair editor →</small></button>
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
            remainingCount={visibleRows.length - renderedRows.length}
            noResultsLabel={labels.noResults}
            onselect={(key) => {
              const row = renderedRows.find((candidate) => candidate.key === key);
              if (row !== undefined) selectRow(row);
            }}
            onadd={() => prepareMutation("create-key")}
            onmarkreview={() => markVisible("needs-review")}
            onapprove={() => markVisible("approved")}
            onloadmore={() => rowLimit += 300}
            bind:open={messagesOpen}
          >
            {#snippet toolbar()}
              <MessageToolbar
                bind:query
                bind:filter
                bind:inputRef={searchInput}
                placeholder={labels.search}
                options={filterOptions}
                filterLabel="Message filters"
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
        onlocalechange={(locale) => uiLocale = locale}
        onthememodechange={changeThemeMode}
        onthemepalettechange={changeThemePalette}
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
        ondiscardreview={discardReview}
        onsavereview={() => void saveReview()}
        onsave={() => void save()}
      />

      {#if selectedRow === undefined}
        <Empty.Root>
          <Empty.Header>
            <Empty.Media variant="icon" class="text-primary">
              <MessageSquareTextIcon aria-hidden="true" />
            </Empty.Media>
            <Empty.Title class="font-serif font-medium">{labels.noSelection}</Empty.Title>
            <Empty.Description>Choose a message from the sidebar to review or edit its translation.</Empty.Description>
          </Empty.Header>
        </Empty.Root>
      {:else}
        <div class="editor-content">
          <MessageHeading
            messageKey={selectedRow.key}
            description={selectedRow.description}
            tags={selectedRow.tags}
            locale={selectedLocale}
            layer={currentDocument?.layer ?? "no document"}
            inheritedFrom={currentCell?.inheritedFrom}
            onrename={() => prepareMutation("rename-key")}
            onduplicate={() => prepareMutation("duplicate-key")}
            ondelete={() => prepareMutation("delete-key")}
          />

          <ReviewWorkflow
            state={currentReviewState}
            dirty={reviewDirty}
            message={reviewMessage ?? "Project notes"}
            disabled={snapshot.review?.error !== undefined}
            stale={currentIsStale}
            terminologyCount={terminology.length}
            qualityCount={localeQuality.length}
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
            label={mode === "translation" ? localeName(selectedLocale) : currentDocument?.path ?? "Resource document"}
            value={editorText}
            resourceValue={currentCell?.entry?.value ?? selectedRow.cells[snapshot.catalog.defaultLocale]?.entry?.value}
            missing={currentCell?.entry === undefined}
            invalid={clientError !== undefined || validation?.success === false}
            onresourcechange={editResourceValue}
            onrawchange={edit}
            onformatraw={formatRaw}
          />

          {#if mode === "translation"}
            <section class="message-preview" aria-live="polite">
              <header>
                <div><strong>Preview</strong><span>Uses the same rules as the generated application message</span></div>
                <span class="preview-state">{previewBusy ? "Compiling…" : previewAst === undefined ? "Unavailable" : selectedLocale}</span>
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
                  <span class="preview-placeholder">Compiling the current draft…</span>
                {:else if previewError}
                  <span class="preview-error">{previewError}</span>
                {:else if previewResult?.kind === "text"}
                  <p>{previewResult.value}</p>
                {:else if previewResult?.kind === "content"}
                  <div class="safe-content">{@render previewNodes(previewResult.nodes)}</div>
                {:else}
                  <span class="preview-placeholder">Edit the message to build a preview.</span>
                {/if}
              </div>
              <p class="safe-note">Semantic markup is displayed as a data tree. Names and attributes are never interpreted as trusted HTML.</p>
            </section>
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
    title={aboutInfo?.product ?? "Runic Translations Editor"}
    description="Application information and privacy-safe diagnostics."
    onopenchange={(open) => aboutDialogOpen = open}
  >
    <div class="grid gap-4">
      {#if aboutBusy}
        <div class="flex items-center gap-2 text-muted-foreground"><Spinner />Reading application information…</div>
      {:else if aboutInfo !== undefined}
        <dl class="grid overflow-hidden rounded-xl border">
          {#each [
            ["Version", aboutInfo.version],
            ["Update channel", aboutInfo.updateChannel],
            ["Source revision", aboutInfo.commit ?? "development build"],
            ["Runtime", aboutInfo.runtime],
            ["Runtime identifier", aboutInfo.runtimeIdentifier],
            ["System", `${aboutInfo.operatingSystem} · ${aboutInfo.architecture}`],
          ] as item (item[0])}
            <div class="grid gap-1 border-b px-4 py-3 last:border-b-0 sm:grid-cols-[9rem_1fr] sm:gap-4">
              <dt class="text-muted-foreground">{item[0]}</dt><dd class="m-0 overflow-wrap-anywhere font-mono text-xs">{item[1]}</dd>
            </div>
          {/each}
        </dl>
      {/if}
      <Alert.Root>
        <Alert.Title>Sanitized diagnostic bundle</Alert.Title>
        <Alert.Description>The zip contains version/runtime information, catalog counts, and grouped diagnostic IDs. It excludes workspace paths, file names, messages, source JSON, and translations.</Alert.Description>
        {#if diagnosticMessage}<p class="text-sm text-primary" aria-live="polite">{diagnosticMessage}</p>{/if}
      </Alert.Root>
      <p class="text-sm text-muted-foreground">Runic Text Resources is MIT licensed. The packaged application includes <code>LICENSE.txt</code> and <code>THIRD-PARTY-NOTICES.md</code>.</p>
    </div>
    {#snippet footer()}
      <Button variant="outline" onclick={() => aboutDialogOpen = false}>Close</Button>
      <Button disabled={diagnosticBusy || aboutBusy} onclick={() => void createDiagnosticBundle()}>
        {#if diagnosticBusy}<Spinner data-icon="inline-start" />{/if}
        {diagnosticBusy ? "Creating bundle…" : "Create diagnostic bundle"}
      </Button>
    {/snippet}
  </AppDialog>
{/if}

{#if terminologyDialogOpen}
  <AppDialog
    open
    title="Project terminology"
    description="Terms stay in the optional versioned sidecar and are checked locally. Nothing is sent to a service."
    class="sm:max-w-4xl"
    onopenchange={(open) => terminologyDialogOpen = open}
  >
    <Field.FieldGroup class="grid gap-3 sm:grid-cols-2">
      <Field.Field><Field.Label for="term-source">Source term</Field.Label><Input id="term-source" bind:value={termSource} placeholder="Save" /></Field.Field>
      <Field.Field><Field.Label for="term-preferred">Preferred translation</Field.Label><Input id="term-preferred" bind:value={termPreferred} placeholder="Speichern" /></Field.Field>
      <Field.Field><Field.Label for="term-locale">Locale</Field.Label><Input id="term-locale" bind:value={termLocale} placeholder="Optional, e.g. de" /></Field.Field>
      <Field.Field><Field.Label for="term-note">Note</Field.Label><Input id="term-note" bind:value={termNote} placeholder="Optional usage guidance" /></Field.Field>
      <Button class="justify-self-start sm:col-span-2" variant="outline" disabled={termSource.trim() === "" || termPreferred.trim() === ""} onclick={addTerm}>
        <PlusIcon data-icon="inline-start" />Add term
      </Button>
    </Field.FieldGroup>
    <div class="mt-5 grid overflow-hidden rounded-xl border">
      {#each terminology as term, index (term)}
        <div class="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-3 border-b px-4 py-3 last:border-b-0">
          <div class="min-w-0">
            <div class="flex min-w-0 flex-wrap items-center gap-2"><strong>{term.source}</strong><span class="text-muted-foreground">→</span><strong>{term.preferred}</strong>{#if term.locale}<Badge variant="outline">{term.locale}</Badge>{/if}</div>
            <p class="truncate text-xs text-muted-foreground">{term.note ?? "No note"}</p>
          </div>
          <Button variant="ghost" size="icon-xs" aria-label={"Remove term " + term.source} onclick={() => removeTerm(index)}><Trash2Icon /></Button>
        </div>
      {:else}
        <p class="p-6 text-center text-sm text-muted-foreground">No terminology entries yet.</p>
      {/each}
    </div>
    {#snippet footer()}
      <Button variant="outline" onclick={() => terminologyDialogOpen = false}>Done</Button>
      <Button disabled={!reviewDirty || reviewSaving} onclick={() => void saveReview()}>
        {#if reviewSaving}<Spinner data-icon="inline-start" />{/if}Save workflow
      </Button>
    {/snippet}
  </AppDialog>
{/if}

{#if reportDialogOpen}
  <AppDialog
    open
    title={`${selectedLocale} quality report`}
    description={`${localeQuality.length} findings across ${qualityKeySet.size} messages. CSV is ordered by key and finding kind.`}
    class="sm:max-w-4xl"
    onopenchange={(open) => reportDialogOpen = open}
  >
    <Textarea class="min-h-[26rem] font-mono text-xs" aria-label="Quality report CSV" readonly value={qualityReportCsv(localeQuality)} />
    {#snippet footer()}<Button variant="outline" onclick={() => reportDialogOpen = false}>Close</Button>{/snippet}
  </AppDialog>
{/if}

{#if repairDocument !== undefined}
  <AppDialog
    open
    title={repairDocument.path}
    description="Edit the raw JSON below. The canonical compiler must accept it before it can replace the file."
    class="sm:max-w-4xl"
    showCloseButton={!repairBusy}
    onopenchange={(open) => { if (!open && !repairBusy) repairDocument = undefined; }}
  >
    <Textarea class="min-h-[26rem] font-mono text-xs" aria-label="Malformed JSON document" bind:value={repairText} spellcheck={false} />
    {#if repairMessage}<Alert.Root variant="destructive" class="mt-4"><Alert.Title>Repair failed</Alert.Title><Alert.Description>{repairMessage}</Alert.Description></Alert.Root>{/if}
    {#snippet footer()}
      <Button variant="outline" disabled={repairBusy} onclick={() => repairDocument = undefined}>Cancel</Button>
      <Button disabled={repairBusy} onclick={() => void saveRepair()}>{#if repairBusy}<Spinner data-icon="inline-start" />{/if}{repairBusy ? "Validating…" : "Validate and save"}</Button>
    {/snippet}
  </AppDialog>
{/if}

{#if openDialogOpen}
  <AppDialog
    open
    title="Open translation project"
    description="Catalogs are discovered below this workspace boundary. You will choose one if several are found."
    showCloseButton={!openingWorkspace && !pickingWorkspace}
    onopenchange={(open) => { if (!openingWorkspace && !pickingWorkspace) openDialogOpen = open; }}
  >
    <Field.Field>
      <Field.Label for="dialog-open-directory">Workspace directory</Field.Label>
      <div class="flex flex-col gap-2 sm:flex-row">
        <Input id="dialog-open-directory" class="min-w-0 flex-1" bind:value={openDirectory} autocomplete="off" />
        <Button variant="outline" disabled={pickingWorkspace || openingWorkspace} onclick={() => void pickWorkspace()}>{pickingWorkspace ? "Choosing…" : "Browse…"}</Button>
      </div>
    </Field.Field>
    {#if clientError}<Alert.Root variant="destructive" class="mt-4"><Alert.Title>Could not open workspace</Alert.Title><Alert.Description>{clientError}</Alert.Description></Alert.Root>{/if}
    {#snippet footer()}
      <Button variant="outline" disabled={openingWorkspace} onclick={() => openDialogOpen = false}>Cancel</Button>
      <Button disabled={openingWorkspace || openDirectory.trim() === ""} onclick={() => void openWorkspace()}>{#if openingWorkspace}<Spinner data-icon="inline-start" />{/if}{openingWorkspace ? "Opening…" : "Open workspace"}</Button>
    {/snippet}
  </AppDialog>
{/if}

{#if mutationDialogOpen && snapshot?.catalog !== undefined}
  <AppDialog
    open
    title={mutationTitle(mutationKind)}
    description="Compiler-backed workspace change. Review the affected files before committing."
    class="sm:max-w-3xl"
    showCloseButton={!mutationBusy}
    onopenchange={(open) => { if (!mutationBusy) mutationDialogOpen = open; }}
  >
    <Field.FieldGroup class="gap-4">
      {#if mutationKind === "add-locale" || mutationKind === "remove-locale" || mutationKind === "set-fallback"}
        <Field.Field>
          <Field.Label for="language-operation">Language operation</Field.Label>
          <Select.Root type="single" value={mutationKind} onValueChange={changeMutationKind}>
            <Select.Trigger id="language-operation" class="w-full">{mutationTitle(mutationKind)}</Select.Trigger>
            <Select.Content><Select.Group><Select.Label>Language operation</Select.Label>
              <Select.Item value="add-locale" label="Add a language">Add a language</Select.Item>
              <Select.Item value="remove-locale" label="Remove a language">Remove a language</Select.Item>
              <Select.Item value="set-fallback" label="Change a fallback">Change a fallback</Select.Item>
            </Select.Group></Select.Content>
          </Select.Root>
        </Field.Field>
      {/if}

      {#if mutationKind === "add-locale"}
        <div class="grid gap-4 sm:grid-cols-2">
          <Field.Field><Field.Label for="mutation-locale">New locale tag</Field.Label><Input id="mutation-locale" bind:value={mutationLocale} oninput={invalidateMutationPreview} placeholder="fr-FR" autocomplete="off" /></Field.Field>
          <Field.Field><Field.Label for="mutation-fallback">Fallback</Field.Label>
            <Select.Root type="single" value={mutationFallback} onValueChange={(value) => { mutationFallback = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="mutation-fallback" class="w-full">{mutationFallback} · {localeName(mutationFallback)}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.locales as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
          <Field.Field><Field.Label for="mutation-copy-from">Copy starter values from</Field.Label>
            <Select.Root type="single" value={mutationCopyFrom} onValueChange={(value) => { mutationCopyFrom = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="mutation-copy-from" class="w-full">{mutationCopyFrom} · {localeName(mutationCopyFrom)}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.locales as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
            <Field.Description>Copied text keeps the new catalog compiler-valid and can then be translated.</Field.Description>
          </Field.Field>
          <Field.Field><Field.Label for="mutation-layer">Layer</Field.Label>
            <Select.Root type="single" value={mutationLayer} onValueChange={(value) => { mutationLayer = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="mutation-layer" class="w-full">{mutationLayer}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.layers as layer (layer.name)}<Select.Item value={layer.name} label={layer.name}>{layer.name}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
        </div>
      {:else if mutationKind === "remove-locale"}
        <div class="grid gap-4 sm:grid-cols-2">
          <Field.Field><Field.Label for="remove-locale">Language to remove</Field.Label>
            <Select.Root type="single" value={mutationLocale} onValueChange={(value) => { mutationLocale = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="remove-locale" class="w-full">{mutationLocale} · {localeName(mutationLocale)}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.locales.filter((locale) => locale.tag !== snapshot?.catalog?.defaultLocale) as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
          <Field.Field><Field.Label for="replacement-fallback">Redirect dependent fallbacks to</Field.Label>
            <Select.Root type="single" value={mutationReplacementFallback} onValueChange={(value) => { mutationReplacementFallback = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="replacement-fallback" class="w-full">{mutationReplacementFallback}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.locales.filter((locale) => locale.tag !== mutationLocale) as locale (locale.tag)}<Select.Item value={locale.tag} label={locale.tag}>{locale.tag}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
        </div>
        <Alert.Root variant="destructive"><Alert.Title>Files will be deleted</Alert.Title><Alert.Description>All resource documents for this locale will be deleted after the preview is confirmed.</Alert.Description></Alert.Root>
      {:else if mutationKind === "set-fallback"}
        <div class="grid gap-4 sm:grid-cols-2">
          <Field.Field><Field.Label for="fallback-locale">Language</Field.Label>
            <Select.Root type="single" value={mutationLocale} onValueChange={(value) => { mutationLocale = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="fallback-locale" class="w-full">{mutationLocale} · {localeName(mutationLocale)}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.locales.filter((locale) => locale.tag !== snapshot?.catalog?.defaultLocale) as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
          <Field.Field><Field.Label for="fallback-target">Fallback</Field.Label>
            <Select.Root type="single" value={mutationFallback} onValueChange={(value) => { mutationFallback = value; invalidateMutationPreview(); }}>
              <Select.Trigger id="fallback-target" class="w-full">{mutationFallback} · {localeName(mutationFallback)}</Select.Trigger>
              <Select.Content><Select.Group>{#each snapshot.catalog.locales.filter((locale) => locale.tag !== mutationLocale) as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
        </div>
        <div class="flex flex-wrap gap-2">{#each snapshot.catalog.locales as locale (locale.tag)}<Badge variant="outline"><strong>{locale.tag}</strong>{locale.tag === mutationLocale ? ` → ${mutationFallback}` : locale.fallback ? ` → ${locale.fallback}` : " · source"}</Badge>{/each}</div>
      {:else if mutationKind === "create-key"}
        <Field.Field><Field.Label for="mutation-target-key">Message key</Field.Label><Input id="mutation-target-key" bind:value={mutationTargetKey} oninput={invalidateMutationPreview} placeholder="Checkout.Actions.Pay" autocomplete="off" /><Field.Description>Use dots to organize messages into groups.</Field.Description></Field.Field>
        <Field.Field><Field.Label for="mutation-initial-value">Initial text</Field.Label><Textarea id="mutation-initial-value" class="min-h-28" bind:value={mutationInitialValue} oninput={invalidateMutationPreview} placeholder="Pay now" /><Field.Description>The initial value is added to every language so strict projects stay valid.</Field.Description></Field.Field>
        <Field.Field><Field.Label for="message-layer">Layer</Field.Label>
          <Select.Root type="single" value={mutationLayer} onValueChange={(value) => { mutationLayer = value; invalidateMutationPreview(); }}><Select.Trigger id="message-layer" class="w-full">{mutationLayer}</Select.Trigger><Select.Content><Select.Group>{#each snapshot.catalog.layers as layer (layer.name)}<Select.Item value={layer.name} label={layer.name}>{layer.name}</Select.Item>{/each}</Select.Group></Select.Content></Select.Root>
        </Field.Field>
      {:else if mutationKind === "rename-key" || mutationKind === "duplicate-key"}
        <Field.Field><Field.Label for="mutation-source-key">Existing key</Field.Label><Input id="mutation-source-key" value={mutationSourceKey} readonly /></Field.Field>
        <Field.Field><Field.Label for="mutation-new-key">{mutationKind === "rename-key" ? "New key or group path" : "Duplicate key"}</Field.Label><Input id="mutation-new-key" bind:value={mutationTargetKey} oninput={invalidateMutationPreview} autocomplete="off" /><Field.Description>The change is applied across every locale and layer where the source message exists.</Field.Description></Field.Field>
      {:else}
        <Alert.Root variant="destructive"><Alert.Title>Delete {mutationSourceKey}?</Alert.Title><Alert.Description>The message will be removed from every locale and layer. The preview below lists every file that will change.</Alert.Description></Alert.Root>
      {/if}
    </Field.FieldGroup>

    {#if mutationError}<Alert.Root variant="destructive" class="mt-4" aria-live="polite"><Alert.Title>Change is not valid</Alert.Title><Alert.Description>{mutationError}</Alert.Description></Alert.Root>{/if}
    {#if mutationPreview?.ok}
      <section class="mt-5 overflow-hidden rounded-xl border" aria-label="Operation preview">
        <header class="flex items-center justify-between gap-3 border-b px-4 py-3"><strong>Operation preview</strong><Badge variant="secondary">{mutationPreview.files.length} affected {mutationPreview.files.length === 1 ? "file" : "files"}</Badge></header>
        {#each mutationPreview.files as file (file.path)}
          <div class="grid grid-cols-[auto_minmax(0,1fr)] items-center gap-3 border-b px-4 py-3 last:border-b-0 sm:grid-cols-[auto_minmax(0,1fr)_auto]"><Badge variant={file.kind === "delete" ? "destructive" : file.kind === "create" ? "default" : "secondary"}>{file.kind}</Badge><code class="truncate text-xs">{file.path}</code><small class="col-start-2 text-muted-foreground sm:col-start-auto">{file.beforeBytes.toLocaleString()} → {file.afterBytes.toLocaleString()} bytes</small></div>
        {/each}
      </section>
    {/if}
    {#snippet footer()}
      <Button variant="outline" disabled={mutationBusy} onclick={() => mutationDialogOpen = false}>Cancel</Button>
      {#if mutationPreview?.ok}
        <Button variant={mutationKind === "remove-locale" || mutationKind === "delete-key" ? "destructive" : "default"} disabled={mutationBusy} onclick={() => void applyMutation()}>{#if mutationBusy}<Spinner data-icon="inline-start" />{/if}{mutationBusy ? "Committing…" : "Commit change"}</Button>
      {:else}
        <Button disabled={mutationBusy} onclick={() => void previewMutation()}>{#if mutationBusy}<Spinner data-icon="inline-start" />{/if}{mutationBusy ? "Checking…" : "Preview change"}</Button>
      {/if}
    {/snippet}
  </AppDialog>
{/if}

{#if projectDialogOpen}
  <AppDialog
    open
    title="New translation project"
    description="Create compiler-valid text resources without overwriting an existing directory."
    class="sm:max-w-3xl"
    showCloseButton={!projectBusy}
    onopenchange={(open) => { if (!open && !projectBusy) closeProjectWizard(); }}
  >
    <ol class="mb-6 grid grid-cols-2 gap-2 sm:grid-cols-4" aria-label="Project creation steps">
      {#each ["Project", "Languages", "Settings", "Review"] as title, index (title)}
        <li class="flex items-center gap-2 text-sm" aria-current={projectStep === index + 1 ? "step" : undefined}>
          <Badge variant={projectStep === index + 1 ? "default" : projectStep > index + 1 ? "secondary" : "outline"}>{projectStep > index + 1 ? "✓" : index + 1}</Badge>
          <span class={projectStep === index + 1 ? "font-medium" : "text-muted-foreground"}>{title}</span>
        </li>
      {/each}
    </ol>

    {#if projectStep === 1}
      <div class="mb-5"><h3 class="font-medium">Where should the translations live?</h3><p class="text-sm text-muted-foreground">The editor creates a new directory and never overwrites an existing one.</p></div>
      <Field.FieldGroup>
        <Field.Field><Field.Label for="project-directory">New project directory</Field.Label><Input id="project-directory" bind:value={projectDirectory} placeholder="/projects/customer-app/Resources" autocomplete="off" /><Field.Description>Enter an absolute path or a path relative to the editor process.</Field.Description></Field.Field>
        <Field.Field><Field.Label for="project-catalog">Catalog ID</Field.Label><Input id="project-catalog" bind:value={projectCatalog} placeholder="product" autocomplete="off" /><Field.Description>Lowercase letters, numbers, dots, and hyphens.</Field.Description></Field.Field>
      </Field.FieldGroup>
    {:else if projectStep === 2}
      <div class="mb-5"><h3 class="font-medium">Which languages does this project use?</h3><p class="text-sm text-muted-foreground">One language is fully supported. Add translations now or later.</p></div>
      <div class="grid gap-3">
        <div class="grid items-end gap-3 rounded-xl border p-4 sm:grid-cols-[1fr_auto]">
          <Field.Field><Field.Label for="project-default-locale">Source/default language</Field.Label><Input id="project-default-locale" bind:value={projectDefaultLocale} placeholder="de" autocomplete="off" /></Field.Field>
          <Badge variant="secondary" class="mb-2">Canonical source</Badge>
        </div>
        {#each projectLocales as locale (locale.id)}
          <div class="grid items-end gap-3 rounded-xl border p-4 sm:grid-cols-[1fr_1fr_auto]">
            <Field.Field><Field.Label for={`project-locale-${locale.id}`}>Additional language</Field.Label><Input id={`project-locale-${locale.id}`} bind:value={locale.tag} placeholder="en" autocomplete="off" /></Field.Field>
            <Field.Field><Field.Label for={`project-fallback-${locale.id}`}>Fallback</Field.Label>
              <Select.Root type="single" value={locale.fallback} onValueChange={(value) => locale.fallback = value}>
                <Select.Trigger id={`project-fallback-${locale.id}`} class="w-full">{locale.fallback || `Default (${projectDefaultLocale || "source"})`}</Select.Trigger>
                <Select.Content><Select.Group><Select.Item value="" label={`Default (${projectDefaultLocale || "source"})`}>Default ({projectDefaultLocale || "source"})</Select.Item>{#each projectLocales.filter((candidate) => candidate.id !== locale.id && candidate.tag.trim() !== "") as candidate (candidate.id)}<Select.Item value={candidate.tag} label={candidate.tag}>{candidate.tag}</Select.Item>{/each}</Select.Group></Select.Content>
              </Select.Root>
            </Field.Field>
            <Button variant="ghost" size="icon-sm" aria-label={`Remove locale ${locale.tag || "row"}`} onclick={() => removeProjectLocale(locale.id)}><Trash2Icon /></Button>
          </div>
        {/each}
        <Button variant="outline" class="justify-self-start" onclick={addProjectLocale}><PlusIcon data-icon="inline-start" />Add another language</Button>
      </div>
    {:else if projectStep === 3}
      <div class="mb-5"><h3 class="font-medium">Generated API and output</h3><p class="text-sm text-muted-foreground">These defaults work for most .NET and ESM consumers.</p></div>
      <Field.FieldGroup>
        <div class="grid gap-4 sm:grid-cols-2">
          <Field.Field><Field.Label for="project-namespace">Code namespace</Field.Label><Input id="project-namespace" bind:value={projectNamespace} autocomplete="off" /></Field.Field>
          <Field.Field><Field.Label for="project-class">Generated class</Field.Label><Input id="project-class" bind:value={projectClassName} autocomplete="off" /></Field.Field>
          <Field.Field><Field.Label for="project-layer">Initial layer</Field.Label><Input id="project-layer" bind:value={projectLayer} autocomplete="off" /></Field.Field>
        </div>
        <Field.Field orientation="horizontal"><Checkbox id="project-esm" bind:checked={projectGenerateEsm} /><Field.Content><Field.Label for="project-esm">Enable ESM output</Field.Label><Field.Description>Generate tree-shakeable modules for TypeScript and browser applications.</Field.Description></Field.Content></Field.Field>
        <Field.Field orientation="horizontal"><Checkbox id="project-starter" bind:checked={projectIncludeStarter} /><Field.Content><Field.Label for="project-starter">Add a starter message</Field.Label><Field.Description>Create <code>Application.Name</code> in every language.</Field.Description></Field.Content></Field.Field>
      </Field.FieldGroup>
    {:else if projectStep === 4 && projectPlan !== undefined}
      <Alert.Root><Alert.Title>Ready to create {projectPlan.catalogId}</Alert.Title><Alert.Description>{projectPlan.locales.length} {projectPlan.locales.length === 1 ? "language" : "languages"} · {projectPlan.files.length} files · compiler validated</Alert.Description></Alert.Root>
      <dl class="mt-4 grid gap-3 rounded-xl border p-4"><div class="grid gap-1 sm:grid-cols-[7rem_1fr]"><dt class="text-muted-foreground">Directory</dt><dd class="m-0 truncate font-mono text-xs">{projectPlan.directory}</dd></div><div class="grid gap-1 sm:grid-cols-[7rem_1fr]"><dt class="text-muted-foreground">Languages</dt><dd class="m-0 font-medium">{projectPlan.locales.map((locale) => locale.tag).join(", ")}</dd></div></dl>
      <section class="mt-4 overflow-hidden rounded-xl border" aria-label="Files to create"><h4 class="border-b px-4 py-3 font-medium">Files to create</h4>{#each projectPlan.files as file (file)}<div class="border-b px-4 py-3 last:border-b-0"><code class="text-xs">{file}</code></div>{/each}</section>
    {/if}

    {#if projectError}<Alert.Root variant="destructive" class="mt-4" aria-live="polite"><Alert.Title>Project is not valid</Alert.Title><Alert.Description>{projectError}</Alert.Description></Alert.Root>{/if}
    {#snippet footer()}
      <Button variant="outline" disabled={projectBusy} onclick={closeProjectWizard}>Cancel</Button>
      {#if projectStep > 1}<Button variant="ghost" disabled={projectBusy} onclick={() => { projectStep -= 1; projectError = undefined; }}>Back</Button>{/if}
      {#if projectStep < 4}
        <Button disabled={projectBusy} onclick={() => void advanceProjectWizard()}>{#if projectBusy}<Spinner data-icon="inline-start" />{/if}{projectBusy ? "Validating…" : "Continue"}</Button>
      {:else}
        <Button disabled={projectBusy || projectPlan?.ok !== true} onclick={() => void createProject()}>{#if projectBusy}<Spinner data-icon="inline-start" />{/if}{projectBusy ? "Creating…" : "Create project"}</Button>
      {/if}
    {/snippet}
  </AppDialog>
{/if}

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
  .mark { display: grid; place-items: center; width: 4rem; height: 4rem; transform: rotate(45deg); border: 1px solid color-mix(in oklch, var(--primary) 65%, var(--border)); border-radius: .35rem; background: linear-gradient(145deg, var(--secondary), var(--background)); box-shadow: inset 0 0 0 .28rem var(--background), 0 1rem 3rem color-mix(in oklch, var(--foreground) 22%, transparent); }
  .mark span { width: 1.15rem; height: 1.15rem; border: 2px solid var(--primary); transform: rotate(45deg); }
  .mark.small { width: 2rem; height: 2rem; border-radius: .2rem; box-shadow: inset 0 0 0 .18rem var(--background); }
  .mark.small span { width: .55rem; height: .55rem; border-width: 1px; }

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
