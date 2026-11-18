import type { MessageListItem } from "./MessageList.svelte";

export const messageVirtualRowHeight = 32;
export const messageVirtualOverscan = 8;

/** What a group row counts over the matching messages it contains, for the selected locale. */
export interface MessageGroupCounts {
  messages: number;
  missing: number;
  stale: number;
  needsReview: number;
}

/** A namespace: the messages whose logical paths start with `path`. */
export interface MessageTreeGroup {
  readonly kind: "group";
  /** The dotted path prefix (`common` or `files.dialog`); the group's identity for expansion state. */
  readonly id: string;
  readonly segment: string;
  readonly path: readonly string[];
  readonly children: MessageTreeNode[];
  readonly counts: MessageGroupCounts;
}

export interface MessageTreeLeaf {
  readonly kind: "message";
  readonly segment: string;
  readonly item: MessageListItem;
}

export type MessageTreeNode = MessageTreeGroup | MessageTreeLeaf;

export interface MessageTree {
  readonly roots: readonly MessageTreeNode[];
  /** The messages of the tree by their flattened key. */
  readonly messages: ReadonlyMap<string, MessageListItem>;
  readonly groupCount: number;
}

interface MessageTreeRowBase {
  /** Unique over groups and messages: `g:` + group id, or `m:` + message key. */
  readonly id: string;
  readonly label: string;
  /** 1 for the top level (`aria-level`). */
  readonly level: number;
  /** The number of siblings and the 1-based position among them (`aria-setsize`, `aria-posinset`). */
  readonly setSize: number;
  readonly posInSet: number;
  /** The row id of the enclosing group, if any. */
  readonly parentId?: string;
}

export interface MessageGroupRow extends MessageTreeRowBase {
  readonly kind: "group";
  readonly groupId: string;
  readonly expanded: boolean;
  readonly counts: MessageGroupCounts;
}

export interface MessageLeafRow extends MessageTreeRowBase {
  readonly kind: "message";
  readonly key: string;
  /** The dotted path (`common.cancel`). */
  readonly dottedPath: string;
  readonly item: MessageListItem;
}

export type MessageTreeRow = MessageGroupRow | MessageLeafRow;

export interface VirtualMessageWindow {
  readonly start: number;
  readonly end: number;
  readonly offset: number;
}

export const groupRowId = (groupId: string): string => `g:${groupId}`;
export const messageRowId = (key: string): string => `m:${key}`;

/** A message's logical path, or its key when the path is unknown. */
export function messagePath(item: Pick<MessageListItem, "key" | "path">): readonly string[] {
  return item.path !== undefined && item.path.length > 0 ? item.path : [item.key];
}

/** The ids of the groups enclosing a message with this path, outermost first. */
export function messageAncestorIds(path: readonly string[]): string[] {
  const ids: string[] = [];
  let id = "";
  for (let index = 0; index < path.length - 1; index += 1) {
    id = index === 0 ? path[index] : `${id}.${path[index]}`;
    ids.push(id);
  }
  return ids;
}

/**
 * Builds the namespace tree of the (already filtered) messages: groups are the path prefixes, leaves the messages,
 * labelled with their last segment. Messages from several files or mounts under the same prefix share one group.
 * Siblings are sorted by segment (ordinal); a message sorts before a group of the same name.
 */
export function buildMessageTree(items: readonly MessageListItem[]): MessageTree {
  const roots: MessageTreeNode[] = [];
  const groups = new Map<string, MessageTreeGroup>();
  const messages = new Map<string, MessageListItem>();
  for (const item of items) {
    messages.set(item.key, item);
    const path = messagePath(item);
    let siblings = roots;
    let id = "";
    for (let index = 0; index < path.length - 1; index += 1) {
      const segment = path[index];
      id = index === 0 ? segment : `${id}.${segment}`;
      let group = groups.get(id);
      if (group === undefined) {
        group = { kind: "group", id, segment, path: path.slice(0, index + 1), children: [], counts: { messages: 0, missing: 0, stale: 0, needsReview: 0 } };
        groups.set(id, group);
        siblings.push(group);
      }
      const counts = group.counts;
      counts.messages += 1;
      if (item.missing) counts.missing += 1;
      if (item.stale) counts.stale += 1;
      if (item.needsReview) counts.needsReview += 1;
      siblings = group.children;
    }
    siblings.push({ kind: "message", segment: path[path.length - 1], item });
  }
  sortNodes(roots);
  for (const group of groups.values()) sortNodes(group.children);
  return { roots, messages, groupCount: groups.size };
}

/** The rows a translator sees: every top-level node, and the children of each expanded group, depth first. */
export function visibleMessageRows(tree: MessageTree, collapsed: ReadonlySet<string>): MessageTreeRow[] {
  const rows: MessageTreeRow[] = [];
  const visit = (nodes: readonly MessageTreeNode[], level: number, parentId: string | undefined, prefix: string): void => {
    const setSize = nodes.length;
    for (let index = 0; index < setSize; index += 1) {
      const node = nodes[index];
      if (node.kind === "group") {
        const id = groupRowId(node.id);
        const expanded = !collapsed.has(node.id);
        rows.push({ kind: "group", id, groupId: node.id, label: node.segment, level, setSize, posInSet: index + 1, parentId, expanded, counts: node.counts });
        if (expanded) visit(node.children, level + 1, id, node.id);
      } else {
        rows.push({
          kind: "message", id: messageRowId(node.item.key), key: node.item.key, label: node.segment,
          dottedPath: prefix === "" ? node.segment : `${prefix}.${node.segment}`,
          level, setSize, posInSet: index + 1, parentId, item: node.item,
        });
      }
    }
  };
  visit(tree.roots, 1, undefined, "");
  return rows;
}

/** Builds the tree and its visible rows in one step (all groups expanded unless `collapsed` names them). */
export function virtualMessageTree(items: readonly MessageListItem[], collapsed: ReadonlySet<string> = new Set()): MessageTreeRow[] {
  return visibleMessageRows(buildMessageTree(items), collapsed);
}

/** Returns only the rows needed for the viewport plus a small scroll overscan. */
export function virtualMessageWindow(
  rowCount: number,
  scrollTop: number,
  viewportHeight: number,
): VirtualMessageWindow {
  const visible = Math.max(1, Math.ceil(Math.max(0, viewportHeight) / messageVirtualRowHeight));
  const first = Math.max(0, Math.floor(Math.max(0, scrollTop) / messageVirtualRowHeight));
  const start = Math.max(0, Math.min(first, rowCount) - messageVirtualOverscan);
  const end = Math.min(rowCount, first + visible + messageVirtualOverscan);
  return { start, end, offset: start * messageVirtualRowHeight };
}

/** The scroll position that brings row `index` into view, moving as little as possible. */
export function scrollTopForRow(index: number, scrollTop: number, viewportHeight: number): number {
  const top = index * messageVirtualRowHeight;
  const bottom = top + messageVirtualRowHeight;
  if (top < scrollTop) return top;
  if (viewportHeight > 0 && bottom > scrollTop + viewportHeight) return bottom - viewportHeight;
  return scrollTop;
}

/**
 * The expansion state of one catalog's tree. `collapsed` is the translator's own choice; while a search or filter is
 * active, every group with a match starts expanded and collapsing works on `transient`, which the next search or
 * filter change discards, so the translator's own state is back once the search is cleared.
 */
export interface MessageTreeExpansion {
  readonly collapsed: ReadonlySet<string>;
  readonly transient: ReadonlySet<string>;
}

export const emptyMessageTreeExpansion: MessageTreeExpansion = { collapsed: new Set(), transient: new Set() };

export function collapsedGroups(state: MessageTreeExpansion, filtering: boolean): ReadonlySet<string> {
  return filtering ? state.transient : state.collapsed;
}

export function setGroupExpanded(state: MessageTreeExpansion, id: string, expanded: boolean, filtering: boolean): MessageTreeExpansion {
  const current = collapsedGroups(state, filtering);
  if (current.has(id) !== expanded) return state;
  const next = new Set(current);
  if (expanded) next.delete(id);
  else next.add(id);
  return filtering ? { ...state, transient: next } : { ...state, collapsed: next };
}

/** Expands the given groups in both the translator's and the search's state, so a revealed message stays visible. */
export function revealGroups(state: MessageTreeExpansion, ids: readonly string[]): MessageTreeExpansion {
  if (!ids.some((id) => state.collapsed.has(id) || state.transient.has(id))) return state;
  const without = (set: ReadonlySet<string>) => {
    if (!ids.some((id) => set.has(id))) return set;
    const next = new Set(set);
    for (const id of ids) next.delete(id);
    return next;
  };
  return { collapsed: without(state.collapsed), transient: without(state.transient) };
}

/** The local-state record of collapsed groups (`runic-translations:message-tree:1`), for the most recent catalogs. */
export const messageTreeStateKey = "runic-translations:message-tree:1";
export const messageTreeStateLimits = { catalogs: 8, collapsedGroups: 2_000 } as const;

interface StoredTreeState {
  version: 1;
  catalogs: { catalog: string; collapsed: string[] }[];
}

/** The collapsed groups recorded for `catalog`; an unreadable record reads as none. */
export function readCollapsedGroups(raw: string | null, catalog: string): Set<string> {
  const entry = parseTreeState(raw).catalogs.find((candidate) => candidate.catalog === catalog);
  return new Set(entry?.collapsed ?? []);
}

/** The record with `catalog`'s collapsed groups first, keeping the most recent catalogs within the limits. */
export function writeCollapsedGroups(raw: string | null, catalog: string, collapsed: ReadonlySet<string>): string {
  const others = parseTreeState(raw).catalogs.filter((candidate) => candidate.catalog !== catalog);
  const own = [...collapsed].sort().slice(0, messageTreeStateLimits.collapsedGroups);
  const catalogs = (own.length === 0 ? others : [{ catalog, collapsed: own }, ...others]).slice(0, messageTreeStateLimits.catalogs);
  return JSON.stringify({ version: 1, catalogs } satisfies StoredTreeState);
}

function parseTreeState(raw: string | null): StoredTreeState {
  if (raw === null) return { version: 1, catalogs: [] };
  try {
    const value: unknown = JSON.parse(raw);
    if (typeof value !== "object" || value === null || (value as { version?: unknown }).version !== 1) return { version: 1, catalogs: [] };
    const catalogs = (value as { catalogs?: unknown }).catalogs;
    if (!Array.isArray(catalogs)) return { version: 1, catalogs: [] };
    return {
      version: 1,
      catalogs: catalogs
        .filter((entry): entry is { catalog: string; collapsed: unknown[] } =>
          typeof entry === "object" && entry !== null && typeof entry.catalog === "string" && Array.isArray(entry.collapsed))
        .map((entry) => ({ catalog: entry.catalog, collapsed: entry.collapsed.filter((id): id is string => typeof id === "string") })),
    };
  } catch {
    return { version: 1, catalogs: [] };
  }
}

function sortNodes(nodes: MessageTreeNode[]): void {
  nodes.sort((left, right) => left.segment < right.segment ? -1 : left.segment > right.segment ? 1
    : left.kind === right.kind ? 0 : left.kind === "message" ? -1 : 1);
}
