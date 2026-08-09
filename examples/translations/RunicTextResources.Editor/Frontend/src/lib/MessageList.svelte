<script lang="ts" module>
  export interface MessageListItem {
    key: string;
    preview: string;
    missing: boolean;
    structured: boolean;
    stale: boolean;
    needsReview: boolean;
  }

  export interface MessageTreeNode {
    segment: string;
    path: string;
    item?: MessageListItem;
    children: MessageTreeNode[];
  }

  function buildTree(items: MessageListItem[]): MessageTreeNode[] {
    const roots: MessageTreeNode[] = [];
    for (const item of items) {
      const segments = item.key.split(".").filter(Boolean);
      const safeSegments = segments.length === 0 ? [item.key] : segments;
      let siblings = roots;
      let path = "";
      for (const segment of safeSegments) {
        path = path === "" ? segment : `${path}.${segment}`;
        let node = siblings.find((candidate) => candidate.segment === segment);
        if (node === undefined) {
          node = { segment, path, children: [] };
          siblings.push(node);
        }
        siblings = node.children;
        if (path === item.key) node.item = item;
      }
    }
    return roots;
  }
</script>

<script lang="ts">
  import CheckCheckIcon from "@lucide/svelte/icons/check-check";
  import ChevronDownIcon from "@lucide/svelte/icons/chevron-down";
  import ListChecksIcon from "@lucide/svelte/icons/list-checks";
  import MessageSquareOffIcon from "@lucide/svelte/icons/message-square-off";
  import MoreHorizontalIcon from "@lucide/svelte/icons/more-horizontal";
  import PlusIcon from "@lucide/svelte/icons/plus";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import { Button, buttonVariants } from "$lib/components/ui/button/index.js";
  import * as Collapsible from "$lib/components/ui/collapsible/index.js";
  import * as DropdownMenu from "$lib/components/ui/dropdown-menu/index.js";
  import * as Empty from "$lib/components/ui/empty/index.js";
  import * as Sidebar from "$lib/components/ui/sidebar/index.js";
  import { cn } from "$lib/utils.js";
  import type { Snippet } from "svelte";
  import { onMount } from "svelte";
  import MessageTreeNodeView from "./MessageTreeNode.svelte";

  let {
    items,
    selectedKey,
    visibleCount,
    remainingCount,
    noResultsLabel,
    toolbar,
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
    toolbar: Snippet;
    onselect: (key: string) => void;
    onadd: () => void;
    onmarkreview: () => void;
    onapprove: () => void;
    onloadmore: () => void;
  } = $props();

  const sidebar = Sidebar.useSidebar();
  let open = $state(true);
  let tree = $derived(buildTree(items));

  onMount(() => {
    open = localStorage.getItem("runic.sidebar.messages") !== "closed";
  });

  function persistOpen(value: boolean): void {
    localStorage.setItem("runic.sidebar.messages", value ? "open" : "closed");
  }

  function selectMessage(key: string): void {
    onselect(key);
    if (sidebar.isMobile) sidebar.setOpenMobile(false);
  }

  function addMessage(): void {
    onadd();
    if (sidebar.isMobile) sidebar.setOpenMobile(false);
  }
</script>

<Collapsible.Root bind:open onOpenChange={persistOpen} class={cn("group/messages", open && "min-h-0 flex flex-1 flex-col")}>
  <Sidebar.Group class={cn("py-1", open && "min-h-0 flex-1")} aria-label="Messages">
    <Sidebar.GroupLabel class="justify-between">
      <Collapsible.Trigger class="flex min-w-0 flex-1 items-center gap-2 text-left">
        <span>Messages</span>
        <Badge variant="secondary">{visibleCount}</Badge>
        <ChevronDownIcon class="ml-auto transition-transform group-data-[state=open]/messages:rotate-180" />
      </Collapsible.Trigger>
      <div class="flex items-center gap-1">
        <DropdownMenu.Root>
          <DropdownMenu.Trigger
            class={buttonVariants({ variant: "ghost", size: "icon-xs" })}
            aria-label="Message bulk actions"
            title="Message bulk actions"
          >
            <MoreHorizontalIcon />
          </DropdownMenu.Trigger>
          <DropdownMenu.Content align="end" class="w-64">
            <DropdownMenu.Label>Visible messages</DropdownMenu.Label>
            <DropdownMenu.Group>
              <DropdownMenu.Item disabled={visibleCount === 0} onclick={onmarkreview}>
                <ListChecksIcon />
                Mark for review
              </DropdownMenu.Item>
              <DropdownMenu.Item disabled={visibleCount === 0} onclick={onapprove}>
                <CheckCheckIcon />
                Approve translations
              </DropdownMenu.Item>
            </DropdownMenu.Group>
          </DropdownMenu.Content>
        </DropdownMenu.Root>
        <Button variant="ghost" size="icon-xs" aria-label="Add message" title="Add message" onclick={addMessage}>
          <PlusIcon />
        </Button>
      </div>
    </Sidebar.GroupLabel>

    <Collapsible.Content class="min-h-0 flex-1 overflow-hidden">
      {@render toolbar()}
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
            <Sidebar.Menu aria-label="Message namespaces" class="px-2">
              {#each tree as node (node.path)}
                <MessageTreeNodeView {node} {selectedKey} onselect={selectMessage} />
              {/each}
            </Sidebar.Menu>
          {/if}

          {#if remainingCount > 0}
            <Button variant="outline" size="xs" class="mx-2 mt-2 w-[calc(100%_-_1rem)]" onclick={onloadmore}>
              Show 300 more · {remainingCount} remaining
            </Button>
          {/if}
        </nav>
      </Sidebar.GroupContent>
    </Collapsible.Content>
  </Sidebar.Group>
</Collapsible.Root>
