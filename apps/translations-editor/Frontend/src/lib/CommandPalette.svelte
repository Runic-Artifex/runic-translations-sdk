<script lang="ts">
  import { tick } from "svelte";
  import * as Dialog from "$lib/components/ui/dialog/index.js";
  import * as Kbd from "$lib/components/ui/kbd/index.js";
  import { Input } from "$lib/components/ui/input/index.js";
  import { getUiText } from "$lib/ui-text";
  import {
    filterCommands,
    groupCommands,
    movePaletteSelection,
    type PaletteCommand,
  } from "$lib/command-palette";

  let {
    open,
    commands,
    onopenchange,
  }: {
    open: boolean;
    commands: readonly PaletteCommand[];
    onopenchange: (open: boolean) => void;
  } = $props();

  const ui = getUiText();

  let query = $state("");
  let activeIndex = $state(0);
  let listElement = $state<HTMLElement | null>(null);
  let searchInput = $state<HTMLInputElement | null>(null);

  let filtered = $derived(filterCommands(commands, query));
  let groups = $derived.by(() => {
    const rendered: Array<{ id: string; label: string; start: number; commands: readonly PaletteCommand[] }> = [];
    let offset = 0;
    for (const group of groupCommands(filtered)) {
      rendered.push({ id: group.id, label: group.label, start: offset, commands: group.commands });
      offset += group.commands.length;
    }
    return rendered;
  });
  let activeId = $derived.by(() => filtered[activeIndex]?.id);

  $effect(() => {
    if (activeIndex >= filtered.length) activeIndex = Math.max(0, filtered.length - 1);
  });
  $effect(() => {
    if (open) reset();
  });

  function reset(): void {
    query = "";
    activeIndex = 0;
  }

  function focusSearch(event: Event): void {
    event.preventDefault();
    searchInput?.focus();
  }

  async function setActive(next: number): Promise<void> {
    activeIndex = next;
    await tick();
    listElement?.querySelector('[data-active="true"]')?.scrollIntoView({ block: "nearest" });
  }

  async function handleKeydown(event: KeyboardEvent): Promise<void> {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      await setActive(movePaletteSelection(activeIndex, 1, filtered.length));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      await setActive(movePaletteSelection(activeIndex, -1, filtered.length));
    } else if (event.key === "Home") {
      event.preventDefault();
      await setActive(0);
    } else if (event.key === "End") {
      event.preventDefault();
      await setActive(Math.max(0, filtered.length - 1));
    } else if (event.key === "Enter") {
      event.preventDefault();
      run(filtered[activeIndex]);
    }
  }

  function run(command: PaletteCommand | undefined): void {
    if (command === undefined || command.disabled === true) return;
    onopenchange(false);
    command.run();
  }
</script>

<Dialog.Root {open} onOpenChange={onopenchange}>
  <Dialog.Content
    showCloseButton={false}
    class="top-[12%] gap-0 translate-y-0 overflow-hidden rounded-2xl p-0 sm:max-w-xl"
    onOpenAutoFocus={focusSearch}
    onkeydown={handleKeydown}
  >
    <Dialog.Title class="sr-only">{ui.text("Ui.CommandPalette.Title")}</Dialog.Title>
    <Dialog.Description class="sr-only">{ui.text("Ui.CommandPalette.Description")}</Dialog.Description>
    <div class="border-b px-3 py-2.5">
      <Input
        bind:ref={searchInput}
        bind:value={query}
        placeholder={ui.text("Ui.CommandPalette.Placeholder")}
        autocomplete="off"
        spellcheck="false"
        aria-label={ui.text("Ui.CommandPalette.SearchAriaLabel")}
        class="border-0 bg-transparent shadow-none focus-visible:ring-0 dark:bg-transparent"
        oninput={() => setActive(0)}
      />
    </div>
    {#if filtered.length === 0}
      <p class="px-4 py-8 text-center text-sm text-muted-foreground">{ui.text("Ui.CommandPalette.NoMatches")}</p>
    {:else}
      <div bind:this={listElement} role="listbox" tabindex="-1" aria-label={ui.text("Ui.CommandPalette.CommandsAriaLabel")} aria-activedescendant={activeId === undefined ? undefined : `palette-option-${activeId}`} class="max-h-80 overflow-y-auto p-1.5">
        {#each groups as group (group.id)}
          <p class="px-2 pt-2 pb-1 text-[0.65rem] font-semibold tracking-wide text-muted-foreground uppercase">{group.label}</p>
          {#each group.commands as command, index (command.id)}
            {@const flatIndex = group.start + index}
            <button
              type="button"
              id={`palette-option-${command.id}`}
              role="option"
              aria-selected={flatIndex === activeIndex}
              aria-disabled={command.disabled === true}
              data-active={flatIndex === activeIndex}
              disabled={command.disabled === true}
              class="flex w-full items-center gap-3 rounded-lg px-2 py-2 text-left text-sm outline-none aria-disabled:pointer-events-none aria-disabled:opacity-50 data-[active=true]:bg-accent data-[active=true]:text-accent-foreground"
              onclick={() => run(command)}
              onpointermove={() => { if (flatIndex !== activeIndex) activeIndex = flatIndex; }}
            >
              <span class="min-w-0 flex-1 truncate">{command.title}</span>
              {#if command.keybinding}
                <Kbd.Root>{command.keybinding}</Kbd.Root>
              {/if}
            </button>
          {/each}
        {/each}
      </div>
    {/if}
  </Dialog.Content>
</Dialog.Root>
