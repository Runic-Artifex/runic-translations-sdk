<script lang="ts">
  import CheckIcon from "@lucide/svelte/icons/check";
  import SaveIcon from "@lucide/svelte/icons/save";
  import Undo2Icon from "@lucide/svelte/icons/undo-2";
  import Redo2Icon from "@lucide/svelte/icons/redo-2";
  import { Button } from "$lib/components/ui/button/index.js";
  import * as Kbd from "$lib/components/ui/kbd/index.js";
  import { Spinner } from "$lib/components/ui/spinner/index.js";
  import * as Sidebar from "$lib/components/ui/sidebar/index.js";
  import { getUiText } from "$lib/ui-text";

  let {
    reviewDirty,
    reviewSaving,
    reviewDisabled,
    saveDisabled,
    saving,
    saveLabel,
    savingLabel,
    saveState,
    isDirty,
    canUndo,
    canRedo,
    historyBusy,
    undoLabel,
    redoLabel,
    ondiscardreview,
    onsavereview,
    onsave,
    onundo,
    onredo,
  }: {
    reviewDirty: boolean;
    reviewSaving: boolean;
    reviewDisabled: boolean;
    saveDisabled: boolean;
    saving: boolean;
    saveLabel: string;
    savingLabel: string;
    saveState: string;
    isDirty: boolean;
    canUndo: boolean;
    canRedo: boolean;
    historyBusy: boolean;
    undoLabel?: string;
    redoLabel?: string;
    ondiscardreview: () => void;
    onsavereview: () => void;
    onsave: () => void;
    onundo: () => void;
    onredo: () => void;
  } = $props();

  const ui = getUiText();

</script>

<header class="flex min-h-16 items-center gap-2 border-b bg-background/80 px-3 backdrop-blur-md sm:px-4 xl:gap-4 xl:px-6">
  <Sidebar.Trigger class="shrink-0 md:hidden" aria-label={ui.text("Ui.Toolbar.OpenNavigation")} />
  <div class="min-w-0 flex-1"></div>

  <div class="flex shrink-0 items-center gap-2">
    <Button variant="ghost" size="icon-xs" disabled={!canUndo || historyBusy} onclick={onundo} aria-label={undoLabel === undefined ? ui.text("Ui.Toolbar.UndoSavedChange") : `${ui.text("Ui.Toolbar.Undo")} ${undoLabel}`} title={undoLabel === undefined ? `${ui.text("Ui.Toolbar.UndoSavedChange")} (Ctrl+Z)` : `${ui.text("Ui.Toolbar.Undo")} ${undoLabel} (Ctrl+Z)`}>
      <Undo2Icon data-icon="inline-start" />
    </Button>
    <Button variant="ghost" size="icon-xs" disabled={!canRedo || historyBusy} onclick={onredo} aria-label={redoLabel === undefined ? ui.text("Ui.Toolbar.RedoSavedChange") : `${ui.text("Ui.Toolbar.Redo")} ${redoLabel}`} title={redoLabel === undefined ? `${ui.text("Ui.Toolbar.RedoSavedChange")} (Ctrl+Shift+Z)` : `${ui.text("Ui.Toolbar.Redo")} ${redoLabel} (Ctrl+Shift+Z)`}>
      <Redo2Icon data-icon="inline-start" />
    </Button>
    {#if reviewDirty}
      <Button variant="ghost" size="icon-xs" class="hidden sm:inline-flex" disabled={reviewSaving} onclick={ondiscardreview} aria-label={ui.text("Ui.Toolbar.DiscardWorkflow")} title={ui.text("Ui.Toolbar.DiscardWorkflow")}>
        <Undo2Icon data-icon="inline-start" />
      </Button>
      <Button variant="outline" size="xs" class="hidden lg:inline-flex" disabled={reviewSaving || reviewDisabled} onclick={onsavereview}>
        {#if reviewSaving}<Spinner data-icon="inline-start" />{/if}
        {reviewSaving ? ui.text("Ui.Toolbar.SavingWorkflow") : ui.text("Ui.Toolbar.SaveWorkflow")}
      </Button>
    {/if}

    <Button
      size="sm"
      variant={isDirty ? "default" : "secondary"}
      disabled={saveDisabled}
      onclick={onsave}
      aria-label={saving ? savingLabel : isDirty ? saveLabel : saveState}
      title={saving ? savingLabel : isDirty ? saveLabel : saveState}
    >
      {#if saving}
        <Spinner data-icon="inline-start" />
      {:else if !isDirty}
        <CheckIcon data-icon="inline-start" />
      {:else}
        <SaveIcon data-icon="inline-start" />
      {/if}
      <span class="hidden sm:inline">{saving ? savingLabel : isDirty ? saveLabel : saveState}</span>
      {#if isDirty}<Kbd.Root class="hidden xl:inline-flex">⌘ S</Kbd.Root>{/if}
    </Button>
  </div>
</header>
