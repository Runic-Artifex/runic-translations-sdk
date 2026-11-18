import { caretAfterChange } from "./state/draft-history.svelte";

/** A field whose text is a draft: the composer's patterns, the message source and the file view. */
export type DraftField = HTMLTextAreaElement | HTMLInputElement;

/** The draft field `target` is, if it is one: a text field inside an element marked `data-draft-field`. */
export function draftField(target: EventTarget | null): DraftField | undefined {
  return (target instanceof HTMLTextAreaElement || target instanceof HTMLInputElement) && target.closest("[data-draft-field]") !== null
    ? target
    : undefined;
}

/** Whether `target` takes typed text, so the editor's single-key shortcuts leave it alone. */
export function isTextEditingTarget(target: EventTarget | null): boolean {
  return target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement ||
    (target instanceof HTMLElement && target.isContentEditable);
}

/**
 * After an undo or redo the field keeps the focus with the caret where the text changed; a field that gave way (the
 * step was in another message, or the composer's view changed) hands the focus to the editor's first field.
 */
export function restoreCaret(field: DraftField | undefined, shown: string | undefined): void {
  if (field === undefined) return;
  if (field.isConnected) {
    field.focus();
    if (shown !== undefined && field.value !== shown) {
      const caret = caretAfterChange(shown, field.value);
      field.setSelectionRange(caret, caret);
    }
    return;
  }
  document.querySelector<HTMLTextAreaElement | HTMLInputElement>("[data-draft-field]")?.focus();
}
