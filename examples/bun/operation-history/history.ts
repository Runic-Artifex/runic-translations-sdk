// The operation history shared by the server and the tray. Entries store references (key and arguments) with English
// fallback text; each process translates them with its own copy of the generated catalogue when it shows them.
import { appendFile, readFile } from "node:fs/promises";
import { m } from "./generated/operations.esm-v5/messages.js";
import { resolveTextReference, type TextReferenceDiagnostic, type TextReferenceWire } from "./generated/operations.esm-v5/transport.js";

export interface Entry { readonly at: string; readonly text: unknown }
export interface Shown { readonly at: string; readonly text: string; readonly outcome: string; readonly diagnostics: readonly TextReferenceDiagnostic[] }

export async function readHistory(paths: readonly string[]): Promise<Entry[]> {
  const entries: Entry[] = [];
  for (const path of paths) {
    let content: string;
    try { content = await readFile(path, "utf8"); } catch { continue; }
    for (const line of content.split("\n")) if (line.trim()) entries.push(JSON.parse(line) as Entry);
  }
  return entries;
}

export async function appendEntry(path: string, text: TextReferenceWire): Promise<void> {
  await appendFile(path, JSON.stringify({ at: new Date().toISOString(), text }) + "\n");
}

// Translated where the message still exists, the stored fallback where it does not; an entry never fails to display.
export function show(entry: Entry, locale: string): Shown {
  const result = resolveTextReference(entry.text, m, { locale });
  return { at: entry.at, text: result.text ?? "(unreadable entry)", outcome: result.outcome, diagnostics: result.diagnostics };
}
