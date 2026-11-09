# Runic.Translations

Run compiled translations in .NET applications with immutable locale snapshots, typed formatting, fallback, and atomic locale switching. The runtime is UI-framework independent, reflection-free, and compatible with NativeAOT.

## Install

```bash
dotnet add package Runic.Translations --version <VERSION>
```

Replace `<VERSION>` with the current version shown on NuGet. The package targets .NET 10. For generated catalogs, install `Runic.Translations.Build` at the same exact version; it owns the generator, catalog item mapping, and optional non-C# artifacts.

## Use a generated catalog

Given the project template's starter catalog with `code.className` set to `AppText`, the generator creates registration and typed accessors. `text.Messages` is the readable surface and names each message by its flattened key, so the starter message `application.title` is `application_title`. The encoded `r_<hex>` members stay available as the stable machine-facing contract. Each encoded segment is `r_` followed by the hexadecimal UTF-8 bytes of its NFC-normalized text, and the encoded segments are joined with `_`.

```csharp
using Example.Translations;
using Runic.Translations;

ITranslationManager manager = await AppTextCatalog.CreateManagerAsync(
    initialLocale: "en");
var text = new AppText(manager);

Console.WriteLine(text.Messages.application_title);

await manager.SetLocaleAsync("de");
Console.WriteLine(text.Messages.application_title);
```

Each successful locale change replaces the complete immutable snapshot. Reads through `manager.Current` do not observe a partially updated catalog.

To start in the user's language, pass an ordered preference list, such as the operating system's user languages. The generated `SupportedLocales` lists the declared locales, and `ResolveLocale(IEnumerable<string>)` chooses one with the pinned CLDR likely subtags and parent locales: `zh-TW` matches a declared `zh-Hant`, `de-CH` matches `de-DE`, and `nb` matches `no`. `CreateManagerFromPreferencesAsync`, `CreateManagerFromPreferences` and `ITranslationManager.SetLocaleFromPreferencesAsync` take the same list (the one-tag `CreateManager(null)` and `SetLocaleAsync` are unchanged), and `LocaleNegotiation.Negotiate` applies the rules to any set of locales. The generated ESM runtime's `negotiateLocale` gives the same answer. A single requested tag that the catalog does not declare is matched by the same rules under the default `ParentsThenDefault` policy, and the snapshot reports how (`LocaleResolution`).

```csharp
ITranslationManager manager = await AppTextCatalog.CreateManagerFromPreferencesAsync(["de-CH", "en"]);
```

## Use catalogs from libraries

A library with its own public catalog takes an `ITranslationContext`, and the application registers every catalog in
one context. `SetLocaleAsync` prepares every catalog's snapshot and publishes them together:

```csharp
TranslationContext translations = await TranslationContext.CreateBuilder()
    .Add(AppTextCatalog.Registration)
    .Add(GridTextCatalog.Registration)          // a library's generated registration
    .AddOverlay(TranslationOverlay.FromPacks("acme.grid", packSource,
        addedLocales: [new TranslationOverlayLocale("fr")]))
    .BuildAsync(initialLocale: "de");

var text = new AppText(translations);           // generated constructor over a context
await translations.SetLocaleAsync("fr");        // every catalog switches together
```

`translations.Catalogs` reports, per catalog, the requested locale, the locale used and whether it was resolved
exactly, through a parent (`es-419` for `es-MX`), to a related locale CLDR likely subtags match (`de-DE` for `de-CH`)
or to the default (`LocaleResolution`); `translations.SupportedLocales` lists the locales every catalog shows, and
`SetLocaleFromPreferencesAsync` chooses among them for a preference list. The latest `SetLocaleAsync` call wins over earlier ones still in flight, and contexts built from one
builder share their catalogs' providers, so a context per window or request is cheap. Overlays are checked key by key,
keep working when the library adds messages, and resolve locale-first. The text class now has a second one-parameter
constructor, so register it with a factory rather than by type in a DI container. See [Catalogs from libraries](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/composition.md) and [Ship translations in a library](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/libraries.md).

## Right-to-left text

`LocaleDirection.Get("ar")` returns `TextDirection.RightToLeft` from pinned CLDR data, the same answer as the ESM
runtime's `localeDirection` and independent of the operating system's culture data. Text formatted in a right-to-left
locale isolates placeholders with Unicode MessageFormat 2's default bidi strategy (`مرحبا {$name}` formats as
`مرحبا \u2068{name}\u2069`), decided per call, so an RTL overlay of an LTR library catalog is isolated too; set
`runtime.bidiIsolation` to `on` or `off` in `runic.json` to choose, and `u:dir` on a placeholder to set its direction.
`BidiText.RemoveIsolates(text)` removes the isolates for plain-text output such as a terminal or a log. See [Right-to-left text](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/bidi.md).

## When to choose this package

Choose `Runic.Translations` for application runtime behavior: generated catalog data, locale resolution, formatting, structured content, translation references, and optional verified external packs. Choose `Runic.Translations.Tooling` when building an editor, compiler host, or workspace tool.

External packs are untrusted until their artifact version, catalog, locale, contract fingerprint, keys, argument contracts, and limits have been verified. Applications that require authenticity must also supply an integrity verifier; schema validation alone does not establish provenance.

Generated RMF2 catalogs use v5 pack factories and strict version/profile
dispatch. A matching fingerprint demonstrates generated-contract compatibility;
it is not a signature or authenticity proof. Unsupported artifact, grammar,
profile, and ABI versions fail explicitly.

## Compatibility and status

This package targets .NET 10. Before 1.0 a minor release may contain breaking changes, each listed with upgrade steps in the release notes (see [Versions before 1.0](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/eng/release/README.md#versions-before-10)). Code generated by release N runs on runtime N or later within the same minor before 1.0 (the same major from 1.0), so a library built with one release works in an application on a later runtime of that minor; a runtime older than the generated code fails explicitly (`RTR0024`). Packed libraries depend on `Runic.Translations` `[N, next minor-0.0)` before 1.0, for example `[0.9.1, 0.10.0-0.0)`, and `[N, next major-0.0)` from 1.0.

Generated code calls the runtime through the `Runic.Translations.Generated` namespace: the `CompiledRmf2*` message model, `TranslationsCompatibility` and `GeneratedCatalogSupport`. These types are binary compatible for code that an earlier release of the same major generated, but they are not application API: their source may change in a minor, and they are marked `[EditorBrowsable(Never)]`. `TextArgumentFormat.Fixed0`–`Fixed6`/`Percent0`–`Percent4` are likewise hidden. Generated readable surfaces implement `IReadableMessages`.

Open and closed sets: the markup bindings (`InlineLinkBinding`, `InlineActionBinding`, `InlineIconBinding`) are a closed set, so a switch over them is exhaustive. `TranslationPackFailureReason`, `TranslationReferenceDiagnosticReason` and the RMF2 document block names are open: handle a value you do not know.

- [Runtime and generated C# example](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/tests/dotnet/Runic.Translations.PackageTests/Program.cs)
- [NativeAOT example](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/tests/dotnet/Runic.Translations.AotTests)
- [RMF2 project guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md)
- [Translate a Runic Views app](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/runic-views.md)
- [Ship translations in a library](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/libraries.md)
- [Ship translation updates without a release](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/ota.md)
- [External translation pack contract](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/specs/translations/wave-b/external-packs.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/specs/translations/THIRD-PARTY-NOTICES.md) for bundled data attribution.
