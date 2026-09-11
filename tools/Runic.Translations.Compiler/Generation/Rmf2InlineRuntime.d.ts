export declare const rmf2RuntimeAbiVersion: 1;
export declare const rmf2Contract: Readonly<Record<string, unknown>>;
export type LinkBinding = Readonly<{ kind: "runic:link"; href: string }>;
export type ActionBinding = Readonly<{ kind: "runic:action"; onActivate: () => void }>;
export type IconBinding = Readonly<{ kind: "runic:icon"; asset: unknown; decorative: boolean; accessibleName?: (locale: string) => string }>;
export type SlotBindings<S extends Record<string, string>> = { readonly [K in keyof S]: S[K] extends "runic:link" ? LinkBinding : S[K] extends "runic:action" ? ActionBinding : IconBinding };
export declare function linkBinding(options: { href: string }): LinkBinding;
export declare function actionBinding(options: { onActivate: () => void }): ActionBinding;
export declare function iconBinding(options: { asset: unknown; decorative: true } | { asset: unknown; decorative: false; accessibleName: (locale: string) => string }): IconBinding;
export interface MarkupOption { readonly type: "string" | "enum" | "number" | "boolean"; readonly values?: readonly string[]; readonly default?: string; readonly literalOnly?: boolean; readonly description?: string; }
export interface MarkupContract { readonly name: string; readonly kind: "paired" | "standalone"; readonly children: "inline" | "none"; readonly interactive: boolean; readonly plainText: "children" | "lineBreak" | "alternateText" | "explicit" | "omit"; readonly options?: Readonly<Record<string, MarkupOption>>; }
export interface InlineElement<T> { readonly name: string; readonly children: readonly T[]; readonly options: Readonly<Record<string, string>>; readonly binding?: LinkBinding | ActionBinding | IconBinding; readonly occurrence: string; readonly locale: string; readonly standalone: boolean; }
export interface MarkupBinding<T> { readonly contract: MarkupContract; readonly render: (element: InlineElement<T>) => T; }
export interface InlineRenderer<T> { render<S extends Record<string, string>>(content: LocalizedContent<S>, options: { slots: SlotBindings<S> }): readonly T[]; extend(bindings: readonly MarkupBinding<T>[]): InlineRenderer<T>; }
export declare function enumOption(values: readonly string[], defaultValue?: string): MarkupOption;
export declare function defineMarkup(contract: MarkupContract): MarkupContract;
export declare function bindMarkup<T>(contract: MarkupContract, render: (element: InlineElement<T>) => T): MarkupBinding<T>;
export declare function createInlineRenderer<T>(factory: { text(value: string): T; element(element: InlineElement<T>): T }, bindings?: readonly MarkupBinding<T>[]): InlineRenderer<T>;
export declare function createDomInlineRenderer(document: Document, bindings?: readonly MarkupBinding<Node>[]): InlineRenderer<Node>;
export declare function toPlainText<S extends Record<string, string>>(content: LocalizedContent<S>, options: { slots: SlotBindings<S>; allowActionLabels?: boolean; annotateLinkDestinations?: boolean; custom?: readonly MarkupBinding<string>[] }): string;
