# Runic.Translations

Run compiled translations in .NET applications with immutable locale snapshots, typed formatting, fallback, and atomic locale switching. The runtime is UI-framework independent, reflection-free, and compatible with NativeAOT.

## Install

```bash
dotnet add package Runic.Translations --version <VERSION>
```

Replace `<VERSION>` with the current preview shown on NuGet. The package targets .NET 10. For generated catalogs, install `Runic.Translations.Build` at the same exact version; it owns the generator, catalog item mapping, and optional non-C# artifacts.

## Use a generated catalog

Given the project template's starter catalog with `code.className` set to `AppText`, the generator creates registration and typed accessors:

```csharp
using Example.Translations;
using Runic.Translations;

ITranslationManager manager = await AppTextCatalog.CreateManagerAsync(
    initialLocale: "en");
var text = new AppText(manager);

Console.WriteLine(text.Application.Name);

await manager.SetLocaleAsync("de");
Console.WriteLine(text.Application.Name);
```

Each successful locale change replaces the complete immutable snapshot. Reads through `manager.Current` do not observe a partially updated catalog.

## When to choose this package

Choose `Runic.Translations` for application runtime behavior: generated catalog data, locale resolution, formatting, structured content, translation references, and optional verified external packs. Choose `Runic.Translations.Tooling` when building an editor, compiler host, or workspace tool.

External packs are untrusted until their artifact version, catalog, locale, contract fingerprint, keys, argument contracts, and limits have been verified. Applications that require authenticity must also supply an integrity verifier; schema validation alone does not establish provenance.

## Compatibility and status

This package is a public preview for .NET 10. Preview releases may contain documented breaking API changes. Keep the runtime and generated source on the same Runic Translations release; runtime ABI mismatches fail explicitly.

- [Runtime and generated C# example](https://github.com/Runic-Artifex/runic-translations/blob/main/dotnet/tests/Runic.Translations.PackageTests/Program.cs)
- [NativeAOT example](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tests/Runic.Translations.AotTests)
- [Compatibility policy](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/compatibility.md)
- [External translation pack contract](https://github.com/Runic-Artifex/runic-translations/blob/main/spec/README.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations/blob/main/THIRD-PARTY-NOTICES.md) for bundled data attribution.
