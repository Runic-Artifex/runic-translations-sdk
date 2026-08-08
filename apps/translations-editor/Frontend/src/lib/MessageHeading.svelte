<script lang="ts">
  import CopyIcon from "@lucide/svelte/icons/copy";
  import PencilIcon from "@lucide/svelte/icons/pencil";
  import Trash2Icon from "@lucide/svelte/icons/trash-2";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import { Button } from "$lib/components/ui/button/index.js";

  let {
    messageKey,
    description,
    tags,
    locale,
    layer,
    inheritedFrom,
    onrename,
    onduplicate,
    ondelete,
  }: {
    messageKey: string;
    description?: string;
    tags: string[];
    locale: string;
    layer: string;
    inheritedFrom?: string;
    onrename: () => void;
    onduplicate: () => void;
    ondelete: () => void;
  } = $props();
</script>

<header class="mx-auto mb-6 flex max-w-[1000px] flex-col items-start justify-between gap-4 xl:flex-row xl:gap-8">
  <div class="min-w-0">
    <div class="mb-2 flex flex-wrap items-center gap-2 font-mono text-xs text-muted-foreground">
      {#each messageKey.split(".") as segment, index (`${segment}-${index}`)}
        {#if index > 0}<span aria-hidden="true">/</span>{/if}<span>{segment}</span>
      {/each}
    </div>
    <h2 class="font-serif text-5xl tracking-tight">{messageKey.split(".").at(-1)}</h2>
    {#if description}<p class="mt-2 max-w-2xl text-sm text-muted-foreground">{description}</p>{/if}
    {#if tags.length > 0}
      <div class="mt-3 flex flex-wrap gap-1.5">
        {#each tags as tag (tag)}<Badge variant="outline">{tag}</Badge>{/each}
      </div>
    {/if}
  </div>

  <div class="flex shrink-0 flex-col items-start gap-2 xl:items-end">
    <div class="flex flex-wrap gap-1.5 xl:justify-end">
      <Badge variant="outline">{locale}</Badge>
      <Badge variant="outline">{layer}</Badge>
      {#if inheritedFrom}<Badge variant="secondary">falls back to {inheritedFrom}</Badge>{/if}
    </div>
    <div class="flex gap-1.5">
      <Button variant="outline" size="xs" title="Rename or move this message" onclick={onrename}>
        <PencilIcon data-icon="inline-start" />
        Rename
      </Button>
      <Button variant="outline" size="xs" title="Duplicate this message" onclick={onduplicate}>
        <CopyIcon data-icon="inline-start" />
        Duplicate
      </Button>
      <Button variant="destructive" size="xs" title="Delete this message" onclick={ondelete}>
        <Trash2Icon data-icon="inline-start" />
        Delete
      </Button>
    </div>
  </div>
</header>
