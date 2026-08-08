<script lang="ts">
  import SaveIcon from "@lucide/svelte/icons/save";
  import Undo2Icon from "@lucide/svelte/icons/undo-2";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import { Button } from "$lib/components/ui/button/index.js";
  import * as Kbd from "$lib/components/ui/kbd/index.js";
  import { Spinner } from "$lib/components/ui/spinner/index.js";
  import * as ToggleGroup from "$lib/components/ui/toggle-group/index.js";

  type LocaleOption = { tag: string; name: string; isSource: boolean };

  let {
    locales,
    selectedLocale,
    defaultLocaleLabel,
    reviewDirty,
    reviewSaving,
    reviewDisabled,
    saveDisabled,
    saving,
    saveLabel,
    savingLabel,
    saveState,
    isDirty,
    onselectlocale,
    ondiscardreview,
    onsavereview,
    onsave,
  }: {
    locales: LocaleOption[];
    selectedLocale: string;
    defaultLocaleLabel: string;
    reviewDirty: boolean;
    reviewSaving: boolean;
    reviewDisabled: boolean;
    saveDisabled: boolean;
    saving: boolean;
    saveLabel: string;
    savingLabel: string;
    saveState: string;
    isDirty: boolean;
    onselectlocale: (locale: string) => void;
    ondiscardreview: () => void;
    onsavereview: () => void;
    onsave: () => void;
  } = $props();
</script>

<header class="flex min-h-16 items-center justify-between gap-2 border-b bg-background/80 px-4 backdrop-blur-md xl:gap-4 xl:px-6">
  <div class="min-w-0 overflow-x-auto py-2">
    <ToggleGroup.Root
      type="single"
      variant="outline"
      size="sm"
      spacing={1}
      value={selectedLocale}
      class="min-w-max"
      aria-label="Editing locale"
      onValueChange={(value) => {
        if (value !== "") onselectlocale(value);
      }}
    >
      {#each locales as locale (locale.tag)}
        <ToggleGroup.Item
          value={locale.tag}
          aria-label={`${locale.tag.toLocaleUpperCase()} ${locale.name}${locale.isSource ? ` ${defaultLocaleLabel}` : ""}`}
          onclick={(event) => {
            if (selectedLocale === locale.tag) event.preventDefault();
          }}
        >
          <Badge variant="outline">{locale.tag.toLocaleUpperCase()}</Badge>
          {locale.name}
          {#if locale.isSource}<Badge variant="secondary">source</Badge>{/if}
        </ToggleGroup.Item>
      {/each}
    </ToggleGroup.Root>
  </div>

  <div class="flex shrink-0 items-center gap-2">
    {#if reviewDirty}
      <Button variant="ghost" size="xs" disabled={reviewSaving} onclick={ondiscardreview}>
        <Undo2Icon data-icon="inline-start" />
        Discard workflow
      </Button>
      <Button variant="outline" size="xs" disabled={reviewSaving || reviewDisabled} onclick={onsavereview}>
        {#if reviewSaving}<Spinner data-icon="inline-start" />{/if}
        {reviewSaving ? "Saving workflow…" : "Save workflow"}
      </Button>
    {/if}

    <Badge variant={isDirty ? "default" : "secondary"}>{saveState}</Badge>
    <Button size="sm" disabled={saveDisabled} onclick={onsave}>
      {#if saving}
        <Spinner data-icon="inline-start" />
      {:else}
        <SaveIcon data-icon="inline-start" />
      {/if}
      {saving ? savingLabel : saveLabel}
      <Kbd.Root>⌘ S</Kbd.Root>
    </Button>
  </div>
</header>
