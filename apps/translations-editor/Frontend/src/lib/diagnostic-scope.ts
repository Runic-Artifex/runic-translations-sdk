import type { EditorDiagnostic, EditorDocument, EditorMessageEntry } from "./contracts";

/**
 * Scoping and ordering of the validation panel (#225 P6, P12): which message a diagnostic is about, where in that
 * message it points, and which of them belong to the message being translated.
 */

/** The documents a diagnostic can point into, with the unsaved drafts and their parsed entries (as `buildRows` reads them). */
export interface DiagnosticSources {
  documents: readonly EditorDocument[];
  drafts?: Readonly<Record<string, string>>;
  parsed?: Readonly<Record<string, { content: string; entries: EditorMessageEntry[] }>>;
}

/** A diagnostic with the message it is about, if any. */
export interface PlacedDiagnostic {
  diagnostic: EditorDiagnostic;
  /** The message row (canonical key) and locale: the backend's, or the entry the diagnostic's span falls into. */
  key?: string;
  locale?: string;
  /** The message's display name: its dotted path (`common.cancel`), else its key. */
  message?: string;
  /** The 1-based line within the message's text, when the diagnostic's span starts inside it. */
  messageLine?: number;
}

export interface DiagnosticSelection {
  key: string;
  locale: string;
}

export interface DiagnosticGroups {
  /** The selected message's diagnostics in the selected locale. */
  message: PlacedDiagnostic[];
  /** Errors about no message (runic.json, a file's structure): they block saving, so they are never collapsed. */
  blocking: PlacedDiagnostic[];
  /** Everything else: other messages, other locales, and warnings about no message. */
  other: PlacedDiagnostic[];
}

const severityRank = (diagnostic: EditorDiagnostic): number => diagnostic.severity === "error" ? 0 : diagnostic.severity === "warning" ? 1 : 2;

/** Errors before warnings; otherwise the reported order. */
export function errorsFirst<T>(items: readonly T[], diagnostic: (item: T) => EditorDiagnostic): T[] {
  return items
    .map((item, index) => ({ item, index }))
    .sort((left, right) => severityRank(diagnostic(left.item)) - severityRank(diagnostic(right.item)) || left.index - right.index)
    .map(({ item }) => item);
}

interface SourceFile {
  locale?: string;
  content: string;
  /** The compiler's entries, only while they were read from `content` (their byte offsets address it). */
  entries?: readonly EditorMessageEntry[];
  bytes?: Uint8Array;
}

function sourceFile(sources: DiagnosticSources, path: string): SourceFile | undefined {
  const document = sources.documents.find((candidate) => candidate.path === path);
  if (document === undefined) return undefined;
  const content = sources.drafts?.[path] ?? document.content;
  const parsed = sources.parsed?.[path];
  const entries = parsed?.content === content ? parsed.entries : content === document.content ? document.entries : undefined;
  return { locale: document.locale, content, entries };
}

const displayName = (path: readonly string[] | undefined): string | undefined => path !== undefined && path.length > 0 ? path.join(".") : undefined;

/** Locates each diagnostic in its message: the backend's row, else the message entry its span starts in. */
export function placeDiagnostics(diagnostics: readonly EditorDiagnostic[], sources: DiagnosticSources | undefined): PlacedDiagnostic[] {
  const files = new Map<string, SourceFile | undefined>();
  const file = (path: string): SourceFile | undefined => {
    if (!files.has(path)) files.set(path, sources === undefined ? undefined : sourceFile(sources, path));
    return files.get(path);
  };
  return diagnostics.map((diagnostic) => {
    const source = file(diagnostic.path);
    const start = diagnostic.span?.startByte;
    let key = diagnostic.key;
    let locale = diagnostic.locale;
    let entry = key === undefined ? undefined : source?.entries?.find((candidate) => candidate.key === key);
    if (key === undefined && start !== undefined && source?.locale !== undefined) {
      // A syntax error inside a message's text names no row; its span says which message it is in.
      entry = source.entries?.find((candidate) => start >= candidate.valueStartByte && start < candidate.valueStartByte + candidate.valueLengthBytes);
      if (entry !== undefined) {
        key = entry.key;
        locale = source.locale;
      }
    }
    if (key === undefined || locale === undefined) return { diagnostic };
    const message = displayName(diagnostic.messagePath) ?? displayName(entry?.path) ?? key;
    let messageLine: number | undefined;
    if (entry !== undefined && source !== undefined && start !== undefined &&
      start >= entry.valueStartByte && start <= entry.valueStartByte + entry.valueLengthBytes) {
      source.bytes ??= new TextEncoder().encode(source.content);
      if (start <= source.bytes.length) {
        messageLine = 1;
        for (let offset = entry.valueStartByte; offset < start; offset += 1) if (source.bytes[offset] === 0x0a) messageLine += 1;
      }
    }
    return { diagnostic, key, locale, message, messageLine };
  });
}

/** Splits placed diagnostics into the selected message's, the never-collapsed blocking ones, and the rest; each errors first. */
export function groupDiagnostics(placed: readonly PlacedDiagnostic[], selection: DiagnosticSelection | undefined): DiagnosticGroups {
  const groups: DiagnosticGroups = { message: [], blocking: [], other: [] };
  for (const item of errorsFirst(placed, (candidate) => candidate.diagnostic)) {
    if (selection !== undefined && item.key === selection.key && item.locale === selection.locale) groups.message.push(item);
    else if (item.key === undefined && item.diagnostic.severity === "error") groups.blocking.push(item);
    else groups.other.push(item);
  }
  return groups;
}

/** The display name of a message: its dotted path from any document's entries, else the key. */
export function messageDisplayName(key: string, sources: DiagnosticSources | undefined): string {
  for (const parsed of Object.values(sources?.parsed ?? {})) {
    const name = displayName(parsed.entries.find((entry) => entry.key === key)?.path);
    if (name !== undefined) return name;
  }
  for (const document of sources?.documents ?? []) {
    const name = displayName(document.entries?.find((entry) => entry.key === key)?.path);
    if (name !== undefined) return name;
  }
  return key;
}
