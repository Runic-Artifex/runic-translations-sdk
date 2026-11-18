<script lang="ts" module>
  export interface MessageListItem {
    /** The flattened code identifier (`common_cancel`). */
    key: string;
    /** The logical path (`["common", "cancel"]`); groups are its prefixes. */
    path: readonly string[];
    preview: string;
    missing: boolean;
    structured: boolean;
    /** Plural forms, other variants, or formatting only; set when `structured` is. */
    structure?: "plural" | "variants" | "formatted";
    stale: boolean;
    needsReview: boolean;
  }

  /** The per-locale counts a group row names besides its number of messages. */
  export type MessageGroupCountKind = "missing" | "stale" | "needsReview";

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
    plural: string;
    variants: string;
    formatted: string;
    stale: string;
    review: string;
  }

</script>

<script lang="ts">
  import CheckCheckIcon from "@lucide/svelte/icons/check-check";
  import ChevronDownIcon from "@lucide/svelte/icons/chevron-down";
  import ChevronRightIcon from "@lucide/svelte/icons/chevron-right";
  import EyeOffIcon from "@lucide/svelte/icons/eye-off";
  import ListChecksIcon from "@lucide/svelte/icons/list-checks";
  import MessageSquareOffIcon from "@lucide/svelte/icons/message-square-off";
  import MoreHorizontalIcon from "@lucide/svelte/icons/more-horizontal";
  import PlusIcon from "@lucide/svelte/icons/plus";
  import { Badge } from "#lib/components/ui/badge/index.js";
  import StateMark from "#lib/StateMark.svelte";
  import { messageStateMark } from "#lib/state-mark.js";
  import { Button, buttonVariants } from "#lib/components/ui/button/index.js";
  import * as Collapsible from "#lib/components/ui/collapsible/index.js";
  import * as DropdownMenu from "#lib/components/ui/dropdown-menu/index.js";
  import * as Empty from "#lib/components/ui/empty/index.js";
  import * as Sidebar from "#lib/components/ui/sidebar/index.js";
  import { getUiText } from "#lib/ui-text.js";
  import { cn } from "#lib/utils.js";
  import type { Snippet } from "svelte";
  import { onMount, tick, untrack } from "svelte";
  import { getLocalEditorState, setLocalEditorState, subscribeLocalEditorState } from "./local-state";
  import {
    buildMessageTree,
    collapsedGroups,
    emptyMessageTreeExpansion,
    messageAncestorIds,
    messagePath,
    messageRowId,
    messageTreeStateKey,
    messageVirtualRowHeight,
    readCollapsedGroups,
    revealGroups,
    scrollTopForRow,
    setGroupExpanded,
    virtualMessageWindow,
    visibleMessageRows,
    writeCollapsedGroups,
    type MessageGroupRow,
    type MessageTreeExpansion,
    type MessageTreeRow,
  } from "./message-virtualization";

  let {
    items,
    selectedKey,
    visibleCount,
    catalogKey = "",
    filterKey = "",
    groupCounts = ["missing", "stale", "needsReview"],
    rowHeight = messageVirtualRowHeight,
    selectedHidden = false,
    translationStates = true,
    reviewActionsDisabled = false,
    noResultsLabel,
    labels,
    toolbar,
    onselect,
    onadd,
    onmarkreview,
    onapprove,
    onclearfilter,
    open = $bindable(true),
  }: {
    items: MessageListItem[];
    selectedKey: string;
    visibleCount: number;
    /** The catalog whose expansion state the tree shows and records. */
    catalogKey?: string;
    /** The active search and filter; empty when none is active. A change starts with every matching group expanded. */
    filterKey?: string;
    /** The counts a group row names besides its number of messages, for the current filter. */
    groupCounts?: readonly MessageGroupCountKind[];
    /** The height of one row in pixels, set by the density. */
    rowHeight?: number;
    /** The selected message exists but the search or filter hides it. */
    selectedHidden?: boolean;
    /**
     * Whether the rows show their translation state and the list offers the bulk review actions; a content-editing
     * project (D074.2) has neither.
     */
    translationStates?: boolean;
    reviewActionsDisabled?: boolean;
    noResultsLabel: string;
    labels: MessageListLabels;
    toolbar: Snippet;
    onselect: (key: string) => void;
    onadd: () => void;
    onmarkreview: () => void;
    onapprove: () => void;
    onclearfilter?: () => void;
    open?: boolean;
  } = $props();

  const ui = getUiText();
  const sidebar = Sidebar.useSidebar();
  let scrollTop = $state(0);
  let viewportHeight = $state(0);
  let scroller = $state<HTMLElement | null>(null);
  let expansion = $state.raw<MessageTreeExpansion>(emptyMessageTreeExpansion);
  let loadedCatalog: string | undefined;
  // The message whose groups were last expanded for it; a new selection reveals its message once.
  let revealedKey = "";
  // The row that takes focus in the tree (roving tabindex); falls back to the selected message, then the first row.
  let activeId = $state<string | undefined>(undefined);
  let filtering = $derived(filterKey !== "");
  let tree = $derived(buildMessageTree(items));
  let rows = $derived(visibleMessageRows(tree, collapsedGroups(expansion, filtering)));
  let rowIndex = $derived(new Map(rows.map((row, index) => [row.id, index])));
  let focusIndex = $derived(
    (activeId === undefined ? undefined : rowIndex.get(activeId)) ?? rowIndex.get(messageRowId(selectedKey)) ?? 0,
  );
  let viewport = $derived(virtualMessageWindow(rows.length, scrollTop, viewportHeight, rowHeight));
  // The viewport's rows, plus the focus row so the tree always keeps one tab stop.
  let renderedRows = $derived.by(() => {
    const rendered: { row: MessageTreeRow; index: number }[] = [];
    if (focusIndex < viewport.start && rows[focusIndex] !== undefined) rendered.push({ row: rows[focusIndex], index: focusIndex });
    for (let index = viewport.start; index < viewport.end; index += 1) rendered.push({ row: rows[index], index });
    if (focusIndex >= viewport.end && rows[focusIndex] !== undefined) rendered.push({ row: rows[focusIndex], index: focusIndex });
    return rendered;
  });

  onMount(() => {
    const refresh = (): void => {
      open = getLocalEditorState("runic.sidebar.messages") !== "closed";
      // The native record loads after the first render; take its collapsed groups once it arrives.
      const stored = readCollapsedGroups(getLocalEditorState(messageTreeStateKey), catalogKey);
      if (!sameSet(stored, expansion.collapsed)) expansion = { ...expansion, collapsed: stored };
    };
    refresh();
    return subscribeLocalEditorState(refresh);
  });

  // Another catalog: its own recorded expansion state, nothing transient, and its selection revealed anew.
  $effect(() => {
    const catalog = catalogKey;
    untrack(() => {
      if (loadedCatalog === catalog) return;
      loadedCatalog = catalog;
      revealedKey = "";
      activeId = undefined;
      expansion = { collapsed: readCollapsedGroups(getLocalEditorState(messageTreeStateKey), catalog), transient: new Set() };
    });
  });

  // A new search or filter starts with every group that has a match expanded.
  $effect(() => {
    void filterKey;
    untrack(() => {
      if (expansion.transient.size > 0) expansion = { ...expansion, transient: new Set() };
    });
  });

  // A newly selected message (from the list, the command palette, a diagnostic or after a reload) gets its groups
  // expanded and is scrolled into view once it is in the list.
  $effect(() => {
    const key = selectedKey;
    const item = tree.messages.get(key);
    if (key === revealedKey || item === undefined) return;
    untrack(() => reveal(key, messagePath(item)));
  });

  function reveal(key: string, path: readonly string[]): void {
    revealedKey = key;
    updateExpansion(revealGroups(expansion, messageAncestorIds(path)));
    activeId = messageRowId(key);
    void tick().then(() => {
      const index = rowIndex.get(messageRowId(key));
      if (index !== undefined) scrollToRow(index);
    });
  }

  function updateExpansion(next: MessageTreeExpansion): void {
    if (next === expansion) return;
    const persist = next.collapsed !== expansion.collapsed;
    expansion = next;
    if (persist) {
      try {
        setLocalEditorState(messageTreeStateKey, writeCollapsedGroups(getLocalEditorState(messageTreeStateKey), catalogKey, next.collapsed));
      } catch (error) {
        // Without the native state (a test page), the expansion state lasts for the session only.
        console.warn("The message tree state could not be recorded.", error);
      }
    }
  }

  function setExpanded(row: MessageGroupRow, expanded: boolean): void {
    updateExpansion(setGroupExpanded(expansion, row.groupId, expanded, filtering));
  }

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

  function clearFilter(): void {
    // The selection is revealed again once the cleared filter shows it.
    revealedKey = "";
    onclearfilter?.();
  }

  function activate(row: MessageTreeRow): void {
    activeId = row.id;
    if (row.kind === "group") setExpanded(row, !row.expanded);
    else selectMessage(row.key);
  }

  function scrollToRow(index: number): void {
    if (scroller === null) return;
    const next = scrollTopForRow(index, scroller.scrollTop, scroller.clientHeight, rowHeight);
    if (next !== scroller.scrollTop) {
      scroller.scrollTop = next;
      scrollTop = next;
    }
  }

  async function focusRow(index: number): Promise<void> {
    const row = rows[Math.max(0, Math.min(rows.length - 1, index))];
    if (row === undefined) return;
    activeId = row.id;
    scrollToRow(rowIndex.get(row.id) ?? 0);
    await tick();
    scroller?.querySelector<HTMLElement>(`[data-row-id="${CSS.escape(row.id)}"]`)?.focus();
  }

  // The ARIA tree keyboard model: Up/Down move, Right expands or enters a group, Left collapses or moves to the
  // parent group, Home/End go to the first/last row, Enter/Space select a message or toggle a group.
  function handleTreeKeydown(event: KeyboardEvent): void {
    if (event.altKey || event.ctrlKey || event.metaKey) return;
    const index = focusIndex;
    const row = rows[index];
    if (row === undefined) return;
    const rightToLeft = scroller !== null && getComputedStyle(scroller).direction === "rtl";
    const key = rightToLeft && event.key === "ArrowRight" ? "ArrowLeft" : rightToLeft && event.key === "ArrowLeft" ? "ArrowRight" : event.key;
    switch (key) {
      case "ArrowDown": void focusRow(index + 1); break;
      case "ArrowUp": void focusRow(index - 1); break;
      case "Home": void focusRow(0); break;
      case "End": void focusRow(rows.length - 1); break;
      case "ArrowRight":
        if (row.kind === "group" && !row.expanded) setExpanded(row, true);
        else if (row.kind === "group" && rows[index + 1]?.parentId === row.id) void focusRow(index + 1);
        break;
      case "ArrowLeft":
        if (row.kind === "group" && row.expanded) setExpanded(row, false);
        else if (row.parentId !== undefined) void focusRow(rowIndex.get(row.parentId) ?? index);
        break;
      case "Enter":
      case " ":
        activate(row);
        break;
      default:
        return;
    }
    event.preventDefault();
  }

  function groupCountText(kind: MessageGroupCountKind, count: number): string {
    return ui.text(kind === "missing" ? "ui_message_tree_group_missing" : kind === "stale" ? "ui_message_tree_group_stale" : "ui_message_tree_group_review", { count });
  }

  function groupLabel(row: MessageGroupRow): string {
    return [
      ui.text("ui_message_tree_group_label", { name: row.label, count: row.counts.messages }),
      ...groupCounts.filter((kind) => row.counts[kind] > 0).map((kind) => groupCountText(kind, row.counts[kind])),
    ].join(", ");
  }

  function sameSet(left: ReadonlySet<string>, right: ReadonlySet<string>): boolean {
    return left.size === right.size && [...left].every((value) => right.has(value));
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
        {#if translationStates}
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
        {/if}
        <Button variant="ghost" size="icon-xs" aria-label={labels.addMessage} title={labels.addMessage} onclick={addMessage}>
          <PlusIcon />
        </Button>
      </div>
    </Sidebar.GroupLabel>

    <Collapsible.Content class="min-h-0 flex-1 overflow-hidden">
      {@render toolbar()}
      {#if selectedHidden}
        <div class="mx-2 mb-2 flex items-start gap-2 rounded-md border border-dashed px-2 py-1.5 text-xs text-muted-foreground" role="status" data-testid="selected-message-hidden">
          <EyeOffIcon class="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />
          <div class="min-w-0">
            <p>{ui.text("ui_message_tree_selected_hidden")}</p>
            {#if onclearfilter !== undefined}
              <Button variant="link" size="xs" class="h-auto p-0 text-xs" onclick={clearFilter}>{ui.text("ui_message_tree_show_all")}</Button>
            {/if}
          </div>
        </div>
      {/if}
      <Sidebar.GroupContent class="min-h-0 flex-1">
        <nav
          class="min-h-0 flex-1 overflow-y-auto pb-3"
          aria-label={labels.messages}
          bind:this={scroller}
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
            <div
              class="relative mx-2"
              role="tree"
              aria-label={labels.messages}
              data-testid="message-tree"
              style:height={`${rows.length * rowHeight}px`}
            >
              {#each renderedRows as { row, index } (row.id)}
                {@const indent = `${(row.level - 1) * 1 + 0.5}rem`}
                {#if row.kind === "group"}
                  <div
                    role="treeitem"
                    class="absolute inset-x-0 flex cursor-default select-none items-center gap-1.5 rounded-md px-2 text-xs font-semibold text-foreground hover:bg-sidebar-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sidebar-ring"
                    style:top={`${index * rowHeight}px`}
                    style:height={`${rowHeight}px`}
                    style:padding-inline-start={indent}
                    data-row-id={row.id}
                    data-group={row.groupId}
                    aria-label={groupLabel(row)}
                    aria-expanded={row.expanded}
                    aria-selected={false}
                    aria-level={row.level}
                    aria-setsize={row.setSize}
                    aria-posinset={row.posInSet}
                    tabindex={index === focusIndex ? 0 : -1}
                    onfocus={() => activeId = row.id}
                    onclick={() => activate(row)}
                    onkeydown={handleTreeKeydown}
                  >
                    <ChevronRightIcon class={cn("size-3.5 shrink-0 transition-transform", row.expanded ? "rotate-90" : "rtl:-scale-x-100")} aria-hidden="true" />
                    <span class="truncate font-mono">{row.label}</span>
                    <span class="ml-auto flex shrink-0 items-center gap-1" aria-hidden="true">
                      {#each groupCounts.filter((kind) => row.counts[kind] > 0) as kind (kind)}
                        <Badge variant="outline" class="px-1.5 font-normal" title={groupCountText(kind, row.counts[kind])}>{groupCountText(kind, row.counts[kind])}</Badge>
                      {/each}
                      <Badge variant="secondary">{row.counts.messages}</Badge>
                    </span>
                  </div>
                {:else}
                  {@const structureLabel = row.item.structure === undefined ? undefined : labels[row.item.structure]}
                  {@const status = row.item.stale ? labels.stale : row.item.needsReview ? labels.review : structureLabel}
                  <div
                    role="treeitem"
                    class={cn("absolute inset-x-0 flex cursor-default items-center gap-2 rounded-md px-2 text-left text-sm hover:bg-sidebar-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sidebar-ring", selectedKey === row.key && "tree-current")}
                    style:top={`${index * rowHeight}px`}
                    style:height={`${rowHeight}px`}
                    style:padding-inline-start={indent}
                    data-row-id={row.id}
                    data-message-key={row.key}
                    aria-label={`${row.label}: ${row.item.preview}${status === undefined ? "" : `, ${status}`}`}
                    aria-selected={selectedKey === row.key}
                    aria-level={row.level}
                    aria-setsize={row.setSize}
                    aria-posinset={row.posInSet}
                    title={`${row.dottedPath}\n${row.item.preview}`}
                    tabindex={index === focusIndex ? 0 : -1}
                    onfocus={() => activeId = row.id}
                    onclick={() => activate(row)}
                    onkeydown={handleTreeKeydown}
                  >
                    {#if translationStates}<StateMark state={messageStateMark(row.item)} />{/if}
                    <span class="truncate font-mono text-[0.92em]">{row.label}</span>
                    {#if status !== undefined}
                      <span class="ml-auto text-xs text-muted-foreground">{status}</span>
                    {/if}
                  </div>
                {/if}
              {/each}
            </div>
          {/if}
        </nav>
      </Sidebar.GroupContent>
    </Collapsible.Content>
  </Sidebar.Group>
</Collapsible.Root>
