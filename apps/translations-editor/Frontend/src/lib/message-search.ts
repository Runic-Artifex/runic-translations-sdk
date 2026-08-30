import { preview, type TranslationRow } from "./resource-model";

/** Recorded budgets cover query work after a catalog has been indexed. */
export const indexedSearchBudgets = {
  tenThousand: { buildMilliseconds: 1_500, queryMilliseconds: 40 },
  fiftyThousand: { buildMilliseconds: 5_000, queryMilliseconds: 80 },
} as const;

export interface MessageSearchIndex {
  matchingRows(query: string): ReadonlySet<TranslationRow>;
}

/**
 * Keeps a compact, pre-normalized corpus and a trigram-to-row index. This
 * makes normal multi-character searches avoid repeatedly formatting every
 * locale cell while preserving substring search semantics.
 */
export function createMessageSearchIndex(rows: readonly TranslationRow[]): MessageSearchIndex {
  const corpus = rows.map(searchableText);
  const trigrams = new Map<string, Set<number>>();
  for (let position = 0; position < corpus.length; position += 1) {
    const text = corpus[position];
    const unique = new Set<string>();
    for (let offset = 0; offset <= text.length - 3; offset += 1) unique.add(text.slice(offset, offset + 3));
    for (const token of unique) (trigrams.get(token) ?? addToken(trigrams, token)).add(position);
  }

  const cache = new Map<string, ReadonlySet<TranslationRow>>();
  return {
    matchingRows(query) {
      const normalized = normalize(query);
      const cached = cache.get(normalized);
      if (cached !== undefined) return cached;
      if (normalized.length === 0) return cacheResult(cache, normalized, new Set(rows));
      if (normalized.length < 3) return cacheResult(cache, normalized,
        new Set(rows.filter((_, position) => corpus[position]?.includes(normalized) ?? false)));
      let candidates: Set<number> | undefined;
      for (let offset = 0; offset <= normalized.length - 3; offset += 1) {
        const matches = trigrams.get(normalized.slice(offset, offset + 3));
        if (matches === undefined) return cacheResult(cache, normalized, new Set());
        candidates = candidates === undefined
          ? new Set(matches)
          : new Set([...candidates].filter((candidate) => matches.has(candidate)));
        if (candidates.size === 0) return cacheResult(cache, normalized, new Set());
      }
      return cacheResult(cache, normalized, new Set(
        [...(candidates ?? [])]
          .filter((position) => corpus[position]?.includes(normalized) ?? false)
          .map((position) => rows[position]),
      ));
    },
  };
}

function searchableText(row: TranslationRow): string {
  return normalize([
    row.key,
    row.description ?? "",
    ...row.tags,
    ...Object.values(row.cells).map((candidate) => preview(candidate.entry)),
  ].join("\n"));
}

function normalize(value: string): string {
  return value.trim().toLocaleLowerCase();
}

function cacheResult(
  cache: Map<string, ReadonlySet<TranslationRow>>,
  query: string,
  rows: ReadonlySet<TranslationRow>,
): ReadonlySet<TranslationRow> {
  cache.set(query, rows);
  return rows;
}

function addToken(index: Map<string, Set<number>>, token: string): Set<number> {
  const positions = new Set<number>();
  index.set(token, positions);
  return positions;
}
