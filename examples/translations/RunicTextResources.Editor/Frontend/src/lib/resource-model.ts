import type { EditorDocument, WorkspaceSnapshot } from "./contracts";

export type ResourceValue = string | Record<string, unknown>;

export interface ResourceEntry {
  key: string;
  value: ResourceValue;
  description?: string;
  tags: string[];
  placeholders?: Record<string, unknown>;
  structured: boolean;
}

export interface TranslationCell {
  document?: EditorDocument;
  entry?: ResourceEntry;
  inheritedFrom?: string;
}

export interface TranslationRow {
  key: string;
  description?: string;
  tags: string[];
  cells: Record<string, TranslationCell>;
  structured: boolean;
}

type JsonObject = Record<string, unknown>;

export function buildRows(
  snapshot: WorkspaceSnapshot | undefined,
  drafts: Record<string, string>,
): TranslationRow[] {
  if (snapshot?.catalog === undefined) return [];
  const documents = snapshot.documents.filter((document) => !document.isManifest);
  const byLocale = new Map<string, EditorDocument[]>();
  for (const document of documents) {
    if (document.locale === undefined) continue;
    const group = byLocale.get(document.locale) ?? [];
    group.push(document);
    byLocale.set(document.locale, group);
  }
  for (const group of byLocale.values()) {
    group.sort((left, right) => layerPriority(snapshot, right.layer) - layerPriority(snapshot, left.layer));
  }

  const entriesByLocale = new Map<string, Map<string, ResourceEntry>>();
  const keys = new Set<string>();
  for (const locale of snapshot.catalog.locales) {
    const entries = new Map<string, ResourceEntry>();
    for (const document of [...(byLocale.get(locale.tag) ?? [])].reverse()) {
      const content = drafts[document.path] ?? document.content;
      for (const entry of flattenDocument(content)) entries.set(entry.key, entry);
    }
    entriesByLocale.set(locale.tag, entries);
    for (const key of entries.keys()) keys.add(key);
  }

  const sourceEntries = entriesByLocale.get(snapshot.catalog.defaultLocale) ?? new Map();
  return [...keys].sort().map((key) => {
    const source = sourceEntries.get(key);
    const cells: Record<string, TranslationCell> = {};
    for (const locale of snapshot.catalog!.locales) {
      const entry = entriesByLocale.get(locale.tag)?.get(key);
      cells[locale.tag] = {
        document: primaryDocument(byLocale.get(locale.tag) ?? []),
        entry,
        inheritedFrom: entry === undefined ? fallbackWithValue(snapshot, entriesByLocale, locale.tag, key) : undefined,
      };
    }
    return {
      key,
      description: source?.description,
      tags: source?.tags ?? [],
      cells,
      structured: [...snapshot.catalog!.locales].some((locale) => entriesByLocale.get(locale.tag)?.get(key)?.structured),
    };
  });
}

export function updateResourceValue(
  content: string,
  key: string,
  value: ResourceValue,
  sourceTemplate?: ResourceEntry,
): string {
  const document = JSON.parse(content) as JsonObject;
  const resources = object(document.resources, "resources");
  const segments = key.split(".");
  let group = resources;
  for (const segment of segments.slice(0, -1)) {
    const existing = group[segment];
    if (!isObject(existing) || "$value" in existing) group[segment] = {};
    group = object(group[segment], segment);
  }
  const leaf = segments.at(-1)!;
  const existing = group[leaf];
  if (isObject(existing) && "$value" in existing) {
    existing.$value = value;
  } else if (sourceTemplate?.placeholders !== undefined) {
    group[leaf] = {
      $value: value,
      $placeholders: structuredClone(sourceTemplate.placeholders),
    };
  } else if (typeof value === "string") {
    group[leaf] = value;
  } else {
    group[leaf] = { $value: value };
  }
  return `${JSON.stringify(document, null, 2)}\n`;
}

export function formatJson(content: string): string {
  return `${JSON.stringify(JSON.parse(content), null, 2)}\n`;
}

export function preview(entry: ResourceEntry | undefined): string {
  if (entry === undefined) return "Not translated";
  if (typeof entry.value === "string") return entry.value;
  const variants = Array.isArray(entry.value.variants) ? entry.value.variants.length : 0;
  return variants === 1 ? "Structured message · 1 variant" : `Structured message · ${variants} variants`;
}

export function coverage(rows: TranslationRow[], locale: string): { translated: number; total: number } {
  return {
    translated: rows.filter((row) => row.cells[locale]?.entry !== undefined).length,
    total: rows.length,
  };
}

function flattenDocument(content: string): ResourceEntry[] {
  try {
    const document = JSON.parse(content) as JsonObject;
    return flattenGroup(object(document.resources, "resources"), []);
  } catch {
    return [];
  }
}

function flattenGroup(group: JsonObject, path: string[]): ResourceEntry[] {
  const entries: ResourceEntry[] = [];
  for (const [name, candidate] of Object.entries(group)) {
    const next = [...path, name];
    if (typeof candidate === "string") {
      entries.push({ key: next.join("."), value: candidate, tags: [], structured: false });
    } else if (isObject(candidate) && "$value" in candidate) {
      const value = candidate.$value;
      if (typeof value === "string" || isObject(value)) {
        entries.push({
          key: next.join("."),
          value,
          description: typeof candidate.$description === "string" ? candidate.$description : undefined,
          tags: Array.isArray(candidate.$tags)
            ? candidate.$tags.filter((tag): tag is string => typeof tag === "string")
            : [],
          placeholders: isObject(candidate.$placeholders)
            ? candidate.$placeholders
            : undefined,
          structured: typeof value !== "string",
        });
      }
    } else if (isObject(candidate)) {
      entries.push(...flattenGroup(candidate, next));
    }
  }
  return entries;
}

function primaryDocument(documents: EditorDocument[]): EditorDocument | undefined {
  return documents[0];
}

function layerPriority(snapshot: WorkspaceSnapshot, layer: string | undefined): number {
  return snapshot.catalog?.layers.find((candidate) => candidate.name === layer)?.priority ?? 0;
}

function fallbackWithValue(
  snapshot: WorkspaceSnapshot,
  entries: Map<string, Map<string, ResourceEntry>>,
  locale: string,
  key: string,
): string | undefined {
  const seen = new Set<string>();
  let current = snapshot.catalog?.locales.find((candidate) => candidate.tag === locale)?.fallback;
  while (current !== undefined && !seen.has(current)) {
    if (entries.get(current)?.has(key)) return current;
    seen.add(current);
    current = snapshot.catalog?.locales.find((candidate) => candidate.tag === current)?.fallback;
  }
  return undefined;
}

function object(value: unknown, name: string): JsonObject {
  if (!isObject(value)) throw new TypeError(`Expected '${name}' to be a JSON object.`);
  return value;
}

function isObject(value: unknown): value is JsonObject {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
