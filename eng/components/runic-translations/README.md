![Runic Translations banner](.github/assets/brand/banner.png)

# Runic Translations

Build localization once and consume it as strongly typed C# or tree-shakable ESM. Runic Translations validates catalogs ahead of time, generates runtime-ready code and artifacts, and keeps message parsing out of your application startup and rendering paths.

It is UI-framework independent, works with NativeAOT, and can feed .NET applications, Vite applications, and SvelteKit projects that provide their own locale routing.

## From MF2 to generated code

New projects use one conventional `translations/` directory. `runic.json`
declares the project; each locale directory contains normal MessageFormat 2
files whose filenames become message identifiers:

```text
translations/
├── runic.json
├── en/application_title.mf2
└── de/application_title.mf2
```

`Runic.Translations.Build` discovers that directory automatically. The source
generator adds typed C# APIs to the compilation, while the build integration
writes ESM and other selected artifacts beneath `obj/`.

```csharp
using Example.Translations;
using Runic.Translations;

ITranslationManager manager = await AppTextCatalog.CreateManagerAsync();
var text = new AppText(manager);

Console.WriteLine(text.application_title);
await manager.SetLocaleAsync("de");
```

With ESM generation enabled, the same catalog is available to Vite through the optional adapter:

```ts
import { m } from "virtual:runic-translations/app";

document.querySelector("#app")!.textContent = m.application_title();
```

See [MF2 projects](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/mf2-projects.md)
and the [Vite quick start](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/quickstart-vite.md).

## Choose a package

Runic Translations is currently a public preview. Replace `<VERSION>` below with the current preview shown on NuGet or npm, and keep the NuGet package family, local tool, generated artifacts, and Vite adapter on that same exact release.

| Package | Install | Choose it when you need |
|---|---|---|
| [`Runic.Translations`](https://www.nuget.org/packages/Runic.Translations) | `dotnet add package Runic.Translations --version <VERSION>` | NativeAOT-compatible runtime snapshots, formatting, fallback, and locale switching |
| [`Runic.Translations.Build`](https://www.nuget.org/packages/Runic.Translations.Build) | `dotnet add package Runic.Translations.Build --version <VERSION>` | Strongly typed C# APIs, MSBuild input mapping, and opt-in JSON, TypeScript, ESM, template, or C++ artifacts |
| [`dotnet-runic-translations`](https://www.nuget.org/packages/dotnet-runic-translations) | `dotnet tool install dotnet-runic-translations --version <VERSION>` | Local MF2 project initialization, validation, generation, verification, and schemas |
| [`Runic.Translations.Templates`](https://www.nuget.org/packages/Runic.Translations.Templates) | `dotnet new install Runic.Translations.Templates::<VERSION>` | Ready-to-build .NET or MF2 project scaffolding |
| [`Runic.Translations.Tooling`](https://www.nuget.org/packages/Runic.Translations.Tooling) | `dotnet add package Runic.Translations.Tooling --version <VERSION>` | Compiler integration, XLIFF interchange, and transactional MF2 authoring |
| [`@runic-artifex/vite-plugin-runic-translations`](https://www.npmjs.com/package/@runic-artifex/vite-plugin-runic-translations) | `npm install --save-dev @runic-artifex/vite-plugin-runic-translations@<VERSION>` | Vite virtual modules, watch integration, and HMR over generated ESM |

Most .NET applications use the runtime and build packages together. Vite applications additionally use the local tool and Vite adapter. Choose Tooling only when building tooling rather than consuming generated translations.

## Compatibility and safety

- The current packages target .NET 10; the generator requires a .NET 10 Roslyn host.
- The runtime and generated C# are reflection-free and compatible with NativeAOT.
- Preview releases may contain documented breaking changes. Package SemVer is separate from the versioned source schemas, generated ABIs, artifacts, and transport contracts.
- For SSR, run rendering in the generated `/server` request context. Explicit locale overrides remain available for calls that intentionally format a different locale.
- Runtime-loaded external packs are optional and are accepted only after their catalog, locale, fingerprint, key, argument, size, and caller-provided integrity contracts pass validation.

Read the [compatibility policy](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/compatibility.md), [capability matrix](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/capabilities.md), and [MF2 project guide](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/mf2-projects.md) before coordinating upgrades or relying on preview locale coverage.

## Documentation and support

- [.NET package guide](https://github.com/Runic-Artifex/runic-translations/blob/main/dotnet/README.md)
- [MF2 project convention](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/mf2-projects.md)
- [ESM backend](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/esm.md)
- [Translation reference transport](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/transport.md)
- [Runic Desktop integration](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/runic-desktop.md)
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
