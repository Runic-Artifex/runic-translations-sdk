<script lang="ts">
  import * as Dialog from "$lib/components/ui/dialog/index.js";
  import { cn } from "$lib/utils.js";
  import type { Snippet } from "svelte";

  let {
    open,
    title,
    description,
    class: className,
    bodyClass,
    showCloseButton = true,
    onopenchange,
    children,
    footer,
  }: {
    open: boolean;
    title: string;
    description?: string;
    class?: string;
    bodyClass?: string;
    showCloseButton?: boolean;
    onopenchange: (open: boolean) => void;
    children: Snippet;
    footer?: Snippet;
  } = $props();
</script>

<Dialog.Root {open} onOpenChange={onopenchange}>
  <Dialog.Content
    {showCloseButton}
    class={cn(
      "flex max-h-[calc(100svh-2rem)] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-2xl",
      className,
    )}
  >
    <Dialog.Header class="shrink-0 border-b px-6 py-5 pr-16">
      <Dialog.Title class="font-serif text-xl font-medium">{title}</Dialog.Title>
      {#if description}<Dialog.Description>{description}</Dialog.Description>{/if}
    </Dialog.Header>
    <div class={cn("min-h-0 flex-1 overflow-y-auto px-6 py-5", bodyClass)}>
      {@render children()}
    </div>
    {#if footer}
      <Dialog.Footer class="shrink-0 border-t px-6 py-4">
        {@render footer()}
      </Dialog.Footer>
    {/if}
  </Dialog.Content>
</Dialog.Root>
