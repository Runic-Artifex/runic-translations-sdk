# Runic.Translations.Tooling

`Runic.Translations.Tooling` is the MF2 authoring facade. It owns compiler
invocation and interchange, and bundles the
authoring assembly `Runic.Translations.Authoring` for MF2 project scaffolding.
The compiler and authoring assemblies ship inside this package; it
intentionally does not reference the runtime or Runic Command Line.

The public API is the compiler (`TranslationCompiler`, `TranslationSource`,
`TranslationCompilerOptions`, `TranslationDiagnostic` and the compilation
result), XLIFF interchange with its review sidecar (`TranslationInterchange`),
artifact inspection (`ArtifactInspector`) and project scaffolding
(`TranslationProjectScaffolder`, `TranslationProjectWriter`). The command
plumbing of `runic-translations`, the workspace transactions, the MF2 syntax
tree and the language service are internal: they serve the tool and the
Translations Editor, which are built from the same release.

`TranslationInterchange.ExportXliff21` and `ImportXliff21` implement a closed,
deterministic XLIFF 2.1 text profile for one successfully compiled catalog.
They export one document per non-default locale, preserve plain text patterns,
placeholder contracts, resource metadata, and review state/notes. The compact
`runic.translations.interchange-review/1` JSON sidecar is the Git-friendly
authoritative form for review data. Rich selector, formatter, or markup messages and source-layer
provenance are never silently flattened: export records deterministic loss
events and import rejects structured units. Document messages are not exchanged
through XLIFF; translate them in the `.rmf2` sources or the editor. This is not
general XLIFF or MF2 conformance.

Compile with `TranslationCompiler.CompileProject` (or the explicit
`CompileMf2Project` entry point) and pass its `Rmf2ProjectCompilation` result
directly to `TranslationInterchange.ExportXliff21`. The first-party editor uses
the same typed boundary and never fabricates a retired catalog carrier. Approved review stamps use a dedicated
closed-text-profile fingerprint, while workspace source freshness remains a
separate conflict check.

```csharp
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;

Rmf2ProjectCompilation compilation =
    TranslationCompiler.CompileProject(projectSource, messageSources);
if (!compilation.Success)
{
    // Inspect compilation.Diagnostics.
}
else
{
    TranslationXliffExportResult xliff =
        TranslationInterchange.ExportXliff21(compilation);
}
```

`TranslationCompiler` is bundled in this package's
`Runic.Translations.Compiler.dll`; no separate compiler package is required.

The package carries the schemas needed for the generated v5 runtime artifacts.

The compiler and authoring assemblies are implementation parts of this package,
not separately versioned products.

## RMF2

Use `Runic.Translations.Build` or `dotnet-runic-translations` for project
emission. The package includes the v5 artifact schema. See
the [RMF2 guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md) for the supported
subset, the closed plain-text interchange boundary, and frozen exclusions.
