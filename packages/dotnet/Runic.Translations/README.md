# Runic.Translations

Run compiled translations in .NET applications with immutable locale snapshots, typed formatting, fallback, and atomic locale switching. The runtime is UI-framework independent, reflection-free, and compatible with NativeAOT.

## Install

```bash
dotnet add package Runic.Translations --version <VERSION>
```

Replace `<VERSION>` with the current preview shown on NuGet. The package targets .NET 10. For generated catalogs, install `Runic.Translations.Build` at the same exact version; it owns the generator, catalog item mapping, and optional non-C# artifacts.

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

## When to choose this package

Choose `Runic.Translations` for application runtime behavior: generated catalog data, locale resolution, formatting, structured content, translation references, and optional verified external packs. Choose `Runic.Translations.Tooling` when building an editor, compiler host, or workspace tool.

External packs are untrusted until their artifact version, catalog, locale, contract fingerprint, keys, argument contracts, and limits have been verified. Applications that require authenticity must also supply an integrity verifier; schema validation alone does not establish provenance.

Generated RMF2 catalogs use v5 pack factories and strict version/profile
dispatch. A matching fingerprint demonstrates generated-contract compatibility;
it is not a signature or authenticity proof. Unsupported artifact, grammar,
profile, and ABI versions fail explicitly.

## Compatibility and status

This package is a public preview for .NET 10. Preview releases may contain documented breaking API changes. Keep the runtime and generated source on the same Runic Translations release; runtime ABI mismatches fail explicitly.

Generated code and the retained pre-v5 message model use public runtime plumbing such as the `CompiledRmf2*` message model, `TranslationsCompatibility`, `CompiledTextMessage.Rmf2V5` and `TextArgumentFormat.Fixed0`–`Fixed6`/`Percent0`–`Percent4`. These are marked `[EditorBrowsable(Never)]`, so they stay out of completion lists; they are not application API.

- [Runtime and generated C# example](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/tests/dotnet/Runic.Translations.PackageTests/Program.cs)
- [NativeAOT example](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/tests/dotnet/Runic.Translations.AotTests)
- [RMF2 project guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md)
- [External translation pack contract](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/specs/translations/wave-b/external-packs.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/specs/translations/THIRD-PARTY-NOTICES.md) for bundled data attribution.
