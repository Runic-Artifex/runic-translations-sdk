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

The project template creates a class library with `translations/runic.json`, a
default-locale grouped RMF2 resource, generated C# APIs, a pinned local tool
manifest, and build-time ESM output. After building, application code can use the
generated catalog:

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

## RMF2 output

All templates and `runic-translations init` create RMF2 resources. The templates
select the canonical RMF2 execution profile and produce typed v5 C# and ESM ABI
4 output. The standalone `init` scaffold currently selects the RMF2 source
layout only, so it retains the existing v4 execution and artifact contract. The
standard templates create structured message paths; the `-rmf2` template
identities remain available for existing projects that use flat message IDs.
Both use the same RMF2 source layout and execution profile.

## Compatibility and status

Template output uses the package version embedded at packing time. If you pass `--packageVersion`, it must identify one matching release of the runtime, build package, and tool. Preview upgrades may change generated project files or source schemas; review the [RMF2 guide](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/rmf2.md) before updating an existing project.

- [Project template source](https://github.com/Runic-Artifex/runic-sdk/tree/main/tools/Runic.Translations.Templates/templates/project)
- [Item template source](https://github.com/Runic-Artifex/runic-sdk/tree/main/tools/Runic.Translations.Templates/templates/item)
- [.NET package guide](https://github.com/Runic-Artifex/runic-sdk/blob/main/packages/dotnet/Runic.Translations/README.md)
- [Vite quick start](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/quickstart-vite.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-sdk/blob/main/LICENSE).
