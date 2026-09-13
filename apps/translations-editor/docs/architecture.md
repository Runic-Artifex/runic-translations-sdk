# Translations Editor architecture

Runic Translations Editor is a workspace application for the same translation
sources that application builds consume. It does not define a second message
language, schema, or validation path.

## Source and semantic authority

Each workspace has one `runic.json` declaration. Its `sourceLayout` selects the
source layout; the editor loads, validates, previews, and saves through the
compiler-backed authoring model. A normal text editor remains a valid way to edit
those sources.

```text
translations/                 # legacy layout when sourceLayout is omitted
  runic.json
  en/
    application_title.mf2
  de/
    application_title.mf2

translations/                 # sourceLayout: "locale-toml"
  runic.json
  en.toml
  de.toml

translations/                 # sourceLayout: "rmf2-v1"
  runic.json
  en.rmf2
  checkout/
    de.rmf2
```

RMF2 files are recursively discovered as `{locale}.rmf2`; directory segments and
explicit groups form their logical namespace. Explicit `sourceRoots` can mount
feature-local resources. The editor uses the compiler's RMF2 source model and
diagnostics, including the resource/markup contracts that apply to that layout.

The editor owns workspace navigation, drafts, review state, interchange, and
atomic workspace mutations. The compiler owns message semantics, diagnostics,
generated APIs, and build-time validation. Review state is stored separately from
authored translation sources.

The editor checks revisions before replacing files, watches for external changes,
and can recover interrupted workspace transactions. Direct saves preserve a
translator's MF2 formatting: determinism means that an identical operation on
identical starting bytes has identical output, not that every message is
reformatted.

## User interfaces

The Svelte frontend renders the editor workflow. Its native host serves the
frontend and owns local application state, diagnostics, and filesystem-facing
workspace operations. The browser profile does not hold durable editor state;
preferences, recent projects, and recovery drafts live in one native per-user
record. Clearing that record does not change workspace files or in-memory work.

Headless commands use the same workspace and compiler path as the interface:

- `validate` reports compiler-backed workspace diagnostics;
- `export`, `report`, and `import --apply` support XLIFF and review JSON
  interchange, with `report` providing the read-only import preview; and
- `diagnostics` creates a local diagnostic bundle and never uploads it.

## Formats and language tooling

The `rmf2-v1` layout is an implemented, bounded execution profile of RMF2; it is
not a claim of full Unicode MF2 conformance. The [RMF2 implementation guide](../../../docs/guides/translations/rmf2.md)
documents its grammar, resource discovery, inline-markup contracts, and current
limits. Existing TOML and legacy MF2 projects retain their layouts and version
markers.

The editor does not maintain a separate translation language or semantic model.
Its RMF2 behavior follows the shared [compiler](../../../tools/Runic.Translations.Compiler/README.md)
and [authoring tools](../../../packages/dotnet/Runic.Translations.Authoring/README.md),
which also serve the CLI and IDE integrations.

General IDE language tooling likewise belongs on that shared semantic authority;
the editor is not an alternative language-server implementation.
