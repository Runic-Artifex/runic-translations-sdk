# Text Resources Wave A contract

This directory freezes the TR0–TR1 language-neutral contracts for
`RunicTextResources`. Together, these contracts form the portable protocol
family `runic.textresources/1`. The standalone planning document remains the source
for product intent; ADR 0001 and ADR 0005 replace its retired product and
diagnostic identities.

## Version set

| Contract | Current writer version |
|---|---:|
| Catalog manifest schema | 1 |
| Resource document schema | 1 |
| Message grammar | 1 |
| Runtime/generated-code ABI | 1 |

Package versions are independent from these integers. The schemas deliberately
have no custom `$id`: the contract registry requires an owned and deployed
schema domain before a canonical URI can be reserved. An instance `$schema`
member is therefore a semantic compiler concern and is not a behavior selector;
`schemaVersion` selects behavior.

## Canonical compiler IR

The pure compiler consumes explicitly classified manifest and document byte
sequences. Source display paths are normalized to `/` separators and affect
diagnostics only. Compilation order never depends on absolute paths, current
directory, environment, clock, current culture, or input enumeration order.

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
and the default locale's ordered key/placeholder contracts. It excludes source
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

The machine-readable [corpus](corpus/README.md) is the executable compatibility
contract. Schema validation alone is intentionally insufficient for normalized
uniqueness, BCP 47 canonicalization, fallback graphs, cross-file merge rules,
pattern/descriptor parity, compiler limits, and generated identifier collisions.
