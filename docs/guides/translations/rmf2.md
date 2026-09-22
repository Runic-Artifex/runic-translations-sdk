# RMF2 implementation guide

`sourceLayout: "rmf2-v1"` opts one catalog into recursive `.rmf2` resources and
Runic inline markup contracts. Legacy `.mf2` projects
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

Typed execution is a separate, explicit opt-in on the same resource layout:

```json
{
  "schemaVersion": 1,
  "sourceLayout": "rmf2-v1",
  "executionProfile": "rmf2-execution-v2",
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
external-pack formatting produce the same identities. The Svelte `./inline` entry point supplies a structural factory and
`LocalizedInline` component for SSR and hydration. It preserves occurrence keys,
native controls, custom snippets and callback teardown; use matching
source/contract versions on server and client.

.NET exposes `Rmf2InlineRenderer`, typed `InlineLinkBinding`,
`InlineActionBinding`, `InlineIconBinding`, and semantic `InlineMarkupRun` trees.
Construct the renderer once from the generated `Rmf2MarkupContract` constant.
Native toolkits map those runs to their own controls and accessibility APIs.
`Runic.Translations.Wpf` maps them to native inline controls and automation peers,
with explicit custom factories and callback deactivation when replacing or
clearing content. Its maintained Windows consumer passes native execution and accessibility
checks in an interactive Windows 11 desktop session, and cross-compiles on Linux.

Plain-text conversion is explicit. `br` becomes LF, link labels are retained
(with optional destination annotation), meaningful icons require a localized
alternate label, and action labels require `allowActionLabels`. Custom web tags
with `children`, `omit`, or `lineBreak` policies use those projections without a
linked string renderer; explicit/alternate-text policies still require an
adapter. .NET follows the same declared policies and rejects custom
explicit/alternate-text projection when no adapter is available.

The [payment fixture](../../../specs/translations/examples/rmf2/README.md)
contains two locales, conditional retry, two links, an icon, a dynamic custom badge,
a feature directory and an executable DOM example.

## CLI, editor, and language service

New projects use RMF2. Create one with `runic-translations init`, then validate,
generate, or start the language service:

```sh
runic-translations init --directory translations --catalog app \
  --default-locale en --namespace Example --class AppText
runic-translations validate --project translations
runic-translations generate --project translations --output obj/translations --emit-csharp --emit-json --emit-esm
runic-translations lsp
```

`Rmf2Workspace` accepts unsaved buffers and revisions. Rename, format,
extract-group, and inline-resource produce validated transaction plans. Extracting
a documented group leaves its authoritative declaration and metadata in place.
Mounted namespace renames update configuration while preserving physical roots.
Resource moves/duplicates/deletes and slot renames update affected slot contracts;
locale add/remove/fallback operations validate the complete fallback graph.

The stdio LSP implements incremental changes, UTF-8/16/32 position negotiation,
recoverable diagnostics, flat document symbols, folding, formatting, contract-aware
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
navigation remains owned by native language services; Runic's resource
references are intentionally scoped to catalog sources. Standard resource F2
therefore refuses a workspace containing application or legacy sources (or
unindexable links) instead of returning a partial edit. The explicit
resource-source transaction is the opt-in boundary; callers must update native
application call sites separately.
Local-variable rename uses parsed symbol locations and rejects capture of an
existing variable. Quoted literals and ordinary message text are preserved.
`runic.extractGroup` and `runic.inlineResource` return `WorkspaceEdit` results;
the client applies them. File-changing commands require client create/delete
capabilities. A separate reader processes cancellation while one worker owns
request state; cancelled and stale requests return protocol errors rather than
obsolete edits. A bounded cache reuses byte-identical syntax across workspace
requests. New unsaved RMF2 files participate in their configured source roots.
Refactors validate the complete catalog.

`runic.preview` returns normalized artifacts and examples; `runic.renderPreview`
executes the verified .NET plan with inert functional bindings. Hover adds
compiled input types and content/fallback locales when validation succeeds.
Explicit `runic.renameInput`, `runic.renameSlot` and `runic.renameResource`
commands return resource-source transactions. Configuration-changing edits require a client that
synchronizes `runic.json` through `runicConfigurationSync` initialization options.

The editor discovers recursive resources and mounted namespaces, exposes logical
rows backed by physical files, edits message values, creates missing translations
in the corresponding locale file, previews and validates through the shared
compiler, and preserves physical revisions. Structural create/move/rename/
duplicate/delete and locale/fallback workflows use shared transactions, and rich
preview controls render the normalized inline tree. Explicit execution-v2
projects are compiled with the v5 carrier; AST 5 preview requests execute the
verified .NET artifact/pack plan on the editor host and return inert semantic
runs. The frontend never coerces AST 5 into the v4 JavaScript executor.

Editor changes are resource-only and do not rewrite application call sites.
Its closed XLIFF 2.1 text profile losslessly round-trips direct plain resources
for either execution profile. Structured declarations, expressions, selectors,
or markup produce the existing semantic-loss report, and the corresponding
text-profile import is refused rather than approximated.

The version-explicit [RMF2 v1 corpus](../../../specs/translations/corpus/rmf2-v1/README.md)
is the shared release oracle for the execution-v2 boundary. The compiler,
generated C#, .NET artifact-v5 loader, generated ESM, and ESM dynamic-pack
loader consume its common typed execution and rejection expectations. It is
evidence for the implemented profile, not a claim of general Unicode MF2 or
general XLIFF conformance.

The [VS Code extension](../../../tools/vscode-runic-translations/README.md) and
[Visual Studio extension](../../../tools/visualstudio-runic-translations/README.md)
package this language server with native IDE navigation and preview commands.
Their READMEs describe configuration, supported refactor scope and host checks.

## Versions and execution limits

RMF2 resource syntax and markup contract/renderer ABI remain version **1**. When
`executionProfile` is omitted, profile **rmf2-execution-v1** uses normalized AST
and resolved locale artifact **4**, .NET RMF2 ABI requirement **1**, and ESM ABI
**3**. Explicit **rmf2-execution-v2** uses grammar/artifact **5**, .NET RMF2 ABI
requirement **2**, and ESM ABI **4**. The generator, CLI/MSBuild integration, and
Vite plugin dispatch from this selector; they never infer v5 from `.rmf2` files.

Artifacts 4 and 5 include the trusted markup registry, slot requirements, and
effective content locales. External pack loading checks these before activation;
payloads never register UI implementations. The public tooling
`BuildRmf2LocalePacks` facade remains v4, while profile-aware build and CLI hosts
own v5 emission. `BuildLocalePackV2` remains version 2.

The named MF2 baseline is LDML **48.2**; the implemented execution subset is
explicit in [rmf2-execution-v1.json](../../../specs/translations/rmf2-execution-v1.json).
The existing nine-family locale matrix still applies. Unsupported function
options produce `RTR0065` rather than being silently ignored or clamped.

`Rmf2ResourceNode.MessageSyntax` exposes the shared `Mf2SyntaxDocument`: original
tokens, expression operands, options, attributes, declarations, selectors and
variant keys with UTF-8 spans. Unknown functions and overlapping markup survive
this syntax pass. The balanced-inline check separately reports `RTR0061`;
expression syntax diagnostics use `RTR0066`, and data-model errors use `RTR0067`.
The deterministic grammar pass retains full variant, key and pattern spans,
Unicode names, declarations, literals, options and annotations. Unformatted
literal locals and their plain aliases fold into output without becoming caller
inputs. Syntax support remains distinct from the bounded executor.

A declaration cannot bind a variable that appeared in any previous declaration,
including variable-valued options. Thus `.local $a = {$n}` followed by
`.input {$n :number}` reports `RTR0067` (Duplicate Declaration) on the later
`$n`; declare the input before the local instead. NFC-equivalent names have the
same identity. Quoted literal or annotation text does not count as a variable
reference. Local forward references and cycles are also data-model errors.
An `.input` can use its own variable as the operand but not within its function
options. For example, `.input {$s :string select=$s}` reports the same Duplicate
Declaration diagnostic at the input binding's name.

When the execution selector is omitted, variable-valued formatter options,
expression annotations, formatted literal operands/locals, and quoted wildcard
execution remain v1 limitations. Execution-v2 implements those typed semantics.
C++ RMF2, terms, and group-atomic fallback remain unsupported in both profiles.
Arbitrary rich XLIFF is outside the supported text-profile
scope; exports report semantic loss. Application-language call-site refactors
are delegated or refused, never implemented as blind text replacement.
Direct legacy-to-RMF2 migration, marketplace distribution, and rich XLIFF are
also outside the frozen RMF2 v1 release boundary.

See [validation and measurements](rmf2-validation.md) for reproducible checks
and the recorded native Windows host results.

The additive [semantic v5 profile](rmf2-semantic-v5.md) defines typed locals,
literal formatting, dynamic options, and precise key selection. Select it
explicitly for .NET/C# and ESM output; omission retains the v1/v4 contract.
