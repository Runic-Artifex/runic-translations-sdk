# RMF2 implementation guide

`sourceLayout: "rmf2-v1"` opts one catalog into recursive `.rmf2` resources and
Runic inline markup contracts. Existing `locale-toml` and legacy `.mf2` projects
keep their existing formats and version markers. This implementation is a bounded
execution profile of the accepted proposals, **not full Unicode MF2 conformance**.
The remaining proposal work is listed below.

## Resources and discovery

```json
{
  "schemaVersion": 1,
  "sourceLayout": "rmf2-v1",
  "catalog": "app",
  "code": { "namespace": "Example", "className": "AppText" },
  "baseLocale": "en"
}
```

```rmf2
# Checkout copy
checkout {
  @param $count - Number of items
  @example {"count":2}
  items =
    .input {$count :integer}
    .match $count
    one {{One item}}
    * {{{$count} items}}
}
```

A root `en.rmf2` containing that group and `checkout/en.rmf2` containing `items`
contribute the same logical path `checkout.items` and generated ID
`checkout_items`. Splitting and joining files does not change caller IDs.
Underscores in source identifiers stay literal: `checkout_items` is not inferred
to be two segments. Ambiguous flattened IDs are errors.

Files are named `{locale}.rmf2`. Directory segments and explicit group names form
the namespace. Groups may reopen across files; only one declaration per locale
may own a group's documentation. Leaves may not duplicate or collide with groups.
Duplicate diagnostics identify both declarations. Case-only directories, filename
aliases for one canonical locale, overlapping mounts, mixed source layouts, and
symlink traversal are rejected.

Without `sourceRoots`, discovery starts beside `runic.json`. Feature sources can
be mounted explicitly; physical paths are relative to the project configuration:

```json
"sourceRoots": [
  { "path": "../src/checkout/i18n", "namespace": ["checkout"] },
  { "path": "../src/account/i18n", "namespace": ["account"] }
]
```

MSBuild's packaged discovery task and the Vite plugin follow these roots, including
new/deleted files. The C# generator consumes the discovered `AdditionalFiles`.
The source-checkout MSBuild import requires building `Runic.Translations.Build`
first to enable mounted discovery; packaged consumers receive that task assembly.
Open the common containing workspace directory in the desktop editor when mounts
lie outside the configuration directory.

## Framing, metadata, and caller contracts

A group delimiter occupies its own structural line. Structural indentation uses
spaces, with siblings at the same depth. One optional space after `=` is framing;
the remaining inline body is message text. Multiline bodies start deeper than the
entry. The first nonblank line establishes the margin; every nonblank continuation
must meet it. Interior indentation and blank lines remain message content.
Leading/trailing blank framing lines are excluded. CRLF is normalized to LF in
the extracted message while each UTF-8 boundary maps back to the original file.
Use a quoted MF2 pattern for intentional leading/trailing newlines or `{{}}` for
an empty pattern. `#` or `=` inside a body is ordinary text.

Comments and `@param $name - description` / `@example {JSON}` attach to the next
entry or group without an intervening blank line. Parameters and example input
types are checked; unknown properties remain in the resource model with a warning.
The raw source, segment paths, comments, properties, spans, and byte map are public
through `Rmf2ResourceReader`. `Read` recovers the resource outline;
`Analyze` adds execution-profile message diagnostics.

The base locale defines caller inputs and functional slots. Translations may omit
unused inputs, select on them differently, reorder content, and add decorative
markup. They cannot introduce inputs or change functional slot kinds. Source
selector trees do not participate in the RMF2 caller fingerprint. Integer/number
selectors default to cardinal selection; explicit `select=ordinal` and
`select=exact` remain available. Numeric exact keys are tested before a variant
fails its match.

Fallback remains per message. The resolved source locale accompanies content and
controls formatting and accessible icon names. Terms and group-atomic fallback
remain deferred.

## Inline markup

The default aliases resolve to `runic:strong`, `runic:em`, `runic:bold`,
`runic:italic`, `runic:code`, `runic:br`, `runic:link`, `runic:action`, and
`runic:icon`. `br` and `icon` are standalone; other defaults are paired.

```rmf2
payment = Read {#link ref=terms}terms{/link}. {#action ref=retry}Retry{/action} {#icon ref=star/}
```

`ref` is a static application slot ID. Link/action may omit it, selecting `link`
or `action`; icons require an explicit ID. Each source slot defaults to exactly
one occurrence in every variant. Conditional/repeated slots require explicit
`markup.slots.<generatedId>.<slot>: {"min":0,"max":1}` bounds, checked against all
source and translated variants. Interactive elements may not nest.

Custom tags use language-neutral declarations in `runic.json`, without executing
application code during compilation:

```json
"markup": {
  "contracts": [{
    "name": "shop:badge", "kind": "paired", "children": "inline",
    "interactive": false, "plainText": "children",
    "options": { "tone": {
      "type": "enum", "values": ["neutral", "positive"], "default": "neutral"
    }}
  }],
  "aliases": { "badge": "shop:badge" }
}
```

Options support string, number, boolean, and enum types, defaults, and
`literalOnly`. `tone=$tone` resolves an input; literal options are checked at
build/load time, dynamic options again before invoking a renderer factory.
`@attributes` are preserved separately as inert annotations and never become UI
properties. Applications map allowed options explicitly.

Generated ESM exports `linkBinding`, `actionBinding`, `iconBinding`, `defineMarkup`,
`enumOption`, `bindMarkup`, `createInlineRenderer`, `createDomInlineRenderer`, and
`toPlainText`. Message return types carry the union of possible slots;
`renderer.render(content, {slots})` checks their typed kinds. Custom contracts and
the generated registry are deeply frozen. `extend` composes a new renderer and
rejects duplicate or incompatible registrations.

The DOM adapter uses text nodes, semantic emphasis, links, buttons and explicit
icon accessibility; it never creates HTML from translation strings. Rendering
does not activate actions. Application adapters own routes, callbacks and assets.
Occurrences use the generated message ID, variant index, and node path; static and
external-pack formatting produce the same identities. SSR integrations must
still use matching source/contract versions and supply their own hydration adapter.

.NET exposes `Rmf2InlineRenderer`, typed `InlineLinkBinding`,
`InlineActionBinding`, `InlineIconBinding`, and semantic `InlineMarkupRun` trees.
Construct the renderer once from the generated `Rmf2MarkupContract` constant.
Native toolkits map those runs to their own controls and accessibility APIs.
This is a headless native API, not a shipped toolkit-specific renderer.

Plain-text conversion is explicit. `br` becomes LF, link labels are retained
(with optional destination annotation), meaningful icons require a localized
alternate label, and action labels require `allowActionLabels`. Custom web tags
need a linked string renderer; .NET follows declared children/omit/break policies
and rejects unsupported custom alternate-text projection.

The [payment fixture](../../../specs/translations/examples/rmf2/README.md)
contains two locales, conditional retry, two links, an icon, a dynamic custom badge,
a feature directory and an executable DOM example.

## CLI, editor, and language service

```sh
runic-translations validate --project translations
runic-translations generate --project translations --output obj/translations --emit-csharp --emit-json --emit-esm
runic-translations migrate-rmf2 --project translations --dry-run
runic-translations migrate-rmf2 --project translations
runic-translations lsp
```

Migration starts from `locale-toml`, builds a complete proposed catalog, and uses
the existing revision-checked filesystem transaction. It keeps original TOML
bytes as `.toml.bak`, reports uncertain comment ownership, preserves explicit
segment paths, and rejects destination collisions. The existing legacy-to-TOML
migration remains separate. Older readers cannot read RMF2; keep the backups when
rolling back and regenerate output for the chosen layout.

`Rmf2Workspace` accepts unsaved buffers and revisions. Rename, format,
extract-group, and inline-resource produce validated transaction plans. Extracting
a documented group leaves its authoritative declaration and metadata in place.
Rename of namespaces supplied by explicit mounts currently requires editing the
mount configuration; it is not exposed as an automatic file operation.

The stdio LSP implements incremental changes, UTF-8/16/32 position negotiation,
recoverable diagnostics, flat document symbols, folding, formatting, basic
completion/hover, logical definitions, and versioned resource rename edits.
The compiler-owned `Rmf2LanguageService` supplies registered custom tags and aliases,
missing markup options, enum values and literal-only/default/required constraints
in completion details and hover. Standalone tags do not offer closing tags.
Variable suggestions use semantic tokens rather than literal text. Definitions and
references for caller inputs span translations of the same logical resource,
including mounted files and unsaved buffers. Locals remain scoped to their own
message, and same-named translation locals are excluded from caller-input results.
Definitions return explicit declarations; an implicitly used input has no
declaration target unless another translation declares it. Application call-site
navigation remains pending.
Local-variable rename uses parsed symbol locations and rejects capture of an
existing variable. Quoted literals and ordinary message text are preserved.
`runic.extractGroup` and `runic.inlineResource` return `WorkspaceEdit` results;
the client applies them. File-changing commands require client create/delete
capabilities. Cancellation notifications are accepted, but expensive requests are
currently synchronous. Open-buffer updates reparse only the changed physical
resource; refactoring validates the complete catalog.

The editor discovers recursive resources and mounted namespaces, exposes logical
rows backed by physical files, edits message values, creates missing translations
in the corresponding locale file, previews and validates through the shared
compiler, and preserves physical revisions. Advanced structural refactor UI and
contract-aware rich preview controls are not yet exposed.

## Versions and remaining proposal work

RMF2 uses resource syntax **1**, execution profile **rmf2-execution-v1**, markup
contract/renderer ABI **1**, and normalized AST/resolved locale artifact **4**.
The additive .NET `Rmf2RuntimeAbiVersion` marker coexists with legacy runtime ABI
1; legacy grammar/AST/pack 2 stays unchanged. Generated ESM retains its existing
module ABI and exports the extra RMF2 marker. Artifact 4 includes the trusted
markup registry, slot requirements, and effective content locales. External pack
loading checks these before activation; payloads never register UI implementations.
`BuildRmf2LocalePacks` is the tooling facade; `BuildLocalePackV2` remains version 2.
Generated dynamic modules retain the historical `decodeLocalePackV2` function name
but check their catalog's actual artifact version.

The named MF2 baseline is LDML **48.2**; the implemented execution subset is
explicit in [rmf2-execution-v1.json](../../../specs/translations/rmf2-execution-v1.json).
The existing nine-family locale matrix still applies. Unsupported function
options produce `RTR0065` rather than being silently ignored or clamped.

`Rmf2ResourceNode.MessageSyntax` exposes the shared `Mf2SyntaxDocument`: original
tokens, expression operands, options, attributes, declarations, selectors and
variant keys with UTF-8 spans. Unknown functions and overlapping markup survive
this syntax pass. The balanced-inline check separately reports `RTR0061`;
expression syntax diagnostics use `RTR0066`. This is an initial data-model slice,
not yet a complete MF2 grammar validator. Unformatted literal locals and their
plain aliases fold into output without becoming caller inputs. Formatting or
selecting a constant local remains an explicit unsupported capability.

The following accepted proposal features remain incomplete:

- Complete MF2 grammar validation and full variant-body spans in the shared
  syntax model; further separation of caller-contract and backend diagnostics.
  The bounded executor still rejects variable-valued formatter options,
  expression annotations and formatted literal locals. Full MF2 execution is
  outside the core milestone.
- Project-aware formatter-option and slot-value completion, fallback and inferred-type hover,
  example previews, catalog-wide input rename, asynchronous cancellation, persistent
  dependency indexes, and automatic configuration edits during mounted renames.
- Toolkit-specific native renderers, framework SSR/hydration adapters, richer
  accessibility policies, pre-resolved renderer dispatch and allocation benchmarks.
- RMF2 structural editor commands and lossless rich XLIFF/Message Resources
  interchange. The existing text-only interchange loss reporting remains in force.
- C++ execution of RMF2, terms, and group-atomic fallback.

These are implementation limits, not claims that the accepted proposals are fully
delivered. No package or repository release version changes with this work.

The [continuation checklist](rmf2-delivery.md) orders the remaining implementation
work, with VS Code and Visual Studio integrations as the final two deliverables.
