declare module "virtual:runic-translations/editor" {
  type Options = Readonly<{ locale?: string }>;
  type Message = (options?: Options) => string;

  export const m: Readonly<{
    readonly "app_advanced": Message;
    readonly "app_all": Message;
    readonly "app_add_message": Message;
    readonly "app_approve_translations": Message;
    readonly "app_default_locale": Message;
    readonly "app_diagnostics": Message;
    readonly "app_eyebrow": Message;
    readonly "app_invalid": Message;
    readonly "app_missing": Message;
    readonly "app_missing_translation": Message;
    readonly "app_message_bulk_actions": Message;
    readonly "app_message_filters": Message;
    readonly "app_messages": Message;
    readonly "app_no_results": Message;
    readonly "app_no_matching_messages": Message;
    readonly "app_no_selection": Message;
    readonly "app_raw": Message;
    readonly "app_reload": Message;
    readonly "app_review": Message;
    readonly "app_save": Message;
    readonly "app_saved": Message;
    readonly "app_saving": Message;
    readonly "app_search": Message;
    readonly "app_simple": Message;
    readonly "app_structured": Message;
    readonly "app_stale": Message;
    readonly "app_title": Message;
    readonly "app_unsaved": Message;
    readonly "app_translated": Message;
    readonly "app_valid": Message;
    readonly "app_workspace": Message;
    readonly "app_visible_messages": Message;
    readonly "app_mark_for_review": Message;
  }>;
}
