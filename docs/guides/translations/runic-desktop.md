# Runic Desktop integration

Runic Translations does not need or provide a presentation-host adapter.
Localization remains owned by the generated C# runtime, generated ESM modules,
locale-pack artifacts, and their existing Vite integration.

For a Runic Desktop application:

- compiled production locale artifacts are ordinary Runic Assets entries and
  use the same `Runic.Assets.Desktop` streaming, cache, and cancellation path as
  the rest of the frontend;
- development ESM modules and locale HMR remain owned by the Runic Translations
  Vite plugin while the Runic Vite plugin injects the Desktop bootstrap;
- runtime locale changes remain `ITranslationManager` or generated-ESM state;
  an application can connect them to its typed application model through its
  chosen application API;
- Runic Desktop does not interpret catalogs, locales, fallbacks, messages, or
  translation diagnostics.

This absence of a `Runic.Translations.Desktop` package is intentional: adding
one would duplicate either Assets delivery, Vite/HMR, or translation-domain
ownership.

## Strings outside the Views

Text that C# shows itself, such as a tray menu, notifications or log lines,
uses the same catalog as the frontend. The C# project names the frontend's
`runic.json` and gets the generated C# API for every message in it:

```xml
<ItemGroup>
  <PackageReference Include="Runic.Translations" Version="<VERSION>" />
  <PackageReference Include="Runic.Translations.Build" Version="<VERSION>" />
  <!-- The page's catalog, which the Vite plugin compiles for the frontend. -->
  <TranslationProject Include="Frontend/translations/runic.json" />
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

When the user picks another language, call `manager.SetLocaleAsync(locale)` with
the locale the ViewModel owns; later calls format in that locale. The generated
catalog reads each locale's messages from data in the assembly on first use, so a
process that shows a few messages pays for the locales it uses and compiles no
per-message code. The API uses no reflection and is safe for trimming and
NativeAOT.

The [shared-catalog example](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/examples/shared-catalog)
is a tray process that formats a frontend's catalog in English and German, and
`bun eng/run.mjs native-aot` publishes it with NativeAOT.
