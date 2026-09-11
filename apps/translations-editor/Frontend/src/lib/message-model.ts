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
  | { kind: "markup"; name: string; attributes: Record<string, string>; children: ArtifactNode[]; standalone?: boolean; variableOptions?: string[]; annotations?: Record<string, string> };
export interface MessageArtifact {
  astVersion: 2 | 4;
  contentLocale?: string;
  inputs: Record<string, ArtifactInput>;
  selectors: ArtifactSelector[];
  variants: Array<{ matches: Record<string, string>; nodes: ArtifactNode[] }>;
}
