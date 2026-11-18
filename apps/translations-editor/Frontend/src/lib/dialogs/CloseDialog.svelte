<script lang="ts">
  import AppDialog from "#lib/AppDialog.svelte";
  import * as Alert from "#lib/components/ui/alert/index.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import { Spinner } from "#lib/components/ui/spinner/index.js";
  import type { CloseGuard } from "#lib/state/close-guard.svelte.js";
  import { displayNotice, getUiText } from "#lib/ui-text.js";

  // The question before the window closes with unsaved work (#225 P19): Save, Discard or Cancel.
  let { guard }: { guard: CloseGuard } = $props();
  const ui = getUiText();
</script>

{#if guard.open}
  <AppDialog
    open
    title={ui.text("ui_close_title")}
    description={ui.text("ui_close_description")}
    class="sm:max-w-lg"
    bodyClass="grid gap-3"
    showCloseButton={!guard.busy}
    onopenchange={(open) => { if (!open) guard.cancel(); }}
  >
    <ul class="grid gap-1.5 text-sm" data-close-summary>
      {#if guard.summary.documents.length > 0}
        <li>
          {ui.text("ui_close_unsaved_documents", { count: guard.summary.documents.length })}
          <span class="block font-mono text-xs text-muted-foreground">{guard.summary.documents.join(", ")}</span>
        </li>
      {/if}
      {#if guard.summary.review}<li>{ui.text("ui_close_unsaved_review")}</li>{/if}
      {#if guard.summary.repair !== undefined}<li>{ui.text("ui_close_unsaved_repair", { path: guard.summary.repair })}</li>{/if}
    </ul>
    {#if guard.message}
      <Alert.Root variant="destructive" role="alert">
        <Alert.Title>{ui.text("ui_close_still_open")}</Alert.Title>
        <Alert.Description>{displayNotice(guard.message, ui)}</Alert.Description>
      </Alert.Root>
    {/if}
    {#snippet footer()}
      <Button variant="outline" disabled={guard.busy} onclick={() => guard.cancel()}>{ui.text("ui_page_cancel")}</Button>
      <Button variant="destructive" disabled={guard.busy} onclick={() => void guard.discard()}>{ui.text("ui_close_discard")}</Button>
      <Button disabled={guard.busy} onclick={() => void guard.save()}>{#if guard.busy}<Spinner data-icon="inline-start" />{/if}{ui.text("ui_close_save")}</Button>
    {/snippet}
  </AppDialog>
{/if}
