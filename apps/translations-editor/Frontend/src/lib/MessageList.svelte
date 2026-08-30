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

  export interface MessageListLabels {
    messages: string;
    bulkActions: string;
    visibleMessages: string;
    markForReview: string;
    approveTranslations: string;
    addMessage: string;
    noMatchingMessages: string;
    missingTranslation: string;
    translated: string;
    structured: string;
    stale: string;
    review: string;
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
  import { getLocalEditorState, setLocalEditorState, subscribeLocalEditorState } from "./local-state";
  import { messageVirtualRowHeight, virtualMessageTree, virtualMessageWindow } from "./message-virtualization";

  let {
    items,
    selectedKey,
    visibleCount,
    reviewActionsDisabled = false,
    noResultsLabel,
    labels,
    toolbar,
    onselect,
    onadd,
    onmarkreview,
    onapprove,
    open = $bindable(true),
  }: {
    items: MessageListItem[];
    selectedKey: string;
    visibleCount: number;
    reviewActionsDisabled?: boolean;
    noResultsLabel: string;
    labels: MessageListLabels;
    toolbar: Snippet;
    onselect: (key: string) => void;
    onadd: () => void;
    onmarkreview: () => void;
    onapprove: () => void;
    open?: boolean;
  } = $props();

  const sidebar = Sidebar.useSidebar();
  let scrollTop = $state(0);
  let viewportHeight = $state(0);
  let tree = $derived(virtualMessageTree(items));
  let window = $derived(virtualMessageWindow(tree.length, scrollTop, viewportHeight));
  let renderedRows = $derived(tree.slice(window.start, window.end));

  onMount(() => {
    const refresh = (): void => {
      open = getLocalEditorState("runic.sidebar.messages") !== "closed";
    };
    refresh();
    return subscribeLocalEditorState(refresh);
  });

  function persistOpen(value: boolean): void {
    setLocalEditorState("runic.sidebar.messages", value ? "open" : "closed");
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
  <Sidebar.Group class={cn("py-1", open && "min-h-0 flex-1")} aria-label={labels.messages}>
    <Sidebar.GroupLabel class="justify-between">
      <Collapsible.Trigger class="flex min-w-0 flex-1 items-center gap-2 text-left">
        <span>{labels.messages}</span>
        <Badge variant="secondary">{visibleCount}</Badge>
        <ChevronDownIcon class="ml-auto transition-transform group-data-[state=open]/messages:rotate-180" />
      </Collapsible.Trigger>
      <div class="flex items-center gap-1">
        <DropdownMenu.Root>
          <DropdownMenu.Trigger
            class={buttonVariants({ variant: "ghost", size: "icon-xs" })}
            aria-label={labels.bulkActions}
            title={labels.bulkActions}
          >
            <MoreHorizontalIcon />
          </DropdownMenu.Trigger>
          <DropdownMenu.Content align="end" class="w-64">
            <DropdownMenu.Label>{labels.visibleMessages}</DropdownMenu.Label>
            <DropdownMenu.Group>
              <DropdownMenu.Item disabled={reviewActionsDisabled || visibleCount === 0} onclick={onmarkreview}>
                <ListChecksIcon />
                {labels.markForReview}
              </DropdownMenu.Item>
              <DropdownMenu.Item disabled={reviewActionsDisabled || visibleCount === 0} onclick={onapprove}>
                <CheckCheckIcon />
                {labels.approveTranslations}
              </DropdownMenu.Item>
            </DropdownMenu.Group>
          </DropdownMenu.Content>
        </DropdownMenu.Root>
        <Button variant="ghost" size="icon-xs" aria-label={labels.addMessage} title={labels.addMessage} onclick={addMessage}>
          <PlusIcon />
        </Button>
      </div>
    </Sidebar.GroupLabel>

    <Collapsible.Content class="min-h-0 flex-1 overflow-hidden">
      {@render toolbar()}
      <Sidebar.GroupContent class="min-h-0 flex-1">
        <nav
          class="min-h-0 flex-1 overflow-y-auto pb-3"
          aria-label={labels.messages}
          bind:clientHeight={viewportHeight}
          onscroll={(event) => scrollTop = event.currentTarget.scrollTop}
        >
          {#if items.length === 0}
            <Empty.Root class="p-6">
              <Empty.Header>
                <Empty.Media variant="icon"><MessageSquareOffIcon /></Empty.Media>
                <Empty.Title>{labels.noMatchingMessages}</Empty.Title>
                <Empty.Description>{noResultsLabel}</Empty.Description>
              </Empty.Header>
            </Empty.Root>
          {:else}
            <div class="relative px-2" style:height={`${tree.length * messageVirtualRowHeight}px`}>
              <div class="absolute inset-x-2" style:transform={`translateY(${window.offset}px)`}>
              {#each renderedRows as row (row.key)}
                {#if row.kind === "branch"}
                  <div
                    class="flex h-8 items-center gap-2 truncate px-2 text-xs font-medium text-muted-foreground"
                    aria-label={row.label}
                    style:padding-inline-start={`${(row.depth - 1) * 1.25 + 0.5}rem`}
                  >
                    <span class="truncate">{row.label}</span><Badge variant="secondary" class="ml-auto">{row.messageCount}</Badge>
                  </div>
                {:else if row.item !== undefined}
                  <button
                    class={cn("flex h-8 w-full items-center gap-2 rounded-md px-2 text-left text-sm hover:bg-sidebar-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sidebar-ring", selectedKey === row.item.key && "bg-sidebar-accent")}
                    aria-label={`${row.item.key}: ${row.item.preview}`}
                    title={`${row.item.key}\n${row.item.preview}`}
                    style:padding-inline-start={`${(row.depth - 1) * 1.25 + 0.5}rem`}
                    onclick={() => selectMessage(row.item!.key)}
                  >
                    <Badge variant={row.item.missing ? "outline" : row.item.structured ? "default" : "secondary"} class="size-2 shrink-0 p-0" aria-label={row.item.missing ? labels.missingTranslation : row.item.structured ? labels.structured : labels.translated}></Badge>
                    <span class="truncate font-mono">{row.label}</span>
                    {#if row.item.stale || row.item.needsReview || row.item.structured}
                      <span class="ml-auto text-xs text-muted-foreground">{row.item.stale ? labels.stale : row.item.needsReview ? labels.review : labels.structured}</span>
                    {/if}
                  </button>
                {/if}
              {/each}
              </div>
            </div>
          {/if}
        </nav>
      </Sidebar.GroupContent>
    </Collapsible.Content>
  </Sidebar.Group>
</Collapsible.Root>
