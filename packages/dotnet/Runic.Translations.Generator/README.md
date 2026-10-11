# Runic.Translations.Generator

This internal analyzer turns a validated MF2 project into strongly typed C# keys, accessors, compiled locale data, and reflection-free registration during compilation. It ships as part of `Runic.Translations.Build`, not as a separately versioned package.

## Install

Install `Runic.Translations.Build` at the same version as `Runic.Translations`. It is intended for C# projects.

The generator targets .NET 10 because it runs the .NET 10 translations compiler, so it needs a Roslyn host running on .NET 10: `dotnet build` and other builds that use the .NET 10 SDK's compiler, and IDE language servers running on .NET 10. A compiler host on .NET Framework or an older .NET cannot load it; the build then reports `CS8032` and generates no translation code.

## Add translation inputs

The easiest setup is to install `Runic.Translations.Build`, which maps its item types to the generator. Without that package, classify Roslyn `AdditionalFiles` explicitly:

```xml
<ItemGroup>
  <AdditionalFiles Include="translations/runic.json"
                   RunicTranslationKind="Project" />
  <AdditionalFiles Include="translations/**/*.mf2"
                   RunicTranslationKind="Mf2" />
</ItemGroup>
```

For a catalog with `code.namespace` set to `Example.Translations`,
`code.className` set to `AppText`, and a grouped `application.title` resource:

```csharp
using Example.Translations;
using Runic.Translations;

ITranslationManager manager = await AppTextCatalog.CreateManagerAsync();
var text = new AppText(manager);

Console.WriteLine(text.Messages.application_title);
```

For startup code that cannot await, such as a WPF application with `StartupUri`,
`AppTextCatalog.CreateManager()` returns the same manager synchronously; the
embedded catalog needs no I/O.

`text.Messages` is the readable surface. It names each message by its
flattened key, as ESM does (`application_title`), and returns
`LocalizedTextContent<AppTextSlots.key>` for messages with markup; bind their
slots with `Bind(new(...))` and named arguments. An input name or slot ID that
is not an ASCII identifier keeps its encoded `r_<hex>` name for that parameter
only. The encoded members, such as `text.r_6170706c69636174696f6e_r_7469746c65`
(each path segment as `r_` plus UTF-8 hexadecimal bytes), remain the stable
machine-facing contract. The readable file needs a `Runic.Translations`
runtime with typed slot support; an older runtime keeps the encoded members and
reports `RTR0068`. See the [.NET consumer quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-dotnet.md)
for a complete template and console application workflow.

The generator reports compiler diagnostics at source locations and writes no
files. Every `RTR` diagnostic links to its entry in the
[diagnostics reference](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/diagnostics.md).
Each translation file is parsed in its own incremental step, so editing one
file recompiles only that file before relinking the catalog, and C# edits do not
rerun translation work. Typed C# becomes part of the current compilation. Locale-v5 JSON and the
cohesive ESM-v5 package belong to the build or CLI surfaces. Standalone
TypeScript, template-manifest, and C++ output groups are not part of the selected
contract.

Grouped `.rmf2` and direct `.mf2` sources emit typed v5 C# that requires RMF2
runtime ABI 2. Retired selectors and incompatible source representations are
rejected rather than producing an older output.

## When to choose this package

Choose `Runic.Translations.Build` for typed C# application APIs and generated web assets. Choose `Runic.Translations.Tooling` for direct programmatic compilation.

## Compatibility and status

The containing Build package is a public preview. It requires a .NET 10 compiler host and emits code against the matching runtime ABI; an incompatible runtime fails explicitly.

- [Generated package consumer](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/tests/dotnet/Runic.Translations.PackageTests)
- [Generator tests and examples](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/tests/dotnet/Runic.Translations.Generator.Tests)
- [RMF2 project guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/specs/translations/THIRD-PARTY-NOTICES.md) for bundled data attribution.

## Optional XAML diagnostics

`Runic.Translations.Build` can pass explicitly selected XAML as additional files
with `RunicTranslationKind=Xaml`. `RunicTranslationCatalog` asserts the local
catalog for a file; `RunicTranslationDefaultCatalog` asserts the default catalog
only when the file has no explicit source declarations. Declare both metadata
names through `CompilerVisibleItemMetadata` when wiring the analyzer manually.
The generator parses the optional markup using XML namespace scopes, then checks
static messages against the same compiled contract and readable naming used by
its C# emission. It does not reference WPF or evaluate markup extensions. See
[the Build package](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/packages/dotnet/Runic.Translations.Build/README.md#check-wpf-xaml-at-build-time)
for setup, diagnostics and the limits of dynamic/external source validation.
