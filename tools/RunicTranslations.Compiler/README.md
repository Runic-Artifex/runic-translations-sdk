# RunicTranslations.Compiler

Compile Runic Translations catalogs inside your own .NET tooling. The compiler accepts UTF-8 catalog and locale sources, returns deterministic diagnostics and a language-neutral compiled model, and can render C#, JSON, TypeScript, ESM, template manifests, and an experimental C++20 surface.

## Install

```bash
dotnet add package RunicTranslations.Compiler --version <VERSION>
```

Replace `<VERSION>` with the current preview shown on NuGet. The package targets .NET 10 and has no UI-framework dependency.

## Compile a catalog

```csharp
using RunicTranslations.Compiler;

var catalog = new TranslationSource(
    "Resources/app.catalog.json",
    File.ReadAllBytes("Resources/app.catalog.json"));
var english = new TranslationSource(
    "Resources/app.en.json",
    File.ReadAllBytes("Resources/app.en.json"));

TranslationCompilation result = TranslationCompiler.Compile([catalog], [english]);

foreach (TranslationDiagnostic diagnostic in result.Diagnostics)
{
    Console.Error.WriteLine($"{diagnostic.Location}: {diagnostic.Id}: {diagnostic.Message}");
}

if (!result.Success)
{
    Environment.ExitCode = 1;
}
```

Inputs are copied by `TranslationSource`; pass normalized logical paths when stable diagnostic locations and fingerprints matter. Use `TranslationCompilerOptions` and cancellation for untrusted or interactive inputs rather than increasing the built-in size, depth, locale, key, value, and placeholder limits without a resource budget.

## When to choose this package

Choose the compiler for custom build hosts, editors, analyzers, or artifact pipelines that need direct access to diagnostics, the compiled model, renderers, or usage analysis. Application projects usually need `RunicTranslations.Generator`, `RunicTranslations.Build`, or `RunicTranslations.Tool` instead.

Compilation is deterministic for the same classified UTF-8 inputs and options. It does not read files, mutate a workspace, or provide a user interface. The authoring package adds supported discovery and mutation operations on top of this kernel.

## Compatibility and status

This package is a public preview for .NET 10. Source schemas, normalized message grammar, artifact schemas, and ESM ABI are versioned separately from package SemVer. Preview upgrades can change diagnostics or APIs; upgrade the package family together and regenerate outputs.

- [Compiler API example](https://github.com/Runic-Artifex/runic-translations/blob/main/dotnet/tests/RunicTranslations.Compiler.Tests/CompilerTests.cs)
- [Schema-v2 guide](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/schema-v2.md)
- [ESM backend](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/esm.md)
- [Catalog analysis](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/analysis.md)
- [Compatibility policy](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/compatibility.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations/blob/main/THIRD-PARTY-NOTICES.md) for bundled data attribution.
