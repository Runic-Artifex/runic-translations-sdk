# Runic.Translations.Compiler

This internal assembly compiles Runic RMF2 and legacy MF2 projects for the shipping Tooling, Build, and CLI products. It accepts UTF-8 sources, returns deterministic diagnostics and a language-neutral compiled model, and can render C#, JSON, TypeScript, ESM, template manifests, and an experimental C++20 surface.

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

For RMF2 inputs, set `"sourceLayout": "rmf2-v1"` in `runic.json`. RMF2 resources preserve explicit directory namespaces. Omitting the discriminator retains the legacy `{locale}/{message-id}.mf2` layout.

Inputs are copied by `TranslationSource`; pass normalized logical paths when stable diagnostic locations and fingerprints matter. Use `TranslationCompilerOptions` and cancellation for untrusted or interactive inputs rather than increasing the built-in size, depth, locale, key, value, and placeholder limits without a resource budget.

## When to choose this package

Tool builders consume the compiler API through `Runic.Translations.Tooling`. Application projects usually need `Runic.Translations.Build` or `dotnet-runic-translations` instead.

Compilation is deterministic for the same classified UTF-8 inputs and options. It does not read files, mutate a workspace, or provide a user interface. The authoring package adds supported discovery and mutation operations on top of this kernel.

Profile-aware shipping hosts inspect `executionProfile` and explicitly select the
internal `Rmf2ExecutionV2` project profile, receiving a separate typed v5 carrier.
The public low-level `CompileProject` entry point remains v4-only and rejects the
selector. See the [v5 project-linking contract](../../specs/translations/rmf2-project-v5.md)
for cross-locale contracts, fingerprint/freshness separation and generated names.

## Compatibility and status

The containing Tooling package is a public preview for .NET 10. Source schemas, normalized message grammar, artifact schemas, and ESM ABI are versioned separately from package SemVer.

- [Compiler API example](https://github.com/Runic-Artifex/runic-sdk/blob/main/tests/dotnet/Runic.Translations.Compiler.Tests/Mf2ProjectTests.cs)
- [RMF2 project guide](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/rmf2.md)
- [ESM backend](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/esm.md)
- [RMF2 project guide](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/rmf2.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-sdk/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-sdk/blob/main/specs/translations/THIRD-PARTY-NOTICES.md) for bundled data attribution.
