- `Runic.Translations`: `TranslationContextBuilder.Add(registration, options)` is
  now `TranslationContextBuilder.AddCatalog(registration, options)`, the name that
  `IRunicTranslationsBuilder.AddCatalog` of `Runic.Translations.Hosting` already
  has for the same operation. Replace `.Add(` with `.AddCatalog(` where a
  `TranslationContext.CreateBuilder()` chain registers a catalog; the arguments
  and the behaviour are unchanged. Code compiled against 0.12 fails with
  `MissingMethodException` until it is rebuilt.
