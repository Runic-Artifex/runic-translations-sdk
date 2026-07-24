# Versioning, ownership edges, and deferred work

The following integers are independent compatibility selectors:

| Contract | Wave B writer |
|---|---:|
| Catalog source schema | 1 |
| Resource source schema | 1 |
| Message grammar | 1 |
| Runtime/generated ABI | 1 |
| Locale artifact / external-pack payload | 1 |
| Template manifest edge | 1 |
| Asset manifest edge | 1 |

Package SemVer is not a behavior selector. Readers may support multiple explicit
versions, but writers emit exactly one documented version. An unsupported value
fails; readers never infer, downgrade, or "best effort" an unknown version.

The Wave A contract fingerprint remains the compatibility key for translated
payloads. Adding, removing, renaming, or changing the placeholders of a canonical
key changes the fingerprint. Changing translated pattern text alone does not.
Changing generated namespace/class or catalog ID is an explicit source migration.

## Cross-owner edges

Text Resources owns the bytes and schemas for locale, template, and asset
metadata. Template, browser, and hosting systems may consume these versioned
artifacts but do not redefine their properties, ordering, hash, or compatibility
rules. This Wave does not edit template, web, or Hosting projects.

The template manifest is value-free and contains stable keys, optional source
metadata normalized to nullable fields, tags, and typed arguments. It authorizes
literal-key/argument validation only; template syntax, escaping, generated
template code, and dynamic-key policy remain owned by the template system.

The asset manifest lists relative path, complete-byte SHA-256, byte length,
media type, and optional locale. A host may aggregate or copy listed assets but
must verify their bytes and must not synthesize a different Text Resources
fingerprint. Host URL routing and deployment policy remain host-owned.

The TypeScript filename and type projection are reserved edge artifacts. A full
browser formatter, TypeScript packaging, template compiler integration, and host
static-asset changes are Wave C/D work. React, Vue, and Svelte projections follow
in Wave E; Angular- and ReactiveUI-specific projections follow in Wave F. Until the
relevant tranche lands, .NET-resolved text is the authoritative cross-runtime
formatting path.

Also deferred to Wave C are plural/select/gender grammar, rich or trusted markup,
automatic filesystem/network pack discovery, translation services, dynamic
assembly scanning, runtime source JSON compilation, custom schema `$id` values,
and new diagnostic identities. Any grammar or wire change begins with a new
version plus corpus changes; it never extends version 1 in place.
