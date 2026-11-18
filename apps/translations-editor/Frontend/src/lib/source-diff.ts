import { sourceTextView, type SourceSegment } from "./message-composer";

/** Whether a part is in both texts, only in the recorded one or only in the current one. */
export type SourceDiffChange = "same" | "removed" | "added";

/** A run of text, or one placeholder (shown as a chip), of a compared source text. */
export type SourceDiffPart =
  | { kind: "text"; text: string; change: SourceDiffChange }
  | { kind: "variable" | "placeholder"; syntax: string; change: SourceDiffChange };

/**
 * The base text a stale review state was recorded for next to the current one (#225 P20): `before` marks what was
 * removed, `after` what was added. A message with declarations or selectors is compared as its MF2 source (`syntax`),
 * any other one as text with placeholders as single, unsplittable parts.
 */
export interface SourceDiff {
  before: SourceDiffPart[];
  after: SourceDiffPart[];
  /** False when the texts read the same: the fingerprint changed through the message's inputs or their types. */
  changed: boolean;
  syntax: boolean;
}

type Token = { kind: "text" | "variable" | "placeholder"; value: string };

// Words (letters, marks, digits), white space and single other characters; scripts written without spaces between
// words (Chinese, Japanese, Thai) compare per character.
const tokenPattern = /[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Thai}]|[\p{L}\p{M}\p{N}_]+|\s+|[^\s]/gu;
// The largest table the word diff fills after the common start and end are taken off; beyond it the middle is shown
// as removed and added as a whole.
const maximumCells = 250_000;

function flatten(source: string, syntax: boolean): SourceSegment[] {
  if (syntax) return [{ kind: "text", text: source }];
  const view = sourceTextView({ value: source });
  return view?.kind === "blocks" ? view.blocks.flatMap(block => block.segments) : [{ kind: "text", text: source }];
}

function tokens(segments: readonly SourceSegment[]): Token[] {
  return segments.flatMap((segment): Token[] => segment.kind === "text"
    ? (segment.text.match(tokenPattern) ?? []).map(value => ({ kind: "text", value }))
    : [{ kind: segment.kind, value: segment.syntax }]);
}

const same = (left: Token, right: Token): boolean => left.kind === right.kind && left.value === right.value;

// The longest common subsequence of the two token lists, as pairs of matching indexes.
function matches(left: readonly Token[], right: readonly Token[]): [number, number][] {
  let start = 0;
  while (start < left.length && start < right.length && same(left[start], right[start])) start += 1;
  let end = 0;
  while (end < left.length - start && end < right.length - start && same(left[left.length - 1 - end], right[right.length - 1 - end])) end += 1;
  const pairs: [number, number][] = [];
  for (let index = 0; index < start; index += 1) pairs.push([index, index]);
  const rows = left.length - start - end, columns = right.length - start - end;
  if (rows > 0 && columns > 0 && rows * columns <= maximumCells) {
    // lengths[i * (columns + 1) + j]: the common subsequence of left[start + i..] and right[start + j..].
    const width = columns + 1;
    const lengths = new Uint32Array((rows + 1) * width);
    for (let i = rows - 1; i >= 0; i -= 1) {
      for (let j = columns - 1; j >= 0; j -= 1) {
        lengths[i * width + j] = same(left[start + i], right[start + j])
          ? lengths[(i + 1) * width + j + 1] + 1
          : Math.max(lengths[(i + 1) * width + j], lengths[i * width + j + 1]);
      }
    }
    for (let i = 0, j = 0; i < rows && j < columns;) {
      if (same(left[start + i], right[start + j])) { pairs.push([start + i, start + j]); i += 1; j += 1; }
      else if (lengths[(i + 1) * width + j] >= lengths[i * width + j + 1]) i += 1;
      else j += 1;
    }
  }
  for (let index = end; index > 0; index -= 1) pairs.push([left.length - index, right.length - index]);
  return pairs;
}

function parts(items: readonly Token[], kept: ReadonlySet<number>, change: "removed" | "added"): SourceDiffPart[] {
  const marks = items.map((_, index): SourceDiffChange => kept.has(index) ? "same" : change);
  // White space alone between two changed words belongs to the change, so "a b" → "c d" reads as one replacement.
  for (let index = 1; index < items.length - 1; index += 1) {
    if (marks[index] === "same" && items[index].kind === "text" && /^\s+$/u.test(items[index].value) && marks[index - 1] === change && marks[index + 1] === change)
      marks[index] = change;
  }
  const result: SourceDiffPart[] = [];
  items.forEach((item, index) => {
    const last = result.at(-1);
    if (item.kind === "text" && last?.kind === "text" && last.change === marks[index]) last.text += item.value;
    else if (item.kind === "text") result.push({ kind: "text", text: item.value, change: marks[index] });
    else result.push({ kind: item.kind, syntax: item.value, change: marks[index] });
  });
  return result;
}

/** A word-level comparison of the recorded base text `before` with the current base text `after`. */
export function sourceDiff(before: string, after: string): SourceDiff {
  const isSyntax = (value: string) => sourceTextView({ value })?.kind === "source";
  const syntax = isSyntax(before) || isSyntax(after);
  const left = tokens(flatten(before, syntax));
  const right = tokens(flatten(after, syntax));
  const pairs = matches(left, right);
  return {
    before: parts(left, new Set(pairs.map(([index]) => index)), "removed"),
    after: parts(right, new Set(pairs.map(([, index]) => index)), "added"),
    changed: pairs.length !== left.length || pairs.length !== right.length,
    syntax,
  };
}
