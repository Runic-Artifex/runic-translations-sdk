- **Deprecated: framework text from your own catalog in Runic.Translations.CommandLine.**
  A message in your application's catalog named after a `runic-command-line`
  key, such as `help { usage = Synopsis }`, still overrides that command-line
  text in 0.14, but the build now reports it as warning `RTR0107`, and
  `TranslationCommandTextResolver` writes a trace warning once when it uses
  one. Runic Translations 0.15 removes this second override path: the resolver
  then asks your catalog only for your own keys (`DescriptionKey` text), and
  the framework text returns. To migrate, move each such message, unchanged,
  to `translations/overrides/runic-command-line/<locale>.rmf2`, add
  `{ "catalog": "runic-command-line", "from": "Runic.Translations.CommandLine" }`
  to `overrides` in `runic.json`, and register `AppTextCatalog.Overlays` with a
  context-based resolver (`new TranslationCommandTextResolver(context, AppTextCatalog.CatalogId)`,
  the `TranslationContextBuilder` constructor, or `AddRunicCommandLineTranslations()`);
  the snapshot and manager constructors ignore overlays. The build then checks
  each override against the framework catalog (`RTR0103`). See
  [Framework text](https://github.com/Runic-Artifex/runic-translations-sdk/blob/v0.14.0/docs/guides/translations/framework-text.md#the-second-path-for-command-line-text-deprecated).
