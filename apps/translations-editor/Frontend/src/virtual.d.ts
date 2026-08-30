declare module "virtual:runic-translations/editor" {
  type Options = Readonly<{ locale?: string }>;
  type Message = (options?: Options) => string;

  export const m: Readonly<{
    readonly "App.Advanced": Message;
    readonly "App.All": Message;
    readonly "App.AddMessage": Message;
    readonly "App.ApproveTranslations": Message;
    readonly "App.DefaultLocale": Message;
    readonly "App.Diagnostics": Message;
    readonly "App.Eyebrow": Message;
    readonly "App.Invalid": Message;
    readonly "App.Missing": Message;
    readonly "App.MissingTranslation": Message;
    readonly "App.MessageBulkActions": Message;
    readonly "App.MessageFilters": Message;
    readonly "App.Messages": Message;
    readonly "App.NoResults": Message;
    readonly "App.NoMatchingMessages": Message;
    readonly "App.NoSelection": Message;
    readonly "App.Raw": Message;
    readonly "App.Reload": Message;
    readonly "App.Review": Message;
    readonly "App.Save": Message;
    readonly "App.Saved": Message;
    readonly "App.Saving": Message;
    readonly "App.Search": Message;
    readonly "App.Simple": Message;
    readonly "App.Structured": Message;
    readonly "App.Stale": Message;
    readonly "App.Title": Message;
    readonly "App.Unsaved": Message;
    readonly "App.Translated": Message;
    readonly "App.Valid": Message;
    readonly "App.Workspace": Message;
    readonly "App.VisibleMessages": Message;
    readonly "App.MarkForReview": Message;
  }>;
}
