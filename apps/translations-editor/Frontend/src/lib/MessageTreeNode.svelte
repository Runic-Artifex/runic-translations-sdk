<script lang="ts">
  import Self from "./MessageTreeNode.svelte";
  import ChevronRightIcon from "@lucide/svelte/icons/chevron-right";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import * as Collapsible from "$lib/components/ui/collapsible/index.js";
  import * as Sidebar from "$lib/components/ui/sidebar/index.js";
  import { cn } from "$lib/utils.js";
  import type { MessageListItem, MessageTreeNode } from "./MessageList.svelte";

  let {
    node,
    selectedKey,
    onselect,
  }: {
    node: MessageTreeNode;
    selectedKey: string;
    onselect: (key: string) => void;
  } = $props();

  let open = $derived(nodeContains(node, selectedKey));
  let count = $derived(messageCount(node));

  function nodeContains(candidate: MessageTreeNode, key: string): boolean {
    return candidate.item?.key === key || candidate.children.some((child) => nodeContains(child, key));
  }

  function messageCount(candidate: MessageTreeNode): number {
    return (candidate.item === undefined ? 0 : 1) + candidate.children.reduce((total, child) => total + messageCount(child), 0);
  }

  function status(item: MessageListItem): "stale" | "review" | "structured" | undefined {
    if (item.stale) return "stale";
    if (item.needsReview) return "review";
    if (item.structured) return "structured";
    return undefined;
  }
</script>

{#snippet messageLeaf(item: MessageListItem, label: string)}
  <Sidebar.MenuItem>
    <Sidebar.MenuButton
      size="sm"
      isActive={selectedKey === item.key}
      class={cn("cursor-pointer", status(item) && "pr-16")}
      aria-current={selectedKey === item.key ? "page" : undefined}
      aria-label={`${item.key}: ${item.preview}`}
      title={`${item.key}\n${item.preview}`}
      onclick={() => onselect(item.key)}
    >
      <Badge
        variant={item.missing ? "outline" : item.structured ? "default" : "secondary"}
        class="size-2 shrink-0 p-0"
        aria-label={item.missing ? "Missing translation" : item.structured ? "Structured message" : "Translated"}
      ></Badge>
      <span class="truncate font-mono">{label}</span>
    </Sidebar.MenuButton>
    {#if status(item)}
      <Sidebar.MenuBadge class="w-auto p-0">
        <Badge variant={item.stale ? "destructive" : "outline"}>{status(item)}</Badge>
      </Sidebar.MenuBadge>
    {/if}
  </Sidebar.MenuItem>
{/snippet}

{#if node.children.length > 0}
  <Sidebar.MenuItem>
    <Collapsible.Root bind:open class="group/message-branch">
      <Collapsible.Trigger>
        {#snippet child({ props })}
          <Sidebar.MenuButton {...props} size="sm" aria-label={`${node.segment}, ${count} ${count === 1 ? "message" : "messages"}`}>
            <ChevronRightIcon class="transition-transform group-data-[state=open]/message-branch:rotate-90" />
            <span class="truncate font-medium">{node.segment}</span>
          </Sidebar.MenuButton>
        {/snippet}
      </Collapsible.Trigger>
      <Sidebar.MenuBadge>{count}</Sidebar.MenuBadge>
      <Collapsible.Content>
        <Sidebar.MenuSub>
          {#if node.item}
            {@render messageLeaf(node.item, "Overview")}
          {/if}
          {#each node.children as child (child.path)}
            <Self node={child} {selectedKey} {onselect} />
          {/each}
        </Sidebar.MenuSub>
      </Collapsible.Content>
    </Collapsible.Root>
  </Sidebar.MenuItem>
{:else if node.item}
  {@render messageLeaf(node.item, node.segment)}
{/if}
