# Text Resources Wave B contract

Wave B implements the R2, R3, and bounded R5 kernel tranche on top of the frozen
Wave A source schemas, message grammar, compiler IR, fingerprint, and runtime ABI
version 1. It does not change the meaning of a Wave A catalog or resource
document.

The normative Wave B topics are split as follows:

- [runtime-semantics.md](runtime-semantics.md) defines locale selection,
  immutable snapshots, formatting, fallback, hot swap, and concurrency;
- [canonical-artifacts.md](canonical-artifacts.md) defines deterministic generated
  files and the locale, template, and asset edge formats;
- [build-cli.md](build-cli.md) defines build and tool behavior, path containment,
  atomic replacement, verification, and exit categories;
- [external-packs.md](external-packs.md) defines untrusted-pack validation,
  integrity order, limits, cache rules, and failure atomicity;
- [versioning-and-edges.md](versioning-and-edges.md) defines independent versions
  and the policy for TypeScript, templates, hosting, and deferred Wave C work.

The machine-readable [Wave B corpus](../corpus/wave-b/index.json) is the shared
language-neutral compatibility input. The schemas are:

| Contract | Writer version | File |
|---|---:|---|
| Resolved locale artifact | 1 | `schemas/locale-artifact-v1.schema.json` |
| External locale pack | 1 | `schemas/external-pack-v1.schema.json` |
| Template manifest edge | 1 | `schemas/template-manifest-v1.schema.json` |
| Asset manifest edge | 1 | `schemas/asset-manifest-v1.schema.json` |

These schemas deliberately have no custom `$id`. A canonical schema URI remains
blocked until the registry owns and publishes a domain. The standard draft URI
declares the JSON Schema dialect only.

All public identities use `RunicTextResources.*`. Retired planning names
are not compatibility aliases.
