<script lang="ts">
  import AppDialog from "#lib/AppDialog.svelte";
  import * as Alert from "#lib/components/ui/alert/index.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import * as Field from "#lib/components/ui/field/index.js";
  import { Input } from "#lib/components/ui/input/index.js";
  import { Spinner } from "#lib/components/ui/spinner/index.js";
  import type { WorkspaceController } from "#lib/state/workspace-controller.svelte.js";
  import { displayNotice, getUiText, type UiMessage } from "#lib/ui-text.js";

  let { workspace, clientError }: { workspace: WorkspaceController; clientError: UiMessage | undefined } = $props();
  const ui = getUiText();
</script>

{#if workspace.openDialogOpen}
  <AppDialog
    open
    title={ui.text("ui_page_open_project_title")}
    description={ui.text("ui_page_open_project_description")}
    showCloseButton={!workspace.openingWorkspace && !workspace.pickingWorkspace}
    onopenchange={(open) => { if (!workspace.openingWorkspace && !workspace.pickingWorkspace) workspace.openDialogOpen = open; }}
  >
    <Field.Field>
      <Field.Label for="dialog-open-directory">{ui.text("ui_page_workspace_directory")}</Field.Label>
      <div class="flex flex-col gap-2 sm:flex-row">
        <Input id="dialog-open-directory" class="min-w-0 flex-1" bind:value={workspace.openDirectory} autocomplete="off" />
        <Button variant="outline" disabled={workspace.pickingWorkspace || workspace.openingWorkspace} onclick={() => void workspace.pick()}>{workspace.pickingWorkspace ? ui.text("ui_page_choosing") : ui.text("ui_page_browse")}</Button>
      </div>
    </Field.Field>
    {#if clientError}<Alert.Root variant="destructive" class="mt-4"><Alert.Title>{ui.text("ui_page_fatal_could_not_open")}</Alert.Title><Alert.Description>{displayNotice(clientError, ui)}</Alert.Description></Alert.Root>{/if}
    {#snippet footer()}
      <Button variant="outline" disabled={workspace.openingWorkspace} onclick={() => workspace.openDialogOpen = false}>{ui.text("ui_page_cancel")}</Button>
      <Button disabled={workspace.openingWorkspace || workspace.openDirectory.trim() === ""} onclick={() => void workspace.open()}>{#if workspace.openingWorkspace}<Spinner data-icon="inline-start" />{/if}{workspace.openingWorkspace ? ui.text("ui_page_opening") : ui.text("ui_page_open_project_action")}</Button>
    {/snippet}
  </AppDialog>
{/if}
