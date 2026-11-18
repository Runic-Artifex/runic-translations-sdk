import { m } from "virtual:runic-translations/editor";
import type { ProjectKind, TextRole } from "./project-kind";

/**
 * The page's labels from the generated editor catalog, in the interface locale. A content-editing project (D074.2)
 * speaks of texts, not messages and translations; the source text of a translation project (its base locale) is
 * edited as "Text", not as a translation.
 */
export function editorLabels(locale: string, kind: ProjectKind = "translation", role: TextRole = kind === "content" ? "source" : "translation") {
  const options = { locale };
  const content = kind === "content";
  const source = content || role === "source";
  return {
    title: m.app_title(options),
    eyebrow: m.app_eyebrow(options),
    search: content ? m.ui_content_search(options) : m.app_search(options),
    all: m.app_all(options),
    missing: m.app_missing(options),
    structured: m.app_structured(options),
    plural: m.app_plural(options),
    variants: m.app_variants(options),
    formatted: m.app_formatted(options),
    save: m.app_save(options),
    saving: m.app_saving(options),
    reload: m.app_reload(options),
    simple: source ? m.ui_content_text(options) : m.app_simple(options),
    advanced: m.app_advanced(options),
    raw: m.app_raw(options),
    noSelection: content ? m.ui_content_no_selection(options) : m.app_no_selection(options),
    noResults: content ? m.ui_content_no_results(options) : m.app_no_results(options),
    valid: m.app_valid(options),
    invalid: m.app_invalid(options),
    saved: m.app_saved(options),
    defaultLocale: m.app_default_locale(options),
    workspace: m.app_workspace(options),
    diagnostics: m.app_diagnostics(options),
    messages: content ? m.ui_content_texts(options) : m.app_messages(options),
    messageBulkActions: m.app_message_bulk_actions(options),
    visibleMessages: m.app_visible_messages(options),
    markForReview: m.app_mark_for_review(options),
    approveTranslations: m.app_approve_translations(options),
    addMessage: content ? m.ui_content_add_text(options) : m.app_add_message(options),
    noMatchingMessages: content ? m.ui_content_no_matching_texts(options) : m.app_no_matching_messages(options),
    missingTranslation: m.app_missing_translation(options),
    translated: m.app_translated(options),
    stale: m.app_stale(options),
    review: m.app_review(options),
    messageFilters: content ? m.ui_content_text_filters(options) : m.app_message_filters(options),
  };
}

export type EditorLabels = ReturnType<typeof editorLabels>;
