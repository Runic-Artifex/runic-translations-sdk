// The operation history shared by the server and the tray. Each line is a timestamp, a tab and a stored text reference (key and
// arguments with English fallback text); each process translates the reference with its own copy of the generated catalogue.
import { appendFile, readFile } from "node:fs/promises";
import { m } from "./generated/operations.esm-v5/messages.js";
import { resolveTextReference, type TextReferenceDiagnostic, type TextReferenceWire } from "./generated/operations.esm-v5/transport.js";

export interface Entry { readonly at: string; readonly text: string }
export interface Shown { readonly at: string; readonly text: string; readonly outcome: string; readonly diagnostics: readonly TextReferenceDiagnostic[] }

export async function readHistory(paths: readonly string[]): Promise<Entry[]> {
  const entries: Entry[] = [];
  for (const path of paths) {
    let content: string;
    try { content = await readFile(path, "utf8"); } catch { continue; }
    for (const line of content.split("\n")) {
      const tab = line.indexOf("\t");
      // The reference stays JSON text here: resolveTextReference checks its size before parsing it.
      if (tab > 0) entries.push({ at: line.slice(0, tab), text: line.slice(tab + 1) });
    }
  }
  return entries;
}

export async function appendEntry(path: string, text: TextReferenceWire): Promise<void> {
  await appendFile(path, `${new Date().toISOString()}\t${JSON.stringify(text)}\n`);
}

// Translated where the message still exists, the stored fallback where it does not; an entry never fails to display.
// The text is plain and may contain stored fallback or argument text: show it as text, never as HTML.
export function show(entry: Entry, locale: string): Shown {
  const result = resolveTextReference(entry.text, m, { locale });
  return { at: entry.at, text: result.text ?? "(unreadable entry)", outcome: result.outcome, diagnostics: result.diagnostics };
}
