# Runic.Translations.Templates

Start a compiler-valid Runic Translations RMF2 project or a complete .NET 10 localization class library.

## Install

```bash
dotnet new install Runic.Translations.Templates::<VERSION>
```

Replace `<VERSION>` with the preview version shown on NuGet. The standalone project targets .NET 10 and pins the Runic Translations runtime, build package, and local tool to that exact release.

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

The project template creates a class library with `translations/runic.json` with `sourceLayout: "rmf2-v1"`, a default-locale RMF2 resource, generated C# APIs, a pinned local tool manifest, and build-time ESM output. After building, application code can use the generated catalog:

```csharp
using Example.Translations;
using Runic.Translations;

ITranslationManager manager = await AppTextCatalog.CreateManagerAsync();
var text = new AppText(manager);

Console.WriteLine(text.application_title);
```

## Add catalog files to an existing project

```bash
dotnet new runic-translations \
  --output . \
  --catalog app \
  --defaultLocale en \
  --namespace Example.Translations \
  --className AppText
```

The item template creates `translations/runic.json` and a default-locale RMF2 file such as `translations/en.rmf2`. RMF2 groups preserve resource namespaces directly. Add the matching `Runic.Translations` and `Runic.Translations.Build` packages; the build discovers the conventional directory without MSBuild items.

Choose the project template for a complete .NET and ESM setup. Choose the item template when a project already owns package versions and build configuration. Use `runic-translations init` when you need multiple locales, explicit fallback edges, or optional starter content in one command.

## Opt into RMF2

The templates and `runic-translations init` create RMF2 resources by default.
Use the RMF2 variants when their additional mounted-resource examples are useful:

```bash
dotnet new runic-translations-rmf2 --output . --catalog app --defaultLocale en --namespace Example.Translations --className AppText
dotnet new runic-translations-project-rmf2 --name Example.Translations --output Example.Translations --packageVersion <VERSION>
```

Both RMF2 templates select `sourceLayout: "rmf2-v1"` together with
`executionProfile: "rmf2-execution-v2"`, producing typed v5 C# and ESM ABI 4
output. The standalone tool also offers `runic-translations init-rmf2`, or
`runic-translations init ... --layout rmf2-v1`, for compatibility scaffolding;
those commands omit the selector and therefore retain the v1 execution / artifact
v4 contract. Add the execution profile to those generated projects when v5
semantics are required. Invalid layout values fail with exit code 2.

## Compatibility and status

Template output uses the package version embedded at packing time. If you pass `--packageVersion`, it must identify one matching release of the runtime, build package, and tool. Preview upgrades may change generated project files or source schemas; review the [RMF2 guide](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/rmf2.md) before updating an existing project.

- [Project template source](https://github.com/Runic-Artifex/runic-sdk/tree/main/tools/Runic.Translations.Templates/templates/project)
- [Item template source](https://github.com/Runic-Artifex/runic-sdk/tree/main/tools/Runic.Translations.Templates/templates/item)
- [.NET package guide](https://github.com/Runic-Artifex/runic-sdk/blob/main/packages/dotnet/Runic.Translations/README.md)
- [Vite quick start](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/quickstart-vite.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-sdk/blob/main/LICENSE).
