<script lang="ts">
  import SaveIcon from "@lucide/svelte/icons/save";
  import Undo2Icon from "@lucide/svelte/icons/undo-2";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import { Button } from "$lib/components/ui/button/index.js";
  import * as Kbd from "$lib/components/ui/kbd/index.js";
  import * as Select from "$lib/components/ui/select/index.js";
  import { Spinner } from "$lib/components/ui/spinner/index.js";
  import * as Sidebar from "$lib/components/ui/sidebar/index.js";
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

  let selectedLocaleOption = $derived(locales.find((locale) => locale.tag === selectedLocale));
</script>

<header class="flex min-h-16 items-center gap-2 border-b bg-background/80 px-3 backdrop-blur-md sm:px-4 xl:gap-4 xl:px-6">
  <Sidebar.Trigger class="shrink-0 md:hidden" aria-label="Open editor navigation" />
  <div class="min-w-0 flex-1 sm:hidden">
    <Select.Root type="single" value={selectedLocale} onValueChange={onselectlocale}>
      <Select.Trigger size="sm" class="w-full" aria-label="Editing locale">
        {selectedLocale.toLocaleUpperCase()} · {selectedLocaleOption?.name ?? selectedLocale}
      </Select.Trigger>
      <Select.Content align="start">
        <Select.Group>
          <Select.Label>Editing locale</Select.Label>
          {#each locales as locale (locale.tag)}
            <Select.Item value={locale.tag} label={`${locale.tag.toLocaleUpperCase()} · ${locale.name}`}>
              {locale.tag.toLocaleUpperCase()} · {locale.name}{locale.isSource ? " · source" : ""}
            </Select.Item>
          {/each}
        </Select.Group>
      </Select.Content>
    </Select.Root>
  </div>
  <div class="no-scrollbar hidden min-w-0 flex-1 overflow-x-auto py-2 sm:block">
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
          <span class="hidden min-[1180px]:inline">{locale.name}</span>
          {#if locale.isSource}<Badge class="hidden min-[1180px]:inline-flex" variant="secondary">source</Badge>{/if}
        </ToggleGroup.Item>
      {/each}
    </ToggleGroup.Root>
  </div>

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
