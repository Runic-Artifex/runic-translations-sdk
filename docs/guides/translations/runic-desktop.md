# Runic Desktop integration

Runic Translations and the Runic application packages connect without an adapter
package: neither references the other, and each side uses the other's public,
typed seams. [Translate a Runic Views app](runic-views.md) walks through all of
them; in short:

- **Locale.** Since runic-sdk 0.17, the application sets one locale on
  `IRunicApplicationLocale`; the C# catalogs follow it with
  `locale.Follow(translations.SetLocaleAsync)` (a `TranslationContext`, or an
  `ITranslationManager`), and every window and its pages follow it, unless a
  window sets a locale of its own (`IRunicWindowLocale`).
- **Text from ViewModels.** A `TranslationReference`, created by the catalog's
  generated `References` factories, crosses the Bridge as its transport JSON;
  the page formats it in the window's language through `configureTranslatedText`
  (`@runic-artifex/views`). The Views generator recognizes the type by name and
  calls `TranslationReference.Parse` and
  `TranslationReferenceJsonContext.Default.TranslationReference`, which Runic
  Translations keeps stable.
- **Native text.** Menus, standard menu titles, `DesktopPlatformTexts` and tray
  text come from the catalog and are set again when the locale changes.
- **Delivery.** Compiled production locale artifacts are ordinary Runic Assets
  entries and use the same `Runic.Assets.Desktop` streaming, cache, and
  cancellation path as the rest of the frontend. Development ESM modules and
  locale HMR remain owned by the Runic Translations Vite plugin while the Runic
  Vite plugin injects the Desktop bootstrap.

Runic Desktop does not interpret catalogs, fallbacks, messages or translation
diagnostics; locale negotiation, formatting and fallback stay in the generated
runtimes.

## Strings outside the Views

Text that C# shows itself, such as a tray menu, notifications or log lines,
uses the same catalog as the frontend. The C# project names the frontend's
`runic.json` and gets the generated C# API for every message in it:

```xml
<ItemGroup>
  <PackageReference Include="Runic.Translations" Version="<VERSION>" />
  <PackageReference Include="Runic.Translations.Build" Version="<VERSION>" />
  <!-- The page's catalog, which the Vite plugin compiles for the frontend. -->
  <RunicTranslationProject Include="Frontend/translations/runic.json" />
</ItemGroup>
```

```csharp
ITranslationManager manager = DashboardTextCatalog.CreateManager(locale);
var text = new DashboardText(manager);
menu.Add(text.Messages.tray_open);
notify(text.Messages.tray_updates_ready(count));
```

The class and namespace come from `code` in `runic.json`. A translation is
written once, in one place, and both builds check it. The path may point
outside the C# project's directory, for example `../Frontend/translations/runic.json`.

When the user picks another language, the application's locale switches the
manager: `IRunicApplicationLocale.Follow(manager.SetLocaleAsync)`, or
`Follow(translations.SetLocaleAsync)` for a `TranslationContext` with several
catalogs (see [The application's language](runic-views.md#4-the-applications-language)).
Later calls format in that locale. The generated
catalog reads each locale's messages from data in the assembly on first use, so a
process that shows a few messages pays for the locales it uses and compiles no
per-message code. The API uses no reflection and is safe for trimming and
NativeAOT.

The [shared-catalog example](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/examples/shared-catalog)
is a tray process that formats a frontend's catalog in English and German, and
`bun eng/run.mjs native-aot` publishes it with NativeAOT.
