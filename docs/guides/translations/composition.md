# Catalogs from libraries (.NET)

A component library can ship its own catalog, compiled into its assembly by `Runic.Translations.Build`. An
application that uses the library needs three things from it:

- one locale for its own catalog and every library catalog, switched once;
- overrides for single library strings, and locales the library lacks;
- a way to see when a catalog shows text in another locale than the one requested.

`TranslationContext` provides the first and third, and `TranslationOverlay` the second. The library itself needs no
code for either.

## The library

Give the catalog `"visibility": "public"` in `runic.json`, so its generated `XCatalog.Registration` and text class are
public. Library code takes an `ITranslationContext` instead of creating a manager:

```csharp
public sealed class GridHeader(ITranslationContext translations)
{
    private readonly GridText _text = new(translations);   // generated constructor

    public string Title => _text.Messages.title;
}
```

`new GridText(context)` uses the manager of the `acme-grid` catalog in that context and throws
`KeyNotFoundException` when the application did not register the catalog. The constructor over an
`ITranslationManager` stays, for code that manages one catalog itself.

## The application

Register every catalog once, the application's own first, and build the context:

```csharp
TranslationContext translations = await TranslationContext.CreateBuilder()
    .Add(AppTextCatalog.Registration)
    .Add(GridTextCatalog.Registration)
    .BuildAsync(initialLocale: settings.Language);

await translations.SetLocaleAsync("fr");   // every catalog switches together
```

`SetLocaleAsync` asks every catalog for its snapshot first and publishes them together, so a reader never sees one
catalog in the new locale and another in the old one. When any catalog fails, or the call is canceled, nothing changes.
`Build()` does the same without awaiting, for startup code that cannot await.

Each catalog keeps its own `ITranslationManager`, which `translations.GetManager("acme-grid")` returns, so adapters such
as the WPF `TranslationSource` keep working. A manager's `SetLocaleAsync` switches the whole context; its `RefreshAsync`
recomposes only its catalog. Managers raise `LocaleChanged` and `SnapshotPublished` as before, and the context then
raises its own `LocaleChanged` once.

`translations.Resolve(reference)` resolves a stored `TranslationReference` of any registered catalog, so validation
text from a library ViewModel is translated like the application's own.

## Requested and resolved locales

Each catalog resolves the requested locale under its own unsupported-locale policy. `translations.Catalogs` reports, per
catalog, the requested locale, the locale it uses and how it got there:

```csharp
foreach (TranslationCatalogStatus catalog in translations.Catalogs)
    if (catalog.LocaleResolution != LocaleResolution.Exact)
        logger.LogInformation("{Catalog} shows {Locale} for {Requested}", catalog.CatalogId, catalog.Locale, catalog.RequestedLocale);
```

`Exact` means the catalog (or an overlay) declares the locale, `Parent` that it uses a parent such as `fr` for
`fr-CA`, and `Default` that it falls back to its default locale. The same values are on every snapshot
(`ITranslationSnapshot.RequestedLocale`, `LocaleResolution`) and on the managers' event data, also for a standalone
`TranslationManager`.

## Overriding library text

An overlay holds the application's messages for a library catalog. Today it comes from locale artifacts v5, the
`acme-grid.<locale>.locale-v5.json` files that `runic-translations generate` writes for a project with the library's
catalog ID:

```csharp
TranslationContext translations = await TranslationContext.CreateBuilder()
    .Add(AppTextCatalog.Registration)
    .Add(GridTextCatalog.Registration)
    .AddOverlay(TranslationOverlay.FromPacks("acme-grid", packSource,
        addedLocales: [new TranslationOverlayLocale("fr", fallbackLocale: "en")]))
    .BuildAsync("fr");
```

`packSource` is an `IExternalTranslationSource`, as for external packs, and receives the catalog ID and each locale of
the chain. Overlays are checked key by key, not with the library's whole-catalog fingerprint:

- an overlay message must have the library message's inputs (names and types), its content kind (inline or document)
  and markup that the library's markup contract accepts, and no markup where the library formats plain text;
- a message that does not match, or whose key a later library release removed, is skipped and listed in
  `TranslationCatalogStatus.OverlayDiagnostics`; the library's text is used instead;
- keys a later library release adds simply show the library's text.

So an overlay written for one library release keeps working with the next.

**Precedence is locale-first.** For each locale of the requested locale's chain, the overlays are tried, then the
library, and only then the next locale. A German user therefore sees the library's German text even where the
application overrides the English one.

**Added locales.** `addedLocales` declares locales the library lacks. An added locale falls back to one library locale,
its default unless `fallbackLocale` names another, and from there along the library's chain. Keys the overlay does not
translate show the overlay's text for that fallback locale if it has one, else the library's. A locale that a later
library release declares itself keeps the library's chain.

A malformed pack, or one that names another catalog or locale, fails the switch with a `TranslationPackException`,
like an external pack. `RefreshAsync` reloads overlay packs.
