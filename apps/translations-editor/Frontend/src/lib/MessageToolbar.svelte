<script lang="ts" module>
  export type MessageFilter = "all" | "missing" | "structured" | "needs-review" | "stale" | "quality";
  export type MessageFilterOption = { value: MessageFilter; label: string; count: number };
</script>

<script lang="ts">
  import ListFilterIcon from "@lucide/svelte/icons/list-filter";
  import SearchIcon from "@lucide/svelte/icons/search";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import { buttonVariants } from "$lib/components/ui/button/index.js";
  import * as DropdownMenu from "$lib/components/ui/dropdown-menu/index.js";
  import * as InputGroup from "$lib/components/ui/input-group/index.js";

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

  let selectedOption = $derived(options.find((option) => option.value === filter) ?? options[0]);
</script>

<div class="px-2 pb-2">
  <InputGroup.Root>
    <InputGroup.Input bind:ref={inputRef} bind:value={query} type="search" {placeholder} />
    <InputGroup.Addon>
      <SearchIcon />
    </InputGroup.Addon>
    <InputGroup.Addon align="inline-end" class="gap-1">
      {#if filter !== "all"}
        <Badge variant="secondary" class="max-w-24 truncate">{selectedOption?.label}</Badge>
      {/if}
      <DropdownMenu.Root>
        <DropdownMenu.Trigger
          class={buttonVariants({ variant: filter === "all" ? "ghost" : "secondary", size: "icon-xs" })}
          aria-label={`${filterLabel}: ${selectedOption?.label ?? filter}`}
          title={`${filterLabel}: ${selectedOption?.label ?? filter}`}
        >
          <ListFilterIcon />
        </DropdownMenu.Trigger>
        <DropdownMenu.Content align="end" class="w-56">
          <DropdownMenu.Label>{filterLabel}</DropdownMenu.Label>
          <DropdownMenu.Group>
            <DropdownMenu.RadioGroup
              value={filter}
              onValueChange={(value) => filter = value as MessageFilter}
            >
              {#each options as option (option.value)}
                <DropdownMenu.RadioItem value={option.value}>
                  <span>{option.label}</span>
                  <Badge variant="secondary" class="ml-auto mr-5">{option.count}</Badge>
                </DropdownMenu.RadioItem>
              {/each}
            </DropdownMenu.RadioGroup>
          </DropdownMenu.Group>
        </DropdownMenu.Content>
      </DropdownMenu.Root>
    </InputGroup.Addon>
  </InputGroup.Root>
</div>
