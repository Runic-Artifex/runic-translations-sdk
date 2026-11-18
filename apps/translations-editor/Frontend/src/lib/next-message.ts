import type { MessageFilter } from "./MessageToolbar.svelte";
import { buildMessageTree, messageTreeKeys } from "./message-virtualization";

/**
 * What "Next" steps through (D074.1): the messages missing in the selected locale, or, while the list shows only
 * outdated messages or those that need review, those. In a content-editing project (D074.2) every text: "text".
 */
export type NextMessageKind = "missing" | "stale" | "needs-review" | "text";
export type NextMessageDirection = "next" | "previous";

/** The kind the Next and Previous commands step through for the list's filter. */
export function nextMessageKind(filter: MessageFilter): NextMessageKind {
  return filter === "stale" || filter === "needs-review" ? filter : "missing";
}

/**
 * The message keys in the order of the namespace tree with every group expanded: siblings by segment, a group's
 * messages before the next sibling. Any subset of messages (a search, a filter) keeps this relative order.
 */
export function messageTreeOrder(items: ReadonlyArray<{ readonly key: string; readonly path: readonly string[] }>): string[] {
  return messageTreeKeys(buildMessageTree(items.map((item) => ({
    key: item.key, path: item.path, preview: "", missing: false, structured: false, stale: false, needsReview: false,
  }))));
}

/**
 * The message after (or before) `current` in tree order among `candidates`. It wraps: after the last candidate comes
 * the first again, and `wrapped` says so. `current` itself is never the answer, so a single candidate that is the
 * current message gives none. A current message that is not a candidate (the save made it complete, the filter hides
 * it) still marks the position; one the tree does not have starts from the top (or, going back, from the bottom).
 */
export function adjacentMessage(
  order: readonly string[],
  candidates: ReadonlySet<string>,
  current: string,
  direction: NextMessageDirection,
): { key: string; wrapped: boolean } | undefined {
  const count = order.length;
  if (count === 0 || candidates.size === 0) return undefined;
  const position = order.indexOf(current);
  const step = direction === "next" ? 1 : -1;
  // Without a position, start just before the first (or just after the last) message.
  const start = position >= 0 ? position : direction === "next" ? -1 : count;
  for (let offset = 1; offset <= count; offset += 1) {
    const raw = start + step * offset;
    const index = ((raw % count) + count) % count;
    const key = order[index];
    if (key === current) continue;
    if (candidates.has(key)) return { key, wrapped: raw < 0 || raw >= count };
  }
  return undefined;
}
