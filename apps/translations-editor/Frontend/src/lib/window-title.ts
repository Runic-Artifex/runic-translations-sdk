import type { UiText } from "./ui-text";

/** The marker in front of the window title while there is unsaved work. */
export const unsavedTitleMarker = "• ";

/**
 * The window title (#225 P14): `<catalog> – Runic Translations Editor`, marked while there is unsaved work, and the
 * plain product name without an open catalog (the welcome, loading and recovery screens). The native window shows the
 * page's title: WebUI's WebView follows `document.title`, as a browser app window does.
 */
export function editorWindowTitle(ui: Pick<UiText, "text">, catalog: string | undefined, unsaved: boolean): string {
  if (catalog === undefined || catalog === "") return ui.text("ui_window_title_product");
  const title = ui.text("ui_window_title_catalog", { catalog });
  return unsaved ? unsavedTitleMarker + title : title;
}
