import type { MessageListItem, MessageTreeNode } from "./MessageList.svelte";

export const messageVirtualRowHeight = 32;
export const messageVirtualOverscan = 8;

export interface VirtualMessageTreeRow {
  readonly kind: "branch" | "message";
  readonly key: string;
  readonly label: string;
  readonly depth: number;
  readonly item?: MessageListItem;
  readonly messageCount?: number;
}

export interface VirtualMessageWindow {
  readonly start: number;
  readonly end: number;
  readonly offset: number;
}

/** Builds the semantic tree once, then flattens it for viewport-windowed rendering. */
export function virtualMessageTree(items: readonly MessageListItem[]): VirtualMessageTreeRow[] {
  return flattenTree(buildTree(items));
}

/** Returns only the rows needed for the viewport plus a small scroll overscan. */
export function virtualMessageWindow(
  rowCount: number,
  scrollTop: number,
  viewportHeight: number,
): VirtualMessageWindow {
  const visible = Math.max(1, Math.ceil(Math.max(0, viewportHeight) / messageVirtualRowHeight));
  const first = Math.max(0, Math.floor(Math.max(0, scrollTop) / messageVirtualRowHeight));
  const start = Math.max(0, first - messageVirtualOverscan);
  const end = Math.min(rowCount, first + visible + messageVirtualOverscan);
  return { start, end, offset: start * messageVirtualRowHeight };
}

function buildTree(items: readonly MessageListItem[]): MessageTreeNode[] {
  const roots: MessageTreeNode[] = [];
  const nodes = new Map<string, MessageTreeNode>();
  for (const item of items) {
    const segments = item.key.split(".").filter(Boolean);
    const safeSegments = segments.length === 0 ? [item.key] : segments;
    let siblings = roots;
    let path = "";
    for (const segment of safeSegments) {
      path = path === "" ? segment : `${path}.${segment}`;
      let node = nodes.get(path);
      if (node === undefined) {
        node = { segment, path, children: [] };
        nodes.set(path, node);
        siblings.push(node);
      }
      siblings = node.children;
      if (path === item.key) node.item = item;
    }
  }
  return roots;
}

function flattenTree(nodes: readonly MessageTreeNode[]): VirtualMessageTreeRow[] {
  const rows: VirtualMessageTreeRow[] = [];
  const visit = (node: MessageTreeNode, depth: number): number => {
    const branchIndex = node.children.length > 0 ? rows.length : -1;
    if (branchIndex >= 0) rows.push({ kind: "branch", key: `branch:${node.path}`, label: node.segment, depth });
    if (node.item !== undefined) {
      rows.push({
        kind: "message",
        key: node.item.key,
        label: node.children.length > 0 ? "Overview" : node.segment,
        depth: node.children.length > 0 ? depth + 1 : depth,
        item: node.item,
      });
    }
    let count = node.item === undefined ? 0 : 1;
    for (const child of node.children) count += visit(child, depth + 1);
    if (branchIndex >= 0) rows[branchIndex] = { ...rows[branchIndex], messageCount: count };
    return count;
  };
  for (const node of nodes) visit(node, 1);
  return rows;
}
