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
    EditorDiagnostic,
    EditorDocument,
    EditorExternalFileChange,
    EditorProjectCreationRequest,
    EditorProjectPlan,
    ValidationResult,
    WorkspaceSnapshot,
  } from "$lib/contracts";
  import { createEditorBridge } from "$lib/editor-bridge";
  import {
    buildRows,
    coverage,
    formatJson,
    preview,
    updateResourceValue,
    type ResourceValue,
    type TranslationRow,
  } from "$lib/resource-model";

  type Filter = "all" | "missing" | "structured";
  type EditorMode = "simple" | "advanced" | "raw";
  type ProjectLocaleDraft = { id: number; tag: string; fallback: string };
  type StoredDraft = { content: string; baseRevision: string };
  type RecentProject = { root: string; catalogId: string; openedAt: string };

  const bridge = createEditorBridge();
  let snapshot = $state.raw<WorkspaceSnapshot>();
  let drafts = $state<Record<string, string>>({});
  let selectedKey = $state("");
  let selectedLocale = $state("");
  let selectedDocumentPath = $state("");
  let filter = $state<Filter>("all");
  let query = $state("");
  let mode = $state<EditorMode>("simple");
  let editorText = $state("");
  let uiLocale = $state("en");
  let loading = $state(true);
  let saving = $state(false);
  let validationBusy = $state(false);
  let validation = $state.raw<ValidationResult>();
  let clientError = $state<string>();
  let operationMessage = $state<string>();
  let searchInput = $state<HTMLInputElement>();
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

  let labels = $derived(labelsFor(uiLocale));
  let rows = $derived(buildRows(snapshot, drafts));
  let visibleRows = $derived.by(() => {
    const normalized = query.trim().toLocaleLowerCase();
    return rows.filter((row) => {
      const cell = row.cells[selectedLocale];
      if (filter === "missing" && cell?.entry !== undefined) return false;
      if (filter === "structured" && !row.structured) return false;
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
  let selectedRow = $derived(rows.find((row) => row.key === selectedKey));
  let currentCell = $derived(selectedRow?.cells[selectedLocale]);
  let currentDocument = $derived(
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
    recentProjects = readRecentProjects();
    void loadWorkspace(false);
    const interval = window.setInterval(() => void checkExternalChanges(), 2_000);
    return () => window.clearInterval(interval);
  });

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

  function selectRow(row: TranslationRow): void {
    selectedKey = row.key;
    validation = undefined;
    clientError = undefined;
    operationMessage = undefined;
    configureEditor();
  }

  function selectLocale(locale: string): void {
    selectedLocale = locale;
    validation = undefined;
    clientError = undefined;
    operationMessage = undefined;
    configureEditor();
  }

  function chooseMode(nextMode: EditorMode): void {
    mode = nextMode;
    clientError = undefined;
    configureEditor(nextMode);
  }

  function configureEditor(preferredMode?: EditorMode): void {
    const row = buildRows(snapshot, drafts).find((candidate) => candidate.key === selectedKey);
    const cell = row?.cells[selectedLocale];
    const document = cell?.document;
    selectedDocumentPath = document?.path ?? "";
    const sourceEntry = row?.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry;
    const nextMode = preferredMode ?? (cell?.entry?.structured || row?.structured ? "advanced" : "simple");
    mode = nextMode;
    if (nextMode === "raw") {
      editorText = document === undefined ? "" : (drafts[document.path] ?? document.content);
    } else if (nextMode === "advanced") {
      editorText = JSON.stringify(cell?.entry?.value ?? sourceEntry?.value ?? "", null, 2);
    } else {
      editorText = typeof cell?.entry?.value === "string" ? cell.entry.value : "";
    }
  }

  function edit(value: string): void {
    editorText = value;
    clientError = undefined;
    operationMessage = undefined;
    const document = currentDocument;
    if (document === undefined) {
      clientError = "This locale has no resource document to edit.";
      return;
    }
    try {
      let content: string;
      if (mode === "raw") {
        content = value;
      } else {
        const resourceValue: ResourceValue = mode === "advanced"
          ? parseResourceValue(value)
          : value;
        const sourceEntry = selectedRow?.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry;
        content = updateResourceValue(
          drafts[document.path] ?? document.content,
          selectedKey,
          resourceValue,
          sourceEntry,
        );
      }
      drafts[document.path] = content;
      persistDrafts();
      scheduleValidation(document.path, content);
    } catch (error) {
      clientError = errorMessage(error);
      validation = { success: false, diagnostics: [] };
    }
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

  function parseResourceValue(value: string): ResourceValue {
    const parsed: unknown = JSON.parse(value);
    if (typeof parsed === "string") return parsed;
    if (typeof parsed === "object" && parsed !== null && !Array.isArray(parsed)) {
      return parsed as Record<string, unknown>;
    }
    throw new TypeError("A resource value must be a string or structured message object.");
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

{#if externalChanges.length > 0}
  <aside class="external-change-banner" aria-live="polite">
    <div><strong>Files changed outside the editor</strong><span>{externalChanges.join(", ")}</span></div>
    <p>{Object.keys(drafts).length > 0 ? "Your local drafts are still intact." : "Reload to read the latest versions."}</p>
    <button class="secondary" onclick={() => { externalChanges = []; externalFileChanges = []; }}>Keep current view</button>
    <button class="secondary" onclick={reviewExternalChanges}>Compare / merge</button>
    <button class="primary" onclick={() => void loadWorkspace(true)}>Reload files</button>
  </aside>
{/if}

{#if Object.keys(recoveredDrafts).length > 0}
  <aside class="draft-recovery-banner" aria-live="polite">
    <div><strong>Unsaved work was recovered</strong><span>{Object.keys(recoveredDrafts).length} document {Object.keys(recoveredDrafts).length === 1 ? "draft" : "drafts"} found in local application storage.</span></div>
    <button class="secondary" onclick={discardSavedDrafts}>Discard</button>
    <button class="primary" onclick={recoverSavedDrafts}>Restore drafts</button>
  </aside>
{/if}

{#if comparedExternalChange !== undefined}
  <div class="dialog-backdrop" role="presentation">
    <div class="project-dialog external-compare-dialog" role="dialog" aria-modal="true" aria-labelledby="external-compare-title">
      <header><div><p class="eyebrow">External change</p><h2 id="external-compare-title">{comparedExternalChange.path}</h2></div>
        <button class="icon-button" aria-label="Close comparison" onclick={() => comparedExternalChange = undefined}>×</button></header>
      <div class="external-compare-grid">
        <label>Editor base<textarea class="code" readonly value={snapshot?.documents.find((document) => document.path === comparedExternalChange?.path)?.content ?? "File was not previously loaded."}></textarea></label>
        <label>Current disk<textarea class="code" readonly value={comparedExternalChange.content ?? "File was deleted externally."}></textarea></label>
      </div>
      <label class="merge-field">Merged draft<textarea class="code" bind:value={mergedExternalText} spellcheck={false}></textarea></label>
      <footer><button class="secondary" onclick={() => comparedExternalChange = undefined}>Keep current view</button>
        <div><button class="primary" onclick={() => void applyExternalMerge()}>Reload base and keep merged draft</button></div></footer>
    </div>
  </div>
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
{:else if snapshot.catalog === undefined}
  <main class="welcome-shell">
    <header class="welcome-brand">
      <div class="mark small" aria-hidden="true"><span></span></div>
      <div><p class="eyebrow">{labels.eyebrow}</p><h1>{labels.title}</h1></div>
      <select aria-label="Editor language" value={uiLocale} onchange={(event) => uiLocale = event.currentTarget.value}>
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
  <main class="app-shell">
    <aside class="sidebar">
      <header class="brand">
        <div class="mark small" aria-hidden="true"><span></span></div>
        <div>
          <p class="eyebrow">{labels.eyebrow}</p>
          <h1>{labels.title}</h1>
        </div>
        <select aria-label="Editor language" value={uiLocale} onchange={(event) => uiLocale = event.currentTarget.value}>
          <option value="en">EN</option>
          <option value="de">DE</option>
        </select>
      </header>

      <section class="workspace-card" aria-label={labels.workspace}>
        <div class="workspace-title">
          <span class={{ "status-dot": true, warning: !snapshot.success }}></span>
          <div>
            <strong>{snapshot.catalog.id}</strong>
            <span>{snapshot.catalog.locales.length} {snapshot.catalog.locales.length === 1 ? "locale" : "locales"} · schema v{snapshot.catalog.schemaVersion}</span>
          </div>
          <button class="icon-button" title={labels.reload} aria-label={labels.reload} onclick={() => void loadWorkspace(true)}>
            ↻
          </button>
        </div>
        <p title={snapshot.root}>{snapshot.root}</p>
        {#if malformedDocuments.length > 0}
          <div class="workspace-repairs">
            <strong>{malformedDocuments.length} malformed {malformedDocuments.length === 1 ? "file" : "files"}</strong>
            {#each malformedDocuments as document (document.path)}
              <button title={document.path} onclick={() => beginRepair(document)}>Repair {document.path}</button>
            {/each}
          </div>
        {/if}
        <button class="new-project-button" onclick={showOpenWorkspaceDialog}>⌁ Open workspace</button>
        <button class="new-project-button" onclick={openProjectWizard}>＋ New project</button>
      </section>

      <section class="locale-overview" aria-label="Locale coverage">
        {#each snapshot.catalog.locales as locale (locale.tag)}
          {@const state = coverage(rows, locale.tag)}
          {@const percent = state.total === 0 ? 100 : Math.round((state.translated / state.total) * 100)}
          <button
            class={selectedLocale === locale.tag ? "locale-card active" : "locale-card"}
            aria-pressed={selectedLocale === locale.tag}
            onclick={() => selectLocale(locale.tag)}
          >
            <span class="locale-code">{locale.tag}</span>
            <span class="locale-copy">
              <strong>{localeName(locale.tag)}</strong>
              <span>{state.translated}/{state.total} translated</span>
            </span>
            <span class="locale-percent">{percent}%</span>
            <span class="progress"><span style:width={`${percent}%`}></span></span>
          </button>
        {/each}
      </section>

      <div class="search-box">
        <span aria-hidden="true">⌕</span>
        <input bind:this={searchInput} type="search" placeholder={labels.search} bind:value={query} />
        <kbd>⌘ K</kbd>
      </div>

      <div class="filters" aria-label="Message filters">
        {#each [
          { value: "all" as const, label: labels.all, count: rows.length },
          { value: "missing" as const, label: labels.missing, count: rows.filter((row) => row.cells[selectedLocale]?.entry === undefined).length },
          { value: "structured" as const, label: labels.structured, count: rows.filter((row) => row.structured).length },
        ] as option (option.value)}
          <button
            class={filter === option.value ? "active" : ""}
            aria-pressed={filter === option.value}
            onclick={() => filter = option.value}
          >{option.label}<span>{option.count}</span></button>
        {/each}
      </div>

      <nav class="message-list" aria-label="Translation messages">
        {#each visibleRows as row (row.key)}
          {@const cell = row.cells[selectedLocale]}
          <button
            class={selectedKey === row.key ? "message active" : "message"}
            aria-current={selectedKey === row.key ? "true" : undefined}
            onclick={() => selectRow(row)}
          >
            <span class={cell?.entry === undefined ? "translation-state missing" : row.structured ? "translation-state structured" : "translation-state"}></span>
            <span class="message-copy">
              <strong>{row.key}</strong>
              <span>{preview(cell?.entry)}</span>
            </span>
            {#if row.structured}<span class="structure-badge">AST</span>{/if}
          </button>
        {:else}
          <div class="empty-list">
            <span>◇</span>
            <p>{labels.noResults}</p>
          </div>
        {/each}
      </nav>
    </aside>

    <section class="editor-shell">
      <header class="editor-toolbar">
        <div class="locale-tabs" role="tablist" aria-label="Editing locale">
          {#each snapshot.catalog.locales as locale (locale.tag)}
            <button
              role="tab"
              aria-selected={selectedLocale === locale.tag}
              class={selectedLocale === locale.tag ? "active" : ""}
              onclick={() => selectLocale(locale.tag)}
            >
              <span>{locale.tag.toLocaleUpperCase()}</span>
              {localeName(locale.tag)}
              {#if locale.tag === snapshot.catalog.defaultLocale}<i title={labels.defaultLocale}>source</i>{/if}
            </button>
          {/each}
        </div>
        <div class="toolbar-actions">
          <span class={isDirty ? "save-state dirty" : "save-state"}>
            <span></span>{isDirty ? labels.unsaved : operationMessage ?? labels.saved}
          </span>
          <button
            class="primary save-button"
            disabled={!isDirty || saving || validationBusy || validation?.success === false || clientError !== undefined}
            onclick={() => void save()}
          >
            <span aria-hidden="true">↓</span>
            {saving ? labels.saving : labels.save}
            <kbd>⌘ S</kbd>
          </button>
        </div>
      </header>

      {#if selectedRow === undefined}
        <div class="no-selection">
          <span>◇</span>
          <h2>{labels.noSelection}</h2>
        </div>
      {:else}
        <div class="editor-content">
          <header class="message-heading">
            <div>
              <div class="breadcrumb">
                {#each selectedRow.key.split(".") as segment, index (`${segment}-${index}`)}
                  {#if index > 0}<span>/</span>{/if}<span>{segment}</span>
                {/each}
              </div>
              <h2>{selectedRow.key.split(".").at(-1)}</h2>
              {#if selectedRow.description}<p>{selectedRow.description}</p>{/if}
              {#if selectedRow.tags.length > 0}
                <div class="tags">{#each selectedRow.tags as tag (tag)}<span>{tag}</span>{/each}</div>
              {/if}
            </div>
            <div class="message-facts">
              <span>{selectedLocale}</span>
              <span>{currentDocument?.layer ?? "no document"}</span>
              {#if currentCell?.inheritedFrom}<span class="fallback">falls back to {currentCell.inheritedFrom}</span>{/if}
            </div>
          </header>

          <div class="mode-tabs" role="tablist" aria-label="Editing mode">
            <button role="tab" aria-selected={mode === "simple"} class={mode === "simple" ? "active" : ""} onclick={() => chooseMode("simple")}>{labels.simple}</button>
            <button role="tab" aria-selected={mode === "advanced"} class={mode === "advanced" ? "active" : ""} onclick={() => chooseMode("advanced")}>{labels.advanced}</button>
            <button role="tab" aria-selected={mode === "raw"} class={mode === "raw" ? "active" : ""} onclick={() => chooseMode("raw")}>{labels.raw}</button>
          </div>

          <section class="writing-area">
            <div class="field-label">
              <label for="translation-value">{mode === "simple" ? localeName(selectedLocale) : mode === "advanced" ? labels.advanced : currentDocument?.path}</label>
              <span>{editorText.length.toLocaleString()} characters</span>
            </div>
            <textarea
              id="translation-value"
              class={{ code: mode !== "simple", invalid: clientError !== undefined || validation?.success === false }}
              value={editorText}
              placeholder={currentCell?.entry === undefined ? "Add this translation…" : undefined}
              spellcheck={mode === "simple"}
              oninput={(event) => edit(event.currentTarget.value)}
            ></textarea>
            <div class="editor-hints">
              <div>
                {#if mode === "simple"}
                  <span>Use <code>{"{name}"}</code> for declared inputs. Literal braces use <code>{"{{braces}}"}</code>.</span>
                {:else if mode === "advanced"}
                  <span>Edit selectors, variants, formats, and semantic markup as a schema-v2 value.</span>
                {:else}
                  <span>Changes here affect the complete resource document.</span>
                {/if}
              </div>
              {#if mode === "raw"}<button onclick={formatRaw}>Format JSON</button>{/if}
            </div>
          </section>

          <section class="validation-panel" aria-live="polite">
            <header>
              <div>
                <span class={validationBusy ? "validation-icon busy" : errorCount > 0 || clientError ? "validation-icon error" : "validation-icon valid"}>
                  {validationBusy ? "…" : errorCount > 0 || clientError ? "!" : "✓"}
                </span>
                <div>
                  <strong>{validationBusy ? "Validating with the Runic compiler…" : errorCount > 0 || clientError ? labels.invalid : labels.valid}</strong>
                  <span>{labels.diagnostics} · {errorCount} errors · {warningCount} warnings</span>
                </div>
              </div>
              <span class="compiler-badge">compiler · schema v{snapshot.catalog.schemaVersion}</span>
            </header>
            {#if clientError}<p class="client-error">{clientError}</p>{/if}
            {#if diagnostics.length > 0}
              <div class="diagnostics">
                {#each diagnostics as diagnostic (`${diagnostic.path}-${diagnostic.id}-${diagnostic.line}-${diagnostic.column}`)}
                  <button onclick={() => selectDiagnostic(diagnostic)}>
                    <span class={diagnostic.severity}>{diagnostic.severity === "error" ? "×" : "△"}</span>
                    <span><strong>{diagnostic.id}</strong>{diagnostic.message}</span>
                    <code>{diagnostic.path}:{diagnostic.line}:{diagnostic.column}</code>
                  </button>
                {/each}
              </div>
            {/if}
          </section>
        </div>
      {/if}
    </section>
  </main>
{/if}

{#if repairDocument !== undefined}
  <div class="dialog-backdrop">
    <div class="project-dialog repair-dialog" role="dialog" aria-modal="true" aria-labelledby="repair-dialog-title">
      <header><div><p class="eyebrow">Repair mode</p><h2 id="repair-dialog-title">{repairDocument.path}</h2></div>
        <button class="icon-button" aria-label="Close repair editor" disabled={repairBusy} onclick={() => repairDocument = undefined}>×</button></header>
      <div class="project-body">
        <p class="repair-guidance">Edit the raw JSON below. The canonical compiler must accept it before it can replace the file.</p>
        <textarea class="code" aria-label="Malformed JSON document" bind:value={repairText} spellcheck={false}></textarea>
        {#if repairMessage}<p class="project-error" aria-live="polite">{repairMessage}</p>{/if}
      </div>
      <footer><button class="secondary" disabled={repairBusy} onclick={() => repairDocument = undefined}>Cancel</button>
        <div><button class="primary" disabled={repairBusy} onclick={() => void saveRepair()}>{repairBusy ? "Validating…" : "Validate and save"}</button></div></footer>
    </div>
  </div>
{/if}

{#if openDialogOpen}
  <div class="dialog-backdrop" role="presentation">
    <div class="project-dialog open-dialog" role="dialog" aria-modal="true" aria-labelledby="open-dialog-title">
      <header><div><p class="eyebrow">Workspace</p><h2 id="open-dialog-title">Open translation project</h2></div>
        <button class="icon-button" aria-label="Close workspace dialog" disabled={openingWorkspace || pickingWorkspace} onclick={() => openDialogOpen = false}>×</button></header>
      <div class="open-workspace-card dialog-card">
        <label for="dialog-open-directory">Workspace directory</label>
        <div><input id="dialog-open-directory" bind:value={openDirectory} autocomplete="off" />
          <button class="secondary" disabled={pickingWorkspace || openingWorkspace} onclick={() => void pickWorkspace()}>{pickingWorkspace ? "Choosing…" : "Browse…"}</button></div>
        <small>Catalogs are discovered below this boundary. You will choose one if several are found.</small>
      </div>
      {#if clientError}<p class="project-error" aria-live="polite">{clientError}</p>{/if}
      <footer><button class="secondary" disabled={openingWorkspace} onclick={() => openDialogOpen = false}>Cancel</button>
        <div><button class="primary" disabled={openingWorkspace || openDirectory.trim() === ""} onclick={() => void openWorkspace()}>{openingWorkspace ? "Opening…" : "Open workspace"}</button></div></footer>
    </div>
  </div>
{/if}

{#if projectDialogOpen}
  <div class="dialog-backdrop">
    <div class="project-dialog" role="dialog" aria-modal="true" aria-labelledby="project-dialog-title">
      <header>
        <div>
          <p class="eyebrow">Create text resources</p>
          <h2 id="project-dialog-title">New translation project</h2>
        </div>
        <button class="icon-button" aria-label="Close project wizard" disabled={projectBusy} onclick={closeProjectWizard}>×</button>
      </header>

      <ol class="project-steps" aria-label="Project creation steps">
        {#each ["Project", "Languages", "Settings", "Review"] as title, index (title)}
          <li class={{ active: projectStep === index + 1, complete: projectStep > index + 1 }}>
            <span>{projectStep > index + 1 ? "✓" : index + 1}</span>{title}
          </li>
        {/each}
      </ol>

      <div class="project-body">
        {#if projectStep === 1}
          <div class="project-intro">
            <span class="project-glyph">◇</span>
            <div>
              <h3>Where should the translations live?</h3>
              <p>The editor creates a new directory and never overwrites an existing one.</p>
            </div>
          </div>
          <div class="form-grid">
            <label class="wide">New project directory
              <input bind:value={projectDirectory} placeholder="/projects/customer-app/Resources" autocomplete="off" />
              <small>Enter an absolute path or a path relative to the editor process.</small>
            </label>
            <label>Catalog ID
              <input bind:value={projectCatalog} placeholder="product" autocomplete="off" />
              <small>Lowercase letters, numbers, dots, and hyphens.</small>
            </label>
          </div>
        {:else if projectStep === 2}
          <div class="project-intro">
            <span class="project-glyph">文</span>
            <div>
              <h3>Which languages does this project use?</h3>
              <p>One language is fully supported. Add translations now or later.</p>
            </div>
          </div>
          <div class="locale-builder">
            <div class="locale-row source">
              <label>Source/default language
                <input bind:value={projectDefaultLocale} placeholder="de" autocomplete="off" />
              </label>
              <span>Canonical source</span>
            </div>
            {#each projectLocales as locale (locale.id)}
              <div class="locale-row">
                <label>Additional language
                  <input bind:value={locale.tag} placeholder="en" autocomplete="off" />
                </label>
                <label>Fallback
                  <select bind:value={locale.fallback}>
                    <option value="">Default ({projectDefaultLocale || "source"})</option>
                    {#each projectLocales.filter((candidate) => candidate.id !== locale.id && candidate.tag.trim() !== "") as candidate (candidate.id)}
                      <option value={candidate.tag}>{candidate.tag}</option>
                    {/each}
                  </select>
                </label>
                <button class="remove-locale" aria-label={`Remove locale ${locale.tag || "row"}`} onclick={() => removeProjectLocale(locale.id)}>×</button>
              </div>
            {/each}
            <button class="secondary add-locale" onclick={addProjectLocale}>＋ Add another language</button>
          </div>
        {:else if projectStep === 3}
          <div class="project-intro">
            <span class="project-glyph">{"{ }"}</span>
            <div>
              <h3>Generated API and output</h3>
              <p>These defaults work for most .NET and ESM consumers.</p>
            </div>
          </div>
          <div class="form-grid">
            <label>Code namespace
              <input bind:value={projectNamespace} autocomplete="off" />
            </label>
            <label>Generated class
              <input bind:value={projectClassName} autocomplete="off" />
            </label>
            <label>Initial layer
              <input bind:value={projectLayer} autocomplete="off" />
            </label>
          </div>
          <div class="project-options">
            <label><input type="checkbox" bind:checked={projectGenerateEsm} /> <span><strong>Enable ESM output</strong><small>Generate tree-shakeable modules for TypeScript and browser applications.</small></span></label>
            <label><input type="checkbox" bind:checked={projectIncludeStarter} /> <span><strong>Add a starter message</strong><small>Create <code>Application.Name</code> in every language.</small></span></label>
          </div>
        {:else if projectStep === 4 && projectPlan !== undefined}
          <div class="project-intro review">
            <span class="project-glyph">✓</span>
            <div>
              <h3>Ready to create {projectPlan.catalogId}</h3>
              <p>{projectPlan.locales.length} {projectPlan.locales.length === 1 ? "language" : "languages"} · {projectPlan.files.length} files · compiler validated</p>
            </div>
          </div>
          <div class="review-card">
            <div><span>Directory</span><code>{projectPlan.directory}</code></div>
            <div><span>Languages</span><strong>{projectPlan.locales.map((locale) => locale.tag).join(", ")}</strong></div>
          </div>
          <div class="file-preview">
            <h4>Files to create</h4>
            {#each projectPlan.files as file (file)}
              <div><span aria-hidden="true">◇</span><code>{file}</code></div>
            {/each}
          </div>
        {/if}

        {#if projectError}<p class="project-error" aria-live="polite">{projectError}</p>{/if}
      </div>

      <footer>
        <button class="secondary" disabled={projectBusy} onclick={closeProjectWizard}>Cancel</button>
        <div>
          {#if projectStep > 1}
            <button class="secondary" disabled={projectBusy} onclick={() => { projectStep -= 1; projectError = undefined; }}>Back</button>
          {/if}
          {#if projectStep < 4}
            <button class="primary" disabled={projectBusy} onclick={() => void advanceProjectWizard()}>{projectBusy ? "Validating…" : "Continue"}</button>
          {:else}
            <button class="primary" disabled={projectBusy || projectPlan?.ok !== true} onclick={() => void createProject()}>{projectBusy ? "Creating…" : "Create project"}</button>
          {/if}
        </div>
      </footer>
    </div>
  </div>
{/if}

<style>
  :global(*) { box-sizing: border-box; }
  :global(:root) {
    font-family: Inter, "Segoe UI", system-ui, sans-serif;
    color: #edf1ec;
    background: #0b0e0d;
    font-synthesis: none;
  }
  :global(body) { margin: 0; min-width: 980px; min-height: 100vh; overflow: hidden; }
  :global(button), :global(input), :global(textarea), :global(select) { font: inherit; }
  :global(button), :global(select) { color: inherit; }
  :global(button:focus-visible), :global(input:focus-visible), :global(textarea:focus-visible), :global(select:focus-visible) { outline: 2px solid #c8a65a; outline-offset: 2px; }
  :global(::selection) { color: #18150d; background: #d5b566; }

  .app-shell { display: grid; grid-template-columns: 390px minmax(0, 1fr); width: 100vw; height: 100vh; background: radial-gradient(circle at 80% -20%, #26332d 0, transparent 40%), #0c100e; }
  .sidebar { display: flex; flex-direction: column; min-height: 0; border-right: 1px solid #2c332e; background: #101412f2; box-shadow: 1rem 0 4rem #0004; }
  .brand { display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: .8rem; padding: 1.25rem 1.25rem 1rem; }
  .brand h1 { margin: .1rem 0 0; font-family: Georgia, serif; font-size: 1.6rem; font-weight: 500; letter-spacing: -.025em; }
  .eyebrow { margin: 0; color: #bda35f; font-size: .64rem; font-weight: 700; letter-spacing: .15em; text-transform: uppercase; }
  .brand select { align-self: start; border: 1px solid #3c443e; border-radius: .4rem; padding: .32rem .45rem; color: #bac2bc; background: #171c19; font-size: .7rem; }
  .mark { display: grid; place-items: center; width: 4rem; height: 4rem; transform: rotate(45deg); border: 1px solid #9b8144; border-radius: .35rem; background: linear-gradient(145deg, #2e3127, #151b17); box-shadow: inset 0 0 0 .28rem #0e120f, 0 1rem 3rem #0007; }
  .mark span { width: 1.15rem; height: 1.15rem; border: 2px solid #d2b96f; transform: rotate(45deg); }
  .mark.small { width: 2rem; height: 2rem; border-radius: .2rem; box-shadow: inset 0 0 0 .18rem #0e120f; }
  .mark.small span { width: .55rem; height: .55rem; border-width: 1px; }

  .workspace-card { margin: .25rem 1rem 1rem; border: 1px solid #303832; border-radius: .7rem; padding: .8rem; background: linear-gradient(135deg, #1a201c, #141915); }
  .workspace-title { display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: .65rem; }
  .workspace-title div { display: grid; gap: .08rem; min-width: 0; }
  .workspace-title strong { overflow: hidden; color: #f4f1e8; font-size: .82rem; text-overflow: ellipsis; white-space: nowrap; }
  .workspace-title span, .workspace-card > p { color: #818b84; font-size: .68rem; }
  .workspace-card > p { overflow: hidden; margin: .6rem 0 0 1.15rem; text-overflow: ellipsis; white-space: nowrap; }
  .workspace-repairs { display: grid; gap: .35rem; margin: .7rem 0 0 1.15rem; border-left: 2px solid #9e554b; padding-left: .6rem; }
  .workspace-repairs strong { color: #e1a49b; font-size: .6rem; }
  .workspace-repairs button { overflow: hidden; border: 0; padding: 0; color: #c48379; text-align: left; text-overflow: ellipsis; white-space: nowrap; background: transparent; font: .56rem ui-monospace, monospace; cursor: pointer; }
  .workspace-repairs button:hover { color: #efb1a7; }
  .new-project-button { width: 100%; margin-top: .7rem; border: 1px dashed #535744; border-radius: .4rem; padding: .45rem; color: #c3ad70; background: #24261d; font-size: .65rem; cursor: pointer; }
  .new-project-button:hover { border-color: #8d7948; color: #eee1b9; background: #2c2c20; }
  .status-dot { width: .5rem; height: .5rem; border-radius: 50%; background: #65b886; box-shadow: 0 0 .6rem #65b88688; }
  .status-dot.warning { background: #d4a95a; box-shadow: 0 0 .6rem #d4a95a88; }
  .icon-button { display: grid; place-items: center; width: 1.8rem; height: 1.8rem; border: 0; border-radius: .35rem; color: #9da69f; background: transparent; cursor: pointer; }
  .icon-button:hover { color: #f2e5be; background: #2a312c; }

  .locale-overview { display: flex; gap: .5rem; padding: 0 1rem 1rem; overflow-x: auto; scrollbar-width: thin; }
  .locale-card { position: relative; display: grid; flex: 1 0 108px; grid-template-columns: auto 1fr auto; gap: .45rem; min-width: 108px; border: 1px solid #2d3530; border-radius: .55rem; padding: .6rem .6rem .7rem; text-align: left; background: #141916; cursor: pointer; overflow: hidden; }
  .locale-card:hover, .locale-card.active { border-color: #786a43; background: #1b211c; }
  .locale-code { display: grid; place-items: center; width: 1.5rem; height: 1.5rem; border-radius: 50%; color: #d8c68d; background: #313127; font-size: .57rem; font-weight: 800; text-transform: uppercase; }
  .locale-copy { display: grid; min-width: 0; }
  .locale-copy strong { overflow: hidden; font-size: .66rem; text-overflow: ellipsis; white-space: nowrap; }
  .locale-copy span { color: #727c75; font-size: .54rem; white-space: nowrap; }
  .locale-percent { color: #929c95; font-family: ui-monospace, monospace; font-size: .56rem; }
  .progress { position: absolute; right: 0; bottom: 0; left: 0; height: 2px; background: #29302b; }
  .progress span { display: block; height: 100%; background: #ba9d56; }

  .search-box { display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: .5rem; margin: 0 1rem .65rem; border: 1px solid #313a34; border-radius: .55rem; padding: .55rem .65rem; background: #0e1210; }
  .search-box:focus-within { border-color: #8e7845; box-shadow: 0 0 0 2px #9a81451c; }
  .search-box > span { color: #778079; font-size: 1.1rem; }
  .search-box input { min-width: 0; border: 0; outline: 0; color: #e5eae6; background: transparent; font-size: .76rem; }
  .search-box input::placeholder { color: #626b65; }
  kbd { border: 1px solid #39413c; border-bottom-color: #4c5650; border-radius: .25rem; padding: .12rem .3rem; color: #778079; background: #191f1b; font: .55rem ui-monospace, monospace; }
  .filters { display: flex; gap: .25rem; padding: 0 1rem .7rem; border-bottom: 1px solid #262d29; }
  .filters button { display: flex; align-items: center; gap: .35rem; border: 0; border-radius: .4rem; padding: .38rem .5rem; color: #79837c; background: transparent; font-size: .66rem; cursor: pointer; }
  .filters button:hover, .filters button.active { color: #e3d8b9; background: #292b22; }
  .filters button span { min-width: 1.1rem; border-radius: .5rem; padding: .05rem .25rem; color: #8d968f; background: #222824; font-size: .54rem; text-align: center; }
  .message-list { flex: 1; min-height: 0; padding: .4rem .55rem 1rem; overflow-y: auto; scrollbar-color: #3a433d transparent; }
  .message { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: .65rem; width: 100%; border: 1px solid transparent; border-radius: .5rem; padding: .62rem .7rem; text-align: left; background: transparent; cursor: pointer; }
  .message:hover { background: #171d19; }
  .message.active { border-color: #4c4934; background: linear-gradient(90deg, #25261d, #1b211c); box-shadow: inset 2px 0 #c0a45e; }
  .translation-state { width: .42rem; height: .42rem; border-radius: 50%; background: #629c74; }
  .translation-state.missing { border: 1px solid #a06055; background: transparent; }
  .translation-state.structured { background: #b99b52; }
  .message-copy { display: grid; gap: .16rem; min-width: 0; }
  .message-copy strong { overflow: hidden; color: #cbd2cd; font: .68rem ui-monospace, "SFMono-Regular", monospace; text-overflow: ellipsis; white-space: nowrap; }
  .message-copy span { overflow: hidden; color: #68726c; font-size: .64rem; text-overflow: ellipsis; white-space: nowrap; }
  .message.active .message-copy strong { color: #f0e6ca; }
  .structure-badge { border: 1px solid #5d5436; border-radius: .25rem; padding: .12rem .22rem; color: #bfa867; font: .48rem ui-monospace, monospace; }
  .empty-list { display: grid; place-items: center; gap: .4rem; padding: 3rem 1rem; color: #5f6862; text-align: center; }
  .empty-list span { font-size: 2rem; color: #766a46; }
  .empty-list p { margin: 0; font-size: .72rem; }

  .editor-shell { display: flex; flex-direction: column; min-width: 0; min-height: 0; }
  .editor-toolbar { display: flex; align-items: center; justify-content: space-between; gap: 1rem; min-height: 4.65rem; border-bottom: 1px solid #2c332e; padding: 0 1.6rem; background: #111613d9; backdrop-filter: blur(16px); }
  .locale-tabs { display: flex; align-self: stretch; gap: .25rem; overflow-x: auto; }
  .locale-tabs button { position: relative; display: flex; align-items: center; gap: .5rem; border: 0; padding: 0 .8rem; color: #7f8982; background: transparent; font-size: .7rem; cursor: pointer; }
  .locale-tabs button::after { position: absolute; right: .8rem; bottom: 0; left: .8rem; height: 2px; background: transparent; content: ""; }
  .locale-tabs button:hover, .locale-tabs button.active { color: #ede6d2; }
  .locale-tabs button.active::after { background: #bea157; box-shadow: 0 -2px .65rem #bea15766; }
  .locale-tabs button > span { display: grid; place-items: center; width: 1.75rem; height: 1.3rem; border-radius: .25rem; color: #c6b577; background: #2e3026; font-size: .54rem; font-weight: 800; }
  .locale-tabs i { border-radius: .25rem; padding: .12rem .25rem; color: #789782; background: #1d2a21; font-size: .48rem; font-style: normal; text-transform: uppercase; }
  .toolbar-actions { display: flex; align-items: center; gap: 1rem; }
  .save-state { display: flex; align-items: center; gap: .42rem; color: #6f7972; font-size: .65rem; white-space: nowrap; }
  .save-state > span { width: .42rem; height: .42rem; border-radius: 50%; background: #5b7e66; }
  .save-state.dirty { color: #b8a66f; }
  .save-state.dirty > span { background: #ca9f4e; box-shadow: 0 0 .5rem #ca9f4e77; }
  .primary { border: 1px solid #d0b460; border-radius: .45rem; padding: .58rem .85rem; color: #1a170e; background: linear-gradient(#d6bd70, #b9984f); font-weight: 750; cursor: pointer; box-shadow: 0 .35rem 1rem #0005; }
  .primary:hover:not(:disabled) { filter: brightness(1.08); }
  .primary:disabled { cursor: not-allowed; filter: grayscale(.5); opacity: .42; }
  .save-button { display: flex; align-items: center; gap: .5rem; font-size: .68rem; }
  .save-button > span { font-size: .85rem; font-weight: 900; }
  .save-button kbd { border-color: #8f783e; color: #5e4e28; background: #d9c47f; }

  .editor-content { flex: 1; min-height: 0; padding: 2rem clamp(2rem, 5vw, 5rem) 3rem; overflow-y: auto; scrollbar-color: #3b443e transparent; }
  .message-heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 2rem; max-width: 1000px; margin: 0 auto 1.6rem; }
  .breadcrumb { display: flex; gap: .4rem; margin-bottom: .55rem; color: #7f8b83; font: .62rem ui-monospace, monospace; }
  .breadcrumb span:nth-child(even) { color: #454e48; }
  .message-heading h2 { margin: 0; color: #f0eee5; font-family: Georgia, serif; font-size: clamp(2rem, 3.2vw, 3rem); font-weight: 500; letter-spacing: -.035em; }
  .message-heading p { max-width: 46rem; margin: .55rem 0 0; color: #8d978f; font-size: .75rem; line-height: 1.55; }
  .tags { display: flex; flex-wrap: wrap; gap: .35rem; margin-top: .7rem; }
  .tags span { border: 1px solid #3d453f; border-radius: 1rem; padding: .18rem .45rem; color: #89948c; background: #171c19; font-size: .56rem; }
  .message-facts { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: .35rem; padding-top: .4rem; }
  .message-facts span { border: 1px solid #333b36; border-radius: .3rem; padding: .25rem .45rem; color: #879188; background: #151a17; font: .56rem ui-monospace, monospace; }
  .message-facts span.fallback { border-color: #5b4d35; color: #bd9d61; }
  .mode-tabs { display: flex; gap: .2rem; max-width: 1000px; margin: 0 auto; border-bottom: 1px solid #303732; }
  .mode-tabs button { position: relative; border: 0; padding: .65rem .8rem; color: #747f77; background: transparent; font-size: .68rem; cursor: pointer; }
  .mode-tabs button::after { position: absolute; right: .7rem; bottom: -1px; left: .7rem; height: 2px; background: transparent; content: ""; }
  .mode-tabs button:hover, .mode-tabs button.active { color: #e8dec1; }
  .mode-tabs button.active::after { background: #b99d57; }
  .writing-area { max-width: 1000px; margin: 1.1rem auto 0; }
  .field-label { display: flex; justify-content: space-between; margin-bottom: .45rem; }
  .field-label label { color: #aeb7b0; font-size: .68rem; font-weight: 650; }
  .field-label span { color: #5e6861; font-size: .58rem; }
  textarea { display: block; width: 100%; min-height: 240px; resize: vertical; border: 1px solid #38413b; border-radius: .65rem; outline: 0; padding: 1.2rem 1.3rem; color: #eef1ed; background: #111613; font-size: 1rem; line-height: 1.7; caret-color: #d4b760; box-shadow: inset 0 1px .25rem #0006, 0 1rem 4rem #0002; }
  textarea:focus { border-color: #8c7847; box-shadow: 0 0 0 3px #ad914426, inset 0 1px .25rem #0006; }
  textarea.code { min-height: 380px; tab-size: 2; color: #d8dfda; font: .78rem/1.65 "SFMono-Regular", Consolas, monospace; white-space: pre; }
  textarea.invalid { border-color: #955b52; box-shadow: 0 0 0 2px #955b5222; }
  .editor-hints { display: flex; justify-content: space-between; gap: 1rem; padding: .65rem .2rem; color: #66716a; font-size: .62rem; }
  .editor-hints code { border-radius: .2rem; padding: .08rem .25rem; color: #a99d7a; background: #1b211d; font: .58rem ui-monospace, monospace; }
  .editor-hints button { border: 0; padding: 0; color: #b69d61; background: transparent; font-size: .62rem; cursor: pointer; }
  .validation-panel { max-width: 1000px; margin: 1.7rem auto 0; border: 1px solid #303833; border-radius: .65rem; background: #111613; overflow: hidden; }
  .validation-panel > header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: .9rem 1rem; }
  .validation-panel > header > div { display: flex; align-items: center; gap: .7rem; }
  .validation-panel > header > div > div { display: grid; gap: .13rem; }
  .validation-panel strong { color: #cfd6d1; font-size: .68rem; }
  .validation-panel header span { color: #707b73; font-size: .57rem; }
  .validation-icon { display: grid; place-items: center; width: 1.65rem; height: 1.65rem; border-radius: 50%; font-size: .7rem; font-weight: 800; }
  .validation-icon.valid { color: #81c394; background: #203528; }
  .validation-icon.error { color: #e18d80; background: #3b2421; }
  .validation-icon.busy { color: #d7ba6c; background: #3a3321; animation: pulse 1s infinite alternate; }
  .compiler-badge { border: 1px solid #38413b; border-radius: .3rem; padding: .25rem .4rem; font-family: ui-monospace, monospace; }
  .client-error { margin: 0; border-top: 1px solid #4b2f2b; padding: .75rem 1rem; color: #e7a097; background: #2c1d1a; font-size: .68rem; }
  .diagnostics { border-top: 1px solid #2b322e; }
  .diagnostics button { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: start; gap: .7rem; width: 100%; border: 0; border-bottom: 1px solid #252c28; padding: .7rem 1rem; text-align: left; background: #0e1210; cursor: pointer; }
  .diagnostics button:last-child { border-bottom: 0; }
  .diagnostics button:hover { background: #161b18; }
  .diagnostics button > span:first-child { display: grid; place-items: center; width: 1.15rem; height: 1.15rem; border-radius: 50%; font-size: .65rem; }
  .diagnostics .error { color: #e28b7f; background: #3b2521; }
  .diagnostics .warning { color: #d8b35d; background: #3b3320; }
  .diagnostics button > span:nth-child(2) { color: #9ca69f; font-size: .63rem; line-height: 1.45; }
  .diagnostics button strong { margin-right: .45rem; color: #d0d7d2; font-family: ui-monospace, monospace; }
  .diagnostics code { color: #606a63; font: .55rem ui-monospace, monospace; white-space: nowrap; }
  .no-selection, .loading-shell, .fatal-shell { display: grid; place-content: center; place-items: center; height: 100vh; padding: 2rem; color: #8d978f; text-align: center; background: radial-gradient(circle at center, #1d2822, #0b0e0d 65%); }
  .no-selection { height: auto; flex: 1; background: transparent; }
  .no-selection > span { color: #837347; font-size: 3rem; }
  .no-selection h2 { font-family: Georgia, serif; font-weight: 500; }
  .loading-shell { gap: 1.5rem; }
  .loading-shell p { margin: 0; color: #a99150; font-size: .65rem; font-weight: 700; letter-spacing: .18em; text-transform: uppercase; }
  .loading-line { width: 12rem; height: 1px; background: linear-gradient(90deg, transparent, #b89b51, transparent); animation: pulse .8s infinite alternate; }
  .fatal-shell { max-width: none; }
  .fatal-shell .mark { margin-bottom: 2rem; }
  .fatal-shell h1 { max-width: 36rem; margin: .8rem 0; color: #eee9da; font-family: Georgia, serif; font-size: 2.6rem; font-weight: 500; }
  .fatal-shell > p:not(.eyebrow) { max-width: 44rem; margin: 0 0 1.5rem; }
  .welcome-shell { width: 100vw; min-height: 100vh; overflow-y: auto; background: radial-gradient(circle at 70% -10%, #29372f, transparent 42%), #0b0e0d; }
  .welcome-brand { display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: .8rem; border-bottom: 1px solid #2c332e; padding: 1rem 1.5rem; background: #101412dd; }
  .welcome-brand h1 { margin: .1rem 0 0; color: #ece8dc; font-family: Georgia, serif; font-size: 1.35rem; font-weight: 500; }
  .welcome-brand select { border: 1px solid #3c443e; border-radius: .4rem; padding: .35rem .5rem; background: #171c19; }
  .welcome-content { width: min(880px, calc(100% - 3rem)); margin: 0 auto; padding: 4rem 0; }
  .welcome-heading { margin-bottom: 1.6rem; }
  .welcome-heading h2 { margin: .55rem 0; color: #f0ece0; font-family: Georgia, serif; font-size: clamp(2rem, 4vw, 3.2rem); font-weight: 500; letter-spacing: -.035em; }
  .welcome-heading > p:last-child { max-width: 42rem; margin: 0; color: #869188; font-size: .78rem; line-height: 1.6; }
  .catalog-choices { display: grid; gap: .65rem; margin: 1.4rem 0; }
  .catalog-choice { display: grid; grid-template-columns: auto minmax(0, 1fr) auto auto; align-items: center; gap: .9rem; width: 100%; border: 1px solid #343d37; border-radius: .65rem; padding: 1rem; text-align: left; background: #111613; cursor: pointer; }
  .catalog-choice:hover { border-color: #7f7048; background: #181d19; }
  .catalog-choice > span:nth-child(2) { display: grid; gap: .2rem; min-width: 0; }
  .catalog-choice strong { color: #e2e6e3; font-size: .82rem; }
  .catalog-choice small { overflow: hidden; color: #66716a; font: .58rem ui-monospace, monospace; text-overflow: ellipsis; white-space: nowrap; }
  .catalog-metrics { color: #879189; font-size: .6rem; line-height: 1.55; text-align: right; }
  .health { min-width: 4.5rem; border-radius: 1rem; padding: .25rem .45rem; color: #83bc91; background: #1d3224; font-size: .56rem; text-align: center; }
  .health.error { color: #df958a; background: #39231f; }
  .open-workspace-card { display: grid; gap: .45rem; margin-top: 1.2rem; border: 1px solid #343d37; border-radius: .65rem; padding: 1rem; background: #111613; }
  .open-workspace-card label { color: #b8c0ba; font-size: .66rem; font-weight: 650; }
  .open-workspace-card > div { display: grid; grid-template-columns: 1fr auto auto; gap: .6rem; }
  .open-workspace-card input { min-width: 0; border: 1px solid #3a433d; border-radius: .45rem; outline: 0; padding: .68rem .75rem; color: #edf0ed; background: #0c100e; font-size: .75rem; }
  .open-workspace-card input:focus { border-color: #8f7945; }
  .open-workspace-card small { color: #626d66; font-size: .58rem; }
  .welcome-actions { display: flex; gap: .65rem; margin-top: .8rem; }
  .repair-list { margin-top: 1.5rem; border: 1px solid #553b36; border-radius: .65rem; background: #17110f; overflow: hidden; }
  .repair-list > header { padding: .8rem 1rem; background: #251815; }
  .repair-list > header > div { display: flex; justify-content: space-between; }
  .repair-list strong { color: #e1afa7; font-size: .7rem; }
  .repair-list header span { color: #9d6c65; font-size: .6rem; }
  .repair-list > button { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: .7rem; width: 100%; border: 0; border-top: 1px solid #432c28; padding: .7rem 1rem; text-align: left; background: transparent; cursor: pointer; }
  .repair-list > button:hover { background: #2a1b18; }
  .repair-list > button > span { display: grid; place-items: center; width: 1.3rem; height: 1.3rem; border-radius: 50%; color: #f0a398; background: #492923; font-weight: 800; }
  .repair-list code { color: #d5b0aa; font: .62rem ui-monospace, monospace; }
  .repair-list small { color: #9b7069; font-size: .58rem; }
  .repair-guidance { margin: 0 0 .8rem; color: #7f8a82; font-size: .68rem; }
  .repair-dialog textarea { min-height: 420px; }
  .external-change-banner { position: fixed; z-index: 30; right: 1rem; bottom: 1rem; display: grid; grid-template-columns: minmax(12rem, 1fr) auto auto auto auto; align-items: center; gap: .7rem; width: min(58rem, calc(100vw - 2rem)); border: 1px solid #75602f; border-radius: .7rem; padding: .8rem; color: #d8d0bd; background: #201c12; box-shadow: 0 1rem 3rem #0008; }
  .external-change-banner div { display: grid; gap: .2rem; min-width: 0; }
  .external-change-banner strong { color: #eadba9; font-size: .7rem; }
  .external-change-banner span { overflow: hidden; color: #a79a77; font: .58rem ui-monospace, monospace; text-overflow: ellipsis; white-space: nowrap; }
  .external-change-banner p { margin: 0; color: #9a9077; font-size: .6rem; }
  .draft-recovery-banner { position: fixed; z-index: 31; right: 1rem; bottom: 1rem; display: grid; grid-template-columns: minmax(14rem, 1fr) auto auto; align-items: center; gap: .7rem; width: min(38rem, calc(100vw - 2rem)); border: 1px solid #456b56; border-radius: .7rem; padding: .8rem; color: #d3ddd6; background: #142019; box-shadow: 0 1rem 3rem #0008; }
  .draft-recovery-banner div { display: grid; gap: .2rem; }
  .draft-recovery-banner strong { color: #b9dfc7; font-size: .7rem; }
  .draft-recovery-banner span { color: #789282; font-size: .59rem; }
  .recent-projects { margin-top: 1.5rem; border: 1px solid #303832; border-radius: .65rem; background: #101512; overflow: hidden; }
  .recent-projects > header { display: flex; justify-content: space-between; padding: .75rem 1rem; background: #171d19; }
  .recent-projects header strong { color: #bfc8c1; font-size: .68rem; }
  .recent-projects header span { color: #626c65; font-size: .56rem; }
  .recent-projects > button { display: grid; grid-template-columns: 1fr auto; align-items: center; width: 100%; border: 0; border-top: 1px solid #29302c; padding: .7rem 1rem; color: #cbd2cd; text-align: left; background: transparent; cursor: pointer; }
  .recent-projects > button:hover { background: #18201b; }
  .recent-projects button > span { display: grid; gap: .2rem; min-width: 0; }
  .recent-projects code { overflow: hidden; color: #68736b; font: .57rem ui-monospace, monospace; text-overflow: ellipsis; white-space: nowrap; }
  .recent-projects small { color: #69746c; font-size: .55rem; }
  .external-compare-dialog { width: min(70rem, calc(100vw - 3rem)); }
  .open-dialog { width: min(42rem, calc(100vw - 3rem)); }
  .dialog-card { margin-top: 0; }
  .dialog-card > div { grid-template-columns: 1fr auto; }
  .external-compare-grid { display: grid; grid-template-columns: 1fr 1fr; gap: .8rem; }
  .external-compare-grid label, .merge-field { display: grid; gap: .35rem; color: #8e9991; font-size: .62rem; }
  .external-compare-grid textarea { min-height: 12rem; color: #89948c; background: #0d110f; }
  .merge-field { margin-top: .8rem; }
  .merge-field textarea { min-height: 12rem; }
  .dialog-backdrop { position: fixed; z-index: 20; inset: 0; display: grid; place-items: center; padding: 2rem; background: #050706d9; backdrop-filter: blur(10px); }
  .project-dialog { display: flex; flex-direction: column; width: min(760px, 100%); max-height: calc(100vh - 4rem); border: 1px solid #444b44; border-radius: .85rem; background: #111613; box-shadow: 0 2rem 8rem #000b; overflow: hidden; }
  .project-dialog > header { display: flex; align-items: flex-start; justify-content: space-between; padding: 1.35rem 1.5rem 1.1rem; border-bottom: 1px solid #2d342f; }
  .project-dialog h2 { margin: .25rem 0 0; color: #f1ead7; font-family: Georgia, serif; font-size: 1.65rem; font-weight: 500; }
  .project-steps { display: grid; grid-template-columns: repeat(4, 1fr); gap: 0; margin: 0; padding: .8rem 1.5rem; border-bottom: 1px solid #292f2b; list-style: none; background: #0e1210; }
  .project-steps li { display: flex; align-items: center; gap: .45rem; color: #626c65; font-size: .62rem; }
  .project-steps li::after { flex: 1; height: 1px; margin-inline: .35rem; background: #303832; content: ""; }
  .project-steps li:last-child::after { display: none; }
  .project-steps li span { display: grid; place-items: center; width: 1.4rem; height: 1.4rem; border: 1px solid #3c453f; border-radius: 50%; font: .55rem ui-monospace, monospace; }
  .project-steps li.active { color: #e1d2a9; }
  .project-steps li.active span { border-color: #b79a50; color: #211b0f; background: #c8ab61; }
  .project-steps li.complete { color: #7fa48a; }
  .project-steps li.complete span { border-color: #477156; color: #8bc29a; background: #1c3324; }
  .project-body { min-height: 370px; padding: 1.5rem; overflow-y: auto; }
  .project-intro { display: flex; align-items: center; gap: 1rem; margin-bottom: 1.4rem; }
  .project-intro h3 { margin: 0 0 .25rem; color: #e6e9e5; font-size: 1rem; }
  .project-intro p { margin: 0; color: #7d8880; font-size: .7rem; line-height: 1.5; }
  .project-glyph { display: grid; flex: 0 0 auto; place-items: center; width: 2.7rem; height: 2.7rem; border: 1px solid #5a5138; border-radius: .6rem; color: #d1b768; background: #2a291e; font: .9rem ui-monospace, monospace; }
  .project-intro.review .project-glyph { border-color: #406b4d; color: #8ac29a; background: #1d3324; }
  .form-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; }
  .form-grid .wide { grid-column: 1 / -1; }
  .form-grid label, .locale-row label { display: grid; gap: .38rem; color: #b8c0ba; font-size: .66rem; font-weight: 650; }
  .form-grid input, .locale-row input, .locale-row select { width: 100%; border: 1px solid #3a433d; border-radius: .45rem; outline: 0; padding: .68rem .75rem; color: #edf0ed; background: #0c100e; font-size: .75rem; }
  .form-grid input:focus, .locale-row input:focus, .locale-row select:focus { border-color: #8f7945; box-shadow: 0 0 0 2px #9a81451c; }
  .form-grid small { color: #657069; font-size: .58rem; font-weight: 400; }
  .locale-builder { display: grid; gap: .7rem; }
  .locale-row { display: grid; grid-template-columns: 1fr 1fr auto; align-items: end; gap: .7rem; border: 1px solid #303833; border-radius: .55rem; padding: .8rem; background: #0e1210; }
  .locale-row.source { grid-template-columns: 1fr auto; border-color: #514a34; background: #1a1b15; }
  .locale-row.source > span { align-self: center; border-radius: .25rem; padding: .25rem .4rem; color: #9eb9a4; background: #213128; font-size: .55rem; }
  .remove-locale { width: 2.2rem; height: 2.2rem; border: 1px solid #553b36; border-radius: .4rem; color: #d48e82; background: #2c1c1a; cursor: pointer; }
  .secondary { border: 1px solid #3d463f; border-radius: .45rem; padding: .58rem .85rem; color: #aab3ac; background: #181e1a; cursor: pointer; }
  .secondary:hover:not(:disabled) { border-color: #626e65; color: #e1e6e2; }
  .add-locale { justify-self: start; color: #c2ac70; }
  .project-options { display: grid; gap: .65rem; margin-top: 1.2rem; }
  .project-options > label { display: flex; align-items: flex-start; gap: .7rem; border: 1px solid #303833; border-radius: .55rem; padding: .8rem; background: #0e1210; cursor: pointer; }
  .project-options input { margin-top: .15rem; accent-color: #c0a45e; }
  .project-options span { display: grid; gap: .18rem; }
  .project-options strong { color: #cbd2cd; font-size: .68rem; }
  .project-options small { color: #6e7971; font-size: .6rem; }
  .project-options code { color: #b9a46d; }
  .review-card { display: grid; gap: .7rem; border: 1px solid #343d37; border-radius: .55rem; padding: .9rem; background: #0e1210; }
  .review-card > div { display: grid; grid-template-columns: 6rem minmax(0, 1fr); gap: .8rem; align-items: start; }
  .review-card span, .file-preview h4 { color: #69746d; font-size: .58rem; font-weight: 650; text-transform: uppercase; letter-spacing: .08em; }
  .review-card code { overflow: hidden; color: #bfc7c1; font: .62rem ui-monospace, monospace; text-overflow: ellipsis; }
  .review-card strong { color: #d5c99f; font-size: .68rem; }
  .file-preview { margin-top: 1rem; border: 1px solid #303833; border-radius: .55rem; overflow: hidden; }
  .file-preview h4 { margin: 0; padding: .7rem .8rem; background: #171c19; }
  .file-preview > div { display: flex; align-items: center; gap: .6rem; border-top: 1px solid #292f2b; padding: .55rem .8rem; background: #0e1210; }
  .file-preview > div span { color: #8c7c4d; }
  .file-preview code { color: #aeb8b1; font: .62rem ui-monospace, monospace; }
  .project-error { margin: 1rem 0 0; border: 1px solid #633d37; border-radius: .45rem; padding: .7rem .8rem; color: #e7a097; background: #2c1d1a; font-size: .67rem; }
  .project-dialog > footer { display: flex; justify-content: space-between; gap: 1rem; border-top: 1px solid #2d342f; padding: .9rem 1.5rem; background: #0e1210; }
  .project-dialog > footer > div { display: flex; gap: .55rem; }
  .project-dialog button:disabled { cursor: not-allowed; opacity: .45; }
  @keyframes pulse { to { opacity: .35; } }
  @media (max-width: 1100px) {
    .app-shell { grid-template-columns: 335px minmax(0, 1fr); }
    .editor-content { padding-inline: 2rem; }
    .locale-copy { display: none; }
    .locale-card { flex-basis: 70px; grid-template-columns: auto 1fr; }
  }
</style>
