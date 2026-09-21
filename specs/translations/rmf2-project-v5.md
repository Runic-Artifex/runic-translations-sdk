# Staged RMF2 v5 project linking

The compiler has a separate `Rmf2ProjectV5` carrier for
`rmf2-execution-v2`. It contains v5 messages directly. It never constructs a
`CompiledMessagePattern` or passes messages through the v4 parser/adapter.

Internal backend integrations select
`TranslationCompiler.CompileProjectForProfile(...,
TranslationProjectProfile.Rmf2ExecutionV2)`; `Current` calls the existing public
compiler unchanged. The discriminated result carries either a current
`TranslationCompilation` or an `Rmf2ProjectCompilationV5`, never both.
`CompileRmf2ProjectV5` is the dedicated typed entry point. Failed v5 compilations
return diagnostics without a project that could accidentally reach emission.

Public `runic.json`/CLI profile activation remains deferred until generated
C#/ESM, pack loading and their integration checks are ready. Project schema v1,
resource syntax `rmf2-v1`, and exported markup contract v1 remain unchanged:
artifact v5 does not imply a project schema version bump. Default project output
remains v4. The staged linker uses the existing locale, mount, completeness,
extra-key, empty-value and runtime policies.

## Caller contracts and executable content

`CanonicalMessages` is sorted by canonical underscore key and supplies stable
IDs, resource paths, NFC caller names and carrier types, slot requirements, and
structured-output/markup obligations. Locale payloads retain their full v5
declarations, typed options, ordered annotations, tagged selector keys and
variants. A translation can omit canonical inputs, introduce locale-specific
locals and selectors, and choose different compatible formatters. It cannot
introduce caller inputs or change their carriers. Unannotated target references
inherit canonical carriers; explicit input declarations must agree exactly.
Int64 values may use decimal formatters in local/pattern expressions.
Allowed noncanonical keys live in `ExtraMessages` with ID -1 and retain validated
dynamic caller/markup contracts; they do not add generated canonical members or
participate in the caller fingerprint. The first direct definition in sorted
source order supplies each extra key's caller contract.

Each locale has direct and fallback-resolved resource lists. Every resource
retains its `ContentLocale`, independently of the requested locale. Validation
of locale-dependent functions uses that content locale. Source diagnostics map
back through resource framing into physical UTF-8 locations.

Markup aliases resolve to registered identities in every variant. The linker
validates paired/standalone form, nesting and interactive ancestors; supplies
typed defaults; and checks literal and input/local option types. Numeric
markup literals are numeric MF2 values, not quoted strings; project-v1 numeric
defaults remain strings and are converted to exact canonical decimal literals.
Closing-event annotations remain intact. Functional refs are static slot IDs.
Source slots default to exactly one occurrence per variant. Conditional source
slots require explicit min/max constraints. Every direct locale must obey those
requirements, and cannot introduce a slot or change its kind. Fallback retains
the already-validated resource.

## Compatibility and freshness

`CallerFingerprint` is SHA-256 over a deterministic, versioned caller contract:

- caller contract version 1, execution profile, grammar 5, runtime ABI 2, and
  generated-name mapping version 1;
- catalog identity, sorted canonical keys and sorted NFC caller names/types;
- canonical slot kinds/cardinalities and app-facing structured/markup contracts.

It excludes translated text, locals, formatter choices, annotations, selector
trees, descriptions, source organization, and content-locale/fallback mappings.
Only registered markup contracts used by canonical messages participate. Alias
spellings, enum declaration ordering and equivalent numeric defaults do not
change compatibility. Adding a markup obligation in any direct locale can
change the canonical message's structured return/renderer contract.

`SourceHash` is a separate SHA-256 hash of the profile and length-framed project
bytes, sorted source identities and complete source bytes. It detects actual
v5 content changes, including changes intentionally excluded from caller
compatibility. Source paths inside the project are relative to its directory.
The exported markup v1 JSON retains effective content-locale mappings for runtime
renderers, but those mappings are excluded from the caller fingerprint.

## Generated-name mapping version 1

`Rmf2GeneratedNamesV1.Identifier(name)` maps the NFC UTF-8 bytes of a name to
`r_` followed by lowercase hexadecimal. All names are encoded, including ASCII
names and language keywords. This is injective over NFC names, portable in C#
and JavaScript identifiers, independent of input order, and immune to accidental
collisions with names that already resemble an encoded name. For example,
`a` maps to `r_61` and `r_61` maps to `r_725f3631`.

`Path(segments)` joins encoded segments with `_`, which cannot occur inside the
hex payload. Segment boundaries are preserved. This contract is tested for
keywords, punctuation, Unicode, composed/decomposed aliases and segment
collisions. Generator emission is a dependent slice; it must use this mapping
and separate generated helper scopes. RMF2 resource keys retain their existing
ASCII path rules and underscore-key collision rejection.

The cross-locale fixture in [corpus/v5-project](corpus/v5-project/README.md)
exercises the carrier and direct runtime lowering. It is not an activated pack.
