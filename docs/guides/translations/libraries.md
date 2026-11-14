# Ship translations in a library

A component library can bring its own text in its own catalog: a grid's
column menu, a date picker's labels, a framework's error messages. An
application that uses the library then gets, without code in the library:

- **one locale** for its own catalog and every library catalog, switched once;
- **overrides** of single library messages, and locales the library lacks,
  checked at build time;
- **updates** of library text at run time, through the same overlay packs as
  its own ([Ship translation updates without a release](ota.md)).

This guide covers what a library author does for .NET (a NuGet package) and for
the web (an npm package), and what the application does in return. The
[component-library example](../../../examples/component-library/README.md)
does all of it, with a library shipped both ways, a .NET application and a
SvelteKit application; CI builds and runs it. The .NET API is described in
[Catalogs from libraries](composition.md), and the web side in
[Several catalogs on one page](esm.md#several-catalogs-on-one-page) and the
[Vite plugin's README](../../../packages/web/vite-plugin-runic-translations/README.md#library-catalogs-from-dependencies).

## The catalog

A library's catalog is an ordinary Runic Translations project
(`translations/runic.json` and its `.rmf2` sources), with three rules:

- **Name the catalog after the package, in lower case:** `acme.widgets` for the
  NuGet package `Acme.Widgets`. Applications name overrides and packs by catalog
  ID, so it must be unique among the libraries an application uses. A .NET
  library whose catalog ID differs from its package ID gets warning `RTR0105`
  when it packs it. Catalog IDs that start with `runic-` are reserved for the
  Runic packages (`RTR0106`; for npm, for the `@runic-artifex` packages).
- **Make the generated code public** for .NET: `"code": { "namespace": "Acme.Widgets", "className": "WidgetsText", "visibility": "public" }`,
  so the application can register `WidgetsTextCatalog.Registration`.
- **Treat keys and inputs as API.** An application's overrides and packs are
  checked key by key against the library's catalog. Removing a key, renaming
  an input, changing its type, or adding markup to a plain-text message breaks
  the overrides that applications wrote for it: their build fails
  (`RTR0103`, `RTR0016`, `RTR0065`) until they change them, and packs skip the
  message at run time. Adding messages and changing wording break nothing.
  Give a message a new key when its meaning changes, and list removed or
  changed keys in the library's release notes.

Comments and `@param` lines on messages are worth writing: they reach the
application's translators through the generated documentation comments, the
editor and XLIFF exports of the overrides.

## .NET: the NuGet package

```xml
<PropertyGroup>
  <PackageId>Acme.Widgets</PackageId>
  <RunicTranslationsPackCatalog>true</RunicTranslationsPackCatalog>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Runic.Translations" Version="0.11.0" />
  <PackageReference Include="Runic.Translations.Build" Version="0.11.0" PrivateAssets="all" />
</ItemGroup>
```

`dotnet pack` then:

- compiles the catalog into the library's assembly, as for an application;
- adds `runic/acme.widgets/` with the catalog's `runic.json`, its sources and
  `contract.json`, and `buildTransitive/Acme.Widgets.props`, which declares the
  packed catalog to every project that references the package, so the
  application's build can check overrides against it;
- depends on `Runic.Translations` `[0.11.0, 0.12.0-0.0)` (see [Versions](#versions)),
  and not on `Runic.Translations.Build`, a development dependency.

The library's code takes the application's `ITranslationContext` and never
creates a manager or switches a locale:

```csharp
public sealed class StorageMeter(ITranslationContext translations)
{
    private readonly WidgetsText _text = new(translations);

    public string Title => _text.Messages.storage_title;
    public string Usage(decimal used, decimal total) => _text.Messages.storage_usage(used: used, total: total);
}
```

`new WidgetsText(context)` throws `KeyNotFoundException` when the application did
not register the catalog. With dependency injection, register it in the
library's own extension method; only the application passes options:

```csharp
public static IServiceCollection AddAcmeWidgets(this IServiceCollection services)
{
    services.AddRunicTranslations().AddCatalog(WidgetsTextCatalog.Registration, context => new WidgetsText(context));
    return services;
}
```

A WPF control library names its catalog in XAML
(`{rt:Message storage_title, Catalog=acme.widgets}`); see
[WPF control libraries](composition.md#wpf-control-libraries). A library that
packs its own `build/` or `buildTransitive/` props, or targets several
frameworks, is covered in [The library](composition.md#the-library).

## The web: the npm package

An npm package ships the same catalog twice: prebuilt, so that it works in any
bundler, and as sources, so that an application's Vite build can regenerate it
with its overrides and locales.

```json
{
  "name": "@acme/widgets",
  "type": "module",
  "exports": {
    ".": { "types": "./index.d.ts", "default": "./index.js" },
    "./runic-translations": "./dist/translations/acme.widgets.esm-v5/web-module-manifest-v3.json"
  },
  "runicTranslations": { "catalog": "acme.widgets", "project": "./translations/runic.json" },
  "files": ["index.js", "index.d.ts", "dist/translations", "translations"],
  "scripts": {
    "build": "dotnet tool run runic-translations -- generate --project translations --output dist/translations --emit-esm"
  }
}
```

- Build the prebuilt catalog before packing (`runic-translations generate
  --emit-esm`), and export its web module manifest as `./runic-translations`.
- `runicTranslations.project` names the packed `runic.json`; its sources must be
  in the package.
- The library imports its catalog by a relative path, never by a
  `virtual:runic-translations/...` ID, so it builds without the Vite plugin:

  ```js
  import { m } from "./dist/translations/acme.widgets.esm-v5/messages.js";

  export const storageTitle = () => m.storage_title();
  ```

- The library needs no dependency on a Runic package: the generated modules
  are self-contained. It sets no locale: its runtime reads the page's shared
  locale channel and, on a server, the request's locale (both since 0.9; see
  [Several catalogs on one page](esm.md#several-catalogs-on-one-page)). A
  component that calls `m.*()` while it renders, such as a Svelte component,
  renders again on a switch when the application's resolver reads reactive
  state, as `useMessageLocale` does.

One package, one catalog ID: two packages with one catalog, or two versions of
one library in an application, fail the application's build with
`dependencies: "auto"` (install one version).

## The application

**.NET.** Register the application's catalog and the library's in one context,
and switch it once:

```csharp
TranslationContext translations = TranslationContext.CreateBuilder()
    .Add(ShopTextCatalog.Registration)
    .Add(WidgetsTextCatalog.Registration)
    .AddOverlays(ShopTextCatalog.Overlays)      // the build-time overrides below
    .Build(settings.Language);

await translations.SetLocaleAsync("fr");
```

**Web.** Turn on discovery in the Vite plugin, and set the locale once in the
root component; on the server, pass the application's `runWithLocale` to
SvelteKit's `createLocaleHandle`:

```ts
runicTranslations({ dependencies: 'auto' })
```

```svelte
<script lang="ts">
  import { useMessageLocale } from '@runic-artifex/translations-svelte';
  useMessageLocale(locale);   // the application's catalog and every library catalog
</script>
```

**Overrides and added locales.** Both builds read the same layout, and one
`translations/` directory can serve both, as in the example, where the .NET
application and the SvelteKit application share it:

```text
translations/
  runic.json                         "overrides": [{ "catalog": "acme.widgets", "from": ["Acme.Widgets", "@acme/widgets"] }]
  overrides/acme.widgets/en.rmf2     the application's wording of single messages
  overrides/acme.widgets/fr.rmf2     a locale the library lacks
```

`from` names the package that ships the catalog: the NuGet package ID for a
.NET build, the npm package name for a Vite build, or both in an array. Each
build matches the packages of its own kind (NuGet IDs never contain `@` or
`/`), and reports `RTR0104` only when none of them ships the catalog. Each
override is validated against that package's catalog. Overrides apply
**locale-first**: a German user sees the library's German text even where the
application overrides the English one. A locale the overrides add falls back
to its nearest CLDR parent among the library's locales and the added ones
(`de-CH` to `de`), else to the library locale that CLDR likely subtags match,
else to the library's base locale; the same chain applies in .NET and on the
web. Details: [Build-time overrides](composition.md#build-time-overrides).

**Run-time updates.** An application can also install packs for a library's
catalog at run time (`TranslationOverlay.FromPacks("acme.widgets", …)` in .NET,
`installLocalePack` from `virtual:runic-translations/acme.widgets/dynamic` on
the web). They follow the same per-key and locale-first rules; see
[Ship translation updates without a release](ota.md).

## Versions

Code generated by release N runs on the runtime of release N or later within
the same minor before 1.0, and within the same major from 1.0. A library packed
with `Runic.Translations.Build` 0.11.0 therefore depends on
`[0.11.0, 0.12.0-0.0)`; an application may use a later 0.11 runtime, and gets NuGet
warning `NU1608` with a runtime outside the range. Release a library update for
each breaking Runic Translations release. Don't pack with
`Runic.Translations.Build` 0.9.0: it wrote the upper bound `0.10.0-0`, and
nuget.org rejects a package with that bound.

On the web, a library's prebuilt modules carry their own runtime. A catalog
generated before 0.9 keeps its own locale state and does not read the shared
locale channel; [Several catalogs on one page](esm.md#several-catalogs-on-one-page)
shows how an application drives it. With `dependencies: "auto"`, the
application's compiler regenerates every library catalog over one shared
runtime, and the regenerated catalog must keep the contract fingerprint of the
prebuilt one, so the package's sources and prebuilt modules must come from one
build.

## Test the library

- Run `runic-translations validate` (and `status`, for translation coverage) in
  the library's CI, as for an application.
- Test the text through a context with the catalog registered, the way an
  application uses it, in every locale the library ships:

  ```csharp
  TranslationContext translations = TranslationContext.CreateBuilder().Add(WidgetsTextCatalog.Registration).Build("de");
  Assert.Equal("Speicher", new StorageMeter(translations).Title);
  ```

- Check the packages: the `.nupkg` contains `runic/<catalog>/contract.json`, and
  `npm pack --dry-run` lists the prebuilt manifest and the `runic.json` that
  `runicTranslations.project` names.

## Limits

- An application overrides a library it references as a package. A library in
  the same solution, referenced with `ProjectReference`, cannot be overridden
  yet (`RTR0104`).
- On the web, discovery needs `node_modules`: Yarn Plug'n'Play is not supported
  by `dependencies: "auto"`, and a catalog library that the application reaches
  only through a package without `runicTranslations` is not found; add it to the
  application's dependencies.
- After changing an installed package, restart the Vite development server;
  Vite does not watch `node_modules`.
