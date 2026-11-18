// Helpers of the starter of a missing translation (#225 P2) and of the length limit at the field (P21). They are pure,
// so the stores and the composer share them and the checks test them directly.

/**
 * Where a message the saved document lacks was placed in the document's draft: the text without the message and the
 * text with it. Taking the message out again gives `without` back, which is the saved text when nothing else changed.
 */
export interface Placement { without: string; with: string }

/** The text inserted between `without` and `with`, if `with` is `without` plus one inserted run. */
function insertion(placement: Placement): string | undefined {
  const { without, with: placed } = placement;
  const shorter = Math.min(without.length, placed.length);
  let prefix = 0;
  while (prefix < shorter && without[prefix] === placed[prefix]) prefix += 1;
  let suffix = 0;
  while (suffix < shorter - prefix && without[without.length - 1 - suffix] === placed[placed.length - 1 - suffix]) suffix += 1;
  if (prefix + suffix !== without.length || placed.length <= without.length) return undefined;
  return placed.slice(prefix, placed.length - suffix);
}

/**
 * `content` without the placed message: `without` while the draft is still the placed text, else the draft with the
 * inserted run removed, when it occurs exactly once (another message of the document was edited meanwhile).
 * `undefined` when the message cannot be found again.
 */
export function removePlacement(content: string, placement: Placement): string | undefined {
  if (content === placement.with) return placement.without;
  const inserted = insertion(placement);
  if (inserted === undefined) return undefined;
  const first = content.indexOf(inserted);
  if (first < 0 || content.indexOf(inserted, first + 1) >= 0) return undefined;
  return content.slice(0, first) + content.slice(first + inserted.length);
}

/**
 * The placement after the draft went from `source` to `result` by inserting or updating the message: the first
 * insertion starts it; an update of the placed text keeps its `without`; an update after other edits of the document
 * finds `without` again in `source`. `undefined` when it cannot.
 */
export function nextPlacement(previous: Placement | undefined, source: string, result: string): Placement | undefined {
  if (previous === undefined) return { without: source, with: result };
  if (source === previous.with) return { without: previous.without, with: result };
  const without = removePlacement(source, previous);
  return without === undefined ? undefined : { without, with: result };
}

export interface PatternVariant { keys: readonly string[]; pattern: string }

const sameKeys = (left: readonly string[], right: readonly string[]) =>
  left.length === right.length && left.every((key, index) => key === right[index]);

/**
 * The source pattern "Copy source" gives a starter variant: the base variant with the same keys, else the base's
 * catch-all (`*` for every selector). Placeholders are MF2 expressions in the pattern, so they stay placeholders.
 */
export function sourcePattern(keys: readonly string[], base: readonly PatternVariant[]): string | undefined {
  return base.find((variant) => sameKeys(variant.keys, keys))?.pattern ??
    base.find((variant) => variant.keys.length > 0 && variant.keys.every((key) => key === "*"))?.pattern ??
    (keys.length === 0 && base.length === 1 ? base[0].pattern : undefined);
}

/** The starter variants "Copy source" changes, in order, with the pattern each gets. */
export function copySourcePlan(starter: readonly (PatternVariant & { id: string })[], base: readonly PatternVariant[]):
  { id: string; keys: readonly string[]; pattern: string }[] {
  return starter.flatMap((variant) => {
    const pattern = sourcePattern(variant.keys, base);
    return pattern === undefined || pattern === variant.pattern ? [] : [{ id: variant.id, keys: variant.keys, pattern }];
  });
}

/**
 * The length of a pattern as RTR0098 counts it against `@maxLength`: its literal text only (escapes resolved,
 * placeholders, formatted values and markup not counted), normalized to NFC, in code points.
 */
export function patternLength(pattern: string): number {
  let text = "";
  for (let index = 0; index < pattern.length;) {
    if (pattern[index] === "\\" && index + 1 < pattern.length) { text += pattern[index + 1]; index += 2; continue; }
    if (pattern[index] !== "{") { text += pattern[index++]; continue; }
    let end = index + 1;
    let quoted = false;
    for (; end < pattern.length; end += 1) {
      if (pattern[end] === "\\") { end += 1; continue; }
      if (pattern[end] === "|") quoted = !quoted;
      if (!quoted && pattern[end] === "}") break;
    }
    if (end >= pattern.length) { text += pattern.slice(index); break; }
    index = end + 1;
  }
  return [...text.normalize("NFC")].length;
}

interface LengthSource { supported: boolean; variants: { pattern: string }[] }

/**
 * The length a message's `@maxLength` limits: that of its longest variant, as RTR0098 measures it. A message without
 * a visual projection is counted from its text (a quoted pattern without its braces).
 */
export function messageLength(authoring: LengthSource | undefined, value: string): number {
  if (authoring?.supported === true && authoring.variants.length > 0) {
    return Math.max(...authoring.variants.map((variant) => patternLength(variant.pattern)));
  }
  const quoted = /^\s*\{\{([\s\S]*)\}\}\s*$/.exec(value);
  return patternLength(quoted?.[1] ?? value);
}

/** The counter of a field with a length limit: `16 / 24`, over the limit when the text is longer. */
export interface LengthLimit { length: number; limit: number; over: boolean }

export function lengthLimit(length: number, limit: number | undefined): LengthLimit | undefined {
  return limit === undefined ? undefined : { length, limit, over: length > limit };
}
