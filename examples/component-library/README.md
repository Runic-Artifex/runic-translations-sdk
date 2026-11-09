# Component library: a library catalog in .NET and on the web

A component library ships its own catalog, in English and German, as a NuGet
package and as an npm package. Two applications use it: a .NET console
application and a SvelteKit application. They share one catalog and one set of
overrides in `translations/`: both override one library message, add French,
switch the locale once for their own text and the library's, and format a price
(`:currency`) beside the library's storage figures (`:unit`).
The [Ship translations in a library](../../docs/guides/translations/libraries.md)
guide explains each step.

| Directory | What it is |
| --- | --- |
| [`library/`](library) | `Acme.Widgets` (NuGet) and `@acme/widgets` (npm): one catalog, `acme.widgets`, in `library/translations/` |
| [`translations/`](translations) | The applications' catalog, `shop`, and their overrides of `acme.widgets` |
| [`app/`](app) | A .NET console application with build-time overrides of the library |
| [`web/`](web) | A SvelteKit application with server rendering and Vite `dependencies: "auto"` |

CI builds and runs all three against the packages of the current commit, as part
of `bun eng/verify-packages.mjs`. After `bun eng/run.mjs pack`, run
`bun eng/verify-examples.mjs` to do the same in a temporary directory.

## The library

`library/translations/runic.json` names the catalog after the package, in lower
case, and makes its generated classes public:

```json
{
  "catalog": "acme.widgets",
  "code": { "namespace": "Acme.Widgets", "className": "WidgetsText", "visibility": "public" },
  "baseLocale": "en",
  "locales": ["en", "de"]
}
```

```rmf2
storage {
  empty = No files yet
  usage = {$used :unit unit=gigabyte} of {$total :unit unit=gigabyte} used
}
```

**NuGet.** [`Acme.Widgets.csproj`](library/Acme.Widgets.csproj) references
`Runic.Translations` and, as a development dependency, `Runic.Translations.Build`,
and sets `RunicTranslationsPackCatalog`. `dotnet pack` then adds the catalog's
sources and contract under `runic/acme.widgets/`, and the package depends on
`Runic.Translations` `[0.9.1, 0.10.0-0.0)`. The library's code takes the
application's `ITranslationContext` and creates no manager of its own:

```csharp
public sealed class StorageMeter(ITranslationContext translations)
{
    private readonly WidgetsText _text = new(translations);

    public string Title => _text.Messages.storage_title;
}
```

**npm.** [`package.json`](library/package.json) exports the prebuilt catalog as
`./runic-translations` and declares its sources in `runicTranslations`;
[`index.js`](library/index.js) imports the prebuilt modules by a relative path:

```json
"exports": {
  ".": { "types": "./index.d.ts", "default": "./index.js" },
  "./runic-translations": "./dist/translations/acme.widgets.esm-v5/web-module-manifest-v3.json"
},
"runicTranslations": { "catalog": "acme.widgets", "project": "./translations/runic.json" }
```

`bun run build` (`runic-translations generate --emit-esm`) writes the prebuilt
catalog to `dist/translations/` before the package is packed.

## The shared catalog

[`translations/runic.json`](translations/runic.json) lists the library's catalog
under `overrides`, and `translations/overrides/acme.widgets/` holds the
applications' text for it:

```text
translations/
  runic.json                       "overrides": [{ "catalog": "acme.widgets", "from": ["Acme.Widgets", "@acme/widgets"] }]
  en.rmf2, de.rmf2, fr.rmf2        the applications' own catalog, shop
  overrides/acme.widgets/en.rmf2   storage.empty = Your drive is empty
  overrides/acme.widgets/fr.rmf2   every library message in French
```

`from` lists both packages of the library: the .NET build matches the NuGet
package `Acme.Widgets`, the Vite build the npm package `@acme/widgets`. A
wording changed here changes in both applications.

## The .NET application

[`Shop.csproj`](app/Shop.csproj) names the shared catalog:

```xml
<RunicTranslationProject Include="../translations/runic.json" />
```

The build checks each override against the packed catalog and compiles them into
`ShopTextCatalog.Overlays`. [`Program.cs`](app/Program.cs) registers both
catalogs and the overlays in one `TranslationContext` and switches it:

```csharp
TranslationContext translations = TranslationContext.CreateBuilder()
    .Add(ShopTextCatalog.Registration)
    .Add(WidgetsTextCatalog.Registration)
    .AddOverlays(ShopTextCatalog.Overlays)
    .Build("en");

await translations.SetLocaleAsync("fr");   // the application's text and the library's
```

It prints:

```text
Languages: de, en, fr
[en] Cloud storage | Storage | Your drive is empty | 3 files · 1.5 GB of 15 GB used | €4.99 per month
[de] Cloud-Speicher | Speicher | Noch keine Dateien | 3 Dateien · 1,5 GB von 15 GB belegt | 4,99 € pro Monat
[de-CH] Cloud-Speicher | Speicher | Noch keine Dateien | 3 Dateien · 1.5 GB von 15 GB belegt | EUR 4.99 pro Monat
  shop shows de for de-CH (Parent)
  acme.widgets shows de for de-CH (Parent)
[fr] Stockage en ligne | Stockage | Aucun fichier pour l’instant | 3 fichiers · 1,5 Go sur 15 Go utilisés | 4,99 € par mois
```

- The English override replaces one message; the library's other English text
  stays.
- A German user sees the library's own German "Noch keine Dateien": overrides
  apply locale-first, so the English override does not hide a translation the
  library has.
- French is a locale the library lacks. The overrides add it, so
  `translations.SupportedLocales` lists it and both catalogs show French text.
- A `de-CH` request shows the `de` text of both catalogs with Swiss number and
  currency formats; `translations.Catalogs` reports how each catalog resolved
  it.

## The SvelteKit application

[`web/`](web) is the same application on the web, on the same catalog and
overrides. [`vite.config.ts`](web/vite.config.ts) names the shared catalog and
turns on `dependencies: "auto"`:

```ts
runicTranslations({ project: '../translations', dependencies: 'auto' })
```

The plugin finds `@acme/widgets` through its `runicTranslations` field,
regenerates its catalog with the overrides and the application's locales
(`en`, `de`, `fr`), and redirects the library's own imports of its prebuilt
modules to the regenerated ones. The library's code is unchanged.

- **One locale switch.** The root layout calls `useMessageLocale(locale)`, which
  sets the shared locale channel that every catalog generated by 0.9 or later
  reads. The library's text switches with the application's, without code in the
  library.
- **Server rendering without a locale flip.** `hooks.server.ts` passes the
  application's `runWithLocale` to `createLocaleHandle`. Every catalog's
  `server.js` shares one request storage, so the library's text renders in the
  request's locale on the server (`/fr` shows "Aucun fichier pour l’instant")
  and stays so when the page hydrates. CI requests `/`, `/de` and `/fr`,
  including 30 interleaved requests, from the production `adapter-node` server,
  then loads `/fr` in Chromium and checks the text after hydration and after
  switching to `de` and back on the client.
- **Server data in the request's locale.** `+page.server.ts` formats library
  text on the server and reads `url`, so SvelteKit loads it again when a switch
  on the client changes the path (`locals` are not tracked).
- **Currency and units.** `m.price({ amount: '4.99' })` and the library's
  `:unit` message format with the pinned CLDR data, as in .NET.

## Run it yourself

The package versions in the project files are those of this release. From the
repository's development shell, pack the packages and run the check:

```sh
bun eng/run.mjs pack
bun eng/verify-examples.mjs
```

To work in the example directory instead, provide a `NuGet.config` with the
package source and a local tool manifest, then:

```sh
cd examples/component-library
dotnet new tool-manifest --output .config
dotnet tool install dotnet-runic-translations --version <VERSION>
dotnet pack library/Acme.Widgets.csproj --output packages     # add ./packages as a package source
dotnet run --project app/Shop.csproj
(cd library && bun run build && bun pm pack --destination .)   # acme-widgets-1.0.0.tgz
(cd web && bun install && bun run dev)                         # web/bun.lock pins the third-party packages
```

There is no leg with a library built by the previous release: Runic
Translations 0.8 packs no catalogs and generates no `Registration`, so its
libraries cannot be overridden. `verify-packages` checks separately that a
library built with the previous release runs on this release's runtime.
