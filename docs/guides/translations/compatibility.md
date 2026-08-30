# Compatibility and versioning

Runic Translations versions packages independently from its serialized and
generated contracts. A package SemVer is not a schema or runtime ABI version.

| Contract | Current version | Compatibility rule |
|---|---:|---|
| Catalog manifest | 2 (reader: 1 and 2) | Writer emits schema 2; compiler accepts schemas 1 and 2. |
| Source resource schema | 3 preview (reader/migration: 1 and 2) | v3 is a tooling interchange boundary; compiler execution remains v1/v2 pending its parser milestone. |
| Message grammar / normalized AST | 2 execution; 3 MF2 subset interchange | Every generated backend consumes the same compiler-owned execution AST. |
| Locale artifact | 1 and locale-pack-v2 | Bytes-first v2 decoders reject unsupported versions before reading messages. |
| ESM ABI | 2 | The Vite adapter rejects generated manifests with another ABI. |
| Web module manifest | 1 | Paths and hashes remain versioned independently from ESM code. |
| Translation-reference transport | 1 | Receivers validate version, catalog, fingerprint, key, and arguments. |

## Package compatibility

Use one exact package version for the established `Runic.Translations.*` NuGet packages,
the `dotnet-runic-translations` local tool, and
`@runic-artifex/vite-plugin-runic-translations`. Generated artifacts should be
consumed by the adapter and runtime from that same release. The editor declares
the exact compiler/tool package family against which it was built.

Preview releases may make breaking changes when the release notes include the
migration and the affected contract version changes where required. Stable
releases follow SemVer for public package APIs. Serialized schemas, artifacts,
ABIs, and transports change only through their explicit embedded version.

## ESM ABI 1 to 2

ABI 2 replaces public encoded message exports with an exact-key namespace:

```diff
- import { m$Common$Hello } from "virtual:runic-translations/app";
- m$Common$Hello({ name: "Ada" });
+ import { m } from "virtual:runic-translations/app";
+ m["Common.Hello"]({ name: "Ada" });
```

Single-segment keys remain available through dot access, for example
`m.ApplicationName()`. Encoded names such as `m$Common$Hello` remain internal
generation details and are not a public compatibility surface.

Regenerate ESM output and update imports together. The Vite adapter reports an
explicit error instead of consuming an ABI 1 manifest.

## Release notes

Every preview or stable release must call out changes to:

- generated public names or signatures;
- source or artifact schemas;
- ESM, runtime, or transport ABI versions;
- compiler diagnostics that can turn an accepted catalog into a rejected one;
- supported locale and formatter capabilities;
- editor/compiler compatibility.
