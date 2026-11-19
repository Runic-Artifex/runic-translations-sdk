import type { EditorCatalog } from "./contracts";
import type { UiText, UiTextArguments } from "./ui-text";

/**
 * What a project is for (D074.2). A project whose runic.json declares one real locale, the base locale, is a
 * content-editing project: people edit the application's text in that language, and nothing is translated. Every
 * other project is a translation project. Configured `pseudoLocales` are generated, read-only developer tools, so they
 * never make a project a translation project.
 */
export type ProjectKind = "translation" | "content";

/** The catalog's locales that people write text in: its locales without the configured pseudo-locales. */
export function realLocales(catalog: Pick<EditorCatalog, "locales" | "pseudoLocales">): EditorCatalog["locales"] {
  const pseudo = new Set((catalog.pseudoLocales ?? []).map((tag) => tag.toLowerCase()));
  return catalog.locales.filter((locale) => !pseudo.has(locale.tag.toLowerCase()));
}

/**
 * The kind of the open project; a translation project while no catalog is open. `WorkspaceView.projectKind` derives
 * it once from the snapshot; everything else reads it there.
 */
export function projectKind(catalog: Pick<EditorCatalog, "defaultLocale" | "locales" | "pseudoLocales"> | undefined): ProjectKind {
  if (catalog === undefined) return "translation";
  const locales = realLocales(catalog);
  return locales.length === 1 && locales[0].tag.toLowerCase() === catalog.defaultLocale.toLowerCase() ? "content" : "translation";
}

/**
 * What the selected text is: a translation of the base text, or the source text itself. The base locale of a
 * translation project holds the source text, and so does the one language of a content-editing project. Review
 * states, the source panel, Copy source and the translation filters belong to translations; the source text has none.
 */
export type TextRole = "translation" | "source";

/**
 * The role of the text in `locale`: the source text in a content-editing project and in the base locale, a translation
 * otherwise. `WorkspaceView.textRole` derives it for the selection; everything else reads it there.
 */
export function textRole(kind: ProjectKind, locale: string, baseLocale: string): TextRole {
  return kind === "content" || (baseLocale !== "" && locale.toLowerCase() === baseLocale.toLowerCase()) ? "source" : "translation";
}

/**
 * The interface text for editing the source text instead of a translation: the composer's heading, its variants and
 * cases, the inline editor, the quality finding and the palette's mode command. The base locale of a translation
 * project uses it, and so does a content-editing project. Each pair takes the same arguments.
 */
export const sourceTextWording: Readonly<Record<string, string>> = {
  ui_composer_translate_message: "ui_content_composer_heading",
  ui_composer_translation_for: "ui_content_composer_text_for",
  ui_composer_default_translation: "ui_content_composer_default_text",
  ui_composer_add_translation_case: "ui_content_composer_add_case",
  ui_inline_remove_variable: "ui_content_inline_remove_variable",
  ui_quality_whitespace: "ui_content_quality_whitespace",
  ui_palette_switch_to_translation_mode: "ui_content_palette_switch_to_text",
};

/**
 * The interface text a content-editing project uses instead of the translation wording: the key the editor asks for,
 * and the key of the same text for someone who edits the application's text. It is the source-text wording plus the
 * project's own (texts instead of messages). Each pair takes the same arguments. Developer-facing text (the code
 * identifier, the File view, the advanced structure, diagnostics) is not replaced.
 */
export const contentWording: Readonly<Record<string, string>> = {
  ...sourceTextWording,
  ui_message_toolbar_search_messages: "ui_content_search_label",
  ui_message_tree_group_label: "ui_content_tree_group_label",
  ui_message_tree_selected_hidden: "ui_content_tree_selected_hidden",
  ui_message_tree_show_all: "ui_content_tree_show_all",
  ui_message_heading_rename_title: "ui_content_heading_rename_title",
  ui_message_heading_duplicate_title: "ui_content_heading_duplicate_title",
  ui_message_heading_delete_title: "ui_content_heading_delete_title",
  ui_page_empty_selection_description: "ui_content_empty_selection_description",
  ui_sidebar_panels_messages_panel: "ui_content_texts_panel",
  ui_workbench_save_and_next_failed: "ui_content_save_and_next_failed",
  ui_palette_focus_message_search: "ui_content_palette_focus_search",
  ui_palette_toggle_messages_section: "ui_content_palette_toggle_texts",
  ui_count_messages: "ui_content_count_texts",
};

/**
 * The key of `key`'s text in a project of `kind` while the selected text has `role`: the content wording in a
 * content-editing project, the source-text wording while a translation project's base locale is selected.
 */
export function projectTextKey(key: string, kind: ProjectKind, role: TextRole = kind === "content" ? "source" : "translation"): string {
  if (kind === "content") return contentWording[key] ?? key;
  return role === "source" ? sourceTextWording[key] ?? key : key;
}

/**
 * The interface text for the open project and selection: `ui` with the content wording while the project is a
 * content-editing project, and with the source-text wording while a translation project's base locale is selected.
 * The page sets it as the components' text, so a component asks for one key and gets the wording that applies.
 */
export function projectUiText(ui: UiText, kind: () => ProjectKind, role?: () => TextRole): UiText {
  return {
    text(key: string, argumentsValue?: UiTextArguments): string {
      return ui.text(projectTextKey(key, kind(), role?.()), argumentsValue);
    },
  };
}
