declare module "virtual:runic-text-resources/editor" {
  type Options = Readonly<{ locale?: string }>;
  type Message = (options?: Options) => string;

  export const m$App$Title: Message;
  export const m$App$Eyebrow: Message;
  export const m$App$Search: Message;
  export const m$App$All: Message;
  export const m$App$Missing: Message;
  export const m$App$Structured: Message;
  export const m$App$Save: Message;
  export const m$App$Saving: Message;
  export const m$App$Reload: Message;
  export const m$App$Simple: Message;
  export const m$App$Advanced: Message;
  export const m$App$Raw: Message;
  export const m$App$NoSelection: Message;
  export const m$App$NoResults: Message;
  export const m$App$Valid: Message;
  export const m$App$Invalid: Message;
  export const m$App$Unsaved: Message;
  export const m$App$Saved: Message;
  export const m$App$DefaultLocale: Message;
  export const m$App$Workspace: Message;
  export const m$App$Diagnostics: Message;
}
