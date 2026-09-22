# Runic Translations contracts

This directory defines the single translation contract emitted by the current
preview. Source organization can be direct `.mf2` messages or grouped `.rmf2`
resources, but both forms link to the same semantic model. They are not separate
execution profiles and cannot be mixed in one project.

## Version set

| Contract | Current writer version |
|---|---:|
| Project schema | 1 |
| Resource syntax | `rmf2-v1` |
| Authoring representation | Direct `.mf2` or grouped `.rmf2` files |
| Message grammar and normalized AST | 5 (`rmf2-execution-v2`) |
| Resolved locale artifact | 5 |
| Runtime/generated-code ABI | RMF2 ABI 2 |
| ESM ABI | 4 (`web-module-manifest-v3`) |
| Transport contract | 1 |

Package versions are independent from these integers. New cross-runtime schemas
use canonical `https://runic-artifex.eu/schemas/translations/` identifiers. An
instance `$schema` member identifies the project schema and is not a behavior
selector. `schemaVersion` identifies the project document shape; source files
select their representation, and all valid projects use `rmf2-execution-v2`.

Every bundled schema's `$id` is its public URL beneath that canonical root. CI
checks that the URL suffix and bundled filename remain identical. The same bytes
can be exported for pinned or offline tooling with `runic-translations schema`.

## Canonical compiler model

The pure compiler consumes one `runic.json` source and classified MF2 sources.
Source display paths are normalized to `/` separators. Compilation order never
depends on absolute paths, current directory, environment, clock, current
culture, or input enumeration order. Locale tags, resource keys, caller names,
and generated names use the deterministic ordering and normalization rules in
the [v5 project-linking contract](rmf2-project-v5.md).

`CallerFingerprint` is the compatibility hash of the v5 caller and markup
contract. `SourceHash` independently records complete project and source bytes.
Translated text, selector layout, and physical source partitioning do not become
caller compatibility merely because they affect freshness.

## Diagnostics and locations

Source diagnostics use normalized paths and one-based line/column values;
columns count UTF-16 code units. Byte spans are zero-based, start-inclusive, and
end-exclusive in the original UTF-8 byte sequence, including any optional BOM. Diagnostics
target the most specific offending property or value token.

`RTR0020` belongs to the build/CLI output-containment surface. `RTR0023`
classifies external-pack rejection, `RTR0024` reports an unsafe generated/runtime
ABI mismatch, and `RTR0031` reports a locale outside the selected backend's
built-in capability registry. Unknown project members, retired selectors, mixed
source representations, and unsupported output switches fail explicitly.

The machine-readable [corpus](corpus/README.md) contains the current v5 release
oracles. Schema validation alone is intentionally insufficient for normalized
uniqueness, BCP 47 canonicalization, fallback graphs, cross-file linking,
caller/markup parity, compiler limits, and generated identifier collisions.
The root Wave A JSON corpus is retained only as a published-0.3 historical
fixture and is not an accepted input contract for the current preview.

The supported authoring convention is documented in
[`../../docs/guides/translations/rmf2.md`](../../docs/guides/translations/rmf2.md).

## RMF2 execution contract

The [RMF2 guide](../../docs/guides/translations/rmf2.md) specifies resource composition,
markup contracts, artifact v5, migration boundaries, and deliberate release
exclusions.
RMF2 fingerprints include caller input/slot contracts, markup registries and effective
locale mappings; source selector trees and physical file organization are excluded.

The [semantic v5 foundation](rmf2-execution-v2.md) and
[option table](rmf2-execution-v2.json) specify the typed model. Every supported
project uses this contract for C#/.NET and ESM generation. Direct `.mf2` sources
are one message per file; grouped `.rmf2` sources carry resource namespaces.
Hosts reject mixed source representations and unknown or retired contract
selectors rather than falling back to an older carrier.
The [v5 project linker](rmf2-project-v5.md) provides an explicit typed
compiler model, cross-locale caller/markup contracts, separate compatibility and
freshness hashes, and the generated-name mapping for the dependent backends.
The version-explicit [`rmf2-v1` corpus](corpus/rmf2-v1/README.md) names the
unchanged resource syntax, not a retired execution profile. It is the shared
release oracle for the current v5 contract: compiler, generated C#, .NET
artifact-v5 loading, generated ESM, and ESM dynamic loading run the same typed
execution and rejection cases. Its exclusions—terms, references, group fallback,
C++ RMF2, rich XLIFF, application call-site rewriting, migration from non-RMF2
legacy formats, and marketplace concerns are deliberate release-boundary
exclusions, not unversioned omissions from the protocol. Read-side compatibility
retentions are listed in the [compatibility-retention ledger](../../docs/guides/translations/compatibility-retention.md).
