/**
 * Virtualization of the list view (D074.1): only the rows in the scroll viewport, a few rows of overscan and the rows
 * that must stay (the grid's tab stop and the edited row) are in the DOM. Rows differ in height (multi-line text, a
 * row's editor), so heights are measured as rows are shown; a row not yet shown counts as the average measured height
 * of its kind.
 */

/** Rows rendered beyond each edge of the viewport. */
export const listOverscanRows = 8;

/** A row's kind decides its estimate before it is measured: namespace headers are shorter than messages. */
export type ListRowKind = "group" | "message";

/** Heights before anything was measured, in CSS pixels: the comfortable density's minimum row and header. */
export const listRowEstimates: Readonly<Record<ListRowKind, number>> = { group: 40, message: 46 };

/** The measured heights of rows by id, with the running average per kind that unmeasured rows are estimated with. */
export class ListRowHeights {
  readonly #heights = new Map<string, number>();
  readonly #totals: Record<ListRowKind, { sum: number; count: number }> = {
    group: { sum: 0, count: 0 },
    message: { sum: 0, count: 0 },
  };
  readonly #kinds = new Map<string, ListRowKind>();

  /** The height of row `id`: measured, or the estimate of its kind. */
  height(id: string, kind: ListRowKind): number {
    return this.#heights.get(id) ?? this.estimate(kind);
  }

  /** What a row of `kind` that was not measured yet counts as. */
  estimate(kind: ListRowKind): number {
    const total = this.#totals[kind];
    return total.count === 0 ? listRowEstimates[kind] : total.sum / total.count;
  }

  /** Records a measured height; returns whether it changed. */
  set(id: string, kind: ListRowKind, height: number): boolean {
    const previous = this.#heights.get(id);
    if (previous !== undefined && Math.abs(previous - height) < 0.5) return false;
    const total = this.#totals[this.#kinds.get(id) ?? kind];
    if (previous !== undefined) {
      total.sum -= previous;
      total.count -= 1;
    }
    this.#heights.set(id, height);
    this.#kinds.set(id, kind);
    this.#totals[kind].sum += height;
    this.#totals[kind].count += 1;
    return true;
  }

  /** Forgets every measurement: the rows' text changed as a whole (another locale, another density). */
  clear(): void {
    this.#heights.clear();
    this.#kinds.clear();
    for (const total of Object.values(this.#totals)) {
      total.sum = 0;
      total.count = 0;
    }
  }
}

/**
 * The top of each row and, last, the height of all rows: `offsets[i]` is where row `i` starts, `offsets[n]` the end.
 */
export function listRowOffsets(rows: readonly { id: string; kind: ListRowKind }[], heights: ListRowHeights): Float64Array {
  const offsets = new Float64Array(rows.length + 1);
  for (let index = 0; index < rows.length; index += 1) {
    offsets[index + 1] = offsets[index] + heights.height(rows[index].id, rows[index].kind);
  }
  return offsets;
}

/** The index of the row at `position` (a pixel offset from the first row's top), clamped to the rows. */
export function listRowAt(offsets: Float64Array, position: number): number {
  const count = offsets.length - 1;
  if (count <= 0) return 0;
  let low = 0;
  let high = count - 1;
  while (low < high) {
    const middle = (low + high + 1) >> 1;
    if (offsets[middle] <= position) low = middle;
    else high = middle - 1;
  }
  return low;
}

/** The rows `[start, end)` that cover the viewport `[top, top + height)` with `overscan` rows on each side. */
export function listWindow(offsets: Float64Array, top: number, height: number, overscan = listOverscanRows): { start: number; end: number } {
  const count = offsets.length - 1;
  if (count <= 0) return { start: 0, end: 0 };
  const first = listRowAt(offsets, Math.max(0, top));
  const last = listRowAt(offsets, Math.max(0, top + Math.max(height, 1) - 1));
  return { start: Math.max(0, first - overscan), end: Math.min(count, last + 1 + overscan) };
}

/** A piece of the rendered list: a run of rows, or the space of the rows left out between them. */
export type ListSlice = { kind: "rows"; start: number; end: number } | { kind: "gap"; id: string; height: number };

/**
 * What the list renders: the window's rows and the `pinned` rows outside it (the tab stop, the edited row, so the
 * focus stays in the DOM while it scrolls out of view), in order, with the space of the rows between them as gaps.
 */
export function listSlices(offsets: Float64Array, window: { start: number; end: number }, pinned: readonly number[]): ListSlice[] {
  const count = offsets.length - 1;
  const runs: { start: number; end: number }[] = [];
  if (window.end > window.start) runs.push({ ...window });
  for (const index of pinned) {
    if (index < 0 || index >= count || (index >= window.start && index < window.end)) continue;
    runs.push({ start: index, end: index + 1 });
  }
  runs.sort((left, right) => left.start - right.start);
  const merged: { start: number; end: number }[] = [];
  for (const run of runs) {
    const previous = merged.at(-1);
    if (previous !== undefined && run.start <= previous.end) previous.end = Math.max(previous.end, run.end);
    else merged.push(run);
  }
  const slices: ListSlice[] = [];
  let cursor = 0;
  for (const run of merged) {
    if (run.start > cursor) slices.push({ kind: "gap", id: `gap:${cursor}`, height: offsets[run.start] - offsets[cursor] });
    slices.push({ kind: "rows", start: run.start, end: run.end });
    cursor = run.end;
  }
  if (cursor < count) slices.push({ kind: "gap", id: `gap:${cursor}`, height: offsets[count] - offsets[cursor] });
  return slices;
}
