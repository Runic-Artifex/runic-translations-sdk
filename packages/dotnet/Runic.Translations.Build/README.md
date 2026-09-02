# Runic.Translations.Build

Connect a conventional Runic MF2 project to MSBuild. The package discovers the project, feeds its inputs to the C# generator, and can invoke a pinned local tool to produce JSON, TypeScript, ESM, template manifests, or the experimental C++20 output.

## Install

```bash
dotnet add package Runic.Translations.Build --version <VERSION>
dotnet new tool-manifest
dotnet tool install dotnet-runic-translations --version <VERSION>
```

Replace `<VERSION>` with the current preview shown on NuGet. The package targets .NET 10, bundles the incremental source generator, and takes no `Microsoft.Build` package dependency. Keep it, `dotnet-runic-translations`, and `Runic.Translations` on the same exact version.

## Configure a project

Place `runic.json` and locale message directories under `translations/`. No
MSBuild items are required:

```xml
<PropertyGroup>
  <TranslationsEmitEsm>true</TranslationsEmitEsm>
</PropertyGroup>
```

```bash
dotnet tool restore
dotnet build
```

The project declaration and MF2 messages become Roslyn `AdditionalFiles` with `RunicTranslationKind` metadata for `Runic.Translations.Generator`. ESM output defaults to `obj/<configuration>/<target-framework>/translations/app.esm/`; consume its `web-module-manifest-v1.json` with the Vite adapter.

## Select generated artifacts

Set one or more of these properties to `true`:

| Property | Output |
|---|---|
| `TranslationsEmitJson` | Compiled locale JSON |
| `TranslationsEmitTypeScript` | TypeScript contract |
| `TranslationsEmitTemplateManifest` | Template manifest |
| `TranslationsEmitEsm` | Tree-shakable ESM modules and declarations |
| `TranslationsEmitCpp` | Experimental C++20 output |

`TranslationsGenerateOnBuild=true` with no individual selection emits JSON, TypeScript, template, and ESM groups; C++ remains explicit. Generated C# is never written to disk by this package—it belongs to the source generator.

Choose this package for generated C# and whenever MSBuild owns input classification or non-C# artifact generation. Use the CLI directly when generation is owned by Vite, CI, or another host.

## Important build behavior

- The current target accepts exactly one `runic.json` project and its discovered `.mf2` messages.
- The default launcher is the project-local `dotnet tool run runic-translations --`; restore the committed tool manifest before building.
- Output must resolve beneath `IntermediateOutputPath`. Unsafe paths fail with `RTR0020`.
- Incremental generation tracks inputs, settings, the tool manifest, declared outputs, and an owned-output inventory.
- Clean and changed-output reconciliation remove only validated files owned by the integration; unrelated files are preserved.
- Generated files are exposed as `@(TranslationsGeneratedFile)` and default beneath `$(IntermediateOutputPath)translations`.

## Compatibility and status

This package is a public preview for .NET 10. Preview targets and properties may change with documented migrations. Use the same release across the package family and regenerate outputs when upgrading.

- [Complete project template](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/templates/Runic.Translations.Templates/templates/project)
- [Vite quick start](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/quickstart-vite.md)
- [ESM backend](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/esm.md)
- [Compatibility policy](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/compatibility.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE).
