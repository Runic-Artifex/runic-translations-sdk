import { inlineFactory, type InlineNode } from "../inline/types.js";

/** A block of an RMF2 document message: `runic:p`, `runic:h`, `runic:ul`, `runic:ol` or `runic:li`. */
export interface DocumentBlock {
  readonly kind: "block";
  readonly name: string;
  readonly options: Readonly<Record<string, string>>;
  /** Inline content of a paragraph, heading or list item, or the items of a list. */
  readonly children: readonly DocumentNode[];
  /** The block's skeleton path, such as `ul[1]/li[2]`; stable across locales for the same source variant. */
  readonly occurrence: string;
  /** The effective content locale, which differs from the requested locale after a fallback. */
  readonly locale: string;
}
export type DocumentNode = DocumentBlock | InlineNode;
export interface DocumentBlockContext { readonly occurrence: string; readonly locale: string }

/** Pass this factory to the catalog-generated createDocumentRenderer once per application/feature. */
export const documentFactory = Object.freeze({
  ...inlineFactory,
  block: (name: string, options: Readonly<Record<string, string>>, children: readonly DocumentNode[], { occurrence, locale }: DocumentBlockContext): DocumentNode =>
    Object.freeze({ kind: "block", name, options, children, occurrence, locale }),
});
