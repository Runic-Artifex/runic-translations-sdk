<script lang="ts">
  import AppDialog from "#lib/AppDialog.svelte";
  import * as Alert from "#lib/components/ui/alert/index.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import { Spinner } from "#lib/components/ui/spinner/index.js";
  import { Textarea } from "#lib/components/ui/textarea/index.js";
  import type { EditorDiagnostic, EditorDiagnosticQuickFix, EditorDocument } from "#lib/contracts.js";
  import type { EditorLabels } from "#lib/editor-labels.js";
  import type { RepairController } from "#lib/state/repair-controller.svelte.js";
  import { displayNotice, getUiText } from "#lib/ui-text.js";
  import ValidationPanel from "#lib/ValidationPanel.svelte";

  let {
    repair,
    documents,
    schemaVersion,
    labels,
    disabled,
    onselectdiagnostic,
    onfix,
  }: {
    repair: RepairController;
    /** The workspace's documents, to name a diagnostic's message. */
    documents: EditorDocument[];
    schemaVersion: number;
    labels: EditorLabels;
    /** Whether an operation in progress keeps the text from changing. */
    disabled: boolean;
    onselectdiagnostic(diagnostic: EditorDiagnostic): void;
    onfix(diagnostic: EditorDiagnostic, fix: EditorDiagnosticQuickFix): void;
  } = $props();
  const ui = getUiText();
</script>

{#if repair.document !== undefined}
  <AppDialog
    open
    title={repair.document.path}
    description={ui.text("ui_page_repair_description")}
    class="sm:max-w-4xl"
    showCloseButton={!repair.busy}
    onopenchange={(open) => { if (!open && !repair.busy) repair.close(); }}
  >
    <Textarea bind:ref={repair.input} class="min-h-[26rem] font-mono text-xs" aria-label={ui.text("ui_page_repair_document_aria_label")} bind:value={repair.text} spellcheck={false} disabled={repair.busy || disabled} />
    {#if repair.validation !== undefined}
      <ValidationPanel busy={repair.busy} diagnostics={repair.validation.diagnostics} clientError={undefined}
        errorCount={repair.validation.diagnostics.filter(item => item.severity === "error").length}
        warningCount={repair.validation.diagnostics.filter(item => item.severity === "warning").length}
        validLabel={labels.valid} invalidLabel={labels.invalid} diagnosticsLabel={labels.diagnostics}
        {schemaVersion}
        sources={{ documents, drafts: { [repair.document.path]: repair.text } }}
        onselect={onselectdiagnostic}
        {onfix} />
    {/if}
    {#if repair.message}<Alert.Root variant="destructive" class="mt-4"><Alert.Title>{ui.text("ui_page_repair_failed")}</Alert.Title><Alert.Description>{displayNotice(repair.message, ui)}</Alert.Description></Alert.Root>{/if}
    {#snippet footer()}
      <Button variant="outline" disabled={repair.busy} onclick={() => repair.close()}>{ui.text("ui_page_cancel")}</Button>
      <Button disabled={repair.busy} onclick={() => void repair.save()}>{#if repair.busy}<Spinner data-icon="inline-start" />{/if}{repair.busy ? ui.text("ui_page_validating") : ui.text("ui_page_repair_validate_save")}</Button>
    {/snippet}
  </AppDialog>
{/if}
