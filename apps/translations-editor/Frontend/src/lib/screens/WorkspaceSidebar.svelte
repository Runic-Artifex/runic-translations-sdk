<script lang="ts">
  import { messageRowHeight } from "#lib/appearance.js";
  import * as Sidebar from "#lib/components/ui/sidebar/index.js";
  import type { EditorDocument, EditorReviewState, WorkspaceSnapshot } from "#lib/contracts.js";
  import type { EditorLabels } from "#lib/editor-labels.js";
  import EditorSettingsFooter from "#lib/EditorSettingsFooter.svelte";
  import EditorSidebarHeader from "#lib/EditorSidebarHeader.svelte";
  import LocaleSwitcher from "#lib/LocaleSwitcher.svelte";
  import MessageList from "#lib/MessageList.svelte";
  import MessageToolbar from "#lib/MessageToolbar.svelte";
  import SidebarSectionPanels from "#lib/SidebarSectionPanels.svelte";
  import type { PreferencesController } from "#lib/state/preferences-controller.svelte.js";
  import type { PreviewController } from "#lib/state/preview-controller.svelte.js";
  import { catalogViewKey, currentManifest, type SelectionController } from "#lib/state/selection-controller.svelte.js";
  import type { WorkspaceController } from "#lib/state/workspace-controller.svelte.js";
  import type { WorkspaceView } from "#lib/state/workspace-view.svelte.js";
  import WorkspacePanel from "#lib/WorkspacePanel.svelte";

  let {
    snapshot,
    workspace,
    selection,
    view,
    preferences,
    preview,
    labels,
    reviewBlocked,
    languagesOpen = $bindable(true),
    messagesOpen = $bindable(true),
    searchInput = $bindable(null),
    onnewproject,
    onrepair,
    onopenlibrary,
    onaddlocale,
    onaddmessage,
    onabout,
  }: {
    /** The open workspace with its catalog. */
    snapshot: WorkspaceSnapshot;
    workspace: WorkspaceController;
    selection: SelectionController;
    view: WorkspaceView;
    preferences: PreferencesController;
    preview: Pick<PreviewController, "pseudoLocale">;
    labels: EditorLabels;
    /** Whether review data cannot change now: the list's bulk review actions are disabled. */
    reviewBlocked: boolean;
    languagesOpen?: boolean;
    messagesOpen?: boolean;
    searchInput?: HTMLInputElement | null;
    onnewproject(): void;
    onrepair(document: EditorDocument): void;
    onopenlibrary(catalog: string): void;
    onaddlocale(): void;
    onaddmessage(): void;
    onabout(): void;
  } = $props();

  const markVisible = (state: EditorReviewState) => view.markVisible(state);
</script>

<Sidebar.Root collapsible="offcanvas">
  <EditorSidebarHeader
    catalogId={snapshot.catalog?.id ?? ""}
    localeCount={snapshot.catalog?.locales.length ?? 0}
    root={snapshot.root}
    success={snapshot.success}
    reloadLabel={labels.reload}
    recentProjects={workspace.recentProjects}
    onreload={() => void workspace.load(true)}
    onopenworkspace={() => workspace.showOpenDialog()}
    {onnewproject}
    onopenrecent={(project) => void workspace.open(project.catalogId, project.root)}
  />

  <Sidebar.Content class="gap-0 overflow-hidden">

  <WorkspacePanel
    malformedDocuments={view.malformedDocuments}
    reviewError={snapshot.review?.error}
    {onrepair}
    currentManifest={currentManifest(snapshot)}
    catalogs={snapshot.catalogs}
    libraries={snapshot.libraries ?? []}
    onswitchcatalog={(catalog) => void workspace.open(catalog.manifestPaths[0] ?? catalog.id)}
    {onopenlibrary}
  />

  <SidebarSectionPanels bind:languagesOpen bind:messagesOpen>
    {#snippet languages()}
      <LocaleSwitcher
        locales={view.localeSummaries}
        selectedLocale={selection.selectedLocale}
        onselect={(locale) => selection.selectLocale(locale)}
        onmanage={onaddlocale}
        pseudoLocales={snapshot.catalog?.pseudoLocales ?? []}
        activePseudoLocale={preferences.pseudoLocalization ? preview.pseudoLocale : undefined}
        onselectpseudo={(locale) => preferences.selectPseudoLocale(locale)}
        bind:open={languagesOpen}
        uiLocale={preferences.uiLocale}
      />
    {/snippet}

    {#snippet messages()}
      <MessageList
        items={view.messageListItems}
        selectedKey={selection.selectedKey}
        visibleCount={view.visibleRows.length}
        catalogKey={catalogViewKey(snapshot)}
        filterKey={selection.filter === "all" && selection.query.trim() === "" ? "" : `${selection.filter}\n${selection.query.trim()}`}
        groupCounts={view.messageGroupCounts}
        rowHeight={messageRowHeight(preferences.density)}
        selectedHidden={view.selectedHiddenByFilter}
        onclearfilter={() => selection.clearFilter()}
        reviewActionsDisabled={reviewBlocked}
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
          plural: labels.plural,
          variants: labels.variants,
          formatted: labels.formatted,
          stale: labels.stale,
          review: labels.review,
        }}
        onselect={(key) => selection.selectVisible(key, view.visibleRows)}
        onadd={onaddmessage}
        onmarkreview={() => markVisible("needs-review")}
        onapprove={() => markVisible("approved")}
        bind:open={messagesOpen}
      >
        {#snippet toolbar()}
          <MessageToolbar
            bind:query={selection.query}
            bind:filter={selection.filter}
            bind:inputRef={searchInput}
            placeholder={labels.search}
            options={view.filterOptions}
            filterLabel={labels.messageFilters}
          />
        {/snippet}
      </MessageList>
    {/snippet}
  </SidebarSectionPanels>
  </Sidebar.Content>
  <EditorSettingsFooter
    locale={preferences.uiLocale}
    locales={preferences.locales}
    themeMode={preferences.themeMode}
    density={preferences.density}
    pseudoLocalization={preferences.pseudoLocalization}
    uiDirection={preferences.uiDirection}
    onlocalechange={(locale) => preferences.changeUiLocale(locale)}
    onthememodechange={(mode) => preferences.changeThemeMode(mode)}
    ondensitychange={(density) => preferences.changeDensity(density)}
    ontogglepseudo={() => preferences.togglePseudoLocalization()}
    ontoggledirection={() => preferences.toggleUiDirection()}
    {onabout}
  />
  <Sidebar.Rail />
</Sidebar.Root>
