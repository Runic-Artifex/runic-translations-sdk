# Runic Translations contracts

This directory freezes the TR0–TR1 language-neutral contracts for
`Runic.Translations`. Together, these contracts form the portable protocol
family `runic.translations/1`. The standalone planning document remains the source
for product intent; ADR 0001 and ADR 0005 replace its retired product and
diagnostic identities.

## Version set

| Contract | Current writer version |
|---|---:|
| MF2 project schema | 1 |
| Authoring messages | MessageFormat 2 `.mf2` files |
| Message grammar | MF2 with the documented Runic v1 profile |
| Normalized message AST | 2 execution AST; 3 MF2-subset interchange AST |
| Resolved locale artifact | 1 (grammar 1) and locale-pack-v2 (grammar 2) |
| Runtime/generated-code ABI | 1 |
| ESM ABI | 3 |
| Transport contract | 1 |

Package versions are independent from these integers. New cross-runtime schemas
use canonical `https://runic-artifex.eu/schemas/translations/` identifiers. An
instance `$schema` member is a semantic compiler concern and is not a behavior
selector; `schemaVersion` selects compiler behavior.

Every bundled schema's `$id` is its public URL beneath that canonical root. CI
checks that the URL suffix and bundled filename remain identical. The same bytes
can be exported for pinned or offline tooling with `runic-translations schema`.

## Canonical compiler IR

The pure compiler consumes one `runic.json` project source and explicitly
classified MF2 message sources. Source display paths are normalized to `/`
separators and affect diagnostics only. Compilation order never depends on
absolute paths, current directory, environment, clock, current culture, or
input enumeration order.

Successful IR observes these orders:

1. catalogs by ordinal catalog ID;
2. layers by ascending signed priority, then ordinal name;
3. locales by ordinal canonical tag;
4. dotted resource keys and placeholder names by ordinal comparison.

The effective default locale after whole-leaf layer replacement defines the
canonical key set and zero-based key IDs. Each other locale retains direct
effective resources and resolved per-key fallback resources. A higher-priority
leaf replaces its entire value and metadata. File paths never establish merge
precedence.

The contract fingerprint is `sha256:` followed by lowercase hexadecimal SHA-256
of canonical UTF-8 JSON containing only the catalog ID, message grammar version,
and the default locale's ordered key/input/selector contracts. It excludes source
paths, translated patterns, descriptions, tags, and insignificant whitespace.

## Diagnostics and locations

Wave A reserves `RTR0001` through `RTR0022` and `RTR0099` under ADR
0005. Source diagnostics use normalized paths and one-based line/column values;
columns count UTF-16 code units. Byte spans are zero-based, start-inclusive, and
end-exclusive in the original UTF-8 byte sequence, including any optional BOM. Diagnostics
target the most specific offending property or value token.

`RTR0020` is reserved but not emitted by the TR0–TR1 compiler kernel because
output paths do not enter the pure compilation API. It becomes executable in the
later build/CLI surface. `RTR0023`, `RTR0024`, and `RTR0099` belong to
later external-pack, generator/runtime-ABI, and unexpected-failure surfaces.
MF2 profile validation uses `RTR0030`; `RTR0031` reports a locale outside a
selected backend's built-in selector registry.

The machine-readable [corpus](corpus/README.md) is the executable compatibility
contract. Schema validation alone is intentionally insufficient for normalized
uniqueness, BCP 47 canonicalization, fallback graphs, cross-file merge rules,
pattern/descriptor parity, compiler limits, and generated identifier collisions.

The supported authoring profile and project convention are documented in
[`../docs/mf2-projects.md`](../docs/mf2-projects.md). `locale-pack-v2` is
documented in [`../docs/locale-pack-v2.md`](../docs/locale-pack-v2.md).
