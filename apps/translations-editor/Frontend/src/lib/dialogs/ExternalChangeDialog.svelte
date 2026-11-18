<script lang="ts">
  import AppDialog from "#lib/AppDialog.svelte";
  import { Button } from "#lib/components/ui/button/index.js";
  import * as Field from "#lib/components/ui/field/index.js";
  import { Textarea } from "#lib/components/ui/textarea/index.js";
  import type { EditorDocument } from "#lib/contracts.js";
  import type { ExternalChangesController } from "#lib/state/external-changes.svelte.js";
  import { getUiText } from "#lib/ui-text.js";

  let {
    externalChanges,
    documents,
  }: {
    externalChanges: ExternalChangesController;
    /** The workspace's documents as the editor loaded them: the base of the comparison. */
    documents: EditorDocument[];
  } = $props();
  const ui = getUiText();
</script>

{#if externalChanges.compared !== undefined}
  <AppDialog
    open
    title={externalChanges.compared.path}
    description={ui.text("ui_page_external_compare_description")}
    class="sm:max-w-6xl"
    bodyClass="grid gap-4"
    onopenchange={(open) => { if (!open) externalChanges.compared = undefined; }}
  >
    <div class="grid gap-4 lg:grid-cols-2">
      <Field.Field>
        <Field.Label for="external-editor-base">{ui.text("ui_page_external_compare_editor_base")}</Field.Label>
        <Textarea id="external-editor-base" class="min-h-64 font-mono text-xs" readonly value={documents.find((document) => document.path === externalChanges.compared?.path)?.content ?? ui.text("ui_page_external_compare_not_previously_loaded")} />
      </Field.Field>
      <Field.Field>
        <Field.Label for="external-current-disk">{ui.text("ui_page_external_compare_current_disk")}</Field.Label>
        <Textarea id="external-current-disk" class="min-h-64 font-mono text-xs" readonly value={externalChanges.compared.content ?? ui.text("ui_page_external_compare_deleted_externally")} />
      </Field.Field>
    </div>
    <Field.Field>
      <Field.Label for="external-merged-draft">{ui.text("ui_page_external_compare_merged_draft")}</Field.Label>
      <Textarea id="external-merged-draft" class="min-h-64 font-mono text-xs" bind:value={externalChanges.merged} spellcheck={false} />
    </Field.Field>
    {#snippet footer()}
      <Button variant="outline" onclick={() => externalChanges.compared = undefined}>{ui.text("ui_page_keep_current_view")}</Button>
      <Button onclick={() => void externalChanges.applyMerge()}>{ui.text("ui_page_external_compare_reload_keep_merged")}</Button>
    {/snippet}
  </AppDialog>
{/if}
