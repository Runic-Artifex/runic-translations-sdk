# RunicTranslations.Templates

Start a compiler-valid Runic Translations catalog or a complete .NET 10 localization class library without hand-writing package references, MSBuild items, or the local tool manifest.

## Install

```bash
dotnet new install RunicTranslations.Templates::<VERSION>
```

Replace `<VERSION>` with the preview version shown on NuGet. The standalone project targets .NET 10 and pins the Runic Translations runtime, generator, build package, and local tool to that exact release.

## Create a complete project

```bash
dotnet new runic-translations-project \
  --name Example.Translations \
  --catalog app \
  --defaultLocale en \
  --namespace Example.Translations \
  --className AppText
cd Example.Translations
dotnet tool restore
dotnet build
```

The project template creates a class library with schema-v2 resources, generated C# APIs, a pinned local tool manifest, and build-time ESM output. After building, application code can use the generated catalog:

```csharp
using Example.Translations;
using RunicTranslations;

ITranslationManager manager = await AppTextCatalog.CreateManagerAsync();
var text = new AppText(manager);

Console.WriteLine(text.Application.Name);
```

## Add catalog files to an existing project

```bash
dotnet new runic-translations \
  --output Resources \
  --catalog app \
  --defaultLocale en \
  --namespace Example.Translations \
  --className AppText
```

The item template creates only the catalog and default-locale document. Add the matching `RunicTranslations`, `RunicTranslations.Generator`, and `RunicTranslations.Build` packages plus `TranslationCatalog` and `TranslationDocument` items to the containing project.

Choose the project template for the fastest complete .NET and ESM setup. Choose the item template when a project already owns package versions and build configuration. Use `runic-translations init` when you need multiple locales, explicit fallback edges, optional starter content, or VS Code schema associations in one command.

## Compatibility and status

Template output uses the package version embedded at packing time. If you pass `--packageVersion`, it must identify one matching release of the runtime, generator, build package, and tool. Preview upgrades may change generated project files or source schemas; review the [compatibility policy](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/compatibility.md) before updating an existing project.

- [Project template source](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/templates/RunicTranslations.Templates/templates/project)
- [Item template source](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/templates/RunicTranslations.Templates/templates/item)
- [.NET package guide](https://github.com/Runic-Artifex/runic-translations/blob/main/dotnet/README.md)
- [Vite quick start](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/quickstart-vite.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE).
