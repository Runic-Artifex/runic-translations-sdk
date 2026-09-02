# Runic.Translations.Compiler

This internal assembly compiles Runic MF2 projects for the shipping Tooling, Build, and CLI products. It accepts UTF-8 sources, returns deterministic diagnostics and a language-neutral compiled model, and can render C#, JSON, TypeScript, ESM, template manifests, and an experimental C++20 surface.

## Install

Install `Runic.Translations.Tooling` when hosting compilation directly. The compiler assembly ships inside that package and is not separately versioned.

## Compile a project

```csharp
using Runic.Translations.Compiler;

var project = new TranslationSource(
    "translations/runic.json",
    File.ReadAllBytes("translations/runic.json"));
var title = new TranslationSource(
    "translations/en/application_title.mf2",
    File.ReadAllBytes("translations/en/application_title.mf2"));

TranslationCompilation result = TranslationCompiler.CompileMf2Project(project, [title]);

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

Tool builders consume the compiler API through `Runic.Translations.Tooling`. Application projects usually need `Runic.Translations.Build` or `dotnet-runic-translations` instead.

Compilation is deterministic for the same classified UTF-8 inputs and options. It does not read files, mutate a workspace, or provide a user interface. The authoring package adds supported discovery and mutation operations on top of this kernel.

## Compatibility and status

The containing Tooling package is a public preview for .NET 10. Source schemas, normalized message grammar, artifact schemas, and ESM ABI are versioned separately from package SemVer.

- [Compiler API example](https://github.com/Runic-Artifex/runic-translations/blob/main/dotnet/tests/Runic.Translations.Compiler.Tests/Mf2ProjectTests.cs)
- [MF2 project guide](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/mf2-projects.md)
- [ESM backend](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/esm.md)
- [Compatibility policy](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/compatibility.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations/blob/main/THIRD-PARTY-NOTICES.md) for bundled data attribution.
