export type EditorShortcut =
  | "undo"
  | "redo"
  | "save"
  | "toggle-command-search"
  | "toggle-pseudo-localization"
  | "toggle-right-to-left"
  | "toggle-artifact-preview";

/**
 * The editor's command for a key press. Undo and redo (Ctrl+Z, Ctrl+Shift+Z, Ctrl+Y, or ⌘ on macOS) are the editor's
 * outside text fields and in its draft fields (the composer, the message source, the file view), whose text is the
 * draft's and has its undo history (#225 P4); any other text field keeps the browser's own undo.
 */
export function editorShortcut(
  event: Pick<KeyboardEvent, "altKey" | "ctrlKey" | "key" | "metaKey" | "shiftKey">,
  textEditingTarget: boolean,
  draftFieldTarget = false,
): EditorShortcut | undefined {
  const key = event.key.toLocaleLowerCase();
  const commandModifier = event.ctrlKey || event.metaKey;
  const editorUndo = !textEditingTarget || draftFieldTarget;

  if (commandModifier && !event.altKey && key === "z" && editorUndo) return event.shiftKey ? "redo" : "undo";
  if (commandModifier && !event.altKey && key === "y" && editorUndo) return "redo";
  if (commandModifier && key === "s") return "save";
  if (commandModifier && key === "k") return "toggle-command-search";
  if (!event.altKey || event.ctrlKey || event.metaKey || textEditingTarget) return undefined;
  if (key === "p") return "toggle-pseudo-localization";
  if (key === "r") return "toggle-right-to-left";
  if (key === "b") return "toggle-artifact-preview";
  return undefined;
}
