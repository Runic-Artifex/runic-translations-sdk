<script lang="ts">
  // The editor's composition root: it creates the stores and controllers of src/lib/state/, wires them to each other
  // and to the host bridge, and lays out the screens and dialogs. Behaviour lives in the controllers; layout in the
  // screens (src/lib/screens/) and dialogs (src/lib/dialogs/).
  import { onMount } from "svelte";
  import { baseLocale as uiBaseLocale, locales as uiLocales } from "virtual:runic-translations/editor/runtime";
  import { onBridgeConnection, onWindowLocale, windowLocale } from "@runic-artifex/views";
  import { createWindowLocaleSync, matchUiLocale, negotiateUiLocale, preferredLanguages, uiLocaleName, uiLocaleStateKey } from "#lib/ui-locale.js";
  import type { EditorLocaleCatalog, WorkspaceSnapshot } from "#lib/contracts.js";
  import CommandPalette from "#lib/CommandPalette.svelte";
  import { buildEditorCommandPalette } from "#lib/command-palette.js";
  import { draftField, isTextEditingTarget } from "#lib/draft-fields.js";
  import { createEditorBridge } from "#lib/editor-bridge.js";
  import { editorShortcut } from "#lib/editor-keyboard.js";
  import { editorLabels, type EditorLabels } from "#lib/editor-labels.js";
  import InterchangeDialog from "#lib/InterchangeDialog.svelte";
  import LibraryCatalogDialog from "#lib/LibraryCatalogDialog.svelte";
  import { localeDisplayName } from "#lib/locale-text.js";
  import { projectUiText } from "#lib/project-kind.js";
  import {
    clearLocalEditorState,
    configureLocalEditorState,
    getLocalEditorState,
    inspectLocalEditorState,
    loadLocalEditorState,
    removeLocalEditorState,
    resumeLocalEditorState,
    setLocalEditorState,
  } from "#lib/local-state.js";
  import { createUiText, errorMessage, errorNotice, notice, setUiText, UiNoticeError } from "#lib/ui-text.js";
  import { editorWindowTitle } from "#lib/window-title.js";
  import AboutDialog from "#lib/dialogs/AboutDialog.svelte";
  import ExternalChangeDialog from "#lib/dialogs/ExternalChangeDialog.svelte";
  import MutationDialog from "#lib/dialogs/MutationDialog.svelte";
  import OpenProjectDialog from "#lib/dialogs/OpenProjectDialog.svelte";
  import ProjectWizardDialog from "#lib/dialogs/ProjectWizardDialog.svelte";
  import QualityReportDialog from "#lib/dialogs/QualityReportDialog.svelte";
  import RepairDialog from "#lib/dialogs/RepairDialog.svelte";
  import TerminologyDialog from "#lib/dialogs/TerminologyDialog.svelte";
  import FatalScreen from "#lib/screens/FatalScreen.svelte";
  import LoadingScreen from "#lib/screens/LoadingScreen.svelte";
  import TransactionRecoveryScreen from "#lib/screens/TransactionRecoveryScreen.svelte";
  import WelcomeScreen from "#lib/screens/WelcomeScreen.svelte";
  import WorkspaceNotices from "#lib/screens/WorkspaceNotices.svelte";
  import WorkspaceShell from "#lib/screens/WorkspaceShell.svelte";
  import { AboutController } from "#lib/state/about-controller.svelte.js";
  import { DiagnosticsStore } from "#lib/state/diagnostics-store.svelte.js";
  import { DraftStore } from "#lib/state/draft-store.svelte.js";
  import { ExternalChangesController } from "#lib/state/external-changes.svelte.js";
  import { EditorFeedback } from "#lib/state/feedback.svelte.js";
  import { HistoryController } from "#lib/state/history-controller.svelte.js";
  import { InterchangeController } from "#lib/state/interchange-controller.svelte.js";
  import { LibraryController } from "#lib/state/library-controller.svelte.js";
  import { MessageDraftStore } from "#lib/state/message-draft-store.svelte.js";
  import { MessageEditingController } from "#lib/state/message-editing.svelte.js";
  import { MutationController } from "#lib/state/mutation-controller.svelte.js";
  import { NextMessageController } from "#lib/state/next-message.svelte.js";
  import { PreferencesController } from "#lib/state/preferences-controller.svelte.js";
  import { defaultPreviewSample, PreviewController } from "#lib/state/preview-controller.svelte.js";
  import { ProjectWizardController } from "#lib/state/project-wizard.svelte.js";
  import { RecoveryController } from "#lib/state/recovery-controller.svelte.js";
  import { RepairController } from "#lib/state/repair-controller.svelte.js";
  import { ReviewStore } from "#lib/state/review-store.svelte.js";
  import { SaveCoordinator } from "#lib/state/save-coordinator.svelte.js";
  import { currentManifest, SelectionController } from "#lib/state/selection-controller.svelte.js";
  import { UnsavedWork } from "#lib/state/unsaved-work.svelte.js";
  import { WorkspaceController } from "#lib/state/workspace-controller.svelte.js";
  import { WorkspaceView } from "#lib/state/workspace-view.svelte.js";

  const bridge = createEditorBridge();
  const windowLocaleSync = createWindowLocaleSync({ current: windowLocale, send: (locale) => bridge.setUiLocale(locale) });
  configureLocalEditorState(bridge);
  // The native editor state: preferences, recent projects and the recovery copies of unsaved work.
  const nativeStorage = { get: getLocalEditorState, set: setLocalEditorState, remove: removeLocalEditorState };
  const confirmMessage = (messageKey: string): boolean => confirm(ui.text(messageKey));
  const historyBusy = (): boolean => history.busy;
  const snapshot = (): WorkspaceSnapshot | undefined => workspace.snapshot;

  const feedback = new EditorFeedback();
  const preferences: PreferencesController = new PreferencesController({
    // Until the native state loads, the page shows the OS preference negotiated against the editor catalog.
    initialLocale: negotiateUiLocale(preferredLanguages(), uiLocales, uiBaseLocale),
    locales: uiLocales,
    storage: nativeStorage,
    windowLocale: windowLocaleSync,
    preview: (): PreviewController => previewController,
    refreshPreview: () => selection.refreshPreview(),
  });
  // The interface text in the project's wording: a content-editing project (D074.2) speaks of texts, not translations,
  // and so does the composer while a translation project's base locale (the source text) is selected.
  const ui = projectUiText(createUiText(() => preferences.uiLocale), () => view.projectKind, () => view.textRole);
  setUiText(ui);
  const localeName = (tag: string): string => localeDisplayName(tag, preferences.uiLocale);

  // Unsaved document text and the one queue per document that changes it (composer, authoring, raw validation, fixes).
  const draftStore: DraftStore = new DraftStore({ bridge, snapshot, storage: nativeStorage });
  // The empty starters of missing translations, edited in memory until they have text (#225 P2).
  const messageDrafts = new MessageDraftStore({ bridge, drafts: draftStore });
  const diagnosticsStore = new DiagnosticsStore(snapshot);
  const review = new ReviewStore({ bridge, snapshot, errorNotice, storage: nativeStorage });
  // The one save: every unsaved document, then the review changes.
  const saveCoordinator: SaveCoordinator = new SaveCoordinator({
    bridge,
    drafts: draftStore,
    review,
    snapshot,
    validated: (path, result) => diagnosticsStore.record("save", path, result),
    install: (next) => workspace.install(next, false),
    // RTR0090 follows the review file: after a save of review data the diagnostics are requested again.
    refreshed: (saved, diagnostics) => diagnosticsStore.refreshWorkspace(saved, diagnostics),
  });
  const previewController: PreviewController = new PreviewController({
    bridge,
    pseudoLocalization: (): boolean => preferences.pseudoLocalization,
    defaultSample: (type) => defaultPreviewSample(type, ui),
    errorNotice,
  });
  const repair = new RepairController({
    bridge, drafts: draftStore, feedback, historyBusy, confirm: confirmMessage,
    install: (next, resetSelection) => workspace.install(next, resetSelection),
    configureEditor: () => selection.configureEditor(),
  });
  const unsaved = new UnsavedWork({ save: saveCoordinator, drafts: draftStore, messageDrafts, review, repair, confirm: confirmMessage });
  const selection: SelectionController = new SelectionController({
    bridge, snapshot, drafts: draftStore, messageDrafts, review, preview: previewController, diagnostics: diagnosticsStore, feedback,
    savedHasMessage: (key, locale) => workspace.savedHasMessage(key, locale),
    editingBlocked: (): boolean => editorMutationBlocked,
    storage: nativeStorage,
  });
  const externalChanges = new ExternalChangesController({
    bridge, snapshot, drafts: draftStore, review, unsaved, feedback, historyBusy,
    idle: () => !(workspace.loading || workspace.openingWorkspace || saveCoordinator.saving || workspace.snapshot === undefined),
    setLoading: (loading) => workspace.loading = loading,
    install: (next) => workspace.install(next, true),
    configureEditor: () => selection.configureEditor(),
  });
  const workspace: WorkspaceController = new WorkspaceController({
    bridge, drafts: draftStore, messageDrafts, review, diagnostics: diagnosticsStore, save: saveCoordinator, selection, unsaved,
    feedback, historyBusy, storage: nativeStorage,
    reloaded: (kind) => {
      if (kind === "load") recovery.reloaded();
      externalChanges.clear();
    },
  });
  const recovery = new RecoveryController({ bridge, workspace, drafts: draftStore, review, selection, feedback, historyBusy });
  const view: WorkspaceView = new WorkspaceView({
    snapshot, selection, drafts: draftStore, review, diagnostics: diagnosticsStore, ui, localeName,
    samples: () => previewController.samples,
    reviewBlocked: () => reviewMutationBlocked,
    labels: () => labels,
    uiDirection: () => preferences.uiDirection,
  });
  const editing = new MessageEditingController({
    bridge, snapshot, selection, drafts: draftStore, messageDrafts, diagnostics: diagnosticsStore, preview: previewController, repair,
    feedback,
    diagnosticSources: () => view.diagnosticSources,
    editingBlocked: () => editorMutationBlocked,
  });
  const history: HistoryController = new HistoryController({
    bridge, snapshot, drafts: draftStore, diagnostics: diagnosticsStore, selection, feedback, ui,
    load: () => workspace.load(false),
    install: (next) => workspace.install(next, false),
    historyBlocked: (): boolean => historyBlocked,
    draftHistoryBlocked: (): boolean => draftHistoryBlocked,
  });
  // Next missing and Save and next (D074.1): the next message to work on in tree order, after the one save.
  const next: NextMessageController = new NextMessageController({
    selection, view, localeName,
    saveAll: () => workspace.saveAll(),
    busy: () => saveCoordinator.saving || editorMutationBlocked,
  });
  const interchange = new InterchangeController({ bridge, workspace, review, unsaved, feedback });
  const wizard = new ProjectWizardController({
    bridge, unsaved, feedback, interchange, historyBusy,
    install: (next) => workspace.install(next, true),
  });
  const mutation = new MutationController({
    bridge, snapshot, selection, unsaved, feedback, historyBusy,
    load: () => workspace.load(false),
    install: (next) => workspace.install(next, true),
  });
  // Library catalogs (T5d): the library is layer 0, read-only; the application's overrides layer 1.
  const library = new LibraryController({
    bridge,
    hasUnsavedWork: () => unsaved.hasUnsavedWork,
    projectChanged: async (deferred) => {
      if (deferred) workspace.projectReloadPending = true;
      else await workspace.load(false);
    },
  });
  const about = new AboutController({
    bridge, historyBusy, confirm: confirmMessage,
    localState: { inspect: inspectLocalEditorState, clear: clearLocalEditorState },
    localStateCleared: () => {
      workspace.recentProjects = [];
      draftStore.recovered = {};
      review.recovered = undefined;
    },
    writeClipboard: (text) => navigator.clipboard.writeText(text),
  });

  // The SDK's CLDR locales (add-language picker, regional preview locales); loaded once.
  let localeCatalog = $state.raw<EditorLocaleCatalog>();
  let searchInput = $state<HTMLInputElement | null>(null);
  let commandPaletteOpen = $state(false);
  let terminologyDialogOpen = $state(false);
  let reportDialogOpen = $state(false);
  let languagesOpen = $state(true);
  let messagesOpen = $state(true);
  let artifactPreviewOpen = $state(false);

  let uiLocale = $derived(preferences.uiLocale);
  let uiDirection = $derived(preferences.uiDirection);
  let labels: EditorLabels = $derived(editorLabels(uiLocale, view.projectKind, view.textRole));
  $effect(() => {
    document.documentElement.lang = uiLocale;
    document.documentElement.dir = uiDirection;
  });
  // "<catalog> – Runic Translations Editor", marked while work is unsaved; the product name without a catalog (#225 P14).
  let windowTitle = $derived(editorWindowTitle(ui, workspace.snapshot?.pendingTransaction === undefined ? workspace.snapshot?.catalog?.id : undefined, unsaved.hasUnsavedWork));
  // What keeps the saved-change history (and the dialogs that replace the workspace) from running.
  let historyBlocked: boolean = $derived(
    history.busy || workspace.loading || saveCoordinator.saving || review.saving || draftStore.validating || mutation.busy ||
    recovery.busy || previewController.busy || wizard.busy || workspace.openingWorkspace || workspace.pickingWorkspace || repair.busy ||
    about.diagnosticBusy || about.busy || interchange.busy ||
    unsaved.hasUnsavedWork || mutation.open || wizard.open || workspace.openDialogOpen ||
    externalChanges.compared !== undefined || repair.document !== undefined || terminologyDialogOpen || reportDialogOpen || about.open,
  );
  let editorMutationBlocked: boolean = $derived(history.busy || saveCoordinator.saving || review.saving || workspace.loading || recovery.busy || workspace.openingWorkspace);
  // Version 1 editor approvals waiting to move into the review file are shown read-only.
  let reviewMutationBlocked: boolean = $derived(editorMutationBlocked || review.saving || workspace.snapshot?.review?.migrationPending === true);
  // Unsaved edits have their own undo history (#225 P4); it waits while an operation that replaces drafts runs.
  let draftHistoryBlocked: boolean = $derived(
    history.busy || workspace.loading || saveCoordinator.saving || review.saving || mutation.busy || recovery.busy || wizard.busy ||
    workspace.openingWorkspace || workspace.pickingWorkspace || repair.busy || interchange.busy || mutation.open || wizard.open ||
    workspace.openDialogOpen || externalChanges.compared !== undefined || repair.document !== undefined,
  );

  const undo = (): void => history.undo();
  const redo = (): void => history.redo();
  const saveAll = async (): Promise<void> => { await workspace.saveAll(); };
  const saveAndNext = (): void => void next.saveAndNext();
  const stepMessage = (direction: "next" | "previous"): void => {
    if (!editorMutationBlocked) next.step(direction);
  };
  const openMutation = (kind: Parameters<MutationController["prepare"]>[0]): void => void mutation.prepare(kind);
  const toggleArtifactPreview = (): void => { artifactPreviewOpen = !artifactPreviewOpen; };

  let paletteCommands = $derived.by(() => {
    const { canUndo, canRedo, undoLabel, redoLabel } = history;
    return buildEditorCommandPalette(
      {
        reloadWorkspace: () => void workspace.load(true),
        openWorkspaceDialog: () => workspace.showOpenDialog(),
        createProject: () => wizard.start(),
        openInterchange: () => interchange.open = true,
        saveAll: () => void saveAll(),
        undo,
        redo,
        nextMessage: () => stepMessage("next"),
        previousMessage: () => stepMessage("previous"),
        saveAndNext,
        focusMessageSearch: () => searchInput?.focus(),
        discardReview: () => review.discard(),
        setMessageReviewState: (state) => view.setCurrentReviewState(state),
        markVisibleMessages: (state) => view.markVisible(state),
        openTerminology: () => terminologyDialogOpen = true,
        openQualityReport: () => reportDialogOpen = true,
        showAbout: () => void about.show(),
        createDiagnosticBundle: () => void about.createDiagnosticBundle(),
        setEditorMode: (mode) => selection.chooseMode(mode),
        selectLocale: (locale) => selection.selectLocale(locale),
        setUiLocale: (locale) => preferences.changeUiLocale(locale),
        setThemeMode: (mode) => preferences.changeThemeMode(mode),
        toggleLanguagesSection: () => languagesOpen = !languagesOpen,
        toggleMessagesSection: () => messagesOpen = !messagesOpen,
        togglePseudoLocalization: () => preferences.togglePseudoLocalization(),
        toggleUiDirection: () => preferences.toggleUiDirection(),
        toggleArtifactPreview,
      },
      {
        locales: view.localeSummaries.map((locale) => ({ tag: locale.tag, name: locale.name })),
        selectedLocale: selection.selectedLocale,
        editorMode: selection.mode,
        uiLocale,
        uiLocales: uiLocales.map((tag) => ({ tag, name: uiLocaleName(tag) })),
        themeMode: preferences.themeMode,
        workspaceReady: workspace.snapshot !== undefined,
        searchAvailable: searchInput !== null,
        unsaved: saveCoordinator.hasUnsaved && !editorMutationBlocked,
        canUndo,
        canRedo,
        stepKind: next.kind,
        canStepNext: next.nextTarget !== undefined && !editorMutationBlocked,
        canStepPrevious: next.previousTarget !== undefined && !editorMutationBlocked,
        saveAndNextKind: next.saveAndNextKind,
        canSaveAndNext: selection.selectedRow !== undefined && !editorMutationBlocked && !saveCoordinator.saving && !next.running,
        undoTitle: undoLabel,
        redoTitle: redoLabel,
        reviewEditable: selection.selectedRow !== undefined && !reviewMutationBlocked &&
          workspace.snapshot?.review?.error === undefined,
        reviewDirty: review.dirty,
        pseudoLocalization: preferences.pseudoLocalization,
        uiDirection,
        artifactPreviewOpen,
        projectKind: view.projectKind,
        textRole: view.textRole,
      },
      ui,
    );
  });

  onMount(() => {
    let disposed = false;
    preferences.restore();
    const colorScheme = window.matchMedia("(prefers-color-scheme: dark)");
    const updateSystemTheme = (): void => preferences.systemThemeChanged();
    colorScheme.addEventListener("change", updateSystemTheme);
    // The CLDR locale list only feeds suggestions; without it the fields stay free text.
    void bridge.listLocales().then((catalog) => { if (!disposed) localeCatalog = catalog; }, () => undefined);
    void (async () => {
      try {
        const nativeStateRecovered = await loadLocalEditorState();
        if (disposed) return;
        preferences.restore();
        workspace.loadRecentProjects();
        if (nativeStateRecovered) {
          feedback.operationMessage = notice("ui_feedback_state_recovered");
        }
      } catch (error) {
        feedback.clientError = error instanceof UiNoticeError ? error.notice : notice("ui_feedback_state_load_failed", { error: errorMessage(error) });
      } finally {
        // The stored choice wins over the OS preference; either becomes the window's locale, also when the native
        // state did not load.
        if (!disposed) preferences.applyUiLocale(matchUiLocale(getLocalEditorState(uiLocaleStateKey), uiLocales) ?? preferences.uiLocale);
      }
      if (!disposed) await workspace.load(false);
    })();
    const interval = window.setInterval(() => void externalChanges.check(), 2_000);
    // Another page of this window, or the application, can switch the window's locale.
    const stopWindowLocale = onWindowLocale((locale) => {
      const match = matchUiLocale(windowLocaleSync.windowChanged(locale), uiLocales);
      if (match !== undefined) preferences.uiLocale = match;
    });
    // A reconnected host keeps the locale it had; a switch made while it was unreachable is sent again, and so is a
    // local-state write the lost connection interrupted.
    const stopConnection = onBridgeConnection((state) => {
      if (state !== "connected") return;
      windowLocaleSync.connected();
      resumeLocalEditorState();
    });
    return () => {
      disposed = true;
      stopWindowLocale();
      stopConnection();
      colorScheme.removeEventListener("change", updateSystemTheme);
      window.clearInterval(interval);
    };
  });

  // The selected locale and message of the open catalog, restored on the next launch (#225 P24).
  $effect(() => selection.persistSelection());

  function handleKeyboard(event: KeyboardEvent): void {
    const shortcut = editorShortcut(event, isTextEditingTarget(event.target), draftField(event.target) !== undefined,
      { widgetTarget: stepWidgetTarget(event.target), messageSteps: !view.contentProject });
    if (shortcut === undefined) return;
    event.preventDefault();
    if (shortcut === "undo") undo();
    else if (shortcut === "redo") redo();
    else if (shortcut === "save") void saveAll();
    else if (shortcut === "save-and-next") { if (workspace.snapshot?.catalog !== undefined) saveAndNext(); }
    else if (shortcut === "next-message") stepMessage("next");
    else if (shortcut === "previous-message") stepMessage("previous");
    else if (shortcut === "toggle-command-search") commandPaletteOpen = !commandPaletteOpen;
    else if (shortcut === "toggle-pseudo-localization") preferences.togglePseudoLocalization();
    else if (shortcut === "toggle-right-to-left") preferences.toggleUiDirection();
    else toggleArtifactPreview();
  }

  // Alt+↑/↓ and Ctrl+Enter belong to a select, combobox, listbox or menu, and to an open dialog: no message step there.
  function stepWidgetTarget(target: EventTarget | null): boolean {
    return target instanceof Element &&
      target.closest('select, [role="combobox"], [role="listbox"], [role="menu"], [role="menubar"], [role="dialog"], [role="alertdialog"]') !== null;
  }

  // The browser's own undo of a draft field (from its context menu) is the editor's: the field is rewritten from the
  // draft after each change, so the browser's undo stack of it is not the draft's.
  function handleBeforeInput(event: InputEvent): void {
    if (draftField(event.target) === undefined) return;
    if (event.inputType === "historyUndo") {
      event.preventDefault();
      undo();
    } else if (event.inputType === "historyRedo") {
      event.preventDefault();
      redo();
    }
  }

  function protectDraft(event: BeforeUnloadEvent): void {
    if (!unsaved.hasUnsavedWork) return;
    event.preventDefault();
  }
</script>

<svelte:head>
  <title>{windowTitle}</title>
    <meta name="description" content={ui.text("ui_page_meta_description")} />
</svelte:head>
<svelte:window onkeydown={handleKeyboard} onbeforeinput={handleBeforeInput} onbeforeunload={protectDraft} />

<WorkspaceNotices {workspace} {externalChanges} {recovery} drafts={draftStore} {review} hasUnsavedWork={unsaved.hasUnsavedWork} />
<ExternalChangeDialog {externalChanges} documents={workspace.snapshot?.documents ?? []} />

{#if workspace.loading}
  <LoadingScreen eyebrow={labels.eyebrow} />
{:else if workspace.snapshot === undefined}
  <FatalScreen
    eyebrow={labels.eyebrow}
    reloadLabel={labels.reload}
    recoveryReloadRequired={recovery.reloadRequired}
    recoveryReloadMessage={recovery.reloadMessage}
    clientError={feedback.clientError}
    onreload={() => void workspace.load(false)}
    onopenother={() => workspace.openDialogOpen = true}
  />
{:else if workspace.snapshot.pendingTransaction !== undefined}
  <TransactionRecoveryScreen
    transaction={workspace.snapshot.pendingTransaction}
    busy={recovery.busy}
    clientError={feedback.clientError}
    onrecover={(mode) => void recovery.recoverTransaction(mode)}
  />
{:else if workspace.snapshot.catalog === undefined}
  <WelcomeScreen
    snapshot={workspace.snapshot}
    {workspace}
    {labels}
    {uiLocale}
    {uiLocales}
    malformedDocuments={view.malformedDocuments}
    clientError={feedback.clientError}
    onlocalechange={(locale) => preferences.changeUiLocale(locale)}
    oncreateproject={() => wizard.start()}
    onrepair={(document) => repair.begin(document)}
  />
{:else}
  <WorkspaceShell
    snapshot={workspace.snapshot}
    catalog={workspace.snapshot.catalog}
    {workspace}
    {selection}
    {view}
    {editing}
    {history}
    {preferences}
    preview={previewController}
    {review}
    drafts={draftStore}
    {messageDrafts}
    diagnostics={diagnosticsStore}
    save={saveCoordinator}
    {library}
    {interchange}
    {labels}
    {localeCatalog}
    clientError={feedback.clientError}
    operationMessage={feedback.operationMessage}
    editingBlocked={editorMutationBlocked}
    reviewBlocked={reviewMutationBlocked}
    {localeName}
    bind:languagesOpen
    bind:messagesOpen
    bind:searchInput
    bind:artifactPreviewOpen
    {next}
    onsave={() => void saveAll()}
    onsaveandnext={saveAndNext}
    onundo={undo}
    onredo={redo}
    onnewproject={() => wizard.start()}
    onrepair={(document) => repair.begin(document)}
    onmutation={openMutation}
    onabout={() => void about.show()}
    onterminology={() => terminologyDialogOpen = true}
    onreport={() => reportDialogOpen = true}
  />
{/if}

<AboutDialog {about} />
<TerminologyDialog bind:open={terminologyDialogOpen} {review} blocked={reviewMutationBlocked} />
<QualityReportDialog
  bind:open={reportDialogOpen}
  locale={selection.selectedLocale}
  findings={view.localeQualityFindings}
  messageCount={view.qualityKeySet.size}
/>

<LibraryCatalogDialog
  bind:open={library.open}
  bind:locale={library.locale}
  busy={library.busy}
  project={`${workspace.snapshot?.root ?? ""}\n${currentManifest(workspace.snapshot)}`}
  libraries={workspace.snapshot?.libraries ?? []}
  library={library.view}
  notice={library.notice}
  conflict={library.conflict}
  onselect={(catalog) => library.select(catalog)}
  onoverride={(key, locale, text, expectedRevision) => library.override(key, locale, text, expectedRevision)}
  onsave={(key, locale, text, expectedRevision) => library.saveOverride(key, locale, text, expectedRevision)}
  onrevert={(key, locale, expectedRevision) => library.revert(key, locale, expectedRevision)}
  onkeep={(key, locale, expectedRevision) => library.keep(key, locale, expectedRevision)}
  onaddlocale={(locale) => library.addLocale(locale)}
  onreload={() => library.reload()}
/>

<InterchangeDialog
  bind:open={interchange.open}
  busy={interchange.busy}
  bind:xliffDirectory={interchange.xliffDirectory}
  bind:xliffImportPath={interchange.xliffImportPath}
  bind:reviewPath={interchange.reviewExportPath}
  bind:reviewImportPath={interchange.reviewImportPath}
  xliffExport={interchange.xliffExport}
  xliffPreview={interchange.xliffPreview}
  reviewExport={interchange.reviewExport}
  reviewPreview={interchange.reviewPreview}
  onexportxliff={() => void interchange.exportXliff()}
  onpreviewxliff={() => void interchange.previewXliffImport()}
  onapplyxliff={() => void interchange.applyXliffImport()}
  onexportreview={() => void interchange.exportReviewJson()}
  onpreviewreview={() => void interchange.previewReviewJsonImport()}
  onapplyreview={() => void interchange.applyReviewJsonImport()}
  bind:catalogImportFrom={interchange.catalogImportFrom}
  bind:catalogImportSource={interchange.catalogImportSource}
  bind:catalogImportOverwrite={interchange.catalogImportOverwrite}
  catalogImportPlan={interchange.catalogImportPlan}
  onpreviewcatalog={() => void interchange.previewCatalogImport()}
  onapplycatalog={() => void interchange.applyCatalogImport()}
/>

<RepairDialog
  {repair}
  documents={workspace.snapshot?.documents ?? []}
  schemaVersion={workspace.snapshot?.catalog?.schemaVersion ?? 1}
  {labels}
  disabled={editorMutationBlocked}
  onselectdiagnostic={(diagnostic) => void editing.selectDiagnostic(diagnostic)}
  onfix={(diagnostic, fix) => void editing.applyDiagnosticFix(diagnostic, fix)}
/>
<OpenProjectDialog {workspace} clientError={feedback.clientError} />
<MutationDialog {mutation} catalog={workspace.snapshot?.catalog} {localeCatalog} addMessageLabel={labels.addMessage} {localeName} />
<ProjectWizardDialog {wizard} {localeCatalog} />

<CommandPalette open={commandPaletteOpen} commands={paletteCommands} onopenchange={(open) => commandPaletteOpen = open} />

<style>
  :global(*) { box-sizing: border-box; }
  /* The font, colours and form-control resets come from layout.css (the brand tokens and Tailwind's preflight), so the
     components' own colour utilities apply. */
  :global(body) { margin: 0; min-width: 0; min-height: 100vh; overflow: hidden; }
  :global(button:focus-visible), :global(input:focus-visible), :global(textarea:focus-visible), :global(select:focus-visible) { outline: 2px solid var(--ring); outline-offset: 2px; }
  :global(::selection) { color: var(--primary-foreground); background: var(--primary); }
</style>
