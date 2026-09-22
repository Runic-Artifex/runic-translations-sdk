# Versioning and ownership edges

The following identifiers are independent compatibility selectors:

| Contract | Current writer |
|---|---:|
| Project schema | 1 |
| Resource syntax | `rmf2-v1` |
| Execution contract | `rmf2-execution-v2` |
| Message grammar / normalized AST | 5 |
| RMF2 runtime/generated ABI | 2 |
| Locale artifact / external-pack payload | 5 |
| ESM ABI | 4 |
| Web module manifest | 3 |
| Generated-name mapping | 1 |
| Asset manifest edge | 1 |

Package SemVer is not a behavior selector. Readers may support multiple explicit
versions, but writers emit exactly one documented version. An unsupported value
fails; readers never infer, downgrade, or "best effort" an unknown version.

The caller fingerprint is the compatibility key for translated payloads. Adding,
removing, renaming, or changing a canonical key's caller or markup contract
changes it. Translated text, locale-specific declarations, and selector layout do
not. `SourceHash` separately records complete source freshness.

## Cross-owner edges

Runic Translations owns the bytes and schemas for locale, ESM, and asset
metadata. Browser and hosting systems may consume these versioned
artifacts but do not redefine their properties, ordering, hash, or compatibility
rules.

The asset manifest lists relative path, complete-byte SHA-256, byte length,
media type, and optional locale. A host may aggregate or copy listed assets but
must verify their bytes and must not synthesize a different Runic Translations
fingerprint. Host URL routing and deployment policy remain host-owned.

The ESM-v5 package owns its JavaScript modules, declaration files, and web module
manifest as one ABI-4 output group. A standalone TypeScript declaration edge,
template manifest, and C++ emitter are not defined by the current contract.
Requests for those retired output groups fail rather than invoking an older
writer.

The runtime does not perform automatic filesystem/network pack discovery,
translation-service access, dynamic assembly scanning, or source compilation.
A grammar or wire change requires a new explicit version and corpus changes;
version 5 is never extended incompatibly in place.
