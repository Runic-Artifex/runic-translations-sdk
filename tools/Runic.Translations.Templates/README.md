# Runic.Translations.Templates

Start a compiler-valid Runic Translations RMF2 project or a complete .NET 10 localization class library.

## Install

```bash
dotnet new install Runic.Translations.Templates@<VERSION>
```

Replace `<VERSION>` with the preview version shown on NuGet. The standalone project targets .NET 10 and pins the Runic Translations runtime, build package, and local tool to that exact release.

## Create a complete project

```bash
dotnet new runic-translations-project \
  --name Example.Translations \
  --catalog app \
  --default-locale en \
  --class-name AppText
cd Example.Translations
dotnet tool restore
dotnet build
```

The project template creates a class library with `translations/runic.json`, a
default-locale grouped RMF2 resource, generated C# APIs and a pinned local tool
manifest. The generated namespace follows `--name` unless you pass `--namespace`.
After building, application code can use the generated catalog:

```csharp
using Example.Translations;
using Runic.Translations;

ITranslationManager manager = await AppTextCatalog.CreateManagerAsync();
var text = new AppText(manager);

Console.WriteLine(text.Messages.application_title);
```

`text.Messages.application_title` is the readable accessor for
`application.title`, named by its flattened key as in ESM
(`m.application_title()`). The generated class also keeps the encoded member
`text.r_6170706c69636174696f6e_r_7469746c65` (each path segment as `r_` plus
UTF-8 hexadecimal bytes), the stable machine-facing contract. Follow the
[.NET consumer quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-dotnet.md)
to run the generated library from a console application and restore it on CI.

## Options

| Option | Default | Meaning |
| --- | --- | --- |
| `--catalog` | `product` | Portable catalog identifier. |
| `--default-locale` | `en` | Base locale tag. |
| `--locales` | none | Additional locale tags, comma-separated, such as `de,fr`. |
| `--namespace` | the name | Namespace of the generated C# API. |
| `--class-name` | `ProductText` | Class name of the generated C# API. |
| `--package-version` | the template's | Runic Translations package and tool version (project template). |
| `--emit-esm` | `false` | Also emit ESM output on build (project template). |

`--locales` declares the locales in `runic.json` and sets
`validation.translationCompleteness` to `warning`. The templates create only the
default-locale file: add `translations/<locale>.rmf2` for each additional
locale. Until a message is translated, the build warns (`RTR0010`) and the
runtime falls back to the base locale. Restore `error` once the translations are
complete.

A .NET consumer needs only the C# that the source generator produces, so the
project template emits no ESM by default. Pass `--emit-esm` when a web front end
consumes this project's generated `web-module-manifest-v3.json` through the Vite
plugin's manifest mode; the build then runs the pinned local tool. A web app that
owns its own translations uses the plugin's project mode instead and needs no
build output from this project.

The camelCase options of earlier previews (`--defaultLocale`, `--className`,
`--packageVersion`) still work as hidden aliases; prefer the kebab-case names,
which match the other Runic templates.

## Add catalog files to an existing project

```bash
dotnet new runic-translations \
  --output . \
  --catalog app \
  --default-locale en \
  --namespace Example.Translations \
  --class-name AppText
```

The item template creates `translations/runic.json` and a default-locale RMF2 file such as `translations/en.rmf2`. RMF2 groups preserve resource namespaces directly. Add the matching `Runic.Translations` and `Runic.Translations.Build` packages; the build discovers the conventional directory without MSBuild items.

Choose the project template for a complete .NET setup, with ESM on request. Choose the item template when a project already owns package versions and build configuration. Use `runic-translations init` when you need multiple locales, explicit fallback edges, or optional starter content in one command.

## RMF2 output

All templates and `runic-translations init` create RMF2 resources. The templates
produce typed v5 C# and, with `--emit-esm`, ESM ABI 4 output. The canonical item and project
templates create structured message paths; use `runic-translations init` when
you need a more customized source layout.

## Compatibility and status

Template output uses the package version embedded at packing time. If you pass `--package-version`, it must identify one matching release of the runtime, build package, and tool. Preview upgrades may change generated project files or source schemas; review the [RMF2 guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md) before updating an existing project.

- [Project template source](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/tools/Runic.Translations.Templates/templates/project)
- [Item template source](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/tools/Runic.Translations.Templates/templates/item)
- [.NET package guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/packages/dotnet/Runic.Translations/README.md)
- [Vite quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-vite.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/LICENSE).
