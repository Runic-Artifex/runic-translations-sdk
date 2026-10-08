import index from "../../../../specs/translations/corpus/rmf2-document-v1/index.json" with { type: "json" };
import oracle from "../../../../specs/translations/corpus/rmf2-document-v1/html.json" with { type: "json" };
import { documentFactory, type DocumentNode } from "../src/document/index.js";
import type { InlineBinding, InlineNode } from "../src/inline/index.js";

type ExpectedInline = string | { name: string; options: Record<string, string>; occurrence: string; children: ExpectedInline[] };
type ExpectedBlock = { name: string; options: Record<string, string>; occurrence: string; blocks?: ExpectedBlock[]; inlines?: ExpectedInline[] };
export type CorpusExecution = { id: string; key: string; locale: string; expected: { contentLocale: string; blocks: ExpectedBlock[] } };

export const executions = index.executions as unknown as CorpusExecution[];
export const expectedHtml: Readonly<Record<string, string>> = oracle.executions;
export const standalone = new Set(["runic:br", "runic:icon"]);

/**
 * Rebuilds the shared corpus' expected block tree as documentFactory output, the shape a catalog's
 * createDocumentRenderer(documentFactory) returns, with the corpus slots bound to the given action callback.
 */
export function corpusNodes(id: string, onActivate: () => void = () => undefined): DocumentNode[] {
  const execution = executions.find(item => item.id === id);
  if (!execution) throw new Error(`Unknown corpus execution '${id}'.`);
  const locale = execution.expected.contentLocale;
  const binding = (ref: string | undefined): InlineBinding | undefined => {
    if (ref === undefined) return undefined;
    const slot = (index.slots as Record<string, { kind: string; href?: string }>)[ref];
    return slot.kind === "runic:link" ? { kind: "runic:link", href: slot.href! } : { kind: "runic:action", onActivate };
  };
  const inline = (node: ExpectedInline): InlineNode => typeof node === "string"
    ? documentFactory.text(node) as InlineNode
    : documentFactory.element({ name: node.name, options: node.options, occurrence: node.occurrence, locale, standalone: standalone.has(node.name),
      children: node.children.map(inline), ...(binding(node.options.ref) ? { binding: binding(node.options.ref) } : {}) });
  const block = (node: ExpectedBlock): DocumentNode => documentFactory.block(node.name, node.options,
    node.blocks ? node.blocks.map(block) : (node.inlines ?? []).map(inline), { occurrence: node.occurrence, locale });
  return execution.expected.blocks.map(block);
}

/** Canonical HTML of the corpus oracle: ordinal attribute order, no hydration comments or framework scoping classes. */
export function canonicalHtml(parent: ParentNode): string {
  const escape = (value: string) => value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
  const visit = (node: Node): string => {
    if (node.nodeType === 3) return escape(node.nodeValue ?? "");
    if (node.nodeType !== 1) return "";
    const element = node as Element;
    const tag = element.tagName.toLowerCase();
    const attributes = [...element.attributes].map(({ name, value }) => [name, name === "class" ? value.split(/\s+/).filter(token => token && !token.startsWith("svelte-")).join(" ") : value] as const)
      .filter(([name, value]) => name !== "class" || value).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)
      .map(([name, value]) => ` ${name}="${value.replaceAll("&", "&amp;").replaceAll("\"", "&quot;")}"`).join("");
    return `<${tag}${attributes}>` + (tag === "br" ? "" : [...element.childNodes].map(visit).join("") + `</${tag}>`);
  };
  return [...parent.childNodes].map(visit).join("");
}
