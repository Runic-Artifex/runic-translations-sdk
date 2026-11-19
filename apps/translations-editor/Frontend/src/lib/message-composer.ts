import type { ResourceValue } from "./resource-model";

import type { EditorMessageProjection } from "./contracts";
import type { StructuredMessage, MessageInput, MessageFormat, MessagePatternNode, MessageVariant, InputType } from "./message-model";
export * from "./message-model";

/** The type a projection's input has in the pattern editor, from its declared type and its formatter. */
export function authoringInputType(type: string, fn?: string): InputType {
  if (fn === "integer") return "int64";
  if (fn === "number") return "decimal";
  if (fn === "datetime") return "instant";
  return ["string", "bool", "int64", "decimal", "date", "time", "instant", "uuid"].includes(type) ? type as InputType : "string";
}

/** The inputs the pattern editor offers for a projection: each input by name, with its type. */
export function authoringInputs(authoring: Pick<EditorMessageProjection, "inputs"> | undefined): Record<string, MessageInput> {
  return Object.fromEntries((authoring?.inputs ?? []).map((input) => [input.name, { type: authoringInputType(input.type, input.function) }]));
}

export function toStructuredMessage(value: ResourceValue | undefined): StructuredMessage {
  if (isStructuredMessage(value)) return structuredClone(value);
  return {
    inputs: {},
    selectors: [],
    variants: [{ match: {}, value: typeof value === "string" ? value : "" }],
  };
}

export function isStructuredMessage(value: unknown): value is StructuredMessage {
  return isObject(value) && isObject(value.inputs) && Array.isArray(value.selectors) && Array.isArray(value.variants);
}

export function nextIdentifier(prefix: string, names: Iterable<string>): string {
  const used = new Set(names);
  if (!used.has(prefix)) return prefix;
  for (let index = 2; ; index += 1) {
    const candidate = `${prefix}${index}`;
    if (!used.has(candidate)) return candidate;
  }
}

export function synchronizeMatches(message: StructuredMessage): StructuredMessage {
  const next = structuredClone(message);
  const names = next.selectors.map((selector) => selector.name);
  for (const variant of next.variants) {
    variant.match = Object.fromEntries(names.map((name) => [name, variant.match[name] || "*"]));
  }
  ensureCatchAll(next);
  return next;
}

export function ensureCatchAll(message: StructuredMessage): void {
  if (message.variants.some((variant) => Object.values(variant.match).every((match) => match === "*"))) return;
  message.variants.push({
    match: Object.fromEntries(message.selectors.map((selector) => [selector.name, "*"])),
    value: "",
  });
}

export function renameInput(message: StructuredMessage, previous: string, nextName: string): StructuredMessage {
  const next = structuredClone(message);
  if (previous === nextName || !(previous in next.inputs)) return next;
  const inputs: Record<string, MessageInput> = {};
  for (const [name, descriptor] of Object.entries(next.inputs)) inputs[name === previous ? nextName : name] = descriptor;
  next.inputs = inputs;
  for (const declaration of next.declarations ?? []) if (declaration.input === previous) declaration.input = nextName;
  for (const selector of next.selectors) if (selector.input === previous) selector.input = nextName;
  for (const variant of next.variants) renameInputInNodes(patternNodes(variant.value), previous, nextName);
  return next;
}

export function renameDeclaration(message: StructuredMessage, previous: string, nextName: string): StructuredMessage {
  const next = structuredClone(message);
  const declaration = next.declarations?.find((candidate) => candidate.name === previous);
  if (declaration !== undefined) declaration.name = nextName;
  for (const variant of next.variants) renameLocalInNodes(patternNodes(variant.value), previous, nextName);
  return next;
}

export function renameSelector(message: StructuredMessage, previous: string, nextName: string): StructuredMessage {
  const next = structuredClone(message);
  const selector = next.selectors.find((candidate) => candidate.name === previous);
  if (selector !== undefined) selector.name = nextName;
  for (const variant of next.variants) {
    const value = variant.match[previous] ?? "*";
    delete variant.match[previous];
    variant.match[nextName] = value;
  }
  return synchronizeMatches(next);
}

export function patternNodes(value: string | MessagePatternNode[]): MessagePatternNode[] {
  if (typeof value !== "string") return value;
  const nodes: MessagePatternNode[] = [];
  let text = "";
  const flush = () => {
    if (text !== "") nodes.push(text);
    text = "";
  };
  for (let index = 0; index < value.length;) {
    if (value.startsWith("{{", index)) {
      text += "{";
      index += 2;
      continue;
    }
    if (value.startsWith("}}", index)) {
      text += "}";
      index += 2;
      continue;
    }
    if (value[index] === "{") {
      const end = value.indexOf("}", index + 1);
      const name = end < 0 ? "" : value.slice(index + 1, end);
      if (/^[A-Za-z_][A-Za-z0-9_]*$/.test(name)) {
        flush();
        nodes.push({ input: name });
        index = end + 1;
        continue;
      }
    }
    text += value[index];
    index += 1;
  }
  flush();
  return nodes;
}

export function patternText(nodes: MessagePatternNode[]): string | undefined {
  let result = "";
  for (const node of nodes) {
    if (typeof node === "string") result += node.replaceAll("{", "{{").replaceAll("}", "}}");
    else if ("input" in node) result += `{${node.input}}`;
    else return undefined;
  }
  return result;
}

function renameInputInNodes(nodes: MessagePatternNode[], previous: string, nextName: string): void {
  for (const node of nodes) {
    if (typeof node === "string") continue;
    if ("input" in node && node.input === previous) node.input = nextName;
    else if ("format" in node && node.format.input === previous) node.format.input = nextName;
    else if ("markup" in node) renameInputInNodes(node.markup.children, previous, nextName);
  }
}

function renameLocalInNodes(nodes: MessagePatternNode[], previous: string, nextName: string): void {
  for (const node of nodes) {
    if (typeof node === "string") continue;
    if ("local" in node && node.local === previous) node.local = nextName;
    else if ("markup" in node) renameLocalInNodes(node.markup.children, previous, nextName);
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** Presentation slots retain the original MF2 expression bytes; the compiler owns semantics. */
export interface Mf2EditorSlot { text: string; token?: string; syntax?: string }

export function mf2VariableSyntax(name: string): string { return `{$${name}}`; }

export function parseMf2Slots(source: string): Mf2EditorSlot[] {
  const slots: Mf2EditorSlot[] = [{ text: "" }];
  for (let index = 0; index < source.length;) {
    const current = slots[slots.length - 1];
    if (source[index] === "\\" && index + 1 < source.length) {
      current.text += source.slice(index, index + 2);
      index += 2;
      continue;
    }
    if (source[index] !== "{") { current.text += source[index++]; continue; }
    let end = index + 1;
    let quoted = false;
    for (; end < source.length; end += 1) {
      if (source[end] === "\\") { end += 1; continue; }
      if (source[end] === "|") quoted = !quoted;
      if (!quoted && source[end] === "}") break;
    }
    if (end === source.length) { current.text += source.slice(index); break; }
    const syntax = source.slice(index, end + 1);
    const match = /^\{\s*\$([\p{L}_][\p{L}\p{N}_-]*)\s*\}$/u.exec(syntax);
    if (match === null) current.text += syntax;
    else {
      current.token = match[1];
      current.syntax = syntax;
      slots.push({ text: "" });
    }
    index = end + 1;
  }
  return slots;
}

export function serializeMf2Slots(slots: Mf2EditorSlot[]): string {
  return slots.map(slot => slot.text + (slot.token === undefined ? "" : slot.syntax ?? mf2VariableSyntax(slot.token))).join("");
}

/** The labels the composer gives a variant: its selector keys, with `*` named as the fallback case. */
export interface VariantTitleLabels { defaultTranslation: string; fallback: string }

/** A variant's title, as the composer and the source panel show it: `count: one · kind: Fallback`. */
export function variantTitle(keys: readonly string[], selectors: readonly string[], labels: VariantTitleLabels): string {
  if (keys.length === 0) return labels.defaultTranslation;
  return keys.map((key, index) => `${selectors[index] ?? ""}: ${key === "*" ? labels.fallback : key}`).join(" · ");
}

/** A run of the read-only source text: plain text, a variable chip, or another placeholder (markup or a formatted value). */
export type SourceSegment =
  | { kind: "text"; text: string }
  | { kind: "variable"; name: string; syntax: string }
  | { kind: "placeholder"; syntax: string };

export interface SourceBlock { id: string; keys: string[]; segments: SourceSegment[] }

/**
 * The base-language text of a message as the translate view shows it: one block per variant, with placeholders apart
 * from the text; or the message source itself when the compiler has no visual projection of it.
 */
export type SourceTextView =
  | { kind: "blocks"; selectors: string[]; blocks: SourceBlock[] }
  | { kind: "source"; text: string };

interface SourceEntryLike {
  value: unknown;
  authoring?: { supported: boolean; selectors: string[]; variants: { id: string; keys: string[]; pattern: string }[] };
}

export function sourceTextView(entry: SourceEntryLike | undefined): SourceTextView | undefined {
  if (entry === undefined) return undefined;
  const authoring = entry.authoring;
  if (authoring?.supported === true && authoring.variants.length > 0) {
    return {
      kind: "blocks",
      selectors: [...authoring.selectors],
      blocks: authoring.variants.map(variant => ({ id: variant.id, keys: [...variant.keys], segments: sourceSegments(variant.pattern) })),
    };
  }
  if (typeof entry.value !== "string") return { kind: "source", text: JSON.stringify(entry.value, null, 2) };
  if (/^\s*\.(?:input|local|match)\b/.test(entry.value)) return { kind: "source", text: entry.value };
  const quoted = /^\s*\{\{([\s\S]*)\}\}\s*$/.exec(entry.value);
  return { kind: "blocks", selectors: [], blocks: [{ id: "pattern", keys: [], segments: sourceSegments(quoted?.[1] ?? entry.value) }] };
}

/** Splits an MF2 pattern into display text (escapes resolved) and placeholders; `{$name}` becomes a variable chip. */
export function sourceSegments(pattern: string): SourceSegment[] {
  const segments: SourceSegment[] = [];
  const text = (value: string) => {
    if (value === "") return;
    const last = segments.at(-1);
    if (last?.kind === "text") last.text += value;
    else segments.push({ kind: "text", text: value });
  };
  for (let index = 0; index < pattern.length;) {
    if (pattern[index] === "\\" && index + 1 < pattern.length) { text(pattern[index + 1]); index += 2; continue; }
    if (pattern[index] !== "{") { text(pattern[index++]); continue; }
    let end = index + 1;
    let quoted = false;
    for (; end < pattern.length; end += 1) {
      if (pattern[end] === "\\") { end += 1; continue; }
      if (pattern[end] === "|") quoted = !quoted;
      if (!quoted && pattern[end] === "}") break;
    }
    if (end >= pattern.length) { text(pattern.slice(index)); break; }
    const syntax = pattern.slice(index, end + 1);
    const variable = /^\{\s*\$([\p{L}_][\p{L}\p{N}_-]*)\s*\}$/u.exec(syntax);
    const literal = /^\{\s*\|((?:[^|\\]|\\.)*)\|\s*\}$/u.exec(syntax);
    if (variable !== null) segments.push({ kind: "variable", name: variable[1], syntax: mf2VariableSyntax(variable[1]) });
    else if (literal !== null) text(literal[1].replace(/\\(.)/gu, "$1"));
    else segments.push({ kind: "placeholder", syntax });
    index = end + 1;
  }
  return segments;
}
