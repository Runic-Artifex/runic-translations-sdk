# Runic.Translations.Build

Connect a conventional Runic MF2 project to MSBuild. The package discovers the
project, feeds its inputs to the typed C# source generator, and can invoke a
pinned local tool to produce locale-v5 JSON or the ESM ABI-4 package.

## Install

```bash
dotnet add package Runic.Translations.Build --version <VERSION>
dotnet new tool-manifest --output .config
dotnet tool install dotnet-runic-translations --version <VERSION>
```

Replace `<VERSION>` with the current preview shown on NuGet. The package targets .NET 10, bundles the incremental source generator, and takes no `Microsoft.Build` package dependency. The generator needs a Roslyn host on .NET 10, such as `dotnet build` with the .NET 10 SDK; see the [generator notes](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/packages/dotnet/Runic.Translations.Generator/README.md#install). Keep it, `dotnet-runic-translations`, and `Runic.Translations` on the same exact version.

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

## Check WPF XAML at build time

For a WPF project with a local `TranslationProject`, declare the catalog used by
its default source (the `catalog` value from `runic.json`):

```xml
<PropertyGroup>
  <TranslationsXamlCatalog>app</TranslationsXamlCatalog>
</PropertyGroup>
```

`Page` and `ApplicationDefinition` items in `UseWPF=true` projects are scanned
automatically. Static `{rt:Message application_title}` and long-form
`Message`/`MessageInput` declarations are checked against the generated readable
surface: exact key, input count, readable parameter names and plain content kind.
Static `rt:TranslationProperties.RichMessage` keys are checked for inline rich
content. Namespace aliases and nested/quoted markup-extension arguments work;
Binding values are not evaluated. Errors `RTR0080`–`RTR0083` include the XAML
file and line. The check uses the compiler's existing catalog and readable-name
policy, with no additional runtime contract or WPF dependency in the generator.

The catalog setting is an assertion about the source your application provides;
it does not create a source. A file containing an explicit `Message.Source` or
`TranslationProperties.Source` declaration (including a Setter using its static
dependency property) is conservatively excluded from the
project default's catalog checks, since resources/styles/templates may select a
different catalog. `Binding.Source` selects binding data and does not disable
catalog checks. To assert a known local catalog for such a file, or to check
an explicitly selected file outside WPF, use:

```xml
<ItemGroup>
  <TranslationXaml Include="Views/Checkout.xaml" Catalog="app" />
</ItemGroup>
```

A per-file `Catalog` assertion also covers explicit sources in that file. The
catalog must be the local `TranslationProject` catalog; external catalogs are
not loaded by this check. Without a catalog assertion, only declaration mistakes
that do not require a catalog (missing Key, duplicate/mixed/gapped inputs) are
checked. Keys and input names from bindings, resources or `x:Static`, and dynamic
input collections, retain runtime validation. For rich messages, argument/slot
values remain runtime checks or typed C# checks. This scan does not type-check
Binding results or arbitrary XAML; WPF continues to compile and load it.

The scan only sees one file at a time. A file without its own source
declaration is checked against `TranslationsXamlCatalog`, even if at runtime it
inherits `TranslationProperties.Source` from a parent in another file (for
example a `UserControl` placed in a window that selects an external catalog), an
`App.xaml` style, or code-behind. Skip such a file with item metadata on the
item that selects it:

```xml
<ItemGroup>
  <Page Update="Views/OrderSummary.xaml" TranslationsValidateXaml="false" />
  <!-- or, for an explicitly selected file -->
  <TranslationXaml Include="Views/Report.xaml" TranslationsValidateXaml="false" />
</ItemGroup>
```

A `Catalog` other than the local one is reported as `RTR0080` rather than
skipped, so that a misspelled assertion is not silently ignored.

Design-time content is not checked: attributes and elements in namespaces listed
by `mc:Ignorable` (such as `d:`) are skipped, as is everything inside
`mc:AlternateContent`, because the branch WPF compiles depends on the namespaces
it understands. The Runic and WPF namespaces stay checked even when listed.
Runic declarations are recognized through the
`clr-namespace:Runic.Translations.Wpf` mapping, with or without
`;assembly=Runic.Translations.Wpf`; the package defines no `XmlnsDefinition` URI.

Set `TranslationsValidateXaml=false` to disable the optional scan. It requires
neither artifact emission nor the CLI tool. The translation sources and XAML
must be in the same generating C# project; a precompiled catalog from a library
is an unresolved external catalog. XAML is checked only after the catalog links:
it is skipped while the translation sources have errors (those are reported
instead), when the project does not supply exactly one `TranslationProject`, and
when the referenced runtime lacks the readable surface (`RTR0068`). XAML edits
revalidate only the edited file; they do not regenerate C# sources.

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

- Discovery follows the declared `TranslationProject` directory, including nondefault paths. Explicit `TranslationMf2` items override corresponding discovery. File membership is recorded in generation state so additions and removals invalidate artifacts.
- The current target accepts exactly one `runic.json` project and either direct `.mf2` messages or recursive grouped `.rmf2` sources. Mixed representations are rejected.
- The default launcher is the project-local `dotnet tool run runic-translations --`; restore the committed tool manifest before building.
- Output must resolve beneath `IntermediateOutputPath`. Unsafe paths fail with `RTR0020`.
- Incremental generation tracks inputs, settings, the tool manifest, declared outputs, and an owned-output inventory.
- Clean and changed-output reconciliation remove only validated files owned by the integration; unrelated files are preserved.
- Generated files are exposed as `@(TranslationsGeneratedFile)` and default beneath `$(IntermediateOutputPath)translations`.

## Compatibility and status

This package is a public preview for .NET 10. Preview targets and properties may change with documented migrations. Use the same release across the package family and regenerate outputs when upgrading.

- [Complete project template](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/tools/Runic.Translations.Templates/templates/project)
- [Vite quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-vite.md)
- [ESM backend](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/esm.md)
- [RMF2 project guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/LICENSE).

## RMF2

The bundled .NET MSBuild task discovers recursive `{locale}.rmf2` files,
direct `.mf2` messages, and explicit `sourceRoots`, including membership changes.
It uses the host Microsoft.Build.Framework assembly. Source-checkout imports
require building this package first. See the [RMF2 guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md).
