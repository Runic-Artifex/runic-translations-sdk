<script lang="ts">
  import CheckIcon from "@lucide/svelte/icons/check";
  import SaveIcon from "@lucide/svelte/icons/save";
  import Undo2Icon from "@lucide/svelte/icons/undo-2";
  import Redo2Icon from "@lucide/svelte/icons/redo-2";
  import { Button } from "#lib/components/ui/button/index.js";
  import * as Kbd from "#lib/components/ui/kbd/index.js";
  import { Spinner } from "#lib/components/ui/spinner/index.js";
  import * as Sidebar from "#lib/components/ui/sidebar/index.js";
  import { getUiText } from "#lib/ui-text.js";
  import { shortcutLabel } from "#lib/shortcut-labels.js";

  let {
    saveDisabled,
    saving,
    saveLabel,
    savingLabel,
    saveState,
    unsaved,
    unsavedDetail,
    canUndo,
    canRedo,
    historyBusy,
    undoLabel,
    redoLabel,
    onsave,
    onundo,
    onredo,
  }: {
    saveDisabled: boolean;
    saving: boolean;
    saveLabel: string;
    savingLabel: string;
    /** The state shown while nothing is unsaved: "Saved", or the last save's notice. */
    saveState: string;
    /** Whether any document text or review data is unsaved; one Save writes all of it. */
    unsaved: boolean;
    /** What is unsaved, such as "Unsaved changes: text in 2 documents and review status". */
    unsavedDetail: string;
    canUndo: boolean;
    canRedo: boolean;
    historyBusy: boolean;
    /** What Undo takes back: "Undo: edit in files.selected" for an unsaved step, else the saved change it undoes. */
    undoLabel: string;
    redoLabel: string;
    onsave: () => void;
    onundo: () => void;
    onredo: () => void;
  } = $props();

  const ui = getUiText();

</script>

<header class="flex min-h-16 items-center gap-2 border-b bg-background/80 px-3 backdrop-blur-md sm:px-4 xl:gap-4 xl:px-6">
  <Sidebar.Trigger class="shrink-0 md:hidden" aria-label={ui.text("ui_toolbar_open_navigation")} />
  <div class="min-w-0 flex-1"></div>

  <div class="flex shrink-0 items-center gap-2">
    <Button variant="ghost" size="icon-xs" disabled={!canUndo || historyBusy} onclick={onundo} aria-label={undoLabel} title={`${undoLabel} (${shortcutLabel("Mod+Z")})`}>
      <Undo2Icon data-icon="inline-start" />
    </Button>
    <Button variant="ghost" size="icon-xs" disabled={!canRedo || historyBusy} onclick={onredo} aria-label={redoLabel} title={`${redoLabel} (${shortcutLabel("Mod+Shift+Z")})`}>
      <Redo2Icon data-icon="inline-start" />
    </Button>
    <!-- One state for all unsaved work: "Saved", or "Unsaved changes" naming what is unsaved. -->
    <span class="sr-only" data-testid="save-state" aria-live="polite">{unsaved ? unsavedDetail : saveState}</span>
    {#if unsaved}
      <span class="hidden max-w-64 truncate text-xs text-muted-foreground md:inline" title={unsavedDetail} aria-hidden="true">{unsavedDetail}</span>
    {/if}

    <Button
      size="sm"
      variant={unsaved ? "default" : "secondary"}
      disabled={saveDisabled}
      onclick={onsave}
      aria-label={saving ? savingLabel : unsaved ? saveLabel : saveState}
      title={saving ? savingLabel : unsaved ? unsavedDetail : saveState}
    >
      {#if saving}
        <Spinner data-icon="inline-start" />
      {:else if !unsaved}
        <CheckIcon data-icon="inline-start" />
      {:else}
        <SaveIcon data-icon="inline-start" />
      {/if}
      <span class="hidden sm:inline">{saving ? savingLabel : unsaved ? saveLabel : saveState}</span>
      {#if unsaved}<Kbd.Root class="hidden xl:inline-flex" data-testid="save-shortcut">{shortcutLabel("Mod+S")}</Kbd.Root>{/if}
    </Button>
  </div>
</header>
