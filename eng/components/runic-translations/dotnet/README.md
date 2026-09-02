# Runic Translations for .NET

Generate strongly typed localization APIs for .NET 10 applications and keep translation parsing out of runtime code. The .NET packages cover compiled runtime snapshots, C# source generation, deterministic MSBuild artifacts, custom compiler integrations, authoring tools, a local CLI, and project templates.

## Start a complete project

```bash
dotnet new install Runic.Translations.Templates::<VERSION>
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
using Runic.Translations;

ITranslationManager manager = await AppTextCatalog.CreateManagerAsync();
var text = new AppText(manager);

Console.WriteLine(text.application_title);
```

The template pins the runtime, build integration, and local tool to one version. It also enables ESM generation for web consumers.

## Package selection

| Package | Role | Typical consumer |
|---|---|---|
| [`Runic.Translations`](https://www.nuget.org/packages/Runic.Translations) | NativeAOT-compatible runtime, immutable snapshots, formatting, fallback, and locale switching | .NET applications |
| [`Runic.Translations.Build`](https://www.nuget.org/packages/Runic.Translations.Build) | Incremental generator, MSBuild items, and opt-in non-C# artifact generation | C# projects and projects sharing catalogs with web or asset pipelines |
| [`dotnet-runic-translations`](https://www.nuget.org/packages/dotnet-runic-translations) | Local `runic-translations` CLI | Developers and CI |
| [`Runic.Translations.Templates`](https://www.nuget.org/packages/Runic.Translations.Templates) | Catalog item and standalone class-library templates | New integrations |
| [`Runic.Translations.Tooling`](https://www.nuget.org/packages/Runic.Translations.Tooling) | MF2 compiler facade, interchange, and transactional authoring | Editors and build tooling |

Most applications install the runtime and build packages together. Replace `<VERSION>` with one exact public preview version in every command:

```bash
dotnet add package Runic.Translations --version <VERSION>
dotnet add package Runic.Translations.Build --version <VERSION>
dotnet new tool-manifest
dotnet tool install dotnet-runic-translations --version <VERSION>
```

Keep all packages and the local tool on the same exact version. Tool builders use `Runic.Translations.Tooling`; ordinary localized applications do not need it.

## Compatibility and status

All packages are publicly available as previews and currently target .NET 10. The generator requires a .NET 10 Roslyn host. Runtime and generated paths are compatible with NativeAOT and have no dependency on Runic Toolkit, CS-WebUI, or another UI framework.

Preview releases may make documented breaking changes. Serialized schemas and generated ABIs are versioned independently from package SemVer; use one release across the package family and regenerate outputs during upgrades. See [compatibility and versioning](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/compatibility.md).

## Documentation, examples, and support

- [Repository quick start](https://github.com/Runic-Artifex/runic-translations#readme)
- [Vite quick start](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/quickstart-vite.md)
- [Package-only generated C# example](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tests/Runic.Translations.PackageTests)
- [NativeAOT example](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tests/Runic.Translations.AotTests)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

The implementation is organized under [`src/`](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/src), [`tools/`](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tools), [`templates/`](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/templates), and [`tests/`](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tests).

Runic Translations is licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE). Third-party notices are available [in the repository](https://github.com/Runic-Artifex/runic-translations/blob/main/THIRD-PARTY-NOTICES.md).
