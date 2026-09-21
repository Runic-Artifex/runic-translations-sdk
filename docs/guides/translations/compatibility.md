# Compatibility and versioning

Runic Translations uses `runic.json` project schema 1 with an explicit source
layout. New projects set `sourceLayout: "locale-toml"` and store MF2 string values
in sibling `{locale}.toml` files. Section tables, nested tables, dotted keys and inline tables group
messages; identifier-safe path segments join with underscores into logical IDs.
Flat keys remain supported, and collisions after joining are rejected. Projects without the
field retain the historical `{locale}/{message_id}.mf2` layout for legacy reading
and migration. Mixed layouts are rejected. JSON catalog manifests and JSON
resource documents are not accepted authoring formats.

| Contract                        | Current version | Compatibility rule                                                          |
| ------------------------------- | --------------: | --------------------------------------------------------------------------- |
| Runic project                   |               1 | `runic.json` is the single project declaration.                             |
| Message source                  |             MF2 | TOML string values; legacy per-message files when `sourceLayout` is absent. |
| Normalized runtime grammar      |               2 | Every generated backend consumes the same compiler-owned execution model.   |
| Locale pack                     |               2 | Decoders reject unsupported versions before reading messages.               |
| ESM ABI                         |               3 | Generated modules expose the typed `m.message_id()` namespace.              |
| Web module manifest             |               1 | Paths and hashes are versioned independently from ESM code.                 |
| Runtime ABI                     |               1 | Generated C# fails closed against an incompatible runtime.                  |
| Translation-reference transport |               1 | Receivers validate version, catalog, fingerprint, key, and arguments.       |

Use one exact release for the `Runic.Translations.*` NuGet packages, the
`dotnet-runic-translations` tool, and
`@runic-artifex/vite-plugin-runic-translations`. Regenerate outputs when the
release changes; generated artifacts are not a hand-authored compatibility
surface.

RMF2 has an explicit dual contract within project schema 1. With
`sourceLayout: "rmf2-v1"` and no execution selector, hosts retain grammar and
artifact 4, .NET RMF2 ABI requirement 1, ESM ABI 3, and web manifest v2. Adding
`executionProfile: "rmf2-execution-v2"` selects grammar/artifact 5, .NET RMF2 ABI
2, ESM ABI 4, and web manifest v3. Hosts reject unknown profiles and do not infer
the newer contract from file extensions. C++ and standalone v5
TypeScript/template contracts remain unsupported.

Preview releases may make breaking changes when release notes identify the
affected contract. Stable releases follow SemVer for public package APIs.
