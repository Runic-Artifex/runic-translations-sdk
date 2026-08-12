# Runic Translations for .NET

Generate strongly typed localization APIs for .NET 10 applications and keep translation parsing out of runtime code. The .NET packages cover compiled runtime snapshots, C# source generation, deterministic MSBuild artifacts, custom compiler integrations, authoring tools, a local CLI, and project templates.

## Start a complete project

```bash
dotnet new install RunicTranslations.Templates::<VERSION>
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

The generated C# registration and accessors are available immediately:

```csharp
using Example.Translations;
using RunicTranslations;

ITranslationManager manager = await AppTextCatalog.CreateManagerAsync();
var text = new AppText(manager);

Console.WriteLine(text.Application.Name);
```

The template pins the runtime, generator, build integration, and local tool to one version. It also enables ESM generation for web consumers.

## Package selection

| Package | Role | Typical consumer |
|---|---|---|
| [`RunicTranslations`](https://www.nuget.org/packages/RunicTranslations) | NativeAOT-compatible runtime, immutable snapshots, formatting, fallback, and locale switching | .NET applications |
| [`RunicTranslations.Generator`](https://www.nuget.org/packages/RunicTranslations.Generator) | Incremental generator for typed keys, accessors, catalog data, and registration | C# projects |
| [`RunicTranslations.Build`](https://www.nuget.org/packages/RunicTranslations.Build) | MSBuild items and opt-in non-C# artifact generation | Projects sharing catalogs with web or asset pipelines |
| [`RunicTranslations.Tool`](https://www.nuget.org/packages/RunicTranslations.Tool) | Local `runic-translations` CLI | Developers and CI |
| [`RunicTranslations.Templates`](https://www.nuget.org/packages/RunicTranslations.Templates) | Catalog item and standalone class-library templates | New integrations |
| [`RunicTranslations.Compiler`](https://www.nuget.org/packages/RunicTranslations.Compiler) | Compiler kernel, diagnostics, IR, renderers, and analysis APIs | Tool builders |
| [`RunicTranslations.Authoring`](https://www.nuget.org/packages/RunicTranslations.Authoring) | Workspace discovery and transactional authoring operations | Editors and authoring tools |

Most applications install the runtime, generator, and build packages together. Replace `<VERSION>` with one exact public preview version in every command:

```bash
dotnet add package RunicTranslations --version <VERSION>
dotnet add package RunicTranslations.Generator --version <VERSION>
dotnet add package RunicTranslations.Build --version <VERSION>
dotnet new tool-manifest
dotnet tool install RunicTranslations.Tool --version <VERSION>
```

Keep all packages and the local tool on the same exact version. `RunicTranslations.Compiler` and `RunicTranslations.Authoring` are for applications that directly host compilation or edit translation workspaces; ordinary localized applications do not need them.

## Compatibility and status

All packages are publicly available as previews and currently target .NET 10. The generator requires a .NET 10 Roslyn host. Runtime and generated paths are compatible with NativeAOT and have no dependency on Runic Toolkit, CS-WebUI, or another UI framework.

Preview releases may make documented breaking changes. Serialized schemas and generated ABIs are versioned independently from package SemVer; use one release across the package family and regenerate outputs during upgrades. See [compatibility and versioning](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/compatibility.md).

## Documentation, examples, and support

- [Repository quick start](https://github.com/Runic-Artifex/runic-translations#readme)
- [Vite quick start](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/quickstart-vite.md)
- [Package-only generated C# example](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tests/RunicTranslations.PackageTests)
- [NativeAOT example](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tests/RunicTranslations.AotTests)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

The implementation is organized under [`src/`](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/src), [`tools/`](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tools), [`templates/`](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/templates), and [`tests/`](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tests).

Runic Translations is licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE). Third-party notices are available [in the repository](https://github.com/Runic-Artifex/runic-translations/blob/main/THIRD-PARTY-NOTICES.md).
