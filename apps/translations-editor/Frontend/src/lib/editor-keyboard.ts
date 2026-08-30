export type EditorShortcut =
  | "undo"
  | "redo"
  | "save"
  | "toggle-command-search"
  | "toggle-pseudo-localization"
  | "toggle-right-to-left"
  | "toggle-artifact-preview";

export function editorShortcut(
  event: Pick<KeyboardEvent, "altKey" | "ctrlKey" | "key" | "metaKey" | "shiftKey">,
  textEditingTarget: boolean,
): EditorShortcut | undefined {
  const key = event.key.toLocaleLowerCase();
  const commandModifier = event.ctrlKey || event.metaKey;

  if (commandModifier && key === "z" && !textEditingTarget) return event.shiftKey ? "redo" : "undo";
  if (commandModifier && key === "y" && !textEditingTarget) return "redo";
  if (commandModifier && key === "s") return "save";
  if (commandModifier && key === "k") return "toggle-command-search";
  if (!event.altKey || event.ctrlKey || event.metaKey || textEditingTarget) return undefined;
  if (key === "p") return "toggle-pseudo-localization";
  if (key === "r") return "toggle-right-to-left";
  if (key === "b") return "toggle-artifact-preview";
  return undefined;
}
