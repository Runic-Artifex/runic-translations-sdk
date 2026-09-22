import type { ResourceValue } from "./resource-model";

import type { StructuredMessage, MessageInput, MessageFormat, MessagePatternNode, MessageVariant } from "./message-model";
export * from "./message-model";

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
