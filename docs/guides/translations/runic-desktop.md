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
- runtime locale changes remain `ITranslationManager` or generated-ESM state
  and may cross Application Bridge only through an application-owned command
  or event contract;
- Runic Desktop does not interpret catalogs, locales, fallbacks, messages, or
  translation diagnostics.

This absence of a `Runic.Translations.Desktop` package is intentional: adding
one would duplicate either Assets delivery, Vite/HMR, or Application Bridge
domain ownership.
