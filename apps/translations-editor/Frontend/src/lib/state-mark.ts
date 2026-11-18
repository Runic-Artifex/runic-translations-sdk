import type { EditorReviewState } from "./contracts";

/**
 * The state shapes of the brand (D074): a hollow square for missing, a filled square for done, a diagonal for
 * outdated, a hollow square in the warning colour for needs review. The colour adds to the shape and to the text that
 * names the state; it never replaces them.
 */
export type StateMarkKind = "missing" | "done" | "outdated" | "review";

/** A message in the tree, for the selected locale. */
export function messageStateMark(item: { missing: boolean; stale: boolean; needsReview: boolean }): StateMarkKind {
  if (item.missing) return "missing";
  if (item.stale) return "outdated";
  if (item.needsReview) return "review";
  return "done";
}

/**
 * A message's review state, as the workflow badge shows it. A draft has text but is not finished: it has no mark of its
 * own, and the badge names it.
 */
export function reviewStateMark(state: EditorReviewState, notTranslated: boolean, stale: boolean): StateMarkKind | undefined {
  if (notTranslated) return "missing";
  if (stale) return "outdated";
  if (state === "needs-review") return "review";
  if (state === "translated" || state === "approved") return "done";
  return undefined;
}

/**
 * A language in the language list: missing while a message has no text, outdated while a reviewed translation's
 * source changed, else done. The source language has no mark.
 */
export function localeStateMark(locale: { isSource: boolean; missing?: number; stale?: number; percent: number }): StateMarkKind | undefined {
  if (locale.isSource) return undefined;
  if (locale.missing === undefined ? locale.percent < 100 : locale.missing > 0) return "missing";
  if ((locale.stale ?? 0) > 0) return "outdated";
  return "done";
}
