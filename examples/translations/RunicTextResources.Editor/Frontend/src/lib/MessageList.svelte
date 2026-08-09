<script lang="ts" module>
  export interface MessageListItem {
    key: string;
    preview: string;
    missing: boolean;
    structured: boolean;
    stale: boolean;
    needsReview: boolean;
  }
</script>

<script lang="ts">
  import MessageSquareOffIcon from "@lucide/svelte/icons/message-square-off";
  import CheckCheckIcon from "@lucide/svelte/icons/check-check";
  import ListChecksIcon from "@lucide/svelte/icons/list-checks";
  import PlusIcon from "@lucide/svelte/icons/plus";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import { Button } from "$lib/components/ui/button/index.js";
  import * as Empty from "$lib/components/ui/empty/index.js";
  import * as Item from "$lib/components/ui/item/index.js";
  import * as Sidebar from "$lib/components/ui/sidebar/index.js";

  let {
    items,
    selectedKey,
    visibleCount,
    remainingCount,
    noResultsLabel,
    onselect,
    onadd,
    onmarkreview,
    onapprove,
    onloadmore,
  }: {
    items: MessageListItem[];
    selectedKey: string;
    visibleCount: number;
    remainingCount: number;
    noResultsLabel: string;
    onselect: (key: string) => void;
    onadd: () => void;
    onmarkreview: () => void;
    onapprove: () => void;
    onloadmore: () => void;
  } = $props();

  const sidebar = Sidebar.useSidebar();

  function selectMessage(key: string): void {
    onselect(key);
    if (sidebar.isMobile) sidebar.setOpenMobile(false);
  }

  function addMessage(): void {
    onadd();
    if (sidebar.isMobile) sidebar.setOpenMobile(false);
  }
</script>

<Sidebar.Group class="min-h-0 flex-1 py-1" aria-label="Messages">
  <Sidebar.GroupLabel class="justify-between">
    <div class="flex items-center gap-2">
      Messages
      <Badge variant="secondary">{visibleCount}</Badge>
    </div>
    <div class="flex items-center gap-1">
      <Button
        variant="ghost"
        size="icon-xs"
        disabled={visibleCount === 0}
        aria-label="Mark visible messages for review"
        title="Mark visible messages for review"
        onclick={onmarkreview}
      >
        <ListChecksIcon />
      </Button>
      <Button
        variant="ghost"
        size="icon-xs"
        disabled={visibleCount === 0}
        aria-label="Approve visible messages"
        title="Approve visible messages"
        onclick={onapprove}
      >
        <CheckCheckIcon />
      </Button>
      <Button variant="ghost" size="icon-xs" aria-label="Add message" title="Add message" onclick={addMessage}>
        <PlusIcon />
      </Button>
    </div>
  </Sidebar.GroupLabel>

  <Sidebar.GroupContent class="min-h-0 flex-1">
  <nav class="min-h-0 flex-1 overflow-y-auto pb-3" aria-label="Translation messages">
    {#if items.length === 0}
      <Empty.Root class="p-6">
        <Empty.Header>
          <Empty.Media variant="icon"><MessageSquareOffIcon /></Empty.Media>
          <Empty.Title>No matching messages</Empty.Title>
          <Empty.Description>{noResultsLabel}</Empty.Description>
        </Empty.Header>
      </Empty.Root>
    {:else}
      <Item.Group class="gap-1">
        {#each items as item (item.key)}
          <Item.Root
            variant={selectedKey === item.key ? "muted" : "default"}
            size="xs"
            aria-current={selectedKey === item.key ? "true" : undefined}
            onclick={() => selectMessage(item.key)}
            class="cursor-pointer"
          >
            {#snippet child({ props })}
              <button type="button" {...props}>
                <Item.Media>
                  <Badge
                    variant={item.missing ? "outline" : item.structured ? "default" : "secondary"}
                    class="size-2 p-0"
                    aria-label={item.missing ? "Missing translation" : item.structured ? "Structured message" : "Translated"}
                  ></Badge>
                </Item.Media>
                <Item.Content class="min-w-0 gap-0.5">
                  <Item.Title class="min-w-0"><code class="truncate">{item.key}</code></Item.Title>
                  <Item.Description class="truncate">{item.preview}</Item.Description>
                </Item.Content>
                {#if item.stale || item.needsReview || item.structured}
                  <Item.Actions class="ml-auto">
                    {#if item.stale}<Badge variant="destructive">stale</Badge>{/if}
                    {#if item.needsReview}<Badge variant="secondary">review</Badge>{/if}
                    {#if item.structured}<Badge variant="outline">AST</Badge>{/if}
                  </Item.Actions>
                {/if}
              </button>
            {/snippet}
          </Item.Root>
        {/each}
      </Item.Group>
    {/if}

    {#if remainingCount > 0}
      <Button variant="outline" size="xs" class="mt-2 w-full" onclick={onloadmore}>
        Show 300 more · {remainingCount} remaining
      </Button>
    {/if}
  </nav>
  </Sidebar.GroupContent>
</Sidebar.Group>
