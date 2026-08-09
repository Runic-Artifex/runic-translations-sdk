<script lang="ts">
  import LanguagesIcon from "@lucide/svelte/icons/languages";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import * as Select from "$lib/components/ui/select/index.js";
  import * as Sidebar from "$lib/components/ui/sidebar/index.js";

  let {
    eyebrow,
    title,
    locale,
    onlocalechange,
  }: {
    eyebrow: string;
    title: string;
    locale: string;
    onlocalechange: (locale: string) => void;
  } = $props();

  let localeLabel = $derived(locale.toLocaleUpperCase());
</script>

<Sidebar.Header class="border-b border-sidebar-border p-3">
  <div class="flex min-w-0 items-center gap-3">
    <Badge variant="outline" class="size-9 shrink-0 justify-center p-0">
      <LanguagesIcon aria-hidden="true" />
    </Badge>
    <div class="min-w-0 flex-1">
      <p class="truncate text-[0.625rem] font-semibold tracking-[0.14em] text-primary uppercase">{eyebrow}</p>
      <h1 class="truncate font-serif text-lg font-medium">{title}</h1>
    </div>
    <Select.Root type="single" value={locale} onValueChange={onlocalechange}>
      <Select.Trigger size="sm" class="w-16" aria-label="Editor language">{localeLabel}</Select.Trigger>
      <Select.Content align="end">
        <Select.Group>
          <Select.Label>Editor language</Select.Label>
          <Select.Item value="en" label="English">EN</Select.Item>
          <Select.Item value="de" label="Deutsch">DE</Select.Item>
        </Select.Group>
      </Select.Content>
    </Select.Root>
  </div>
</Sidebar.Header>
