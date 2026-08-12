![Runic Translations banner](.github/assets/brand/banner.png)

# Runic Translations

Build localization once and consume it as strongly typed C# or tree-shakable ESM. Runic Translations validates catalogs ahead of time, generates runtime-ready code and artifacts, and keeps message parsing out of your application startup and rendering paths.

It is UI-framework independent, works with NativeAOT, and can feed .NET applications, Vite applications, and SvelteKit projects that provide their own locale routing.

## From catalog to generated code

The project template is the shortest complete path through the toolchain:

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

This creates a schema-v2 catalog and locale document under `Resources/`. The source generator adds typed C# APIs to the compilation, while the build integration writes ESM and other selected artifacts beneath `obj/`.

```csharp
using Example.Translations;
using RunicTranslations;

ITranslationManager manager = await AppTextCatalog.CreateManagerAsync();
var text = new AppText(manager);

Console.WriteLine(text.Application.Name);
await manager.SetLocaleAsync("de");
```

With ESM generation enabled, the same catalog is available to Vite through the optional adapter:

```ts
import { m } from "virtual:runic-translations/app";

document.querySelector("#app")!.textContent = m["Application.Name"]();
```

See the [Vite quick start](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/quickstart-vite.md) for tool pinning, Vite configuration, locale documents, and CI verification.

## Choose a package

Runic Translations is currently a public preview. Replace `<VERSION>` below with the current preview shown on NuGet or npm, and keep the NuGet package family, local tool, generated artifacts, and Vite adapter on that same exact release.

| Package | Install | Choose it when you need |
|---|---|---|
| [`RunicTranslations`](https://www.nuget.org/packages/RunicTranslations) | `dotnet add package RunicTranslations --version <VERSION>` | NativeAOT-compatible runtime snapshots, formatting, fallback, and locale switching |
| [`RunicTranslations.Generator`](https://www.nuget.org/packages/RunicTranslations.Generator) | `dotnet add package RunicTranslations.Generator --version <VERSION>` | Strongly typed C# keys, accessors, catalog data, and registration |
| [`RunicTranslations.Build`](https://www.nuget.org/packages/RunicTranslations.Build) | `dotnet add package RunicTranslations.Build --version <VERSION>` | MSBuild input mapping and opt-in JSON, TypeScript, ESM, template, or C++ artifacts |
| [`RunicTranslations.Tool`](https://www.nuget.org/packages/RunicTranslations.Tool) | `dotnet tool install RunicTranslations.Tool --version <VERSION>` | Local initialization, validation, generation, verification, import, schema, and analysis commands |
| [`RunicTranslations.Templates`](https://www.nuget.org/packages/RunicTranslations.Templates) | `dotnet new install RunicTranslations.Templates::<VERSION>` | Ready-to-build .NET project or catalog item scaffolding |
| [`RunicTranslations.Compiler`](https://www.nuget.org/packages/RunicTranslations.Compiler) | `dotnet add package RunicTranslations.Compiler --version <VERSION>` | Direct compiler and renderer integration in custom tooling |
| [`RunicTranslations.Authoring`](https://www.nuget.org/packages/RunicTranslations.Authoring) | `dotnet add package RunicTranslations.Authoring --version <VERSION>` | Safe workspace discovery, project creation, mutation, and editor state |
| [`@runic-artifex/vite-plugin-runic-translations`](https://www.npmjs.com/package/@runic-artifex/vite-plugin-runic-translations) | `npm install --save-dev @runic-artifex/vite-plugin-runic-translations@<VERSION>` | Vite virtual modules, watch integration, and HMR over generated ESM |

Most .NET applications use the runtime, generator, and build packages together. Vite applications additionally use the local tool and Vite adapter. Choose the compiler or authoring packages only when building tooling rather than consuming generated translations.

## Compatibility and safety

- The current packages target .NET 10; the generator requires a .NET 10 Roslyn host.
- The runtime and generated C# are reflection-free and compatible with NativeAOT.
- Preview releases may contain documented breaking changes. Package SemVer is separate from the versioned source schemas, generated ABIs, artifacts, and transport contracts.
- For SSR, pass the locale explicitly for each ESM message call. A request-global locale resolver is not safe for concurrent requests.
- Runtime-loaded external packs are optional and are accepted only after their catalog, locale, fingerprint, key, argument, size, and caller-provided integrity contracts pass validation.

Read the [compatibility policy](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/compatibility.md), [capability matrix](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/capabilities.md), and [schema-v2 guide](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/schema-v2.md) before coordinating upgrades or relying on preview locale coverage.

## Documentation and support

- [.NET package guide](https://github.com/Runic-Artifex/runic-translations/blob/main/dotnet/README.md)
- [ESM backend](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/esm.md)
- [Catalog analysis](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/analysis.md)
- [JSON and inlang importing](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/importing.md)
- [VS Code schema setup](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/vscode.md)
- [Translation reference transport](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/transport.md)
- [Runic Translations Editor](https://github.com/Runic-Artifex/runic-translations-editor)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

## Development

Enter the Nix development shell and run the repository verification pipeline:

```bash
nix develop
./eng/verify.sh
```

The pipeline tests the .NET packages, packed tool and templates, NativeAOT consumer, generated C# and ESM, npm package, type declarations, and production tree-shaking build.

## License

Runic Translations is licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE). Third-party components retain their own terms; see [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations/blob/main/THIRD-PARTY-NOTICES.md).
