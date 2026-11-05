import type { UiText } from "./ui-text";
import type {
  EditorDiagnostic,
  EditorReviewEntry,
  EditorReviewState,
  EditorTerminologyEntry,
} from "./contracts";
import type { TranslationRow } from "./resource-model";
import {
  compatibleSemantics,
  containsSemanticTerm,
  identicalSemanticText,
  reviewSemantics,
  semanticTokens,
} from "./semantic-review";

// Editor-only checks (identical, whitespace, terminology, bidi) beside the SDK's diagnostics for the row
// ("diagnostic": the compiler's RTR0010 and RTR0094–RTR0098, and RTR0090 for a stale review state).
export interface QualityIssue {
  kind: "identical" | "whitespace" | "terminology" | "bidi" | "diagnostic";
  key: string;
  locale: string;
  message: string;
  code?: string;
  diagnostic?: EditorDiagnostic;
}

export interface TranslationSuggestion {
  key: string;
  source: string;
  translation: string;
  score: number;
}

export function reviewIdentity(key: string, locale: string): string {
  return key + "\0" + locale;
}

export function reviewMap(entries: EditorReviewEntry[]): Map<string, EditorReviewEntry> {
  return new Map(entries.map((entry) => [reviewIdentity(entry.key, entry.locale), entry]));
}

export function effectiveReviewState(
  entry: EditorReviewEntry | undefined,
  translated: boolean,
): EditorReviewState {
  return entry?.state ?? (translated ? "translated" : "draft");
}

// "Stale" is the SDK's definition, computed by the backend from the review file's per-unit fingerprint (RTR0090).
export function isStale(entry: EditorReviewEntry | undefined): boolean {
  return entry?.stale === true;
}

// RTR0090 follows the workflow being edited: once a stale state is set again (or the entry removed), its warning goes,
// before the review file is saved and the catalog reloaded.
export function withCurrentReview(diagnostics: readonly EditorDiagnostic[], reviews: Map<string, EditorReviewEntry>): EditorDiagnostic[] {
  return diagnostics.filter((diagnostic) => diagnostic.id !== "RTR0090" || diagnostic.key === undefined || diagnostic.locale === undefined ||
    isStale(reviews.get(reviewIdentity(diagnostic.key, diagnostic.locale))));
}

// The diagnostics attached to one row of the given locale, in the order the backend reports them.
export function rowDiagnostics(diagnostics: readonly EditorDiagnostic[], key: string, locale: string): EditorDiagnostic[] {
  return diagnostics.filter((diagnostic) => diagnostic.key === key && diagnostic.locale === locale);
}

export function qualityIssues(
  rows: TranslationRow[],
  sourceLocale: string,
  locale: string,
  diagnostics: readonly EditorDiagnostic[],
  terminology: EditorTerminologyEntry[],
  ui?: UiText,
): QualityIssue[] {
  const result: QualityIssue[] = [];
  const keys = new Set(rows.map((row) => row.key));
  for (const diagnostic of diagnostics) {
    if (diagnostic.locale !== locale || diagnostic.key === undefined || !keys.has(diagnostic.key)) continue;
    result.push({ kind: "diagnostic", key: diagnostic.key, locale, code: diagnostic.id, message: diagnostic.message, diagnostic });
  }
  for (const row of rows) {
    const sourceEntry = row.cells[sourceLocale]?.entry;
    const targetEntry = row.cells[locale]?.entry;
    const target = targetEntry?.value;
    if (target === undefined) continue;
    const sourceSemantic = reviewSemantics(sourceEntry);
    const targetSemantic = reviewSemantics(targetEntry);
    if (sourceSemantic !== undefined && targetSemantic !== undefined) {
      if (locale !== sourceLocale && sourceSemantic.text.some((text) => text.trim().length > 0) &&
          identicalSemanticText(sourceSemantic, targetSemantic)) {
        result.push({ kind: "identical", key: row.key, locale, message: (ui?.text("ui_quality_identical") ?? "Translation is identical to the source.") });
      }
      if (targetSemantic.hasBoundaryWhitespace) {
        result.push({ kind: "whitespace", key: row.key, locale, message: (ui?.text("ui_quality_whitespace") ?? "Translation has leading or trailing whitespace.") });
      }
      for (const term of terminology) {
        if (term.locale !== undefined && term.locale !== locale) continue;
        if (containsSemanticTerm(sourceSemantic, term.source) &&
            !containsSemanticTerm(targetSemantic, term.preferred)) {
          result.push({
            kind: "terminology", key: row.key, locale,
            message: ui?.text("ui_quality_terminology", { term: term.preferred }) ?? "Preferred term '" + term.preferred + "' is missing.",
          });
        }
      }
    }
  }
  return result.sort((left, right) =>
    left.key.localeCompare(right.key) || left.kind.localeCompare(right.kind) || (left.code ?? "").localeCompare(right.code ?? ""));
}

// Bidi/mixed-direction findings: invisible bidirectional control characters
// are flagged in both directions; mixed strong-direction runs are flagged when
// the RTL simulation is active because that is where they visually break.
const bidiControlPattern = /[\u{061C}\u{200E}\u{200F}\u{202A}-\u{202E}\u{2066}-\u{2069}]/u;
const strongLtrPattern = /[A-Za-z\u{00C0}-\u{024F}]/u;
const strongRtlPattern = /[\u{0590}-\u{08FF}\u{FB1D}-\u{FDFF}\u{FE70}-\u{FEFF}]/u;

export function bidiIssues(
  entries: ReadonlyArray<{ key: string; locale: string; text: string }>,
  context: "ltr" | "rtl",
  ui?: UiText,
): QualityIssue[] {
  const result: QualityIssue[] = [];
  for (const entry of entries) {
    const hasControls = bidiControlPattern.test(entry.text);
    const mixedRuns = strongLtrPattern.test(entry.text) && strongRtlPattern.test(entry.text);
    if (!hasControls && !(context === "rtl" && mixedRuns)) continue;
    result.push({
      kind: "bidi",
      key: entry.key,
      locale: entry.locale,
      message: hasControls
        ? (ui?.text("ui_quality_bidi_controls") ?? "Translation contains invisible bidirectional control characters.")
        : (ui?.text("ui_quality_bidi_mixed") ?? "Translation mixes left-to-right and right-to-left runs; verify it under right-to-left simulation."),
    });
  }
  return result.sort((left, right) =>
    left.key.localeCompare(right.key) || left.message.localeCompare(right.message));
}

export function translationSuggestions(
  rows: TranslationRow[],
  sourceLocale: string,
  targetLocale: string,
  key: string,
): TranslationSuggestion[] {
  const current = reviewSemantics(rows.find((row) => row.key === key)?.cells[sourceLocale]?.entry);
  if (current === undefined) return [];
  const currentTokens = semanticTokens(current);
  if (currentTokens.size === 0) return [];
  return rows.flatMap((row): TranslationSuggestion[] => {
    if (row.key === key) return [];
    const sourceEntry = row.cells[sourceLocale]?.entry;
    const targetEntry = row.cells[targetLocale]?.entry;
    const source = sourceEntry?.value;
    const translation = targetEntry?.value;
    if (typeof source !== "string" || typeof translation !== "string") return [];
    const sourceSemantic = reviewSemantics(sourceEntry);
    const targetSemantic = reviewSemantics(targetEntry);
    if (sourceSemantic === undefined || targetSemantic === undefined ||
        !compatibleSemantics(current, sourceSemantic) || !compatibleSemantics(current, targetSemantic)) return [];
    const score = similarity(currentTokens, semanticTokens(sourceSemantic));
    return score < .2 ? [] : [{ key: row.key, source, translation, score }];
  }).sort((left, right) => right.score - left.score || left.key.localeCompare(right.key)).slice(0, 5);
}

export function qualityReportCsv(issues: QualityIssue[]): string {
  const escape = (value: string) => '"' + value.replaceAll('"', '""') + '"';
  return [
    ["key", "locale", "kind", "message"].map(escape).join(","),
    ...issues.map((issue) => [issue.key, issue.locale, issue.code ?? issue.kind, issue.message].map(escape).join(",")),
  ].join("\n") + "\n";
}

function similarity(left: Set<string>, right: Set<string>): number {
  if (left.size === 0 || right.size === 0) return 0;
  let intersection = 0;
  for (const token of left) if (right.has(token)) intersection += 1;
  return intersection / (left.size + right.size - intersection);
}
