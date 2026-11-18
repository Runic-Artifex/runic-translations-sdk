<script lang="ts">
  import ArrowDownIcon from "@lucide/svelte/icons/arrow-down";
  import { stepTitle } from "#lib/command-palette.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import * as Kbd from "#lib/components/ui/kbd/index.js";
  import { Spinner } from "#lib/components/ui/spinner/index.js";
  import type { EditorCatalog, WorkspaceSnapshot } from "#lib/contracts.js";
  import type { EditorLabels } from "#lib/editor-labels.js";
  import EditorModeSwitcher from "#lib/EditorModeSwitcher.svelte";
  import MessageHeading from "#lib/MessageHeading.svelte";
  import { overrideLayer, type TranslationRow } from "#lib/resource-model.js";
  import ReviewWorkflow from "#lib/ReviewWorkflow.svelte";
  import type { DiagnosticsStore } from "#lib/state/diagnostics-store.svelte.js";
  import type { DraftStore } from "#lib/state/draft-store.svelte.js";
  import type { HistoryController } from "#lib/state/history-controller.svelte.js";
  import type { MessageDraftStore } from "#lib/state/message-draft-store.svelte.js";
  import type { MessageEditingController } from "#lib/state/message-editing.svelte.js";
  import type { MutationKind } from "#lib/state/mutation-controller.svelte.js";
  import type { NextMessageController } from "#lib/state/next-message.svelte.js";
  import type { ReviewStore } from "#lib/state/review-store.svelte.js";
  import type { SelectionController } from "#lib/state/selection-controller.svelte.js";
  import type { WorkspaceView } from "#lib/state/workspace-view.svelte.js";
  import { shortcutLabel } from "#lib/shortcut-labels.js";
  import TranslationEditor from "#lib/TranslationEditor.svelte";
  import { displayNotice, getUiText, notice, type UiMessage } from "#lib/ui-text.js";

  let {
    snapshot,
    catalog,
    row,
    selection,
    view,
    editing,
    review,
    drafts,
    messageDrafts,
    diagnostics,
    history,
    next,
    labels,
    clientError,
    editingBlocked,
    reviewBlocked,
    localeName,
    onmutation,
    onterminology,
    onreport,
    onsaveandnext,
  }: {
    snapshot: WorkspaceSnapshot;
    /** The open catalog. */
    catalog: EditorCatalog;
    /** The selected message. */
    row: TranslationRow;
    selection: SelectionController;
    view: WorkspaceView;
    editing: MessageEditingController;
    review: ReviewStore;
    drafts: Pick<DraftStore, "authoringPending">;
    messageDrafts: Pick<MessageDraftStore, "pending">;
    diagnostics: Pick<DiagnosticsStore, "latest">;
    history: Pick<HistoryController, "revision">;
    next: Pick<NextMessageController, "saveAndNextOffered" | "saveAndNextKind" | "notice" | "running">;
    labels: EditorLabels;
    clientError: UiMessage | undefined;
    /** Whether an operation in progress keeps the editor from changing. */
    editingBlocked: boolean;
    /** Whether review data cannot change now. */
    reviewBlocked: boolean;
    localeName(tag: string): string;
    /** Opens the structural change `kind` of the selected message. */
    onmutation(kind: MutationKind): void;
    onterminology(): void;
    onreport(): void;
    /** Saves, then selects the next message of the same kind (Ctrl+Enter). */
    onsaveandnext(): void;
  } = $props();
  const ui = getUiText();
  const saveAndNextLabel = $derived(stepTitle("save_and_next", next.saveAndNextKind, ui));
</script>

<!-- The selected message: its heading, review workflow, editor mode and the translation editor. -->
<MessageHeading
  messageKey={row.key}
  messagePath={row.path}
  description={row.description}
  tags={row.tags}
  locale={selection.selectedLocale}
  overrideLayer={overrideLayer(snapshot, selection.currentDocument?.layer)}
  inheritedFrom={selection.currentCell?.inheritedFrom}
  onrename={() => onmutation("rename-key")}
  onduplicate={() => onmutation("duplicate-key")}
  ondelete={() => onmutation("delete-key")}
/>

<ReviewWorkflow
  state={view.currentReviewState}
  notTranslated={selection.currentCell?.entry === undefined && (view.currentReview === undefined || view.currentReview.implicit === true)}
  dirty={review.dirty}
  message={displayNotice(review.message ?? notice("ui_page_project_notes"), ui)}
  disabled={snapshot.review?.error !== undefined || reviewBlocked}
  stale={view.currentIsStale}
  sourceDiff={view.currentSourceDiff}
  sourceLocale={catalog.defaultLocale}
  terminologyCount={review.terminology.length}
  qualityCount={view.localeQualityFindings.length}
  note={view.currentReview?.note ?? ""}
  qualityIssues={view.currentQuality}
  suggestions={view.memorySuggestions}
  onstatechange={(state) => view.setCurrentReviewState(state)}
  onconfirm={() => view.confirmCurrentReview()}
  onnotechange={(note) => view.setCurrentReviewNote(note)}
  ondiscard={() => review.discard()}
  {onterminology}
  {onreport}
  onqualityfilter={() => selection.filter = "quality"}
  onsuggestion={(value) => editing.applySuggestion(value)}
  onfix={(diagnostic, fix) => void editing.applyDiagnosticFix(diagnostic, fix)}
/>

<EditorModeSwitcher
  mode={selection.mode}
  simpleLabel={labels.simple}
  rawLabel={labels.raw}
  onchange={(mode) => selection.chooseMode(mode)}
/>

<TranslationEditor
  identity={`${selection.selectedDocumentPath}\u0000${selection.selectedKey}\u0000${selection.selectedLocale}`}
  mode={selection.mode}
  locale={selection.selectedLocale}
  label={selection.mode === "translation" ? localeName(selection.selectedLocale) : selection.currentDocument?.path ?? ui.text("ui_page_resource_document")}
  value={selection.mode === "translation" && selection.currentStarter !== undefined ? selection.currentStarter.text : selection.editorText}
  resourceValue={selection.currentStarter?.text ?? selection.currentCell?.entry?.value ?? ""}
  sourceLocale={catalog.defaultLocale}
  sourceLocaleName={localeName(catalog.defaultLocale)}
  sourceEntry={row.cells[catalog.defaultLocale]?.entry}
  authoring={selection.currentStarter?.projection ?? selection.currentCell?.entry?.authoring}
  authoringBusy={drafts.authoringPending > 0 || messageDrafts.pending > 0}
  onoperation={(operation) => editing.applyAuthoringOperation(operation)}
  maxLength={selection.currentStarter !== undefined ? selection.currentStarter.maxLength : selection.currentCell?.entry?.maxLength}
  lengthWarning={view.currentQuality.find((issue) => issue.code === "RTR0098")?.message}
  starterLoading={selection.starterLoading}
  oncopysource={selection.currentStarter !== undefined && selection.sourceAuthoring?.supported === true ? () => editing.copySource() : undefined}
  sourceSelection={selection.sourceSelection}
  missing={selection.currentCell?.entry === undefined}
  invalid={clientError !== undefined || diagnostics.latest?.success === false}
  disabled={editingBlocked}
  onresourcechange={(value) => editing.editResourceValue(value)}
  onrawchange={(value) => editing.edit(value)}
  onformatraw={() => editing.formatRaw()}
  revision={history.revision}
/>

<!-- Save and next (D074.1): shown while the selected message was missing or outdated when it was selected. The one save,
     then the next message of the same kind; a blocked save stays and says why. -->
{#if next.saveAndNextOffered && selection.mode === "translation" || next.notice !== undefined}
  <div class="mx-auto mt-3 flex w-full max-w-[1000px] flex-wrap items-center justify-end gap-x-3 gap-y-2" data-testid="save-and-next-bar">
    {#if next.notice !== undefined}
      <p class="mr-auto min-w-0 text-xs text-muted-foreground" role="status" data-testid="workbench-notice">{displayNotice(next.notice, ui)}</p>
    {/if}
    {#if next.saveAndNextOffered && selection.mode === "translation"}
      <Button
        size="sm"
        disabled={editingBlocked || next.running}
        onclick={onsaveandnext}
        aria-keyshortcuts="Control+Enter Meta+Enter"
        title={`${saveAndNextLabel} (${shortcutLabel("Mod+Enter")})`}
        data-testid="save-and-next"
      >
        {#if next.running}<Spinner data-icon="inline-start" />{:else}<ArrowDownIcon data-icon="inline-start" />{/if}
        {saveAndNextLabel}
        <Kbd.Root class="hidden sm:inline-flex">{shortcutLabel("Mod+Enter")}</Kbd.Root>
      </Button>
    {/if}
  </div>
{/if}
