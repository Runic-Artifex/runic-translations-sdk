import { m } from "virtual:runic-translations/editor";

/** The page's labels from the generated editor catalog, in the interface locale. */
export function editorLabels(locale: string) {
  const options = { locale };
  return {
    title: m.app_title(options),
    eyebrow: m.app_eyebrow(options),
    search: m.app_search(options),
    all: m.app_all(options),
    missing: m.app_missing(options),
    structured: m.app_structured(options),
    plural: m.app_plural(options),
    variants: m.app_variants(options),
    formatted: m.app_formatted(options),
    save: m.app_save(options),
    saving: m.app_saving(options),
    reload: m.app_reload(options),
    simple: m.app_simple(options),
    advanced: m.app_advanced(options),
    raw: m.app_raw(options),
    noSelection: m.app_no_selection(options),
    noResults: m.app_no_results(options),
    valid: m.app_valid(options),
    invalid: m.app_invalid(options),
    saved: m.app_saved(options),
    defaultLocale: m.app_default_locale(options),
    workspace: m.app_workspace(options),
    diagnostics: m.app_diagnostics(options),
    messages: m.app_messages(options),
    messageBulkActions: m.app_message_bulk_actions(options),
    visibleMessages: m.app_visible_messages(options),
    markForReview: m.app_mark_for_review(options),
    approveTranslations: m.app_approve_translations(options),
    addMessage: m.app_add_message(options),
    noMatchingMessages: m.app_no_matching_messages(options),
    missingTranslation: m.app_missing_translation(options),
    translated: m.app_translated(options),
    stale: m.app_stale(options),
    review: m.app_review(options),
    messageFilters: m.app_message_filters(options),
  };
}

export type EditorLabels = ReturnType<typeof editorLabels>;
