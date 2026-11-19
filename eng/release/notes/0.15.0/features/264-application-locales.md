## Runic packages declare the catalog's languages ([#264](https://github.com/Runic-Artifex/runic-translations-sdk/issues/264))

When a Runic Desktop application does not set `RunicApplicationLocales`, Runic.Translations.Build adds the languages
of its `runic.json` as `RunicApplicationLocale` items: `baseLocale` first, as the development language, then
`locales`, without `pseudoLocales`. `dotnet runic package` then declares them in the macOS, MSIX and Linux packages,
and the macOS development bundle of `dotnet run` lists them in `CFBundleLocalizations`, so macOS shows its own text in
the user's language without the languages being listed twice. The items are added during evaluation, which is how
`dotnet runic package` reads the project. A project that sets the property keeps its value, the project's own items
still give a language its name and description, several `runic.json` files add their languages in order, and
`RunicTranslationsFillApplicationLocales` set to `false` turns this off.
