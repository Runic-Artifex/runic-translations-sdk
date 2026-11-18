<script lang="ts">
  import * as Alert from "#lib/components/ui/alert/index.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import type { DraftStore } from "#lib/state/draft-store.svelte.js";
  import type { ExternalChangesController } from "#lib/state/external-changes.svelte.js";
  import type { RecoveryController } from "#lib/state/recovery-controller.svelte.js";
  import type { ReviewStore } from "#lib/state/review-store.svelte.js";
  import type { WorkspaceController } from "#lib/state/workspace-controller.svelte.js";
  import { getUiText } from "#lib/ui-text.js";

  let {
    workspace,
    externalChanges,
    recovery,
    drafts,
    review,
    hasUnsavedWork,
  }: {
    workspace: WorkspaceController;
    externalChanges: ExternalChangesController;
    recovery: RecoveryController;
    drafts: Pick<DraftStore, "recovered">;
    review: Pick<ReviewStore, "recovered">;
    hasUnsavedWork: boolean;
  } = $props();
  const ui = getUiText();
</script>

<!-- The notices about the workspace on disk: a project change that waits for unsaved work, files another program
     changed, and unsaved work an earlier session left. -->
{#if workspace.projectReloadPending}
  <Alert.Root class="mx-4 mt-2" data-testid="project-reload-pending">
    <Alert.Title>{ui.text("ui_library_project_changed_title")}</Alert.Title>
    <Alert.Description class="flex flex-wrap items-center gap-2">
      <p>{ui.text("ui_library_project_changed_unsaved")}</p>
      <Button size="xs" onclick={() => void workspace.load(true)}>{ui.text("ui_page_external_changes_reload_files")}</Button>
      <Button variant="ghost" size="xs" onclick={() => workspace.projectReloadPending = false}>{ui.text("ui_page_keep_current_view")}</Button>
    </Alert.Description>
  </Alert.Root>
{/if}

{#if externalChanges.paths.length > 0}
  <div class="pointer-events-none fixed inset-x-2 bottom-2 z-50 mx-auto max-w-[calc(100vw-1rem)] sm:inset-x-4 sm:bottom-4 sm:max-w-4xl">
    <Alert.Root class="pointer-events-auto pr-4 shadow-xl" aria-live="polite">
      <Alert.Title>{ui.text("ui_page_external_changes_title")}</Alert.Title>
      <Alert.Description class="min-w-0">
        <p class="truncate font-mono text-xs">{externalChanges.paths.join(", ")}</p>
        <p>{hasUnsavedWork ? ui.text("ui_page_external_changes_unsaved_intact") : ui.text("ui_page_external_changes_reload_latest")}</p>
      </Alert.Description>
      <Alert.Action class="static col-span-full mt-2 flex flex-wrap justify-end gap-2">
        <Button variant="ghost" size="xs" onclick={() => externalChanges.clear()}>{ui.text("ui_page_keep_current_view")}</Button>
        <Button variant="outline" size="xs" onclick={() => externalChanges.compare()}>{ui.text("ui_page_external_changes_compare_merge")}</Button>
        <Button size="xs" onclick={() => void workspace.load(true)}>{ui.text("ui_page_external_changes_reload_files")}</Button>
      </Alert.Action>
    </Alert.Root>
  </div>
{/if}

{#if recovery.hasRecoveredWork}
  <div class="pointer-events-none fixed inset-x-2 bottom-2 z-50 mx-auto max-w-[calc(100vw-1rem)] sm:inset-x-4 sm:bottom-4 sm:max-w-2xl">
    <Alert.Root class="pointer-events-auto pr-4 shadow-xl" aria-live="polite" data-testid="recovered-work">
      <Alert.Title>{ui.text("ui_page_recovered_drafts_title")}</Alert.Title>
      <Alert.Description>
        {#if Object.keys(drafts.recovered).length > 0}<p>{ui.text("ui_count_recovered_drafts", { count: Object.keys(drafts.recovered).length })}</p>{/if}
        {#if review.recovered !== undefined}<p>{ui.text("ui_page_recovered_review")}</p>{/if}
      </Alert.Description>
      <Alert.Action class="static col-span-full mt-2 flex flex-col gap-2 min-[360px]:flex-row min-[360px]:justify-end">
        <Button variant="ghost" size="xs" onclick={() => recovery.discardRecoveredWork()}>{ui.text("ui_page_discard")}</Button>
        <Button size="xs" onclick={() => recovery.restoreRecoveredWork()}>{ui.text("ui_page_recovered_drafts_restore")}</Button>
      </Alert.Action>
    </Alert.Root>
  </div>
{/if}
