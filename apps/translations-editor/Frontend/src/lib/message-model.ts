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

export interface MessageArtifact {
  astVersion: 5;
  profile: "rmf2-execution-v2";
  inputs: Array<{ name: string; type: string }>;
}

export type PreviewNode =
  | { kind: "text"; value: string }
  | { kind: "element"; name: string; attributes: Record<string, string>; children: PreviewNode[] };

export type MessagePreviewResult =
  | { kind: "text"; value: string }
  | { kind: "content"; nodes: PreviewNode[] };
