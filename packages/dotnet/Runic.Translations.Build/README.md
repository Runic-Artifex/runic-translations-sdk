# Runic.Translations.Build

Connect a conventional Runic MF2 project to MSBuild. The package discovers the
project, feeds its inputs to the typed C# source generator, and can invoke a
pinned local tool to produce locale-v5 JSON or the ESM ABI-4 package.

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

The project declaration and messages become Roslyn `AdditionalFiles` with
`RunicTranslationKind` metadata for `Runic.Translations.Generator`. ESM output
defaults to `obj/<configuration>/<target-framework>/translations/app.esm-v5/`
and `web-module-manifest-v3.json`; consume it with the Vite adapter.

## Select generated artifacts

Set one or more of these properties to `true`:

| Property | Output |
|---|---|
| `TranslationsEmitJson` | `{catalog}.{locale}.locale-v5.json` plus `asset-manifest-v1` |
| `TranslationsEmitEsm` | Cohesive ESM-v5 modules, declarations, and `web-module-manifest-v3.json` |

`TranslationsGenerateOnBuild=true` with no individual selection emits the JSON
and ESM groups; C# remains source-generator owned. The retired
`TranslationsEmitTypeScript`, `TranslationsEmitTemplateManifest`, and
`TranslationsEmitCpp` selections are unsupported and fail with `RTR0065`; they
never activate an older renderer. Generated C# is never written
to disk by this package—it belongs to the source generator.

Choose this package for generated C# and whenever MSBuild owns input classification or non-C# artifact generation. Use the CLI directly when generation is owned by Vite, CI, or another host.

## Important build behavior

- Discovery follows the declared `TranslationProject` directory, including nondefault paths. Explicit legacy `TranslationMf2` items override corresponding discovery. File membership is recorded in generation state so additions and removals invalidate artifacts.
- The current target accepts exactly one `runic.json` project and either direct `.mf2` messages or recursive grouped `.rmf2` sources. Mixed representations are rejected.
- The default launcher is the project-local `dotnet tool run runic-translations --`; restore the committed tool manifest before building.
- Output must resolve beneath `IntermediateOutputPath`. Unsafe paths fail with `RTR0020`.
- Incremental generation tracks inputs, settings, the tool manifest, declared outputs, and an owned-output inventory.
- Clean and changed-output reconciliation remove only validated files owned by the integration; unrelated files are preserved.
- Generated files are exposed as `@(TranslationsGeneratedFile)` and default beneath `$(IntermediateOutputPath)translations`.

## Compatibility and status

This package is a public preview for .NET 10. Preview targets and properties may change with documented migrations. Use the same release across the package family and regenerate outputs when upgrading.

- [Complete project template](https://github.com/Runic-Artifex/runic-sdk/tree/main/tools/Runic.Translations.Templates/templates/project)
- [Vite quick start](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/quickstart-vite.md)
- [ESM backend](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/esm.md)
- [RMF2 project guide](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/rmf2.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-sdk/blob/main/LICENSE).

## RMF2

The bundled .NET MSBuild task discovers recursive `{locale}.rmf2` files,
direct `.mf2` messages, and explicit `sourceRoots`, including membership changes.
It uses the host Microsoft.Build.Framework assembly. Source-checkout imports
require building this package first. See the [RMF2 guide](../../../docs/guides/translations/rmf2.md).
