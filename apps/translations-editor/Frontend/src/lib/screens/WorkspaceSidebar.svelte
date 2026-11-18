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
  import { getUiText } from "#lib/ui-text.js";

  let {
    snapshot,
    workspace,
    selection,
    view,
    preferences,
    preview,
    labels,
    reviewBlocked,
    localeName,
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
    localeName(tag: string): string;
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
  const ui = getUiText();
  // A content-editing project (D074.2) names its language and its number of texts: "German · 13 texts".
  const summary = $derived(view.contentProject && snapshot.catalog !== undefined
    ? ui.text("ui_content_project_summary", { language: localeName(snapshot.catalog.defaultLocale), count: selection.rows.length })
    : undefined);
</script>

<Sidebar.Root collapsible="offcanvas">
  <EditorSidebarHeader
    catalogId={snapshot.catalog?.id ?? ""}
    localeCount={snapshot.catalog?.locales.length ?? 0}
    {summary}
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

  <SidebarSectionPanels bind:languagesOpen bind:messagesOpen singleLanguage={view.contentProject}>
    {#snippet languages()}
      <LocaleSwitcher
        content={view.contentProject}
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
        filterKey={view.filter === "all" && selection.query.trim() === "" ? "" : `${view.filter}\n${selection.query.trim()}`}
        groupCounts={view.messageGroupCounts}
        rowHeight={messageRowHeight(preferences.density)}
        selectedHidden={view.selectedHiddenByFilter}
        onclearfilter={() => selection.clearFilter()}
        translationStates={!view.editingSource}
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
            bind:filter={() => view.filter, (filter) => selection.filter = filter}
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
