import type { UiText } from "./ui-text";
import type { EditorDocument, EditorMessageEntry, WorkspaceSnapshot } from "./contracts";

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

const newMf2DocumentRevision = "new-mf2-document";

export function buildRows(
  snapshot: WorkspaceSnapshot | undefined,
  drafts: Record<string, string>,
  parsedDrafts: Record<string, { content: string; entries: EditorMessageEntry[] }> = {},
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
  const documentsByLocale = new Map<string, Map<string, EditorDocument>>();
  const keys = new Set<string>();
  for (const locale of snapshot.catalog.locales) {
    const entries = new Map<string, ResourceEntry>();
    const entryDocuments = new Map<string, EditorDocument>();
    for (const document of [...(byLocale.get(locale.tag) ?? [])].reverse()) {
      const content = drafts[document.path] ?? document.content;
      const parsed = parsedDrafts[document.path];
      const messageEntries = parsed?.content === content ? parsed.entries : document.entries;
      for (const entry of flattenDocument(content, document.path, messageEntries)) {
        entries.set(entry.key, entry);
        entryDocuments.set(entry.key, document);
      }
    }
    entriesByLocale.set(locale.tag, entries);
    documentsByLocale.set(locale.tag, entryDocuments);
    for (const key of entries.keys()) keys.add(key);
  }

  const sourceEntries = entriesByLocale.get(snapshot.catalog.defaultLocale) ?? new Map();
  const mf2Manifest = snapshot.documents.find((document) => document.isManifest && document.path.endsWith("runic.json"));
  return [...keys].sort().map((key) => {
    const source = sourceEntries.get(key);
    const cells: Record<string, TranslationCell> = {};
    for (const locale of snapshot.catalog!.locales) {
      const entry = entriesByLocale.get(locale.tag)?.get(key);
      cells[locale.tag] = {
        document: documentsByLocale.get(locale.tag)?.get(key) ??
          (mf2Manifest !== undefined
            ? missingMessageDocument(mf2Manifest, locale.tag, key, primaryDocument(byLocale.get(locale.tag) ?? []))
            : primaryDocument(byLocale.get(locale.tag) ?? [])),
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

function missingMessageDocument(
  manifest: EditorDocument | undefined,
  locale: string,
  key: string,
  existing?: EditorDocument,
): EditorDocument | undefined {
  if (manifest === undefined) return undefined;
  const separator = manifest.path.lastIndexOf("/");
  const directory = separator < 0 ? "" : manifest.path.slice(0, separator + 1);
  let localeToml = false;
  try { localeToml = JSON.parse(manifest.content).sourceLayout === "locale-toml"; } catch { /* Invalid manifest is diagnosed by the compiler. */ }
  if (localeToml && existing !== undefined) return existing;
  return {
    path: localeToml ? `${directory}${locale}.toml` : `${directory}${locale}/${key}.mf2`,
    content: "",
    revision: newMf2DocumentRevision,
    isManifest: false,
    isMalformed: false,
    locale,
    layer: "base",
  };
}

export function updateResourceValue(
  content: string,
  _key: string,
  value: ResourceValue,
  _sourceTemplate?: ResourceEntry,
): string {
  if (typeof value !== "string") throw new TypeError("MF2 messages must be edited as MF2 source text.");
  return value.endsWith("\n") ? value : `${value}\n`;
}

export function formatJson(content: string): string {
  return `${content.replaceAll("\r\n", "\n").trimEnd()}\n`;
}

export function preview(entry: ResourceEntry | undefined, ui?: UiText): string {
  if (entry === undefined) return ui?.text("ui_resource_not_translated") ?? "";
  if (typeof entry.value === "string") return entry.value;
  const variants = Array.isArray(entry.value.variants) ? entry.value.variants.length : 0;
  return ui?.text("ui_count_resource_variants", { count: variants }) ?? String(variants);
}

export function coverage(rows: TranslationRow[], locale: string): { translated: number; total: number } {
  return {
    translated: rows.filter((row) => row.cells[locale]?.entry !== undefined).length,
    total: rows.length,
  };
}

function flattenDocument(content: string, path: string, entries?: EditorMessageEntry[]): ResourceEntry[] {
  if (path.toLowerCase().endsWith(".toml")) return (entries ?? []).map((entry) => ({
    key: entry.key, value: entry.content, tags: [],
    structured: /^\s*\.(?:input|local|match)\b/m.test(entry.content) || entry.content.includes("{#"),
  }));
  if (!path.toLowerCase().endsWith(".mf2")) return [];
  const key = path.slice(path.lastIndexOf("/") + 1, -".mf2".length);
  return [{ key, value: content, tags: [], structured: /^\s*\.(?:input|local|match)\b/m.test(content) || content.includes("{#") }];
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
