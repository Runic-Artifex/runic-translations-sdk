# Runic.Translations.Compiler

This bundled assembly compiles Runic RMF2 projects for the shipping Tooling,
Build, and CLI products. It accepts UTF-8 sources, returns deterministic
diagnostics and v5 contract metadata, and supports direct `.mf2` and grouped
`.rmf2` inputs through one semantic execution model.

## Install

Install `Runic.Translations.Tooling` when hosting compilation directly. The compiler assembly ships inside that package and is not separately versioned.

## Compile a project

```csharp
using Runic.Translations.Compiler;

var project = new TranslationSource(
    "translations/runic.json",
    File.ReadAllBytes("translations/runic.json"));
var title = new TranslationSource(
    "translations/en.rmf2",
    File.ReadAllBytes("translations/en.rmf2"));

Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileProject(project, [title]);

foreach (TranslationDiagnostic diagnostic in result.Diagnostics)
{
    Console.Error.WriteLine($"{diagnostic.Location}: {diagnostic.Id}: {diagnostic.Message}");
}

if (!result.Success)
{
    Environment.ExitCode = 1;
}
else
{
    Console.WriteLine($"{result.CatalogId}: {result.CallerFingerprint}");
}
```

Grouped `.rmf2` inputs preserve explicit directory namespaces; direct `.mf2`
inputs use one message per locale/key path. The compiler infers the representation
and rejects mixed inputs.

Inputs are copied by `TranslationSource`; pass normalized logical paths when stable diagnostic locations and fingerprints matter. Use `TranslationCompilerOptions` and cancellation for untrusted or interactive inputs rather than increasing the built-in size, depth, locale, key, value, and placeholder limits without a resource budget.

## When to choose this package

Tool builders consume the compiler API through `Runic.Translations.Tooling`. Application projects usually need `Runic.Translations.Build` or `dotnet-runic-translations` instead.

Compilation is deterministic for the same classified UTF-8 inputs and options. It does not read files, mutate a workspace, or provide a user interface. The authoring package adds supported discovery and mutation operations on top of this kernel.

The public result exposes deterministic catalog, locale, caller-contract, and
source-freshness metadata. `Runic.Translations.Tooling` accepts that same typed
result for XLIFF export; the normalized executable graph remains an internal
implementation detail. Shipping hosts use the typed `Rmf2ExecutionV2` project carrier. See the
[v5 project-linking contract](../../specs/translations/rmf2-project-v5.md) for
cross-locale contracts, fingerprint/freshness separation and generated names.

## Compatibility and status

The containing Tooling package is a public preview for .NET 10. Source schemas, normalized message grammar, artifact schemas, and ESM ABI are versioned separately from package SemVer.

The 0.4 preview intentionally replaces the former public
`TranslationCompilation`/`CompiledTextCatalog` graph with
`Rmf2ProjectCompilationV5`. That older graph, its analysis API, and its renderer
hand-off cannot represent the selected semantic contract and are no longer a
public compatibility surface.

- [Compiler API example](https://github.com/Runic-Artifex/runic-sdk/blob/main/tests/dotnet/Runic.Translations.Compiler.Tests/Mf2ProjectTests.cs)
- [RMF2 project guide](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/rmf2.md)
- [ESM backend](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/esm.md)
- [RMF2 project guide](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/rmf2.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-sdk/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-sdk/blob/main/specs/translations/THIRD-PARTY-NOTICES.md) for bundled data attribution.
