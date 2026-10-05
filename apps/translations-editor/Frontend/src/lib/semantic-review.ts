import type { ResourceValue } from "./resource-model";
import type { EditorMessageSemantic } from "./contracts";

// These fields come from the compiler projection. Text contains literal content
// per pattern, without declarations or expression syntax. Boundary whitespace
// is computed before expressions are removed, so `Hello {$name}` stays clean.
export type ReviewSemantics = EditorMessageSemantic;

type ReviewEntry = { value: ResourceValue; semantic?: ReviewSemantics };

export type SemanticReviewState =
  | { supported: true; origin: "compiler" | "plain-text"; semantic: ReviewSemantics }
  | { supported: false; reason: "compiler-required" | "compiler-unavailable" };

export function semanticReviewState(entry: ReviewEntry | undefined): SemanticReviewState {
  if (entry?.semantic !== undefined) {
    return entry.semantic.supported
      ? { supported: true, origin: "compiler", semantic: entry.semantic }
      : { supported: false, reason: "compiler-unavailable" };
  }
  // This compatibility path is deliberately limited to unescaped plain text.
  // Never infer declarations, expressions, selectors or markup with another
  // MF2 parser. Unsupported source needs a current compiler projection.
  if (typeof entry?.value !== "string" || /[{}\\]|^\s*[.@#]/m.test(entry.value)) {
    return { supported: false, reason: "compiler-required" };
  }
  const text = entry.value.replace(/\r?\n$/, "");
  return {
    supported: true,
    origin: "plain-text",
    semantic: {
      text: [text], placeholders: [], slots: [], supported: true,
      hasBoundaryWhitespace: text !== text.trim(),
    },
  };
}

export function reviewSemantics(entry: ReviewEntry | undefined): ReviewSemantics | undefined {
  const state = semanticReviewState(entry);
  return state.supported ? state.semantic : undefined;
}

export function compatibleSemantics(left: ReviewSemantics, right: ReviewSemantics): boolean {
  return sameSet(left.placeholders, right.placeholders) && sameSet(left.slots, right.slots);
}

export function identicalSemanticText(left: ReviewSemantics, right: ReviewSemantics): boolean {
  return compatibleSemantics(left, right) &&
    left.text.length === right.text.length &&
    left.text.every((text, index) => text === right.text[index]);
}

export function containsSemanticTerm(semantic: ReviewSemantics, term: string): boolean {
  if (term.trim() === "") return false;
  const foldedTerm = term.toLowerCase();
  return semantic.text.some((text) => text.toLowerCase().includes(foldedTerm));
}

export function semanticTokens(semantic: ReviewSemantics): Set<string> {
  return new Set(semantic.text.flatMap((text) =>
    text.toLowerCase().split(/[^\p{L}\p{N}]+/u).filter((item) => item.length > 1)));
}

function sameSet(left: readonly string[], right: readonly string[]): boolean {
  const leftSet = new Set(left);
  const rightSet = new Set(right);
  return leftSet.size === rightSet.size && [...leftSet].every((item) => rightSet.has(item));
}
