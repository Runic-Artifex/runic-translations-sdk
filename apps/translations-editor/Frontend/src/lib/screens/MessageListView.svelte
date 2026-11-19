<script lang="ts">
  import MessageSquareOffIcon from "@lucide/svelte/icons/message-square-off";
  import PanelsTopLeftIcon from "@lucide/svelte/icons/panels-top-left";
  import { tick } from "svelte";
  import { Button } from "#lib/components/ui/button/index.js";
  import * as Empty from "#lib/components/ui/empty/index.js";
  import type { EditorCatalog } from "#lib/contracts.js";
  import type { EditorLabels } from "#lib/editor-labels.js";
  import { inlineVariant, listMessageRows, type ListGroupRow, type ListMessageRow, type ListRow, type ListRowStateName } from "#lib/list-view.js";
  import { ListRowHeights, listRowOffsets, listSlices, listWindow, type ListRowKind } from "#lib/list-virtualization.js";
  import { localeDirection } from "#lib/locale-text.js";
  import { authoringInputs, type SourceSegment, type SourceTextView } from "#lib/message-composer.js";
  import PatternEditor from "#lib/PatternEditor.svelte";
  import StateMark from "#lib/StateMark.svelte";
  import type { DiagnosticsStore } from "#lib/state/diagnostics-store.svelte.js";
  import type { MessageEditingController } from "#lib/state/message-editing.svelte.js";
  import type { SelectionController } from "#lib/state/selection-controller.svelte.js";
  import type { WorkspaceView } from "#lib/state/workspace-view.svelte.js";
  import { getUiText, type UiMessage } from "#lib/ui-text.js";
  import { unsavedTitleMarker } from "#lib/window-title.js";

  let {
    catalog,
    selection,
    view,
    editing,
    diagnostics,
    labels,
    clientError,
    editingBlocked,
    revision,
    localeName,
    onopenworkbench,
  }: {
    /** The open catalog. */
    catalog: EditorCatalog;
    selection: SelectionController;
    view: WorkspaceView;
    editing: Pick<MessageEditingController, "applyAuthoringOperation">;
    diagnostics: Pick<DiagnosticsStore, "latest">;
    labels: EditorLabels;
    clientError: UiMessage | undefined;
    /** Whether an operation in progress keeps the editor from changing. */
    editingBlocked: boolean;
    /** The draft history's revision: an undo or redo shows the draft again in the row's fields. */
    revision: number;
    localeName(tag: string): string;
    /** Shows the selected message in the workbench: its plural forms, variants or structure, its issues. */
    onopenworkbench(): void;
  } = $props();
  const ui = getUiText();
  const helpId = $props.id();

  // The side-by-side list (D074.1): one row per message the sidebar's search and filter let through, in tree order
  // under namespace headers. The current row is the selected message, so the workbench and the list share it. A row
  // whose text is one pattern is edited in place with the workbench's pattern editor, through the same draft, undo and
  // save; any other row shows a summary and opens in the workbench.
  const rows = $derived(view.listRows);
  const messageRows = $derived(listMessageRows(rows));
  const rowIndex = $derived(new Map(messageRows.map((row, index) => [row.key, index])));
  // The source text (the base locale, or a content-editing project, D074.2): no source column and no states, the
  // developers' note instead.
  const sourceText = $derived(view.editingSource);
  const locale = $derived(selection.selectedLocale);
  const baseLocale = $derived(catalog.defaultLocale);
  const columnCount = $derived(sourceText ? 3 : 4);
  const current = $derived.by(() => {
    const index = rowIndex.get(selection.selectedKey);
    return index === undefined ? undefined : messageRows[index];
  });
  // The row with the grid's one tab stop: the selection while the list shows it, else the first row.
  const focusKey = $derived(current?.key ?? messageRows[0]?.key);
  // The text the selected row edits: the starter of a missing translation, or the message's own.
  const authoring = $derived(selection.currentStarter?.projection ?? selection.currentCell?.entry?.authoring);
  const variant = $derived(inlineVariant(authoring));
  const inputs = $derived(authoringInputs(authoring));
  const invalid = $derived(clientError !== undefined || diagnostics.latest?.success === false);
  const labelsByState = $derived<Record<ListRowStateName, string>>({
    missing: labels.missing,
    outdated: ui.text("ui_list_state_outdated"),
    "needs-review": ui.text("ui_workflow_needs_review"),
    approved: ui.text("ui_workflow_approved"),
    translated: ui.text("ui_workflow_translated"),
    draft: ui.text("ui_workflow_draft"),
  });
  const structureLabel = (row: ListMessageRow): string | undefined => row.structure === undefined ? undefined : labels[row.structure];

  let grid = $state<HTMLElement | null>(null);
  let scroller = $state<HTMLElement | null>(null);

  // Virtualization: only the rows in view, some overscan and the row with the tab stop (also the edited row) are in the
  // DOM; the rows left out are gaps of their height. Heights are measured as rows are shown (multi-line text, a row's
  // editor), and a row not shown yet counts as the average of its kind.
  const heights = new ListRowHeights();
  let measured = $state(0);
  let scrollTop = $state(0);
  let viewportHeight = $state(0);
  let headHeight = $state(0);
  const rowPosition = $derived(new Map(rows.map((row, index) => [row.id, index])));
  const offsets = $derived.by(() => {
    void measured;
    return listRowOffsets(rows, heights);
  });
  const pinnedIndex = $derived(focusKey === undefined ? undefined : rowPosition.get(`m:${focusKey}`));
  type RenderedItem = { kind: "gap"; id: string; height: number } | { kind: "row"; id: string; row: ListRow; position: number };
  const span = $derived(listWindow(offsets, scrollTop - headHeight, viewportHeight));
  const rendered = $derived.by((): RenderedItem[] => {
    let gaps = 0;
    return listSlices(offsets, span, pinnedIndex === undefined ? [] : [pinnedIndex]).flatMap((slice): RenderedItem[] => slice.kind === "gap"
      ? [{ kind: "gap" as const, id: `gap:${gaps++}`, height: slice.height }]
      : rows.slice(slice.start, slice.end).map((row, offset) => ({ kind: "row" as const, id: row.id, row, position: slice.start + offset })));
  });
  const measuredRows = new WeakMap<Element, { id: string; kind: ListRowKind }>();
  let resizeObserver: ResizeObserver | undefined;

  // The rows in view stay where they are while rows are measured. Two things move them: a row shown for the first time
  // above the viewport (scrolling up) is taller or shorter than its estimate, and a new average changes the estimate of
  // the rows not shown above the window, the gap's height. The scroll position moves by both, once the gap is updated.
  function measureRows(entries: ResizeObserverEntry[]): void {
    const anchor = (scroller?.scrollTop ?? 0);
    const start = span.start;
    const gapBefore = offsets[start];
    let shift = 0;
    let changed = false;
    for (const entry of entries) {
      const target = measuredRows.get(entry.target);
      if (target === undefined || !entry.target.isConnected) continue;
      const height = entry.borderBoxSize?.[0]?.blockSize ?? entry.target.getBoundingClientRect().height;
      if (height <= 0) continue;
      const before = heights.height(target.id, target.kind);
      if (!heights.set(target.id, target.kind, height)) continue;
      changed = true;
      const index = rowPosition.get(target.id);
      if (index !== undefined && offsets[index] + before <= anchor) shift += height - before;
    }
    if (!changed) return;
    measured += 1;
    void tick().then(() => {
      const correction = shift + (offsets[start] ?? gapBefore) - gapBefore;
      if (correction !== 0 && scroller !== null) scroller.scrollTop += correction;
      // A row being revealed moves while the rows around it are measured: it is revealed again once they are shown.
      const target = revealing;
      if (target === undefined) return;
      if (performance.now() > target.until || target.key !== selection.selectedKey) revealing = undefined;
      else reveal(target.key);
    });
  }

  function measureRow(id: string, kind: ListRowKind) {
    return (element: HTMLElement) => {
      resizeObserver ??= new ResizeObserver(measureRows);
      measuredRows.set(element, { id, kind });
      resizeObserver.observe(element);
      return () => {
        resizeObserver?.unobserve(element);
        measuredRows.delete(element);
      };
    };
  }

  $effect(() => () => resizeObserver?.disconnect());

  // Another language or the source text's columns: the rows' heights change as a whole.
  let measuredFor = "";
  $effect(() => {
    const signature = `${locale}\u0000${sourceText}`;
    if (signature === measuredFor) return;
    if (measuredFor !== "") {
      heights.clear();
      measured += 1;
    }
    measuredFor = signature;
  });

  // The row being revealed, for a moment after the selection changed: until the user scrolls, or measuring settles.
  let revealing: { key: string; until: number } | undefined;

  // The user scrolls: the list stops revealing the selection.
  $effect(() => {
    const element = scroller;
    if (element === null) return;
    const stop = (): void => { revealing = undefined; };
    element.addEventListener("wheel", stop, { passive: true });
    element.addEventListener("pointerdown", stop, { passive: true });
    return () => {
      element.removeEventListener("wheel", stop);
      element.removeEventListener("pointerdown", stop);
    };
  });

  function revealSoon(key: string): void {
    revealing = { key, until: performance.now() + 1_000 };
    reveal(key);
  }

  /** Scrolls the row of `key` into view; it is in the DOM, as the selected row is never left out. */
  function reveal(key: string): void {
    const element = rowElement(key);
    if (element !== null) {
      element.scrollIntoView({ block: "nearest" });
      return;
    }
    const index = rowPosition.get(`m:${key}`);
    if (index === undefined || scroller === null) return;
    const top = offsets[index];
    const bottom = offsets[index + 1];
    if (top < scroller.scrollTop) scroller.scrollTop = top;
    else if (headHeight + bottom > scroller.scrollTop + scroller.clientHeight) scroller.scrollTop = headHeight + bottom - scroller.clientHeight;
  }
  // Whether the selected row is being edited. It stays on while Tab, Ctrl+Enter or Next moves to another row that is
  // edited in place.
  let editMode = $state(false);
  // The row whose field takes the focus once it is shown (a starter can still be loading).
  let pendingFocus = $state<{ key: string; last: boolean }>();
  let shownKey = "";

  const editingRow = (row: ListMessageRow): boolean => editMode && row.inline && row.key === selection.selectedKey;

  // Another selection (the tree, Next missing, Save and next): the list scrolls to it, and an edit goes on in that row
  // when it is edited in place and the focus was in the list.
  $effect(() => {
    const key = selection.selectedKey;
    const row = current;
    if (key === shownKey) return;
    shownKey = key;
    if (editMode && row?.inline !== true) editMode = false;
    const active = document.activeElement;
    if (editMode && row !== undefined && pendingFocus?.key !== key && (active === null || active === document.body || grid?.contains(active) === true)) {
      pendingFocus = { key, last: false };
    }
    // Rows measured once they are in view can move it: it is revealed again while they are (`measureRows`).
    void tick().then(() => revealSoon(key));
  });

  // The field of the row that is to be edited, once it is there.
  $effect(() => {
    const target = pendingFocus;
    if (target === undefined || variant === undefined || target.key !== selection.selectedKey || selection.starterLoading) return;
    void tick().then(() => {
      if (pendingFocus !== target) return;
      const fields = editorFields(target.key);
      const field = target.last ? fields.at(-1) : fields[0];
      if (field === undefined) return;
      pendingFocus = undefined;
      field.focus();
      field.setSelectionRange(field.value.length, field.value.length);
    });
  });

  function rowElement(key: string): HTMLElement | null {
    return grid?.querySelector<HTMLElement>(`[data-list-row="${CSS.escape(key)}"]`) ?? null;
  }

  function editorFields(key: string): HTMLTextAreaElement[] {
    return [...(rowElement(key)?.querySelectorAll<HTMLTextAreaElement>("[data-pattern-box] textarea[data-draft-field]") ?? [])];
  }

  async function focusCell(key: string): Promise<void> {
    await tick();
    const cell = rowElement(key)?.querySelector<HTMLElement>("[data-list-text]");
    cell?.focus();
  }

  function select(row: ListMessageRow): void {
    if (row.key !== selection.selectedKey) selection.selectVisible(row.key, view.visibleRows);
  }

  /** Moves the current row to `index`; while editing, the edit goes on there when that row is edited in place. */
  function moveTo(index: number, keepEditing: boolean, last = false): void {
    const row = messageRows[Math.max(0, Math.min(messageRows.length - 1, index))];
    if (row === undefined) return;
    select(row);
    if (keepEditing && row.inline) {
      editMode = true;
      pendingFocus = { key: row.key, last };
    } else {
      editMode = false;
      void focusCell(row.key);
    }
  }

  /** Enter or a click: edits a row in place, or opens a message with more structure in the workbench. */
  function activate(row: ListMessageRow): void {
    select(row);
    if (!row.inline) {
      onopenworkbench();
      return;
    }
    if (editingBlocked) return;
    if (selection.mode !== "translation") selection.chooseMode("translation");
    editMode = true;
    pendingFocus = { key: row.key, last: true };
  }

  function stopEditing(row: ListMessageRow): void {
    editMode = false;
    pendingFocus = undefined;
    void focusCell(row.key);
  }

  // The grid's keys on a cell: Up and Down (Home, End) move between rows, Enter edits or opens the row.
  function cellKeydown(event: KeyboardEvent, row: ListMessageRow, index: number): void {
    if (event.target !== event.currentTarget) {
      editorKeydown(event, row, index);
      return;
    }
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    switch (event.key) {
      case "ArrowDown": moveTo(index + 1, false); break;
      case "ArrowUp": moveTo(index - 1, false); break;
      case "Home": moveTo(0, false); break;
      case "End": moveTo(messageRows.length - 1, false); break;
      case "Enter": activate(row); break;
      default: return;
    }
    event.preventDefault();
  }

  // In a row's editor: Escape stops editing; Tab goes through the row's text fields, then to the next row's text
  // (Shift+Tab back). The last row's Tab leaves the list as usual.
  function editorKeydown(event: KeyboardEvent, row: ListMessageRow, index: number): void {
    if (event.defaultPrevented || event.isComposing || event.altKey || event.ctrlKey || event.metaKey) return;
    if (event.key === "Escape") {
      event.preventDefault();
      stopEditing(row);
      return;
    }
    if (event.key !== "Tab" || !(event.target instanceof HTMLTextAreaElement)) return;
    const fields = editorFields(row.key);
    const position = fields.indexOf(event.target);
    if (position < 0) return;
    const step = event.shiftKey ? -1 : 1;
    const field = fields[position + step];
    if (field !== undefined) {
      event.preventDefault();
      field.focus();
      return;
    }
    const target = index + step;
    if (target < 0 || target >= messageRows.length) return;
    event.preventDefault();
    moveTo(target, true, event.shiftKey);
  }

  function cellClick(event: MouseEvent, row: ListMessageRow, text: boolean): void {
    if (text && editingRow(row)) return;
    if (text) activate(row);
    else {
      select(row);
      if (!editingRow(row)) void focusCell(row.key);
    }
    event.preventDefault();
  }

  function groupLabel(row: ListGroupRow): string {
    return row.group === "" ? ui.text("ui_list_top_level") : row.group;
  }

  function groupCounts(row: ListGroupRow): string {
    const parts = [ui.text("ui_count_messages", { count: row.messages })];
    if (row.missing > 0) parts.push(ui.text("ui_message_tree_group_missing", { count: row.missing }));
    return parts.join(" · ");
  }
</script>

{#snippet segments(items: SourceSegment[])}{#each items as segment, index (index)}{#if segment.kind === "text"}{segment.text}{:else}<span class="placeholder-chip mx-0.5 align-baseline" data-placeholder={segment.kind}>{segment.syntax}</span>{/if}{/each}{/snippet}

{#snippet textView(text: SourceTextView, tag: string)}
  {#if text.kind === "source"}
    <code class="line-clamp-3 font-mono text-xs whitespace-pre-wrap" lang={tag} dir={localeDirection(tag)}>{text.text}</code>
  {:else}
    <span class="text-message whitespace-pre-wrap" lang={tag} dir={localeDirection(tag)}>{#each text.blocks as block, index (block.id)}{#if index > 0}<span class="list-separator">{" · "}</span>{/if}{@render segments(block.segments)}{/each}</span>
  {/if}
{/snippet}

{#snippet unsavedMark()}
  <span class="list-unsaved" role="img" aria-label={ui.text("ui_list_unsaved")} title={ui.text("ui_list_unsaved")} data-unsaved-mark>{unsavedTitleMarker.trim()}</span>
{/snippet}

{#snippet listRow(row: ListRow, position: number)}
  {#if row.kind === "group"}
    <div class="list-group" role="row" aria-rowindex={position + 2} data-list-group={row.group} {@attach measureRow(row.id, "group")}>
      <div role="rowheader" aria-colspan={columnCount}>
        <span class="font-mono">{groupLabel(row)}</span>
        <span class="list-group-counts">{groupCounts(row)}</span>
      </div>
    </div>
  {:else}
    {@const index = rowIndex.get(row.key) ?? 0}
    {@const selected = row.key === selection.selectedKey}
    {@const editingThis = editingRow(row)}
    <div
      class={["list-row", selected && "list-current"]}
      role="row"
      aria-selected={selected}
      aria-rowindex={position + 2}
      data-list-row={row.key}
      data-state={row.state?.name}
      data-inline={row.inline}
      data-unsaved={row.unsaved}
      {@attach measureRow(row.id, "message")}
    >
      <div role="gridcell" tabindex="-1" class="list-path" title={row.dottedPath} onclick={(event) => cellClick(event, row, false)} onkeydown={(event) => cellKeydown(event, row, index)}>
        {#if row.unsaved}{@render unsavedMark()}{/if}
        <span class="font-mono">{row.dottedPath}</span>
      </div>
      {#if !sourceText}
        <div role="gridcell" tabindex="-1" class="list-source" onclick={(event) => cellClick(event, row, false)} onkeydown={(event) => cellKeydown(event, row, index)}>
          {#if row.source !== undefined}{@render textView(row.source, baseLocale)}{/if}
        </div>
      {/if}
      <div
        role="gridcell"
        tabindex={row.key === focusKey && !editingThis ? 0 : -1}
        class="list-text"
        data-list-text
        aria-readonly={!row.inline}
        onclick={(event) => cellClick(event, row, true)}
        onkeydown={(event) => cellKeydown(event, row, index)}
      >
        {#if editingThis}
          {#if variant !== undefined}
            <div inert={editingBlocked} class:opacity-60={editingBlocked} data-list-editor>
              {#key `${selection.selectedDocumentPath}\u0000${row.key}\u0000${locale}`}
                <PatternEditor
                  compact
                  pattern={variant.pattern}
                  {inputs}
                  {locale}
                  {revision}
                  sourceToggle={false}
                  label={ui.text("ui_list_edit_label", { message: row.dottedPath, language: localeName(locale) })}
                  onchange={(pattern) => void editing.applyAuthoringOperation({ kind: "set-pattern", variantId: variant.id, pattern })}
                />
              {/key}
            </div>
            {#if invalid}
              <p class="mt-1 flex flex-wrap items-center gap-x-2 text-xs text-destructive" role="status" data-testid="list-row-errors">
                {ui.text("ui_list_has_errors")}
                <Button variant="link" size="xs" class="h-auto p-0 text-xs" onclick={onopenworkbench}>{ui.text("ui_list_open_in_workbench")}</Button>
              </p>
            {/if}
          {:else if selection.starterLoading || row.missing}
            <p class="text-sm text-muted-foreground" role="status">{ui.text("ui_translation_editor_starter_loading")}</p>
          {:else}
            <Button variant="link" size="xs" class="h-auto p-0 text-xs" onclick={onopenworkbench}>{ui.text("ui_list_open_in_workbench")}</Button>
          {/if}
        {:else if row.text !== undefined}
          {@render textView(row.text, locale)}
          {#if !row.inline}
            <span class="list-structure">
              {#if structureLabel(row) !== undefined}<span>{structureLabel(row)}</span>{/if}
              <PanelsTopLeftIcon class="size-3" aria-hidden="true" />
              <span class="sr-only">{ui.text("ui_list_open_in_workbench")}</span>
            </span>
          {/if}
        {:else}
          <span class="list-missing">{ui.text("ui_resource_not_translated")}</span>
          {#if !row.inline}
            <span class="list-structure">
              {#if structureLabel(row) !== undefined}<span>{structureLabel(row)}</span>{/if}
              <PanelsTopLeftIcon class="size-3" aria-hidden="true" />
              <span class="sr-only">{ui.text("ui_list_open_in_workbench")}</span>
            </span>
          {/if}
        {/if}
      </div>
      {#if sourceText}
        <div role="gridcell" tabindex="-1" class="list-note" onclick={(event) => cellClick(event, row, false)} onkeydown={(event) => cellKeydown(event, row, index)}>
          {row.note ?? ""}
        </div>
      {:else if row.state !== undefined}
        <div role="gridcell" tabindex="-1" class="list-state" onclick={(event) => cellClick(event, row, false)} onkeydown={(event) => cellKeydown(event, row, index)}>
          <StateMark state={row.state.mark} />
          <span>{labelsByState[row.state.name]}</span>
        </div>
      {/if}
    </div>
  {/if}
{/snippet}

<div
  class="list-scroller"
  data-testid="list-view"
  bind:this={scroller}
  bind:clientHeight={viewportHeight}
  style:scroll-padding-top={`${headHeight}px`}
  onscroll={(event) => scrollTop = event.currentTarget.scrollTop}
>
  {#if messageRows.length === 0}
    <Empty.Root>
      <Empty.Header>
        <Empty.Media variant="icon"><MessageSquareOffIcon /></Empty.Media>
        <Empty.Title>{labels.noMatchingMessages}</Empty.Title>
        <Empty.Description>{labels.noResults}</Empty.Description>
      </Empty.Header>
    </Empty.Root>
  {:else}
    <p id={helpId} class="sr-only">{ui.text("ui_list_keyboard_help")}</p>
    <div
      bind:this={grid}
      class={["list-grid", sourceText && "list-source-text"]}
      role="grid"
      aria-label={labels.messages}
      aria-describedby={helpId}
      aria-colcount={columnCount}
      aria-rowcount={rows.length + 1}
      data-list-locale={locale}
      data-list-rendered={rendered.length}
    >
      <div class="list-head" role="row" aria-rowindex={1} bind:offsetHeight={headHeight}>
        <div role="columnheader">{ui.text("ui_list_column_path")}</div>
        {#if sourceText}
          <div role="columnheader">{ui.text("ui_content_text")}</div>
          <div role="columnheader">{ui.text("ui_content_developer_note")}</div>
        {:else}
          <div role="columnheader">{localeName(baseLocale)}</div>
          <div role="columnheader">{localeName(locale)}</div>
          <div role="columnheader">{ui.text("ui_list_column_state")}</div>
        {/if}
      </div>

      {#each rendered as item (item.id)}
        {#if item.kind === "gap"}
          <div class="list-gap" aria-hidden="true" style:height={`${item.height}px`}></div>
        {:else}
          {@render listRow(item.row, item.position)}
        {/if}
      {/each}
    </div>
  {/if}
</div>

<style>
  .list-scroller {
    flex: 1;
    min-width: 0;
    min-height: 0;
    overflow: auto;
    /* The list keeps the rows in view in place itself while rows are measured (virtualization). */
    overflow-anchor: none;
    scrollbar-color: var(--border) transparent;
    /* Comfortable rows; the compact density tightens them (D074.4). */
    --list-cell-block: 0.625rem;
    --list-cell-inline: 0.875rem;
    --list-row-min: 2.75rem;
  }
  :global(:root[data-density="compact"]) .list-scroller {
    --list-cell-block: 0.3125rem;
    --list-cell-inline: 0.625rem;
    --list-row-min: 2.125rem;
  }
  .list-grid {
    --list-columns: minmax(9rem, 0.55fr) minmax(0, 1fr) minmax(0, 1fr) minmax(7.5rem, 9rem);
    min-width: 44rem;
  }
  .list-grid.list-source-text { --list-columns: minmax(9rem, 0.55fr) minmax(0, 1.4fr) minmax(0, 0.8fr); }
  .list-head, .list-row {
    display: grid;
    grid-template-columns: var(--list-columns);
    border-bottom: 1px solid var(--border);
  }
  .list-head {
    position: sticky;
    top: 0;
    z-index: 1;
    background: var(--card);
    color: var(--muted-foreground);
    font-family: var(--font-mono);
    font-size: var(--ui-text-xs);
    letter-spacing: 0.08em;
    text-transform: uppercase;
  }
  .list-head > div, .list-row > div {
    min-width: 0;
    padding: var(--list-cell-block) var(--list-cell-inline);
  }
  .list-group > div {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: 0.6rem;
    padding: calc(var(--list-cell-block) + 0.25rem) var(--list-cell-inline) var(--list-cell-block);
    border-bottom: 1px solid var(--border);
    font-size: var(--ui-text);
    font-weight: 650;
  }
  .list-group-counts {
    color: var(--muted-foreground);
    font-family: var(--font-mono);
    font-size: var(--ui-text-xs);
    font-weight: 500;
  }
  .list-row {
    min-height: var(--list-row-min);
    align-items: start;
  }
  .list-row > div { cursor: default; outline: none; }
  .list-row > div:focus-visible { box-shadow: inset 0 0 0 2px var(--ring); border-radius: var(--radius-control, 4px); }
  /* Paint: the current place, the selected message, with a painted edge as in the tree. */
  .list-current { background: var(--card); box-shadow: inset 2px 0 0 var(--paint); }
  .list-path { color: var(--muted-foreground); font-size: var(--ui-text-xs); overflow-wrap: anywhere; padding-top: calc(var(--list-cell-block) + 0.2rem) !important; }
  .list-source { color: var(--foreground); }
  /* The unsaved mark: the window title's marker, before the path of a message with unsaved work. */
  .list-unsaved {
    display: inline-block;
    margin-inline-end: 0.35rem;
    color: var(--foreground);
    font-weight: 700;
    line-height: 1;
  }
  .list-text { cursor: text !important; }
  .list-missing { color: var(--muted-foreground); font-style: italic; font-size: var(--message-text); }
  .list-structure {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    margin-inline-start: 0.5rem;
    color: var(--muted-foreground);
    font-size: var(--ui-text-xs);
    vertical-align: middle;
  }
  .list-separator { color: var(--muted-foreground); }
  .list-state {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    color: var(--muted-foreground);
    font-size: var(--ui-text-xs);
    padding-top: calc(var(--list-cell-block) + 0.25rem) !important;
  }
  .list-note { color: var(--muted-foreground); font-size: var(--ui-text-xs); }
</style>
