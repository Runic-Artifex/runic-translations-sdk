import type { Snippet } from "svelte";

export type InlineBinding =
  | Readonly<{ kind: "runic:link"; href: string }>
  | Readonly<{ kind: "runic:action"; onActivate: () => void }>
  | Readonly<{ kind: "runic:icon"; asset: unknown; decorative: boolean; accessibleName?: (locale: string) => string }>;
export interface InlineElement {
  readonly name: string;
  readonly children: readonly InlineNode[];
  readonly options: Readonly<Record<string, string>>;
  readonly binding?: InlineBinding;
  readonly occurrence: string;
  readonly locale: string;
  readonly standalone?: boolean;
}
export type InlineNode = Readonly<{ kind: "text"; value: string }> | Readonly<InlineElement & { kind: "element" }>;
export type InlineSnippets = Readonly<Record<string, Snippet<[InlineElement]>>>;

/** Pass this factory to the catalog-generated createInlineRenderer once per application/feature. */
export const inlineFactory = Object.freeze({
  text: (value: string): InlineNode => Object.freeze({ kind: "text", value }),
  element: (element: InlineElement): InlineNode => Object.freeze({ ...element, kind: "element" }),
});
