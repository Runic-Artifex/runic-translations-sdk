import type {
  EditorReviewEntry,
  EditorReviewState,
  EditorTerminologyEntry,
} from "./contracts";
import type { ResourceValue, TranslationRow } from "./resource-model";

export interface QualityIssue {
  kind: "missing" | "identical" | "whitespace" | "terminology" | "stale";
  key: string;
  locale: string;
  message: string;
}

export interface TranslationSuggestion {
  key: string;
  source: string;
  translation: string;
  score: number;
}

export function sourceFingerprint(value: ResourceValue | undefined): string | undefined {
  if (value === undefined) return undefined;
  const text = stableJson(value);
  let hash = 0xcbf29ce484222325n;
  for (let index = 0; index < text.length; index += 1) {
    hash ^= BigInt(text.charCodeAt(index));
    hash = BigInt.asUintN(64, hash * 0x100000001b3n);
  }
  return "fnv1a64:" + hash.toString(16).padStart(16, "0");
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

export function isStale(entry: EditorReviewEntry | undefined, currentSource: ResourceValue | undefined): boolean {
  const fingerprint = sourceFingerprint(currentSource);
  return entry?.sourceFingerprint !== undefined &&
    fingerprint !== undefined &&
    entry.sourceFingerprint !== fingerprint;
}

export function qualityIssues(
  rows: TranslationRow[],
  sourceLocale: string,
  locale: string,
  reviewEntries: EditorReviewEntry[],
  terminology: EditorTerminologyEntry[],
): QualityIssue[] {
  const reviews = reviewMap(reviewEntries);
  const result: QualityIssue[] = [];
  for (const row of rows) {
    const source = row.cells[sourceLocale]?.entry?.value;
    const target = row.cells[locale]?.entry?.value;
    if (target === undefined) {
      result.push({ kind: "missing", key: row.key, locale, message: "Translation is missing." });
      continue;
    }
    if (typeof source === "string" && typeof target === "string") {
      if (locale !== sourceLocale && source.trim().length > 0 && target === source) {
        result.push({ kind: "identical", key: row.key, locale, message: "Translation is identical to the source." });
      }
      if (target !== target.trim()) {
        result.push({ kind: "whitespace", key: row.key, locale, message: "Translation has leading or trailing whitespace." });
      }
      for (const term of terminology) {
        if (term.locale !== undefined && term.locale !== locale) continue;
        if (source.toLocaleLowerCase().includes(term.source.toLocaleLowerCase()) &&
            !target.toLocaleLowerCase().includes(term.preferred.toLocaleLowerCase())) {
          result.push({
            kind: "terminology", key: row.key, locale,
            message: "Preferred term '" + term.preferred + "' is missing.",
          });
        }
      }
    }
    const review = reviews.get(reviewIdentity(row.key, locale));
    if (isStale(review, source)) {
      result.push({ kind: "stale", key: row.key, locale, message: "Source changed after this review state was recorded." });
    }
  }
  return result.sort((left, right) =>
    left.key.localeCompare(right.key) || left.kind.localeCompare(right.kind));
}

export function translationSuggestions(
  rows: TranslationRow[],
  sourceLocale: string,
  targetLocale: string,
  key: string,
): TranslationSuggestion[] {
  const current = rows.find((row) => row.key === key)?.cells[sourceLocale]?.entry?.value;
  if (typeof current !== "string" || current.trim() === "") return [];
  const currentTokens = tokens(current);
  return rows.flatMap((row): TranslationSuggestion[] => {
    if (row.key === key) return [];
    const source = row.cells[sourceLocale]?.entry?.value;
    const translation = row.cells[targetLocale]?.entry?.value;
    if (typeof source !== "string" || typeof translation !== "string") return [];
    const score = similarity(currentTokens, tokens(source));
    return score < .2 ? [] : [{ key: row.key, source, translation, score }];
  }).sort((left, right) => right.score - left.score || left.key.localeCompare(right.key)).slice(0, 5);
}

export function qualityReportCsv(issues: QualityIssue[]): string {
  const escape = (value: string) => '"' + value.replaceAll('"', '""') + '"';
  return [
    ["key", "locale", "kind", "message"].map(escape).join(","),
    ...issues.map((issue) => [issue.key, issue.locale, issue.kind, issue.message].map(escape).join(",")),
  ].join("\n") + "\n";
}

function stableJson(value: unknown): string {
  if (Array.isArray(value)) return "[" + value.map(stableJson).join(",") + "]";
  if (typeof value === "object" && value !== null) {
    return "{" + Object.entries(value).sort(([left], [right]) => left.localeCompare(right))
      .map(([name, child]) => JSON.stringify(name) + ":" + stableJson(child)).join(",") + "}";
  }
  return JSON.stringify(value);
}

function tokens(value: string): Set<string> {
  return new Set(value.toLocaleLowerCase().split(/[^\p{L}\p{N}]+/u).filter((item) => item.length > 1));
}

function similarity(left: Set<string>, right: Set<string>): number {
  if (left.size === 0 || right.size === 0) return 0;
  let intersection = 0;
  for (const token of left) if (right.has(token)) intersection += 1;
  return intersection / (left.size + right.size - intersection);
}
