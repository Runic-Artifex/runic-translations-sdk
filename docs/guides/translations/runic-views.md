# Translate a Runic Views app

> **Requires runic-sdk 0.17 or later.** This guide uses `IRunicApplicationLocale`,
> `syncDocumentLocale`, `configureFieldErrorText` and other runic-sdk APIs that
> 0.17 adds. runic-sdk 0.17 is not released at the time of writing; until it
> is, the linked runic-sdk READMEs on `main` describe it.

A Runic Views application shows text in three places: the page, which formats
its own messages; ViewModels, which produce status lines and validation
messages; and native surfaces such as menus, the tray and dialogs, which C#
sets. This guide connects all three to one Runic Translations catalog and one
language switch, in six steps:

1. [One catalog](#1-one-catalog-for-the-page-and-c) for the page and the C# side.
2. [References from ViewModels](#2-text-from-viewmodels-references), created by the generated factories.
3. [The page runtime](#3-the-page), which formats messages and references in the window's language.
4. [The application's language](#4-the-applications-language), set once for every window and the C# catalog.
5. [Native text](#5-native-text): menus, dialogs, the tray.
6. [Tests](#6-tests).

It needs Runic Translations 0.9 or later and runic-sdk 0.17 or later
(`Runic.Application.Views`, `@runic-artifex/views` and the package of your page
framework; see the note above). The Runic side of each step is described in detail in
[Translated text](https://github.com/Runic-Artifex/runic-sdk/blob/main/packages/dotnet/Runic.Application.Views/README.md#translated-text)
in the `Runic.Application.Views` README.

## 1. One catalog for the page and C#

Keep the catalog with the frontend, where the Vite plugin compiles it for the
page, and name the same `runic.json` in the C# project, which then gets the
generated C# API for every message:

```text
Notes/
  Notes.csproj
  Frontend/
    vite.config.ts                 runicTranslations({ project: 'translations' })
    translations/runic.json        "catalog": "notes", "code": { "namespace": "Notes", "className": "NotesText" }
    translations/en.rmf2
    translations/de.rmf2
```

```xml
<ItemGroup>
  <PackageReference Include="Runic.Translations" Version="0.11.0" />
  <PackageReference Include="Runic.Translations.Build" Version="0.11.0" PrivateAssets="all" />
  <RunicTranslationProject Include="Frontend/translations/runic.json" />
</ItemGroup>
```

A translation is written once and both builds check it. See
[Strings outside the Views](runic-desktop.md#strings-outside-the-views) and the
[shared-catalog example](../../../examples/shared-catalog/README.md), which has
a C# process and a Vite page on one catalog.

The same `runic.json` overrides component libraries for both builds. For a
library that ships a NuGet package for the C# side and an npm package for the
page, list both in `from`; each build matches its own package:

```json
"overrides": [{ "catalog": "acme.grid", "from": ["Acme.Grid", "@acme/grid"] }]
```

Its `translations/overrides/acme.grid/` files then change the library's text,
or add a locale it lacks, in C# and on the page (with the Vite plugin's
`dependencies: 'auto'`) alike; see
[Use the library in an application](libraries.md#the-application).

## 2. Text from ViewModels: references

A ViewModel does not format sentences. It produces a `TranslationReference`, a
message key with typed arguments and an English fallback, and the page formats
it in the window's language, again whenever the language changes. The
generated `References` class of the catalog creates one per plain-text
message:

```rmf2
ready = Ready
saved = Saved {$title}
```

```csharp
public partial class EditorViewModel : ObservableObject
{
    [ObservableProperty]
    public partial TranslationReference Status { get; set; } = NotesText.References.ready();

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _store.SaveAsync(Title);
        Status = NotesText.References.saved(title: Title);
    }
}
```

A member, DTO member, command input or result of type `TranslationReference`
crosses the Bridge as the reference's JSON and is a `TextReference` on the page.
The Views packages do not reference Runic Translations: the Views generator
recognizes the type by name, and the code it generates into your application
calls `TranslationReference.Parse` and
`TranslationReferenceJsonContext.Default.TranslationReference`. Runic
Translations keeps both members stable. Messages with markup have no factory,
as a reference resolves to plain text; see
[text references](transport.md) for nesting one message in another and for what
happens when a stored reference meets a changed catalog.

**Validation messages.** Give a validation message a reference, and the page
shows it in the window's language, with the message as the fallback. For the
built-in DataAnnotations attributes, register `RunicValidationOptions.TextFor`:

```rmf2
validation {
  required = Enter a {$field}.
  too_long = The {$field} can have at most {$max :integer} characters.
}
```

```csharp
services.AddSingleton(new RunicValidationOptions
{
    TextFor = (attribute, member) => attribute switch
    {
        RequiredAttribute => Text(NotesText.References.validation_required(field: member.DisplayName)),
        MaxLengthAttribute maximum => Text(NotesText.References.validation_too_long(field: member.DisplayName, max: maximum.Length)),
        _ => null,   // keep the attribute's own message
    },
});

static RunicTextReference Text(TranslationReference reference) =>
    RunicTextReference.Create(reference, TranslationReferenceJsonContext.Default.TranslationReference);
```

`member.DisplayName` is the member's `[Display(Name = …)]`, else its
`[DisplayName]`, else the property name, as written in C#. To translate the
field's name too, write a message per field. A reference to the name as a
message argument (`new TranslationReferenceArgument(nameReference)`) does not
fit the generated factories, whose `field` parameter is a `string`: build that
reference by hand, as [Writing references](transport.md#writing-references)
shows.
`RunicValidationMessage.Text`, `RunicValidationResult` and ReactiveUI.Validation
take references the same way; see
[Translated text](https://github.com/Runic-Artifex/runic-sdk/blob/main/packages/dotnet/Runic.Application.Views/README.md#translated-text).

## 3. The page

Connect the page's Translations runtime to the window once, in `main.ts`. The
window's locale comes from the application (step 4); the page follows it and
formats every reference again on a switch. With Svelte:

```ts
import { configureTranslatedText, syncDocumentLocale } from '@runic-artifex/views';
import { useTextLocale } from '@runic-artifex/svelte';
import { m } from 'virtual:runic-translations/notes';
import { configureSharedLocaleResolver, createLocaleSource, localeDirection } from 'virtual:runic-translations/notes/runtime';
import { resolveTextReference } from 'virtual:runic-translations/notes/transport';

const locale = createLocaleSource({ initialLocale: navigator.language });
const textLocale = useTextLocale();
// Every catalog on the page follows the window's language, library catalogs included.
configureSharedLocaleResolver(() => textLocale.current ?? locale.getRequestedLocale());
configureTranslatedText({
  ...locale,
  resolve: (reference, language) => resolveTextReference(reference, m, { locale: language }).text,
  direction: localeDirection,
});
syncDocumentLocale();   // <html lang dir> follow the window's language
```

- `m.*()` calls in templates render again on a switch because the resolver
  reads `useTextLocale()`, which is reactive. React, Vue and Angular differ in
  how they read it; see the table under **The page** in
  [Translated text](https://github.com/Runic-Artifex/runic-sdk/blob/main/packages/dotnet/Runic.Application.Views/README.md#translated-text).
- `configureSharedLocaleResolver` sets the locale of every catalog that has no
  resolver of its own, so text of component libraries
  ([Ship translations in a library](libraries.md)) switches with the page's. A
  page with only its own catalog can call the catalog's
  `configureLocaleResolver` instead, as the Runic README does.
- Show references with the framework's `useText` helper
  (`useText(() => view.state?.status).current` in Svelte). A reference the
  page cannot translate shows its fallback text.

**References of several catalogs.** When ViewModels send references of a
library's catalog too, resolve them by catalog. With the Vite plugin's
`dependencies: 'auto'`, the page's library catalog is regenerated with the
overrides of step 1, so a reference resolves to the same text as in C#:

```ts
import * as notesTransport from 'virtual:runic-translations/notes/transport';
import * as gridTransport from 'virtual:runic-translations/acme.grid/transport';
import { m as grid } from 'virtual:runic-translations/acme.grid';

const resolveReference = notesTransport.createReferenceResolver([
  { transport: notesTransport, messages: m },
  { transport: gridTransport, messages: grid },
]);
configureTranslatedText({
  ...locale,
  resolve: (reference, language) => resolveReference(reference, { locale: language }).text,
  direction: localeDirection,
});
```

**Field errors.** A number, date or time field that cannot read the user's text
reports a `FieldError` with a `reason` and `arguments`. Give the reasons
messages, and `configureFieldErrorText` shows them in the window's language:

```rmf2
field {
  not_a_number = “{$text}” is not a number.
  too_many_fraction_digits = “{$text}” can have at most {$scale :integer} decimal places.
}
```

```ts
import { configureFieldErrorText } from '@runic-artifex/views';

configureFieldErrorText((error, language) => {
  const options = language === undefined ? {} : { locale: language };
  const { text = '', scale = 0 } = error.arguments;
  if (error.reason === 'not-a-number') return m.field_not_a_number({ text }, options);
  if (error.reason === 'too-many-fraction-digits') return m.field_too_many_fraction_digits({ text, scale }, options);
  return undefined;   // the English message
});
```

The reasons are `required`, `not-a-number`, `not-a-whole-number`,
`too-many-fraction-digits`, `out-of-range`, `not-a-boolean`, `not-a-choice`,
`not-a-date`, `not-a-time`, `not-a-date-time`, `offset-out-of-range` and
`no-text-form`; `@runic-artifex/views` documents the arguments of each
(`FieldErrorReason`). Return `undefined` for a reason you do not know.

**Server rendering.** `configureTranslatedText` configures the page's runtime
for everything on the page. Under SvelteKit server rendering, call it in the
browser only, and render with `runWithLocale` or `{ locale }` on the server;
see [Several catalogs on one page](esm.md#several-catalogs-on-one-page).

## 4. The application's language

Most applications show one language in every window. Set it on
`IRunicApplicationLocale`, which `AddRunicWindowService()` registers, and let
the C# catalog follow it:

```csharp
TranslationContext translations = await TranslationContext.CreateBuilder()
    .Add(NotesTextCatalog.Registration)
    .BuildAsync();

var locale = services.GetRequiredService<IRunicApplicationLocale>();
locale.Follow(translations.SetLocaleAsync);           // the C# catalog switches first
locale.LocaleChanged += (_, _) => UpdateNativeText();  // then menus, tray, dialogs (step 5)

// The user's choice, else the best match for the operating system's languages.
await locale.SetLocaleAsync(settings.Language
    ?? LocaleNegotiation.Negotiate(DesktopLanguages.Preferred, translations.SupportedLocales, NotesTextCatalog.DefaultLocale));
```

A language switch, from a menu command or a command the page calls, sets the
same locale:

```csharp
[RelayCommand]
private async Task SwitchLanguageAsync(string language)
{
    await _locale.SetLocaleAsync(language);
    _settings.Language = language;
}
```

- A switch awaits the followers (the context: every catalog in it switches
  together), then moves every window that has no locale of its own, then raises
  `LocaleChanged`, so code that rebuilds native text reads text that is already
  translated.
- A window that needs its own language sets `IRunicWindowLocale.Locale`, and
  `null` to follow the application again. Save `OwnLocale`, not `Locale`, to
  restore a window's choice.
- The page cannot choose a language of its own: a switch on the page calls a
  command that sets the application's (or the window's) locale.
- Negotiate against `translations.SupportedLocales`, the locales every catalog
  shows, so no window mixes languages. `DesktopLanguages.Preferred` lists the
  operating system's languages in order; `LocaleNegotiation` matches them with
  CLDR likely subtags and parent locales (`de-CH` to `de`, `zh-TW` to
  `zh-Hant`).
- Format numbers and dates in messages rather than with `ToString` in C#:
  messages format in the window's language. For C# formatting that must follow
  the window, opt in to `FlowCultureFromLocale`, which runs a window's bridged
  invocations in its locale's culture.

With `Runic.Translations.Hosting`, register the context in the same service
collection (`services.AddRunicTranslations(...).AddCatalog(NotesTextCatalog.Registration, context => new NotesText(context))`)
and pass `GetRequiredService<ITranslationContext>().SetLocaleAsync` to `Follow`.
A web application (CS-WebUI) still has one locale per window; its
`RunicConnection.Languages` reports the browser's languages for the application
to negotiate.

## 5. Native text

Runic ships no translations of its own. Native surfaces take their text from
your catalog, and you set it again in the `LocaleChanged` handler. With Runic
Desktop, the menus belong to the `DesktopHost` that `RunicDesktopHost.Run`
starts (`desktopHost.Desktop`):

```csharp
return RunicDesktopHost.Run(provider, options, async desktopHost =>
{
    var locale = desktopHost.Services.GetRequiredService<IRunicApplicationLocale>();
    locale.LocaleChanged += (_, _) => UpdateNativeText(desktopHost.Desktop);
    UpdateNativeText(desktopHost.Desktop);
    // … open the windows
});

void UpdateNativeText(DesktopHost desktop)
{
    var text = new NotesText(translations);
    desktop.SetMenuBar(BuildMenus(text));
    desktop.SetStandardMenuTitles(new DesktopStandardMenuTitles
    {
        About = text.Messages.menu_about(app: "Notes"),
        Quit = text.Messages.menu_quit(app: "Notes"),
        Edit = text.Messages.menu_edit,
    });
    DesktopPlatformTexts.Current = new DesktopPlatformTexts
    {
        UntitledFileName = text.Messages.untitled,
        SaveFileTitle = text.Messages.save_file_title,
    };
    RebuildTrayMenu(text);
}
```

- **Menus.** `DesktopHost.SetMenuBar` replaces the application's menus.
  `DesktopHost.SetStandardMenuTitles` retitles the standard menus that Runic
  adds on macOS (About, Edit, Quit, …); on Windows and Linux, which show no
  standard menus, the call only checks the titles. The About, Hide and Quit
  titles may contain `{0}` for the application's name, and no other title may
  contain braces; formatting the name into the message, as above, lets the
  translation place it. On CS-WebUI, `RunicCsWebUiHost.SetMenuBar` replaces the
  menus, and there are no standard menus.
- **Dialogs and pages Runic shows.** `DesktopPlatformTexts.Current` holds the
  text Runic shows where the platform has none: dialog titles, the suggested
  "Untitled" name, shortcut key names, browser error pages. Dialogs opened later
  use the new text.
- **Tray and notifications.** Format them from the context when you show them,
  and rebuild a tray menu on a switch.
- **Direction and the web view.** With Runic Desktop on Windows and Linux, the
  window's locale gives the native window its direction (right to left for
  `ar`, `he`, `fa`, …), also after a switch, and the web view its language
  (its context menu, form validation, `navigator.language`) when the window
  opens: after a switch, the web view's language stays until the window opens
  again. Options the window sets (`LayoutDirection`, `Language`) win, and a
  window with a `ProfilePath` keeps the platform's language, since WebView2
  allows one language per profile. macOS takes both from the application
  bundle (below).
  `syncDocumentLocale()` (step 3) sets `lang` and `dir` on the page. See
  [Runic.Application.Views.Desktop](https://github.com/Runic-Artifex/runic-sdk/blob/main/packages/dotnet/Runic.Application.Views.Desktop/README.md).
- **macOS.** List your languages in `RunicApplicationLocales`
  (`<RunicApplicationLocales>en;de</RunicApplicationLocales>`), so macOS shows
  its own text in the application (the About panel, open and save panels) in
  the user's language; see
  [Languages](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/packaging.md#languages).

## 6. Tests

- **Catalog.** Run `runic-translations validate` in CI; the build already
  checks both sides of the shared catalog.
- **ViewModels.** `RunicWindowTestHost.Create<EditorViewModel>(services)` binds
  the window to the services' locale. `await host.ApplicationLocale.SetLocaleAsync("de")`
  switches the application, and `host.Content.Locale` shows the window's
  locale. A view state (`RunicViewState<TModel>`, from `host.Root.Snapshot()`
  or a state tracker's `State`) reads a `TranslationReference` member with
  `Read`; resolve it with the context to check the text a user sees:

  ```csharp
  TranslationReference status = host.Root.Snapshot().Read(vm => vm.Status);
  await translations.SetLocaleAsync("de");   // de.rmf2: saved = Gespeichert: {$title}
  Assert.Equal("Gespeichert: Einkauf", translations.Resolve(status).Text);
  ```

- **Pages.** The mock bridge's `setWindowLocale("de")` pushes a locale as the
  application does, so a page test can check that a template's `m.*()` text
  renders again.

## Checklist

- [ ] One `runic.json` with the frontend, named in the C# project.
- [ ] ViewModels produce `TranslationReference`s from `NotesText.References`, never formatted sentences.
- [ ] `RunicValidationOptions.TextFor` for built-in attributes; `configureFieldErrorText` for field errors.
- [ ] `configureTranslatedText`, `useTextLocale` (or your framework's equivalent) and `syncDocumentLocale` in `main.ts`; `configureSharedLocaleResolver` with library catalogs.
- [ ] `IRunicApplicationLocale.Follow(translations.SetLocaleAsync)` and an initial locale from settings or `DesktopLanguages.Preferred`.
- [ ] Menus, standard menu titles, `DesktopPlatformTexts` and the tray set again on `LocaleChanged`.
- [ ] `RunicApplicationLocales` for macOS.
