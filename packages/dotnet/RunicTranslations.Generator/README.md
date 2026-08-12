# RunicTranslations.Generator

Turn validated translation catalogs into strongly typed C# keys, accessors, compiled locale data, and reflection-free registration during compilation. Generated code is compatible with NativeAOT and adds no runtime parser.

## Install

```bash
dotnet add package RunicTranslations.Generator --version <VERSION>
```

Replace `<VERSION>` with the current preview shown on NuGet. The analyzer package and its `RunicTranslations` runtime dependency must use the same exact version. It requires a .NET 10 Roslyn host and is intended for C# projects.

## Add translation inputs

The easiest setup is to install `RunicTranslations.Build`, which maps its item types to the generator. Without that package, classify Roslyn `AdditionalFiles` explicitly:

```xml
<ItemGroup>
  <AdditionalFiles Include="Resources/app.catalog.json"
                   RunicTranslationKind="Catalog" />
  <AdditionalFiles Include="Resources/app.en.json"
                   RunicTranslationKind="Document" />
</ItemGroup>
```

For a catalog with `code.namespace` set to `Example.Translations` and `code.className` set to `AppText`:

```csharp
using Example.Translations;
using RunicTranslations;

ITranslationManager manager = await AppTextCatalog.CreateManagerAsync();
var text = new AppText(manager);

Console.WriteLine(text.Application.Name);
```

The generator reports compiler diagnostics at source locations and writes no files. C# becomes part of the current compilation; JSON, TypeScript, ESM, template manifests, and experimental C++ output belong to the build or CLI surfaces.

## When to choose this package

Choose the generator for typed C# application APIs. Choose `RunicTranslations.Build` alongside it when you want concise `TranslationCatalog` and `TranslationDocument` items or generated web assets. Choose `RunicTranslations.Compiler` for direct programmatic compilation rather than build-time source generation.

## Compatibility and status

This package is a public preview. It requires a .NET 10 compiler host and emits code against the matching runtime ABI; an incompatible runtime fails explicitly. Keep the generator, runtime, build package, and local tool on the same release.

- [Generated package consumer](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tests/RunicTranslations.PackageTests)
- [Generator tests and examples](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tests/RunicTranslations.Generator.Tests)
- [Schema-v2 guide](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/schema-v2.md)
- [Compatibility policy](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/compatibility.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations/blob/main/THIRD-PARTY-NOTICES.md) for bundled data attribution.
