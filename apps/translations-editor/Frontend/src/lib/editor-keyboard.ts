import { shortcutPlatform, type ShortcutPlatform } from "./shortcut-labels";

export type EditorShortcut =
  | "undo"
  | "redo"
  | "save"
  | "save-and-next"
  | "next-message"
  | "previous-message"
  | "toggle-command-search"
  | "toggle-pseudo-localization"
  | "toggle-right-to-left"
  | "toggle-artifact-preview";

/** Where a key press happened, beyond whether its target takes text. */
export interface EditorShortcutContext {
  /** Whose keyboard conventions apply; detected from the window when absent. */
  readonly platform?: ShortcutPlatform;
  /**
   * The target is a widget that uses Alt+↑/↓ or Ctrl+Enter itself, or sits in a dialog or menu: a select, a
   * combobox, a listbox, a menu or a dialog. The message steps leave it alone.
   */
  readonly widgetTarget?: boolean;
}

/**
 * The editor's command for a key press. Undo and redo (Ctrl+Z, Ctrl+Shift+Z, Ctrl+Y, or ⌘ on macOS) are the editor's
 * outside text fields and in its draft fields (the composer, the message source, the file view), whose text is the
 * draft's and has its undo history (#225 P4); any other text field keeps the browser's own undo.
 *
 * The message steps (D074.1): Alt+↓ and Alt+↑ select the next and previous message to work on, and Ctrl+Enter (⌘Enter)
 * saves and selects the next one. They work outside text fields and in the draft fields, never in another text field,
 * a widget that has its own use for the keys, or a dialog. On macOS ⌥↑/⌥↓ move the caret by paragraph in a text
 * field, so there they work outside text fields only. AltGr is Ctrl+Alt on Windows: it is never a step, and AltGr
 * elsewhere does not set Alt. A key press that is part of an input method composition is never a step.
 */
export function editorShortcut(
  event: Pick<KeyboardEvent, "altKey" | "ctrlKey" | "key" | "metaKey" | "shiftKey"> & { readonly isComposing?: boolean },
  textEditingTarget: boolean,
  draftFieldTarget = false,
  context: EditorShortcutContext = {},
): EditorShortcut | undefined {
  const key = event.key.toLocaleLowerCase();
  const commandModifier = event.ctrlKey || event.metaKey;
  const editorUndo = !textEditingTarget || draftFieldTarget;

  if (commandModifier && !event.altKey && key === "z" && editorUndo) return event.shiftKey ? "redo" : "undo";
  if (commandModifier && !event.altKey && key === "y" && editorUndo) return "redo";
  if (commandModifier && key === "s") return "save";
  if (commandModifier && key === "k") return "toggle-command-search";
  const step = editorUndo && context.widgetTarget !== true && event.isComposing !== true;
  if (commandModifier && !event.altKey && !event.shiftKey && key === "enter" && step) return "save-and-next";
  if (event.altKey && !commandModifier && !event.shiftKey && (key === "arrowdown" || key === "arrowup") && step &&
    (!textEditingTarget || (context.platform ?? shortcutPlatform()) !== "mac")) {
    return key === "arrowdown" ? "next-message" : "previous-message";
  }
  if (!event.altKey || event.ctrlKey || event.metaKey || textEditingTarget) return undefined;
  if (key === "p") return "toggle-pseudo-localization";
  if (key === "r") return "toggle-right-to-left";
  if (key === "b") return "toggle-artifact-preview";
  return undefined;
}
