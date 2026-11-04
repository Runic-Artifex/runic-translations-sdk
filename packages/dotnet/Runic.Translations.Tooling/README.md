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
deterministic XLIFF 2.1 profile (`runic.xliff21.closed-text`, version 3) for one
successfully compiled catalog. Export writes one document per non-default
locale. Plain messages, text with placeholders, are exchanged losslessly: each
placeholder is a `<ph>` whose `equiv` is its exact MF2 expression, and import
checks it against the caller contract. A message's comment and `@param` lines
become a readable `<note category="description">` beside the machine notes.
Messages with selectors, declarations or markup and source-layer provenance are
never silently flattened: export marks them `translate="no"` and records
deterministic loss events, and import skips them. Document messages are not
exchanged through XLIFF; translate them in the `.rmf2` sources or the editor.
This is not general XLIFF or MF2 conformance.

Import is tolerant of what TMS tools write and decides **per unit**:
`TranslationXliffImportResult.Units` says whether each unit was applied, skipped
(no target, state `initial`, an empty target without a translated state, a
structured message) or refused (an unknown placeholder or inline code, invalid
MF2), with a stable code such as `XLIFF21-UNTRANSLATED`; `Messages` holds the
applied ones. Foreign notes, unknown attributes, module metadata, `<mrk>`
annotations, groups and resegmented units are accepted, and what is dropped is
reported in `Report`. Import reads XLIFF 2.0 and 2.1 and profile 2 files.
The overload with the live `Rmf2ProjectCompilation` also checks the catalog and
locales, uses the current caller contract, and applies a unit whose source
changed since export as needs-review, or refuses it
(`TranslationXliffImportOptions.SourceChangePolicy`).

Review state maps to XLIFF as `draft` → `initial`, `translated` → `translated`,
`needs-review` → `translated` with `subState="runic:needs-review"`, and
`approved` → `final`; on import a segment state is authoritative when no
matching Runic review note is present, and `reviewed` reads as approved. The
compact `runic.translations.interchange-review/2` JSON (`ExportReviewJson`,
`ImportReviewJson`, which also reads `/1`) is the Git-friendly authoritative
form. Each entry records a per-unit source fingerprint
(`ComputeSourceFingerprints`: the key, the source text and the caller contract),
so an edit to one message never invalidates the review of another. Export writes
an approval whose fingerprint no longer matches as needs-review and reports
`REVIEW-STALE`.

`runic-translations xliff export`, `xliff import` and `review mark` in
[dotnet-runic-translations](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/tools/dotnet-runic-translations/README.md)
use this API and write imports into `.rmf2` and `.mf2` sources as the editor does.

Compile with `TranslationCompiler.CompileProject` (or the explicit
`CompileMf2Project` entry point) and pass its `Rmf2ProjectCompilation` result
directly to `TranslationInterchange.ExportXliff21`. The first-party editor uses
the same typed boundary and never fabricates a retired catalog carrier. Review
stamps use per-unit source fingerprints, while workspace source freshness remains
a separate conflict check.

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
