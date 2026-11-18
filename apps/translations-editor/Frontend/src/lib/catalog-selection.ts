/**
 * The last selected locale and message of each catalog, restored on the next launch (#225 P24). One local-state entry
 * holds the most recent catalogs (the native store keeps at most 64 entries), as the message tree does for its
 * expansion state.
 */
export const catalogSelectionStateKey = "runic-translations:catalog-selection:1";
export const catalogSelectionLimits = { catalogs: 16, valueLength: 1_024 } as const;

export interface CatalogSelection {
  readonly locale: string;
  readonly key: string;
}

interface StoredSelection extends CatalogSelection {
  readonly catalog: string;
}

/** The remembered selection of `catalog` (its workspace root and manifest), or undefined. */
export function readCatalogSelection(raw: string | null, catalog: string): CatalogSelection | undefined {
  const entry = parseSelections(raw).find((candidate) => candidate.catalog === catalog);
  return entry === undefined ? undefined : { locale: entry.locale, key: entry.key };
}

/** The record with `catalog`'s selection first, keeping the most recent catalogs within the limits. */
export function writeCatalogSelection(raw: string | null, catalog: string, selection: CatalogSelection): string {
  const others = parseSelections(raw).filter((candidate) => candidate.catalog !== catalog);
  const own: StoredSelection = { catalog, locale: selection.locale, key: selection.key };
  const catalogs = (valid(own) ? [own, ...others] : others).slice(0, catalogSelectionLimits.catalogs);
  return JSON.stringify({ version: 1, catalogs });
}

function parseSelections(raw: string | null): StoredSelection[] {
  if (raw === null) return [];
  try {
    const value = JSON.parse(raw) as { version?: unknown; catalogs?: unknown };
    if (typeof value !== "object" || value === null || value.version !== 1 || !Array.isArray(value.catalogs)) return [];
    return value.catalogs.filter((candidate): candidate is StoredSelection => valid(candidate))
      .map(({ catalog, locale, key }) => ({ catalog, locale, key }))
      .slice(0, catalogSelectionLimits.catalogs);
  } catch {
    return [];
  }
}

function valid(candidate: unknown): candidate is StoredSelection {
  if (typeof candidate !== "object" || candidate === null) return false;
  const { catalog, locale, key } = candidate as Record<string, unknown>;
  return [catalog, locale, key].every((value) => typeof value === "string" && value.length <= catalogSelectionLimits.valueLength) &&
    catalog !== "";
}
