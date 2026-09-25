# Translations Editor architecture

Runic Translations Editor is a workspace application for the same translation
sources that application builds consume. It does not define a second message
language, schema, or validation path.

## Source and semantic authority

Each workspace has one `runic.json` declaration. The editor infers direct `.mf2`
or grouped `.rmf2` sources and loads, validates, previews, and saves through the
compiler-backed authoring model. A normal text editor remains a valid way to edit
those sources.

```text
translations/                 # direct MF2 messages
  runic.json
  en/
    application_title.mf2
  de/
    application_title.mf2

translations/                 # grouped RMF2 resources
  runic.json
  en.rmf2
  de.rmf2

translations/                 # grouped RMF2 resources
  runic.json
  en.rmf2
  checkout/
    de.rmf2
```

RMF2 files are recursively discovered as `{locale}.rmf2`; directory segments and
explicit groups form their logical namespace. Explicit `sourceRoots` can mount
feature-local resources. The editor uses the compiler's RMF2 source model and
diagnostics, including the resource/markup contracts that apply to that layout.
The editor uses the compiler's v5 project carrier. Its internal interchange
projection normalizes the typed result only for closed text interchange. It
never converts a project into a retired catalog carrier, which would lose direct-resource
identity, raw syntax, caller contracts, and freshness information.

Discovery is reconciled from the configured roots on every load/check, so
mounted add, change, delete, and rename events are handled as membership
changes. Desktop and IDE hosts must open a common containing workspace when a
mount is outside the configuration directory; no host silently follows a
symlink or reparse point into an unconfigured tree. CLI and MSBuild perform the
same bounded rescan on each invocation/build.

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

The Svelte frontend renders the editor workflow. A generated Runic Views
Window connects typed TypeScript calls to an application-scoped `EditorViewModel`
and `EditorSession` through the CS-WebUI host. The host serves the frontend and owns local application state, diagnostics, and filesystem-facing
workspace operations. The browser profile does not hold durable editor state;
preferences, recent projects, and recovery drafts live in one native per-user
record. Clearing that record does not change workspace files or in-memory work.

The root ViewModel publishes stable routed ViewModels for workspace, document
tools, review, interchange, diagnostics, local state, and project setup, plus a
collection of `EditorDocumentViewModel` instances. Each feature owns named
ReactiveUI commands and its latest typed result. The generated web routes
carry correlated command responses to the typed TypeScript editor bridge.
Selecting a document reads its path, content, and file revision. Validation
and save run on that document's generated commands; `EditorSession` checks the
file revision before atomic replacement. The active input draft and unsaved
review edits stay in the browser UI so they can be discarded or recovered
without changing the shared workspace. Persisted review state and workspace
mutations remain in the compiler-backed session.

Headless commands use the same workspace and compiler path as the interface:

- `validate` reports compiler-backed workspace diagnostics;
- `export`, `report`, and `import --apply` support XLIFF and review JSON
  interchange, with `report` providing the read-only import preview; and
- `diagnostics` creates a local diagnostic bundle and never uploads it.

The XLIFF 2.1 implementation is a deterministic closed plain-text profile.
Direct plain resources round-trip; declarations, expressions, selectors, and
markup appear as explicit semantic-loss entries and are refused on text-profile
import. A review approval uses the closed-text-profile fingerprint, separately
from source freshness, so a compatible review state cannot hide a stale catalog.
Imports and structural transactions write only declared translation resources
and editor review state. They never edit application call sites or convert an
RMF2 project into direct `.mf2` files.

For rich preview, the editor host renders typed v5 requests through the
verified .NET artifact/pack path. The Views command returns validated semantic runs;
the Svelte frontend treats links, actions, icons, and custom markup as inert
display data. It does not load application renderers, execute callbacks, or
coerce AST 5 into a JavaScript evaluator.

## Formats and language tooling

Grouped RMF2 resources implement a bounded execution profile; that is not a
claim of full Unicode MF2 conformance. The [RMF2 implementation guide](../../../docs/guides/translations/rmf2.md)
documents its grammar, resource discovery, inline-markup contracts, and current
limits. Direct MF2 sources remain supported as the alternate source
representation.

The editor does not maintain a separate translation language or semantic model.
Its RMF2 behavior follows the shared [compiler](../../../tools/Runic.Translations.Compiler/README.md)
and [authoring tools](../../../packages/dotnet/Runic.Translations.Authoring/README.md),
which also serve the CLI and IDE integrations.

General IDE language tooling likewise belongs on that shared semantic authority;
the editor is not an alternative language-server implementation.
