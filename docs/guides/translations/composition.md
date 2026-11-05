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
`ITranslationManager` stays, for code that manages one catalog itself, and keeps priority for a `null` argument, so
`new GridText(null)` still compiles.

**Dependency injection.** The text class now has two one-parameter constructors. When a container can supply both an
`ITranslationManager` and an `ITranslationContext`, Microsoft.Extensions.DependencyInjection cannot choose between them
for a type registration such as `services.AddSingleton<GridText>()` and throws "ambiguous constructors". Register text
classes with a factory instead, for example `AddCatalog<GridText>(..., context => new GridText(context))` from
`Runic.Translations.Hosting`, or `services.AddSingleton(sp => new GridText(sp.GetRequiredService<ITranslationContext>()))`.

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

**The latest switch wins.** A `SetLocaleAsync` call supersedes every earlier call that has not published yet: the earlier
call's preparation is canceled through its token, its result is discarded and it completes without error. When the user
picks German, whose overlay pack is still downloading, and then English again, the context stays on English, and a
slow switch never delays a later one.

**Contexts share their catalogs.** Contexts built from one builder share one provider per catalog: catalog data is read
once, and overlay packs are loaded, checked and composed once per locale. A context per window or per request therefore
costs a snapshot lookup per catalog. Keep the builder and call `BuildAsync` for each window or request; a new builder
starts with empty caches.

Each catalog keeps its own `ITranslationManager`, which `translations.GetManager("acme-grid")` returns, so adapters such
as the WPF `TranslationSource` keep working. A manager's `SetLocaleAsync` switches the whole context; its `RefreshAsync`
recomposes only its catalog. Managers raise `LocaleChanged` and `SnapshotPublished` as before, and the context then
raises its own `LocaleChanged` once. Events are raised in publication order; when switches overlap, only the latest
state is raised, starting from what each subscriber last saw, so no subscriber ends on a superseded locale.

With dependency injection, `Runic.Translations.Hosting` registers the shared context, and the application and each
library add their catalogs in their own registration methods:

```csharp
services.AddRunicTranslations(options => options.InitialLocale = settings.Language)
    .AddCatalog(AppTextCatalog.Registration, context => new AppText(context));
services.AddRunicTranslations().AddCatalog(GridTextCatalog.Registration, context => new GridText(context));   // in the library
```

Only the application passes options; libraries call `AddRunicTranslations()` without them. The context is a singleton by
default, for desktop applications; a web application sets `options.Lifetime = ServiceLifetime.Scoped` so that each
request switches its own context. With overlays, call `await services.InitializeRunicTranslationsAsync()` once after
building the service provider. See the [Hosting README](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/packages/dotnet/Runic.Translations.Hosting/README.md).

`translations.Resolve(reference)` resolves a stored `TranslationReference` of any registered catalog, so validation
text from a library ViewModel is translated like the application's own.

### WPF control libraries

A control library's `Generic.xaml` names its catalog, and the application shares its context with XAML once at startup:

```csharp
TranslationSource.UseContext(translations, registration => new WpfInlineRenderer(registration.Rmf2MarkupContract, Navigate));
TranslationSource.Default = TranslationSource.ForCatalog(AppTextCatalog.CatalogId);
```

```xml
<ControlTemplate TargetType="local:DataGridHeader">
  <TextBlock Text="{rt:Message grid_title, Catalog=acme-grid}"/>
</ControlTemplate>
```

`TranslationSource.ForCatalog(id)` returns one source per catalog over the context's manager and the catalog's generated
readable surface (`XCatalog.Registration` creates it), so library text follows the application's locale switches. Set
`rt:TranslationProperties.Source="{rt:CatalogSource acme-grid}"` on a template root to use the catalog for every message
and rich message below it. The library's build checks the keys against its own catalog. See the
[WPF README](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/packages/dotnet/Runic.Translations.Wpf/README.md#control-libraries).

### Runic.CommandLine apps

`TranslationCommandTextResolver` treats the application's catalog as an overlay of the `runic-command-line` catalog,
locale-first, and takes a context or a `TranslationContextBuilder` to apply overlays of the framework text, such as French
packs. See [Translate a Runic.CommandLine app](command-line.md).

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
`TranslationManager` and for external packs. A snapshot from a custom `ITranslationSnapshotFactory` reports what it
implements; the interface's defaults report its locale as `Exact`.

The requested locale is also the one numbers, percentages, dates and times format in when it shares the message's
language (see [localized formatting](../../../specs/translations/rmf2-execution-v2.md#localized-formatting)): a `de-CH` request shows the `de` text with Swiss separators. A switch from
`de` to `de-CH` therefore publishes new snapshots and raises `LocaleChanged` on every manager and on the context,
although each catalog stays on `de`. Managers raise `LocaleChanged` whenever the resolved locale or the requested tag
changes.

`translations.SupportedLocales` lists the locales in which every catalog shows its own text (with the locales overlays
add): offer these in a language picker to avoid mixed-language screens. Each catalog's own locales are in
`Catalogs[i].SupportedLocales`.

During development, a non-exact resolution is reported when a context publishes: at the verbose level of the
`Runic-Translations` event source (for `dotnet-trace` or an `EventListener`), and in the output window of an attached
debugger.

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

**Added locales.** `addedLocales` declares locales the library lacks. An added locale falls back to `fallbackLocale`
when it names one. Without it, it falls back to its nearest parent that the library declares or an overlay adds, so
`de-CH` falls back to the library's `de`, and without such a parent to the library's default locale. From the first
library locale on, the library's chain applies. Keys the overlay does not translate show the overlay's text for that
fallback locale if it has one, else the library's. A locale that a later library release declares itself keeps the
library's chain. Two overlays that add one locale with different fallbacks are an error.

**Fallback copies.** A pack built with relaxed completeness (`"translationCompleteness": "allow"`) contains copies of
base-locale text for the keys it does not translate. These copies are not application text for a locale the library
declares: a French user sees the library's English (or the application's own `en` pack), exactly as an English user
does. They apply only to locales that overlays add.

A malformed pack, or one that names another catalog or locale, fails the switch with a `TranslationPackException`,
like an external pack. `RefreshAsync` reloads overlay packs; other contexts of the same builder get the reloaded text on
their next switch or refresh.

## Registering a catalog more than once

Independent libraries may register a catalog they share; a repeated registration with the same contract fingerprint is
ignored. A catalog's `TranslationOptions` belong to one registration, normally the application's: a registration
without options accepts another's, and two registrations with different options throw `ArgumentException`, as does a
registration with another contract fingerprint.

## Versions

Code generated by release N runs on runtime N or later within the same minor before 1.0, and within the same major from
1.0. A library packed with `Runic.Translations.Build` N therefore depends on `Runic.Translations` `[N, next minor-0)`
before 1.0, for example `[0.9.0, 0.10.0-0)`, and `[N, next major-0)` from 1.0, for example `[1.2.0, 2.0.0-0)`. An
application may use a later runtime within that range than the library was built with. The `-0` upper bound is the
lowest prerelease of the next breaking release, so its previews are outside the range as well; an application on a
runtime outside the range gets NuGet warning NU1608 at restore. Multi-targeted libraries and symbol packages
(`.snupkg`) get the same range.

A catalog generated before 0.9 has no `Registration`. Create one from its generated members:

```csharp
var grid = new TranslationCatalogRegistration(GridTextCatalog.CatalogId, GridTextCatalog.DefaultLocale,
    GridTextCatalog.ContractFingerprint, GridTextCatalog.Rmf2MarkupContract, GridTextCatalog.CreateProvider);
```
