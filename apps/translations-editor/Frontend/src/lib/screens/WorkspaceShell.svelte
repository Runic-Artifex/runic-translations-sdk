<script lang="ts">
  import MessageSquareTextIcon from "@lucide/svelte/icons/message-square-text";
  import PanelsTopLeftIcon from "@lucide/svelte/icons/panels-top-left";
  import Rows3Icon from "@lucide/svelte/icons/rows-3";
  import { tick } from "svelte";
  import { Button } from "#lib/components/ui/button/index.js";
  import * as Empty from "#lib/components/ui/empty/index.js";
  import * as Sidebar from "#lib/components/ui/sidebar/index.js";
  import type { EditorCatalog, EditorDocument, EditorLocaleCatalog, WorkspaceSnapshot } from "#lib/contracts.js";
  import type { EditorLabels } from "#lib/editor-labels.js";
  import EditorToolbar from "#lib/EditorToolbar.svelte";
  import type { DiagnosticsStore } from "#lib/state/diagnostics-store.svelte.js";
  import type { DraftStore } from "#lib/state/draft-store.svelte.js";
  import type { HistoryController } from "#lib/state/history-controller.svelte.js";
  import type { InterchangeController } from "#lib/state/interchange-controller.svelte.js";
  import type { LibraryController } from "#lib/state/library-controller.svelte.js";
  import type { MessageDraftStore } from "#lib/state/message-draft-store.svelte.js";
  import type { MessageEditingController } from "#lib/state/message-editing.svelte.js";
  import type { MutationKind } from "#lib/state/mutation-controller.svelte.js";
  import { noneNotice, type NextMessageController } from "#lib/state/next-message.svelte.js";
  import type { PreferencesController } from "#lib/state/preferences-controller.svelte.js";
  import type { PreviewController } from "#lib/state/preview-controller.svelte.js";
  import type { ReviewStore } from "#lib/state/review-store.svelte.js";
  import type { SaveCoordinator } from "#lib/state/save-coordinator.svelte.js";
  import type { SelectionController } from "#lib/state/selection-controller.svelte.js";
  import type { WorkspaceController } from "#lib/state/workspace-controller.svelte.js";
  import type { WorkspaceView } from "#lib/state/workspace-view.svelte.js";
  import { stepTitle } from "#lib/command-palette.js";
  import { displayNotice, getUiText, notice, type UiMessage } from "#lib/ui-text.js";
  import type { WorkspaceLayout } from "#lib/list-view.js";
  import MessageEditorPane from "./MessageEditorPane.svelte";
  import MessageListView from "./MessageListView.svelte";
  import PreviewSection from "./PreviewSection.svelte";
  import ValidationArea from "./ValidationArea.svelte";
  import WorkspaceSidebar from "./WorkspaceSidebar.svelte";

  let {
    snapshot,
    catalog,
    workspace,
    selection,
    view,
    editing,
    history,
    preferences,
    preview,
    review,
    drafts,
    messageDrafts,
    diagnostics,
    save,
    library,
    interchange,
    next,
    labels,
    localeCatalog,
    clientError,
    operationMessage,
    editingBlocked,
    reviewBlocked,
    localeName,
    languagesOpen = $bindable(true),
    messagesOpen = $bindable(true),
    searchInput = $bindable(null),
    artifactPreviewOpen = $bindable(false),
    onsave,
    onsaveandnext,
    onundo,
    onredo,
    onnewproject,
    onrepair,
    onmutation,
    onabout,
    onterminology,
    onreport,
  }: {
    snapshot: WorkspaceSnapshot;
    /** The open catalog. */
    catalog: EditorCatalog;
    workspace: WorkspaceController;
    selection: SelectionController;
    view: WorkspaceView;
    editing: MessageEditingController;
    history: HistoryController;
    preferences: PreferencesController;
    preview: PreviewController;
    review: ReviewStore;
    drafts: DraftStore;
    messageDrafts: MessageDraftStore;
    diagnostics: DiagnosticsStore;
    save: SaveCoordinator;
    library: LibraryController;
    interchange: InterchangeController;
    /** Next missing and Save and next (D074.1). */
    next: NextMessageController;
    labels: EditorLabels;
    localeCatalog: EditorLocaleCatalog | undefined;
    clientError: UiMessage | undefined;
    operationMessage: UiMessage | undefined;
    editingBlocked: boolean;
    reviewBlocked: boolean;
    localeName(tag: string): string;
    languagesOpen?: boolean;
    messagesOpen?: boolean;
    searchInput?: HTMLInputElement | null;
    artifactPreviewOpen?: boolean;
    onsave(): void;
    /** Saves, then selects the next message of the same kind (Ctrl+Enter). */
    onsaveandnext(): void;
    onundo(): void;
    onredo(): void;
    onnewproject(): void;
    onrepair(document: EditorDocument): void;
    onmutation(kind: MutationKind): void;
    onabout(): void;
    onterminology(): void;
    onreport(): void;
  } = $props();
  const ui = getUiText();

  const selectedLocale = $derived(selection.selectedLocale);
  const uiDirection = $derived(preferences.uiDirection);
  // What the toolbar names as unsaved: text in some documents, review status, or both.
  const unsavedDetail = $derived.by(() => {
    const summary = save.unsavedSummary;
    const parts = [
      ...(summary.documents.length > 0 ? [ui.text("ui_count_unsaved_documents", { count: summary.documents.length })] : []),
      ...(summary.review ? [ui.text("ui_toolbar_unsaved_review")] : []),
    ];
    return parts.length === 0 ? "" : ui.text("ui_toolbar_unsaved_changes", { changes: new Intl.ListFormat(preferences.uiLocale, { type: "conjunction" }).format(parts) });
  });
  // Save needs unsaved work and no save or validation running; unsaved documents must be valid, review changes need a
  // readable review file.
  const saveDisabled = $derived(!save.hasUnsaved || save.saving || review.saving || drafts.validating ||
    (save.unsavedSummary.documents.length > 0
      ? diagnostics.latest?.success === false || clientError !== undefined
      : snapshot.review?.error !== undefined));
  const nextLabel = $derived(stepTitle("next", next.kind, ui));
  // The workbench or the side-by-side list (D074.1), one selection for both.
  const layout = $derived(preferences.layout);
  const layouts: { value: WorkspaceLayout; key: string; icon: typeof Rows3Icon }[] = [
    { value: "workbench", key: "ui_view_workbench", icon: PanelsTopLeftIcon },
    { value: "list", key: "ui_view_list", icon: Rows3Icon },
  ];

  /** A row of the list opens in the workbench: the same selection, its editor focused. */
  async function openInWorkbench(): Promise<void> {
    if (selection.mode !== "translation") selection.chooseMode("translation");
    preferences.changeLayout("workbench");
    await tick();
    document.querySelector<HTMLElement>('[data-testid="editor-column"] textarea[data-draft-field]')?.focus();
  }
</script>

<!-- The workbench (D074.1): the message tree in the sidebar, the editor in the middle and a rail with the preview and
     the diagnostics on the right. Below 1100px the rail folds under the editor column; below 768px the sidebar
     collapses behind the toolbar's toggle. -->
<Sidebar.Provider style="--sidebar-width: clamp(16rem, 22vw, 21rem); --sidebar-width-mobile: min(20rem, calc(100vw - 1rem));" class="h-svh min-h-0 overflow-hidden">
  <WorkspaceSidebar
    {snapshot}
    {workspace}
    {selection}
    {view}
    {preferences}
    {preview}
    {labels}
    {reviewBlocked}
    {localeName}
    bind:languagesOpen
    bind:messagesOpen
    bind:searchInput
    {onnewproject}
    {onrepair}
    onopenlibrary={(catalogId) => void library.openLibrary(catalogId)}
    onaddlocale={() => onmutation("add-locale")}
    onaddmessage={() => onmutation("create-key")}
    {onabout}
  />

  <Sidebar.Inset class="editor-shell min-h-0 min-w-0 overflow-hidden">
    <EditorToolbar
      {saveDisabled}
      saving={save.saving}
      saveLabel={labels.save}
      savingLabel={labels.saving}
      saveState={displayNotice(operationMessage ?? notice("app_saved"), ui)}
      unsaved={save.hasUnsaved}
      {unsavedDetail}
      canUndo={history.canUndo}
      canRedo={history.canRedo}
      historyBusy={history.busy}
      undoLabel={history.undoLabel}
      redoLabel={history.redoLabel}
      showNext={!view.contentProject}
      {nextLabel}
      nextDisabled={next.nextTarget === undefined || editingBlocked}
      nextDetail={next.nextTarget === undefined ? displayNotice(noneNotice(next.kind, localeName(selectedLocale)), ui) : ""}
      {onsave}
      {onundo}
      {onredo}
      onnext={() => next.step("next")}
    >
      {#snippet leading()}
        <div class="inline-flex shrink-0 items-center gap-0.5 rounded-md border bg-background p-0.5" role="group" aria-label={ui.text("ui_view_label")} data-testid="view-switch">
          {#each layouts as option (option.value)}
            <Button
              variant={layout === option.value ? "secondary" : "ghost"}
              size="xs"
              aria-pressed={layout === option.value}
              aria-label={ui.text(option.key)}
              title={ui.text(option.key)}
              data-layout={option.value}
              onclick={() => preferences.changeLayout(option.value)}
            >
              <option.icon data-icon="inline-start" aria-hidden="true" />
              <span class="hidden sm:inline">{ui.text(option.key)}</span>
            </Button>
          {/each}
        </div>
        {#if (snapshot.libraries?.length ?? 0) > 0}
          <Button variant="outline" size="sm" disabled={workspace.loading || library.busy} onclick={() => void library.openLibrary(library.view?.catalog ?? snapshot.libraries![0].catalog)}>{ui.text("ui_library_open")}…</Button>
        {/if}
        <Button variant="outline" size="sm" disabled={workspace.loading || interchange.busy} onclick={() => interchange.open = true}>{ui.text("ui_page_interchange")}…</Button>
      {/snippet}
    </EditorToolbar>

    {#if layout === "list"}
      <div class="flex min-h-0 min-w-0 flex-1 flex-col" dir={uiDirection}>
        <MessageListView
          {catalog}
          {selection}
          {view}
          {editing}
          {diagnostics}
          {labels}
          {clientError}
          {editingBlocked}
          revision={history.revision}
          {localeName}
          onopenworkbench={() => void openInWorkbench()}
        />
      </div>
    {:else if selection.selectedRow === undefined}
      <Empty.Root>
        <Empty.Header>
          <Empty.Media variant="icon" class="text-primary">
            <MessageSquareTextIcon aria-hidden="true" />
          </Empty.Media>
          <Empty.Title class="font-[620]">{labels.noSelection}</Empty.Title>
          <Empty.Description>{ui.text("ui_page_empty_selection_description")}</Empty.Description>
        </Empty.Header>
      </Empty.Root>
    {:else}
      <div class="workbench" lang={selectedLocale} dir={uiDirection} data-testid="workbench">
        <div class="editor-column" data-testid="editor-column">
          <MessageEditorPane
            {snapshot}
            {catalog}
            row={selection.selectedRow}
            {selection}
            {view}
            {editing}
            {review}
            {drafts}
            {messageDrafts}
            {diagnostics}
            {history}
            {next}
            {labels}
            {clientError}
            {editingBlocked}
            {reviewBlocked}
            {localeName}
            {onmutation}
            {onterminology}
            {onreport}
            {onsaveandnext}
          />
        </div>

        <aside class="editor-rail" aria-label={ui.text("ui_workbench_rail")} data-testid="editor-rail">
          {#if selection.mode === "translation"}
            <PreviewSection
              {preview}
              {selection}
              {localeCatalog}
              pseudoLocalization={preferences.pseudoLocalization}
              {uiDirection}
              bind:artifactPreviewOpen
            />
          {/if}

          <ValidationArea
            {catalog}
            row={selection.selectedRow}
            locale={selectedLocale}
            {drafts}
            {diagnostics}
            {view}
            {editing}
            {labels}
            {clientError}
          />
        </aside>
      </div>
    {/if}
  </Sidebar.Inset>
</Sidebar.Provider>

<style>
  /* Narrow windows: one column that scrolls as a whole, the rail under the editor column. */
  .workbench { flex: 1; min-width: 0; min-height: 0; overflow-x: hidden; overflow-y: auto; scrollbar-color: var(--border) transparent; }
  .editor-column { min-width: 0; padding: 1.25rem 1rem 1rem; }
  .editor-rail { display: grid; align-content: start; gap: 1rem; min-width: 0; border-top: 1px solid var(--border); padding: 1.25rem 1rem 2rem; }
  @media (min-width: 640px) {
    .editor-column { padding: 1.5rem 1.5rem 1rem; }
    .editor-rail { padding: 1.5rem; }
  }
  /* Wide windows: the editor column and the rail side by side, each scrolling on its own. */
  @media (min-width: 1100px) {
    .workbench { display: grid; grid-template-columns: minmax(0, 1fr) clamp(18rem, 26vw, 26rem); overflow: hidden; }
    .editor-column { min-height: 0; overflow-y: auto; padding: 2rem clamp(1.5rem, 3vw, 3rem) 3rem; scrollbar-color: var(--border) transparent; }
    .editor-rail { min-height: 0; overflow-y: auto; border-top: 0; border-inline-start: 1px solid var(--border); padding: 1.5rem 1.25rem 2rem; background: var(--card); scrollbar-color: var(--border) transparent; }
  }
</style>
