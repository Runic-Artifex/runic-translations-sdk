# Compatibility and versioning

Runic Translations v1 has one source contract: `runic.json` project schema 1
plus standard MF2 files at `{locale}/{message_id}.mf2`. JSON catalog manifests
and JSON resource documents are not accepted authoring formats.

| Contract | Current version | Compatibility rule |
|---|---:|---|
| Runic project | 1 | `runic.json` is the single project declaration. |
| Message source | MF2 | One identifier-safe message per `.mf2` file. |
| Normalized runtime grammar | 2 | Every generated backend consumes the same compiler-owned execution model. |
| Locale pack | 2 | Decoders reject unsupported versions before reading messages. |
| ESM ABI | 3 | Generated modules expose the typed `m.message_id()` namespace. |
| Web module manifest | 1 | Paths and hashes are versioned independently from ESM code. |
| Runtime ABI | 1 | Generated C# fails closed against an incompatible runtime. |
| Translation-reference transport | 1 | Receivers validate version, catalog, fingerprint, key, and arguments. |

Use one exact release for the `Runic.Translations.*` NuGet packages, the
`dotnet-runic-translations` tool, and
`@runic-artifex/vite-plugin-runic-translations`. Regenerate outputs when the
release changes; generated artifacts are not a hand-authored compatibility
surface.

Preview releases may make breaking changes when release notes identify the
affected contract. Stable releases follow SemVer for public package APIs.
