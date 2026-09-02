# Runic.Translations.Generator

This internal analyzer turns a validated MF2 project into strongly typed C# keys, accessors, compiled locale data, and reflection-free registration during compilation. It ships as part of `Runic.Translations.Build`, not as a separately versioned package.

## Install

Install `Runic.Translations.Build` at the same version as `Runic.Translations`. It requires a .NET 10 Roslyn host and is intended for C# projects.

## Add translation inputs

The easiest setup is to install `Runic.Translations.Build`, which maps its item types to the generator. Without that package, classify Roslyn `AdditionalFiles` explicitly:

```xml
<ItemGroup>
  <AdditionalFiles Include="translations/runic.json"
                   RunicTranslationKind="Project" />
  <AdditionalFiles Include="translations/**/*.mf2"
                   RunicTranslationKind="Mf2" />
</ItemGroup>
```

For a catalog with `code.namespace` set to `Example.Translations` and `code.className` set to `AppText`:

```csharp
using Example.Translations;
using Runic.Translations;

ITranslationManager manager = await AppTextCatalog.CreateManagerAsync();
var text = new AppText(manager);

Console.WriteLine(text.Application.Name);
```

The generator reports compiler diagnostics at source locations and writes no files. C# becomes part of the current compilation; JSON, TypeScript, ESM, template manifests, and experimental C++ output belong to the build or CLI surfaces.

## When to choose this package

Choose `Runic.Translations.Build` for typed C# application APIs and generated web assets. Choose `Runic.Translations.Tooling` for direct programmatic compilation.

## Compatibility and status

The containing Build package is a public preview. It requires a .NET 10 compiler host and emits code against the matching runtime ABI; an incompatible runtime fails explicitly.

- [Generated package consumer](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tests/Runic.Translations.PackageTests)
- [Generator tests and examples](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tests/Runic.Translations.Generator.Tests)
- [MF2 project guide](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/mf2-projects.md)
- [Compatibility policy](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/compatibility.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations/blob/main/THIRD-PARTY-NOTICES.md) for bundled data attribution.
