# Runic Translations kernel contract

This contract defines the runtime, generated-output, build, and external-pack
edges for the selected semantic translation contract. All current writers use
resource syntax `rmf2-v1`, execution contract `rmf2-execution-v2`, grammar/AST
and locale artifact 5, RMF2 runtime ABI 2, ESM ABI 4, and web manifest 3.

The normative topics are split as follows:

- [runtime-semantics.md](runtime-semantics.md) defines locale selection,
  immutable snapshots, formatting, fallback, hot swap, and concurrency;
- [canonical-artifacts.md](canonical-artifacts.md) defines deterministic C#,
  locale-v5 JSON, asset-manifest-v1, and ESM-v5 outputs;
- [build-cli.md](build-cli.md) defines build and tool behavior, path containment,
  atomic replacement, verification, and exit categories;
- [external-packs.md](external-packs.md) defines untrusted-pack validation,
  integrity order, limits, cache rules, and failure atomicity;
- [versioning-and-edges.md](versioning-and-edges.md) defines independent versions
  and ownership boundaries.

The RMF2 v5 corpus is the shared language-neutral compatibility input. The
schemas are:

| Contract | Writer version | File |
|---|---:|---|
| Resolved locale artifact | 5 | `schemas/locale-artifact-v5.schema.json` |
| External locale pack | 5 | `schemas/external-pack-v5.schema.json` |
| Asset manifest edge | 1 | `schemas/asset-manifest-v1.schema.json` |

Each schema has a canonical `https://runic-artifex.eu/schemas/translations/`
identifier and a bundled filename with the same suffix. Schema validation is a
structural check; the semantic compiler and pack readers enforce the additional
cross-document, normalization, resource-limit, and caller-contract rules.

All public identities use `Runic.Translations.*`. Retired names are not
compatibility aliases.
