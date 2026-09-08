# Runic.Translations.Compiler

This internal assembly compiles Runic locale TOML and legacy MF2 projects for the shipping Tooling, Build, and CLI products. It accepts UTF-8 sources, returns deterministic diagnostics and a language-neutral compiled model, and can render C#, JSON, TypeScript, ESM, template manifests, and an experimental C++20 surface.

## Install

Install `Runic.Translations.Tooling` when hosting compilation directly. The compiler assembly ships inside that package and is not separately versioned.

## Compile a project

```csharp
using Runic.Translations.Compiler;

var project = new TranslationSource(
    "translations/runic.json",
    File.ReadAllBytes("translations/runic.json"));
var title = new TranslationSource(
    "translations/en.toml",
    File.ReadAllBytes("translations/en.toml"));

TranslationCompilation result = TranslationCompiler.CompileProject(project, [title]);

foreach (TranslationDiagnostic diagnostic in result.Diagnostics)
{
    Console.Error.WriteLine($"{diagnostic.Location}: {diagnostic.Id}: {diagnostic.Message}");
}

if (!result.Success)
{
    Environment.ExitCode = 1;
}
```

For TOML inputs, set `"sourceLayout": "locale-toml"` in `runic.json`. `en.toml` supports root keys, dotted keys, nested tables and inline-table containers with MF2 string leaves. Identifier-safe path segments join with underscores for generated message IDs; collisions are rejected. Omitting the discriminator retains the legacy `{locale}/{message-id}.mf2` layout. See the [locale profile](../../specs/translations/locale-toml-v1.md).

Inputs are copied by `TranslationSource`; pass normalized logical paths when stable diagnostic locations and fingerprints matter. Use `TranslationCompilerOptions` and cancellation for untrusted or interactive inputs rather than increasing the built-in size, depth, locale, key, value, and placeholder limits without a resource budget.

## When to choose this package

Tool builders consume the compiler API through `Runic.Translations.Tooling`. Application projects usually need `Runic.Translations.Build` or `dotnet-runic-translations` instead.

Compilation is deterministic for the same classified UTF-8 inputs and options. It does not read files, mutate a workspace, or provide a user interface. The authoring package adds supported discovery and mutation operations on top of this kernel.

## Compatibility and status

The containing Tooling package is a public preview for .NET 10. Source schemas, normalized message grammar, artifact schemas, and ESM ABI are versioned separately from package SemVer.

- [Compiler API example](https://github.com/Runic-Artifex/runic-sdk/blob/main/tests/dotnet/Runic.Translations.Compiler.Tests/Mf2ProjectTests.cs)
- [MF2 project guide](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/mf2-projects.md)
- [ESM backend](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/esm.md)
- [Compatibility policy](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/compatibility.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-sdk/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-sdk/blob/main/specs/translations/THIRD-PARTY-NOTICES.md) for bundled data attribution.
