import type { ResourceValue } from "./resource-model";

export const inputTypes = ["string", "bool", "int64", "decimal", "date", "time", "instant", "uuid"] as const;
export const formatFunctions = ["string", "integer", "number", "date", "time", "datetime", "uuid", "relativeTime"] as const;
export const selectorFunctions = ["plural", "ordinal", "literal"] as const;
export const relativeTimeUnits = ["second", "minute", "hour", "day", "week", "month", "year"] as const;

export type InputType = typeof inputTypes[number];
export type FormatFunction = typeof formatFunctions[number];
export type SelectorFunction = typeof selectorFunctions[number];

export interface MessageInput { type: InputType; format?: string }
export interface MessageFormat {
  name: string;
  input: string;
  function: FormatFunction;
  format?: string;
  unit?: typeof relativeTimeUnits[number];
  numeric?: "always" | "auto";
}
export interface MessageSelector { name: string; input: string; function: SelectorFunction }
export interface MessageMarkup {
  markup: { name: string; attributes?: Record<string, string>; children: MessagePatternNode[] };
}
export type MessagePatternNode =
  | string
  | { input: string }
  | { local: string }
  | { format: Omit<MessageFormat, "name"> }
  | MessageMarkup;
export interface MessageVariant { match: Record<string, string>; value: string | MessagePatternNode[] }
export interface StructuredMessage extends Record<string, unknown> {
  inputs: Record<string, MessageInput>;
  declarations?: MessageFormat[];
  selectors: MessageSelector[];
  variants: MessageVariant[];
}

export interface ArtifactInput { type: "string" | "bool" | "int" | "number" | "date" | "time" | "datetime" | "guid"; format: string }
export interface ArtifactSelector { name: string; input: string; function: SelectorFunction }
export type ArtifactNode =
  | { kind: "text"; value: string }
  | { kind: "input"; input: string }
  | { kind: "format"; input: string; function: FormatFunction; format: string; unit?: string; numeric?: string }
  | { kind: "markup"; name: string; attributes: Record<string, string>; children: ArtifactNode[] };
export interface MessageArtifact {
  astVersion: 2;
  inputs: Record<string, ArtifactInput>;
  selectors: ArtifactSelector[];
  variants: Array<{ matches: Record<string, string>; nodes: ArtifactNode[] }>;
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

export function sourceMessageToArtifact(value: StructuredMessage): MessageArtifact {
  const inputs = inferredInputs(value);
  const declarations = new Map((value.declarations ?? []).map((declaration) => [declaration.name, declaration]));
  return {
    astVersion: 2,
    inputs: Object.fromEntries(Object.entries(inputs).map(([name, input]) => [name, {
      type: artifactType(input.type),
      format: input.format ?? defaultFormat(input.type),
    }])),
    selectors: structuredClone(value.selectors),
    variants: value.variants.map((variant) => ({
      matches: structuredClone(variant.match),
      nodes: compileNodes(patternNodes(variant.value), declarations),
    })),
  };
}

function inferredInputs(message: StructuredMessage): Record<string, MessageInput> {
  const inputs = structuredClone(message.inputs);
  const ensure = (name: string, type: InputType): void => {
    inputs[name] ??= { type };
  };
  for (const selector of message.selectors) {
    ensure(selector.input, selector.function === "literal" ? "string" : "int64");
  }
  for (const declaration of message.declarations ?? []) {
    ensure(declaration.input, inputTypeForFunction(declaration.function));
  }
  const visit = (nodes: MessagePatternNode[]): void => {
    for (const node of nodes) {
      if (typeof node === "string" || "local" in node) continue;
      if ("input" in node) ensure(node.input, "string");
      else if ("format" in node) ensure(node.format.input, inputTypeForFunction(node.format.function));
      else visit(node.markup.children);
    }
  };
  for (const variant of message.variants) visit(patternNodes(variant.value));
  return inputs;
}

function inputTypeForFunction(fn: FormatFunction): InputType {
  return ({
    string: "string",
    integer: "int64",
    number: "decimal",
    date: "date",
    time: "time",
    datetime: "instant",
    uuid: "uuid",
    relativeTime: "decimal",
  } satisfies Record<FormatFunction, InputType>)[fn];
}

function compileNodes(nodes: MessagePatternNode[], declarations: Map<string, MessageFormat>): ArtifactNode[] {
  return nodes.map((node): ArtifactNode => {
    if (typeof node === "string") return { kind: "text", value: node };
    if ("input" in node) return { kind: "input", input: node.input };
    if ("local" in node) {
      const declaration = declarations.get(node.local);
      if (declaration === undefined) return { kind: "text", value: "" };
      return formatNode(declaration);
    }
    if ("format" in node) return formatNode(node.format);
    return {
      kind: "markup",
      name: node.markup.name,
      attributes: structuredClone(node.markup.attributes ?? {}),
      children: compileNodes(node.markup.children, declarations),
    };
  });
}

function formatNode(format: Omit<MessageFormat, "name">): ArtifactNode {
  return {
    kind: "format",
    input: format.input,
    function: format.function,
    format: format.format ?? defaultFunctionFormat(format.function),
    ...(format.function === "relativeTime" ? { unit: format.unit ?? "day", numeric: format.numeric ?? "auto" } : {}),
  };
}

function artifactType(type: InputType): ArtifactInput["type"] {
  return ({ int64: "int", decimal: "number", instant: "datetime", uuid: "guid" } as const)[type as "int64"] ?? type as ArtifactInput["type"];
}

function defaultFormat(type: InputType): string {
  return ({
    string: "none", bool: "lower", int64: "plain", decimal: "plain",
    date: "iso", time: "iso", instant: "iso", uuid: "d",
  } satisfies Record<InputType, string>)[type];
}

function defaultFunctionFormat(fn: FormatFunction): string {
  return ({
    string: "none", integer: "plain", number: "plain", date: "iso", time: "iso",
    datetime: "iso", uuid: "d", relativeTime: "plain",
  } satisfies Record<FormatFunction, string>)[fn];
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
