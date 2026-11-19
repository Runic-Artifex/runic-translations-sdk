- **Breaking: a message in your own catalog no longer overrides command-line
  framework text.** 0.14 deprecated this second override path; 0.15 removes it.
  `TranslationCommandTextResolver` asks your application's catalog only for your
  own keys (`DescriptionKey` text), so a message named after a
  `runic-command-line` key, such as `help { usage = Synopsis }`, has no effect,
  and the built-in framework text is shown. The resolver no longer writes its
  trace warning. Warning `RTR0107` now says that the message has no effect and
  names the key (#243). To keep the text, move each such message, unchanged, to
  `translations/overrides/runic-command-line/<locale>.rmf2`, add
  `{ "catalog": "runic-command-line", "from": "Runic.Translations.CommandLine" }`
  to `overrides` in `runic.json`, and register `AppTextCatalog.Overlays` with a
  context-based resolver (`new TranslationCommandTextResolver(context, AppTextCatalog.CatalogId)`,
  the `TranslationContextBuilder` constructor, or `AddRunicCommandLineTranslations()`);
  the snapshot and manager constructors ignore overlays. The build then checks
  each override against the framework catalog (`RTR0103`). If the message is
  your own text that only shares the name, rename it. See
  [Framework text](https://github.com/Runic-Artifex/runic-translations-sdk/blob/v0.15.0/docs/guides/translations/framework-text.md#messages-named-after-command-line-framework-keys).
