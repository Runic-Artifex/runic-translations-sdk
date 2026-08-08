<script lang="ts" module>
  export type MessageFilter = "all" | "missing" | "structured" | "needs-review" | "stale" | "quality";
  export type MessageFilterOption = { value: MessageFilter; label: string; count: number };
</script>

<script lang="ts">
  import SearchIcon from "@lucide/svelte/icons/search";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import * as InputGroup from "$lib/components/ui/input-group/index.js";
  import * as Kbd from "$lib/components/ui/kbd/index.js";
  import * as ToggleGroup from "$lib/components/ui/toggle-group/index.js";

  let {
    query = $bindable(),
    filter = $bindable(),
    inputRef = $bindable(null),
    placeholder,
    options,
    filterLabel,
  }: {
    query: string;
    filter: MessageFilter;
    inputRef: HTMLInputElement | null;
    placeholder: string;
    options: MessageFilterOption[];
    filterLabel: string;
  } = $props();
</script>

<div class="grid gap-2 border-b border-border/70 px-4 pb-3">
  <InputGroup.Root>
    <InputGroup.Input bind:ref={inputRef} bind:value={query} type="search" {placeholder} />
    <InputGroup.Addon>
      <SearchIcon />
    </InputGroup.Addon>
    <InputGroup.Addon align="inline-end">
      <Kbd.Root>⌘ K</Kbd.Root>
    </InputGroup.Addon>
  </InputGroup.Root>

  <ToggleGroup.Root
    type="single"
    variant="outline"
    size="sm"
    spacing={1}
    value={filter}
    class="flex w-full flex-wrap justify-start gap-1"
    aria-label={filterLabel}
    onValueChange={(value) => {
      if (value !== "") filter = value as MessageFilter;
    }}
  >
    {#each options as option (option.value)}
      <ToggleGroup.Item
        value={option.value}
        class="h-7 min-w-0 gap-1 px-2 text-xs"
        aria-label={option.label}
        onclick={(event) => {
          if (filter === option.value) event.preventDefault();
        }}
      >
        {option.label}
        <Badge variant="secondary" class="h-4 min-w-4 px-1 text-[0.625rem]">{option.count}</Badge>
      </ToggleGroup.Item>
    {/each}
  </ToggleGroup.Root>
</div>
