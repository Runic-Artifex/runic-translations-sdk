<script lang="ts">
  import MessageSquareTextIcon from "@lucide/svelte/icons/message-square-text";
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
  import type { PreferencesController } from "#lib/state/preferences-controller.svelte.js";
  import type { PreviewController } from "#lib/state/preview-controller.svelte.js";
  import type { ReviewStore } from "#lib/state/review-store.svelte.js";
  import type { SaveCoordinator } from "#lib/state/save-coordinator.svelte.js";
  import type { SelectionController } from "#lib/state/selection-controller.svelte.js";
  import type { WorkspaceController } from "#lib/state/workspace-controller.svelte.js";
  import type { WorkspaceView } from "#lib/state/workspace-view.svelte.js";
  import { displayNotice, getUiText, notice, type UiMessage } from "#lib/ui-text.js";
  import MessageEditorPane from "./MessageEditorPane.svelte";
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
</script>

<Sidebar.Provider style="--sidebar-width: 21rem; --sidebar-width-mobile: min(20rem, calc(100vw - 1rem));" class="h-svh min-h-0 overflow-hidden">
  <WorkspaceSidebar
    {snapshot}
    {workspace}
    {selection}
    {view}
    {preferences}
    {preview}
    {labels}
    {reviewBlocked}
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
      {onsave}
      {onundo}
      {onredo}
    />

    <div class="flex justify-end border-b px-4 py-2">
      {#if (snapshot.libraries?.length ?? 0) > 0}
        <Button variant="outline" size="sm" class="mr-2" disabled={workspace.loading || library.busy} onclick={() => void library.openLibrary(library.view?.catalog ?? snapshot.libraries![0].catalog)}>{ui.text("ui_library_open")}…</Button>
      {/if}
      <Button variant="outline" size="sm" disabled={workspace.loading || interchange.busy} onclick={() => interchange.open = true}>{ui.text("ui_page_interchange")}…</Button>
    </div>

    {#if selection.selectedRow === undefined}
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
      <div class="editor-content" lang={selectedLocale} dir={uiDirection}>
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
          {labels}
          {clientError}
          {editingBlocked}
          {reviewBlocked}
          {localeName}
          {onmutation}
          {onterminology}
          {onreport}
        />

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
      </div>
    {/if}
  </Sidebar.Inset>
</Sidebar.Provider>

<style>
  .editor-content { flex: 1; min-width: 0; min-height: 0; padding: 1.25rem 1rem 2rem; overflow-x: hidden; overflow-y: auto; scrollbar-color: var(--border) transparent; }
  @media (min-width: 640px) { .editor-content { padding: 1.5rem; } }
  @media (min-width: 1100px) { .editor-content { padding: 2rem clamp(2rem, 5vw, 5rem) 3rem; } }
</style>
