<script lang="ts">
  import SaveIcon from "@lucide/svelte/icons/save";
  import Undo2Icon from "@lucide/svelte/icons/undo-2";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import { Button } from "$lib/components/ui/button/index.js";
  import * as Kbd from "$lib/components/ui/kbd/index.js";
  import { Spinner } from "$lib/components/ui/spinner/index.js";
  import * as Sidebar from "$lib/components/ui/sidebar/index.js";

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
    ondiscardreview,
    onsavereview,
    onsave,
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
    ondiscardreview: () => void;
    onsavereview: () => void;
    onsave: () => void;
  } = $props();

</script>

<header class="flex min-h-16 items-center gap-2 border-b bg-background/80 px-3 backdrop-blur-md sm:px-4 xl:gap-4 xl:px-6">
  <Sidebar.Trigger class="shrink-0 md:hidden" aria-label="Open editor navigation" />
  <div class="min-w-0 flex-1"></div>

  <div class="flex shrink-0 items-center gap-2">
    {#if reviewDirty}
      <Button variant="ghost" size="icon-xs" class="hidden sm:inline-flex" disabled={reviewSaving} onclick={ondiscardreview} aria-label="Discard workflow changes" title="Discard workflow changes">
        <Undo2Icon data-icon="inline-start" />
      </Button>
      <Button variant="outline" size="xs" class="hidden lg:inline-flex" disabled={reviewSaving || reviewDisabled} onclick={onsavereview}>
        {#if reviewSaving}<Spinner data-icon="inline-start" />{/if}
        {reviewSaving ? "Saving workflow…" : "Save workflow"}
      </Button>
    {/if}

    <Badge class="hidden sm:inline-flex" variant={isDirty ? "default" : "secondary"}>{saveState}</Badge>
    <Button size="sm" disabled={saveDisabled} onclick={onsave} aria-label={saving ? savingLabel : saveLabel} title={saving ? savingLabel : saveLabel}>
      {#if saving}
        <Spinner data-icon="inline-start" />
      {:else}
        <SaveIcon data-icon="inline-start" />
      {/if}
      <span class="hidden sm:inline">{saving ? savingLabel : saveLabel}</span>
      <Kbd.Root class="hidden xl:inline-flex">⌘ S</Kbd.Root>
    </Button>
  </div>
</header>
