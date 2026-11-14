// The declaration map of the generated type declarations (virtual.d.ts, dependencies/<catalog>.d.ts): the compiler's
// messages.d.ts.map moved to the lines where the plugin copies messages.d.ts, so Go to Definition on m.key() in an editor
// opens the message's .rmf2/.mf2 source line. The maps are an editor aid: a missing or invalid compiler map only leaves the
// declarations without one.
import { lstatSync } from "node:fs";
import { readFile, rename, rm, writeFile } from "node:fs/promises";
import { randomUUID } from "node:crypto";
import { dirname, isAbsolute, join, relative, resolve, sep } from "node:path";

/** A decoded mapping segment: generated column, and source index, line and column for a mapped one (all 0-based). */
type Segment = readonly [number] | readonly [number, number, number, number];

/** The compiler's map of one messages.d.ts, with its sources resolved to absolute paths. */
export interface MessagesDeclarationMap {
  readonly declarations: string;
  readonly sources: readonly string[];
  readonly lines: readonly (readonly Segment[])[];
}

/** One copy of messages.d.ts in the written declarations: the line its body starts at, its lines and the lines it dropped first. */
export interface DeclarationCopy {
  readonly startLine: number;
  readonly lines: readonly string[];
  readonly skippedLines: number;
  readonly indent: string;
}

const comment = /^\/\/# sourceMappingURL=(.+)$/mu;

/** Reads the map that messages.d.ts names (only a sibling file); undefined when it has none or it is unusable. */
export async function readMessagesDeclarationMap(declarationsPath: string, declarations: string): Promise<MessagesDeclarationMap | undefined> {
  const name = comment.exec(declarations)?.[1]?.trim();
  if (!name || name.includes("/") || name.includes("\\") || !name.endsWith(".map")) return undefined;
  const path = join(dirname(declarationsPath), name);
  try {
    if (lstatSync(path).isSymbolicLink()) return undefined;
    const map = JSON.parse(await readFile(path, "utf8")) as { version?: unknown; sources?: unknown; mappings?: unknown; sourceRoot?: unknown };
    if (map.version !== 3 || !Array.isArray(map.sources) || !map.sources.every(item => typeof item === "string") ||
        typeof map.mappings !== "string" || (map.sourceRoot !== undefined && map.sourceRoot !== ""))
      return undefined;
    const lines = decode(map.mappings, map.sources.length);
    if (!lines) return undefined;
    return { declarations, sources: (map.sources as string[]).map(source => resolve(dirname(path), source)), lines };
  } catch {
    return undefined;
  }
}

/**
 * The map of declarations written to <paramref name="path"/> with the given copies of messages.d.ts. Every line starts with
 * a generated-only segment: tsserver takes the next mapping for a position without one, which must not be a message.
 */
export function composeDeclarationMap(path: string, lineCount: number, map: MessagesDeclarationMap, copies: readonly DeclarationCopy[]): string {
  const original = map.declarations.split("\n");
  const lines: Segment[][] = Array.from({ length: lineCount }, () => [[0]]);
  for (const copy of copies) {
    copy.lines.forEach((line, index) => {
      const source = copy.skippedLines + index, target = copy.startLine + index;
      // Only a line copied unchanged but for its indentation keeps its mappings.
      if (target >= lineCount || original[source] === undefined || line !== copy.indent + original[source]) return;
      for (const segment of map.lines[source] ?? [])
        if (segment.length === 4) lines[target].push([segment[0] + copy.indent.length, segment[1], segment[2], segment[3]]);
        else if (segment[0] !== 0) lines[target].push([segment[0] + copy.indent.length]);
    });
  }
  const directory = dirname(path);
  const sources = map.sources.map(source => {
    const relativePath = relative(directory, source);
    return isAbsolute(relativePath) ? source : relativePath.split(sep).join("/");
  });
  return JSON.stringify({ version: 3, file: path.slice(directory.length + 1), sourceRoot: "", sources, names: [], mappings: encode(lines) }) + "\n";
}

/** Writes or, without a map, removes <declarations>.map; tsserver reads that path even without a sourceMappingURL comment. */
export async function writeDeclarationMap(path: string, map: string | undefined): Promise<void> {
  if (map === undefined) {
    await rm(path, { force: true });
    return;
  }
  let previous: string | undefined;
  try {
    previous = await readFile(path, "utf8");
  } catch {
    previous = undefined;
  }
  if (previous === map) return;
  const temporary = `${path}.${process.pid}.${randomUUID()}.tmp`;
  try {
    await writeFile(temporary, map, { flag: "wx" });
    await rename(temporary, path);
  } catch (error) {
    await rm(temporary, { force: true });
    throw error;
  }
}

const base64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

function decode(mappings: string, sourceCount: number): Segment[][] | undefined {
  const lines: Segment[][] = [];
  let source = 0, sourceLine = 0, sourceColumn = 0;
  for (const text of mappings.split(";")) {
    const segments: Segment[] = [];
    let column = 0;
    for (const field of text === "" ? [] : text.split(",")) {
      const values: number[] = [];
      let value = 0, shift = 0;
      for (const character of field) {
        const digit = base64.indexOf(character);
        if (digit < 0) return undefined;
        value += (digit & 31) << shift;
        if (digit & 32) { shift += 5; continue; }
        values.push(value & 1 ? -(value >>> 1) : value >>> 1);
        value = 0; shift = 0;
      }
      if (shift !== 0 || (values.length !== 1 && values.length !== 4)) return undefined;
      column += values[0];
      if (column < 0) return undefined;
      if (values.length === 1) { segments.push([column]); continue; }
      source += values[1]; sourceLine += values[2]; sourceColumn += values[3];
      if (source < 0 || source >= sourceCount || sourceLine < 0 || sourceColumn < 0) return undefined;
      segments.push([column, source, sourceLine, sourceColumn]);
    }
    lines.push(segments);
  }
  return lines;
}

function encode(lines: readonly (readonly Segment[])[]): string {
  let source = 0, sourceLine = 0, sourceColumn = 0;
  return lines.map(segments => {
    let column = 0;
    return [...segments].sort((left, right) => left[0] - right[0]).map(segment => {
      let text = vlq(segment[0] - column);
      column = segment[0];
      if (segment.length === 4) {
        text += vlq(segment[1] - source) + vlq(segment[2] - sourceLine) + vlq(segment[3] - sourceColumn);
        [, source, sourceLine, sourceColumn] = segment;
      }
      return text;
    }).join(",");
  }).join(";");
}

function vlq(value: number): string {
  let remaining = value < 0 ? (-value << 1) | 1 : value << 1, text = "";
  do {
    let digit = remaining & 31;
    remaining >>>= 5;
    if (remaining > 0) digit |= 32;
    text += base64[digit];
  } while (remaining > 0);
  return text;
}
