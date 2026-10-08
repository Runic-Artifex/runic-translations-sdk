# RMF2 implementation guide

Runic projects use either direct `.mf2` messages or recursive grouped `.rmf2`
resources with inline markup contracts. The representation is inferred from the
source files; mixing them in one project is rejected. This implementation is a
bounded execution profile of the supported contract, **not full Unicode MF2 conformance**.
The `rmf2-execution-v2` release boundary is frozen for this preview; the
exclusions near the end of this guide are deliberate contract boundaries, not
unfinished work inside this release.

## Resources and discovery

```json
{
  "schemaVersion": 1,
  "catalog": "app",
  "code": { "namespace": "Example", "className": "AppText" },
  "baseLocale": "en"
}
```

All supported projects use the typed `rmf2-execution-v2` model and emit v5
artifacts. A configuration selector cannot opt back into a retired carrier.

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
aliases for one canonical locale, overlapping mounts, mixed source
representations, and
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

As in Unicode MessageFormat 2, numeric selection uses the number as it is
displayed. Percent style multiplies by 100, `maximumFractionDigits` rounds and
`minimumFractionDigits` adds visible zeros before the CLDR rules apply. In
English, `{$n :number minimumFractionDigits=1}` displays `1.0` for 1 and selects
`other`, and `{$rate :number style=percent}` selects `one` for 0.01 (`1%`). Exact
keys compare the same displayed value: 1 as a percent matches key `100`. The
typed value is unchanged, so another expression over the same variable starts
from the original number.

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

Options support string, number, integer, boolean, and enum types, defaults, and
`literalOnly`. Integer options take optional `minimum`/`maximum` bounds and
accept only canonical decimal integers such as `3` or `-1`. Custom contracts
may declare `"placement": "inline"`, the default and only custom placement. `tone=$tone` resolves an input; literal options are checked at
build/load time, dynamic options again before invoking a renderer factory.
`@attributes` are preserved separately as inert annotations and never become UI
properties. Applications map allowed options explicitly.

Generated ESM exports `linkBinding`, `actionBinding`, `iconBinding`, `defineMarkup`,
`enumOption`, `bindMarkup`, `createInlineRenderer`, `createDocumentRenderer`,
`createDomInlineRenderer`, and `toPlainText`. Message return types carry the union of possible slots;
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

The readable generated C# surface types each structured message's slots. A
message with markup returns `LocalizedTextContent<AppTextSlots.key>`, and
`Bind(new(...))` takes one constructor argument per slot, in slot-ID order, typed
by its kind:

| Slot kind | C# binding type |
|---|---|
| `runic:link` | `InlineLinkBinding` |
| `runic:action` | `InlineActionBinding` |
| `runic:icon` | `InlineIconBinding` |

Every slot is a required constructor argument, including a conditional
(`min: 0`) slot, because a translation may use it. A slot ID that is not an
ASCII identifier, such as `help-link`, uses its encoded name (`r_<hex>`) as the
argument and property name. `Rmf2InlineRenderer.Render`, `ToPlainText` and WPF
`SetContent` accept the resulting `BoundLocalizedTextContent` and apply the same
runtime checks as the string-key overloads. Unlike ESM's structural
`SlotBindings<S>`, C# slot types are nominal per message: another message's
slots do not convert even when their shape matches.

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

## Document messages

A message whose top level is made of blocks is a *document*. The block
vocabulary is built in: `p` (paragraph), `h level=1..6` (heading), `ul` and
`ol` (flat lists; `ol` takes `start` and `marker`), and `li` (list item). `p`,
`h` and `li` contain ordinary inline content.

```rmf2
help =
  {#h level=1}Getting started{/h}
  {#p}Open the {#strong}Settings{/strong} page.{/p}
  {#ol}{#li}Choose a language.{/li}{#li}Restart the app.{/li}{/ol}
```

The base locale decides whether a message is inline or a document, and every
translation must keep that kind (`RTR0070`). Translators change text and inline
markup, but the block structure (which blocks, in which order, with which
options) must match a source variant (`RTR0074`). Inside a block, line breaks in
the source become one space, except next to `br`, U+200B, or between CJK wide
characters; leading and trailing whitespace is removed. Inline messages keep
their whitespace exactly.

The `p`, `h`, `ul`, `ol` and `li` names cannot be used as project aliases.
Nested lists, tables, quotes and custom block elements are not part of this
version. The normative rules are in the
[document profile](../../../specs/translations/rmf2-document-profile-v1.md).

### Rendering documents

A document message's generated C# accessor returns `LocalizedDocumentContent`
(readable surface: `LocalizedDocumentContent<AppTextSlots.key>`, bound with
`Bind(new(...))` like inline content). Render it with `Rmf2DocumentRenderer`,
constructed once from `Rmf2MarkupContract.Link(...)` over the generated `Rmf2MarkupContract` constant. `Render`
returns a list of `DocumentBlock` records with the canonical `Name`
(`runic:p`, `runic:h`, `runic:ul`, `runic:ol`, `runic:li`), the resolved
`Options` with defaults, the child `Blocks` of a list, the `Inlines` of a
paragraph, heading or list item, and an `Occurrence` path such as `ul[1]/li[2]`.
The path is the same in every locale for the same source variant. Inline
elements inside a document carry `InlineMarkupRun.Occurrence` as
`<block path>/<index path>`, for example `p[1]/0` or `ul[1]/li[2]/1.0`; text
runs have none.
`Rmf2InlineRenderer` rejects document messages and `Rmf2DocumentRenderer`
rejects inline messages.

Generated ESM returns `LocalizedDocument` for document messages. Create a
renderer with `createDocumentRenderer({text, element, block})`; the block
factory is called as `block(name, options, children, {occurrence, locale})`
after its children. The [web document adapters](#web-document-adapters) and the
[WPF document adapter](#wpf-document-adapter) build on it.

`ToPlainText` (.NET, with `Rmf2PlainTextOptions`) and `toPlainText` (ESM)
project documents with fixed rules: top-level blocks are separated by a blank
line and list items by a line break. Unordered items start with the list marker
(default `- `, `ListMarker` / `listMarker`); ordered items start with `n. `,
counted from `start` in the `ol` marker style (decimal, bijective letters, or
roman numerals for 1 to 3999 with a decimal fallback). Further lines of an item
are indented by two spaces. Empty blocks are skipped, so the text never starts
or ends with a line break. `Rmf2PlainTextOptions.Custom` and the ESM `custom`
bindings supply projections for custom inline tags with `explicit` or
`alternateText` policies only; `children`, `omit` and `lineBreak` tags keep their
declared projection even when a binding is supplied.

### Web document adapters

Both web adapters render the same semantic HTML from text nodes and elements,
never from HTML strings, and set no inline `style` attributes, so they work
under a strict content security policy and in SSR output:

| Block | HTML |
| --- | --- |
| `p` | `<p class="runic-leaf">` |
| `h level=n` | `<h{base + n - 1} class="runic-leaf">`; past `h6`, `<h6 role="heading" aria-level="…">` |
| `ul` | `<ul>` |
| `ol start marker` | `<ol>` with `start` (when not 1) and `type` `a`, `A`, `i` or `I` (when not decimal) |
| `li` | `<li class="runic-leaf">` |

Paragraphs and headings with no children or only empty text, and lists with
no items, render no element. List items are always rendered. A paragraph whose
only content is an element, such as a decorative icon, is kept even though the
plain-text projection drops it. Occurrence paths keep counting skipped blocks,
so the paragraph after an empty one is still `p[2]`.

Every block and inline element carries `data-runic-occurrence`, and each
top-level block carries `lang` with the effective content locale, which differs
from the requested locale after a fallback. Leaves keep their text verbatim
through the `runic-leaf` class, which needs `white-space: pre-wrap`. The Svelte
component ships that rule; a page that uses the DOM adapter adds it to its own
stylesheet:

```css
.runic-leaf { white-space: pre-wrap; }
```

The heading base defaults to 2, so `h level=1` becomes `<h2>` under a page's
own `<h1>`; pass `headingBase` (1 to 9) to change it. Browsers number `ol`
items the same way as the plain-text projection: bijective letters past `z`
and decimal past 3999 for roman markers. Inline content uses the same mapping
as inline messages, except for `bold` and `italic`: both document adapters
render them as `<b>` and `<i>`, while a standalone `LocalizedInline` keeps
`<span class="bold">` and `<span class="italic">` and the inline DOM renderer
(`createDomInlineRenderer`) keeps a `<span>` with an inline `font-weight` or
`font-style` style.
`code` is `<code>` everywhere.

Generated ESM exports `createDomDocumentRenderer(document, {headingBase,
custom})`. `render(content, {slots})` returns fresh nodes that the adapter
does not track: they are never retired, so the application owns their
lifetime. `setContent(target, content, {slots})` builds the nodes first, so a
binding failure leaves the displayed content untouched, then replaces the
target's children. The target is an `Element` or a `DocumentFragment`, which
includes a `ShadowRoot`. `setContent` and `clearContent(target)` retire the
previous render, so its actions no longer call back and its links lose their
`href`, even when the application retained the detached nodes. Retirement
tracks renders per generated runtime module: content that another catalog's
runtime placed into the same target is replaced but not retired. For a
`DocumentFragment` target, retirement is tracked on the fragment itself: after
the fragment is appended to an element, a `setContent` on that element does not
retire the fragment's earlier render.

```js
import { m } from "./generated/app.esm/messages.js";
import { actionBinding, createDomDocumentRenderer, linkBinding } from "./generated/app.esm/runtime.js";

const renderer = createDomDocumentRenderer(document);
renderer.setContent(help, m.guide_backup({ fileName }), {
  slots: { guide: linkBinding({ href: "/guide" }), check: actionBinding({ onActivate: check }) },
});
```

The Svelte `./document` entry exports `documentFactory` and
`LocalizedDocument`, which renders leaf content with `LocalizedInline`'s markup
in document mode (`<b>` and `<i>`) and accepts the same `custom` snippets and a
`headingBase` prop, validated on every render. Create the catalog renderer
once with `createDocumentRenderer(documentFactory)`. `LocalizedDocument` keys
blocks by their occurrence path and inline elements by name, slot and
occurrence, so hydration adopts the server elements and a different slot at
the same position gets a new control. Component teardown retires actions and
links that leave the tree; a retired action stays inert even if the
application re-attaches and re-enables it. Inline
occurrence keys count the text before an element, so a translation that adds
text before a link changes the link's key; do not keep application state keyed
by inline occurrences across content changes.

```svelte
<script lang="ts">
  import { LocalizedDocument, documentFactory } from "@runic-artifex/translations-svelte/document";
  import { m } from "virtual:runic-translations/app";
  import { actionBinding, createDocumentRenderer, linkBinding } from "virtual:runic-translations/app/runtime";
  const renderer = createDocumentRenderer(documentFactory);
  let { fileName, check } = $props();
  const nodes = $derived(renderer.render(m.guide_backup({ fileName }), {
    slots: { guide: linkBinding({ href: "/guide" }), check: actionBinding({ onActivate: check }) },
  }));
</script>

<LocalizedDocument {nodes} />
```

Both adapters render every execution of the shared document corpus to the
canonical HTML in
[`html.json`](../../../specs/translations/corpus/rmf2-document-v1/html.json).

### WPF document adapter

`Runic.Translations.Wpf` renders document messages with `WpfDocumentRenderer`
into a read-only `FlowDocumentScrollViewer`. Construct it once per catalog from
the generated `Rmf2MarkupContract` constant, with the same navigation handler,
custom inline factories and theme callback as `WpfInlineRenderer`, then call
`SetContent` on the UI thread:

```csharp
var documents = new WpfDocumentRenderer(AppTextCatalog.Rmf2MarkupContract, Navigate);
documents.SetContent(helpViewer, text.Messages.guide_backup(fileName: fileName).Bind(new(
    check: new InlineActionBinding(Check),
    guide: new InlineLinkBinding(guideUri))));
```

| Block | WPF |
| --- | --- |
| `p` | `Paragraph` |
| `h level=n` | bold `Paragraph` with `AutomationProperties.HeadingLevel` `HeadingBase + n - 1` (clamped at 9), exposed to UI Automation as a heading |
| `ul` | `List` with `MarkerStyle="Disc"`, exposed as a UI Automation list |
| `ol start marker` | `List` with `StartIndex` and `Decimal`, `LowerLatin`, `UpperLatin`, `LowerRoman` or `UpperRoman` markers |
| `li` | `ListItem` holding a `Paragraph` without margin, exposed as a list item with its position and set size |

Paragraphs and headings with no children or only empty text, and lists with
no items, are not rendered. List items are always rendered. A paragraph whose
only content is an element, such as a decorative icon, is kept even though the
plain-text projection drops it. Headings and list items are named for UI Automation
by their copy text (action labels and meaningful icon text included, no list
marker), so Narrator reads "Check the result." rather than the bullet. An
`ol` whose numbering would pass 2147483647 throws `TranslationFormatException`,
because WPF list markers count from an `int` `StartIndex`.

`HeadingBase` (1 to 9) defaults to 2. WPF numbers letter markers bijectively
past `z` and falls back to decimal past 3999 for roman markers, like the
plain-text projection. Inline content uses the inline mapping; links and
actions additionally get their slot name as `AutomationProperties.AutomationId`.
Use the slot name, not the inline occurrence key, to find them: an occurrence
key counts the text before an element and changes with the translation.

Each call builds a fresh `FlowDocument` with `Language` and `FlowDirection` from
the effective content locale and binds its font family, size and foreground to
the viewer. These local bindings override an implicit `FlowDocument` style, so
style the viewer instead. Headings, lists and list items are internal
subclasses that still pick up implicit `Paragraph`, `List` and `ListItem`
styles. A binding failure leaves the displayed document untouched. Replacing
or clearing the document (`ClearContent`) retires its callbacks: retained links
and action buttons are disabled and no longer call back. If the application
sets `viewer.Document` itself, the previous render keeps its callbacks until the
next `SetContent` or `ClearContent` on that viewer. The theme callback is
called with the canonical contract name for every block and markup inline
(plain text runs are not passed), for example to size `runic:h` headings by
`AutomationProperties.GetHeadingLevel`.

Copying (and dragging) a selection puts the plain-text projection on the
clipboard instead of WPF's text and rich formats: a selection inside one
paragraph or item copies the selected text; a selection across blocks keeps list
markers, a line break between items and a blank line between blocks. Action
labels and meaningful icon text are copied, link destinations and decorative
icons are not. The clipboard text uses Windows line endings (`\r\n`). The
adapter does not offer RTF or XAML clipboard formats, because they would
serialise each link's `NavigateUri` and leak destinations that the projection
leaves out.

`FlowDocumentScrollViewer` is the supported host: links and action buttons are
tab stops and activate with Enter, there is no editor caret or editor focus
model, and selection and copy work. A read-only `RichTextBox` with
`IsDocumentEnabled` also activates links on a plain click, but it is an editor
with a caret and its own focus handling, so the adapter does not target it.

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
therefore refuses a workspace containing application or other non-resource sources (or
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
commands return resource-source transactions.

Document messages get block-aware tooling. Completion offers block tags at the
root of a document message, inline markup at the root of an inline message (a
translation follows the content kind of its base message, and a message without
content yet gets both), only `li` inside a list, and only inline markup inside a
paragraph, heading or list item; it also offers enum values and small integer
ranges such as heading levels. Hover on a block tag shows its placement, child
model and plain-text rendering, and hover on a message shows its content kind
and, for documents, the locked structure (one skeleton per distinct source
variant). Blocks that span lines fold, and headings appear under their message
in document symbols. Formatting lays out a plain document message with one block
per line and two spaces of indentation per list level; leaf text is kept byte
for byte. Messages with declarations, `.match`, quoted patterns, text between
blocks or unbalanced tags are left unchanged, and the other messages in the file
are still formatted. A quick fix for `RTR0078` joins one line break between
Thai, Lao, Khmer or Myanmar characters; each break of a message has its own fix
named after its lines. For a document message `runic.renderPreview` returns a
`blocks` tree (name, options, occurrence, child blocks and inline `runs`); its
top-level `runs` member holds the plain-text projection as a fallback for
clients that only render inline runs, such as the VS Code and Visual Studio
previews. Configuration-changing edits require a client that
synchronizes `runic.json` through `runicConfigurationSync` initialization options.

The editor discovers recursive resources and mounted namespaces, exposes logical
rows backed by physical files, edits message values, creates missing translations
in the corresponding locale file, previews and validates through the shared
compiler, and preserves physical revisions. Structural create/move/rename/
duplicate/delete and locale/fallback workflows use shared transactions, and rich
preview controls render the normalized inline tree. Document messages render as
inert paragraphs, headings and lists, with the pseudo-localization simulation
applied per block. Projects compile with the
v5 carrier; AST 5 preview requests execute the verified .NET artifact/pack plan
on the editor host and return inert semantic runs. The frontend never coerces
AST 5 into a JavaScript executor.

Editor changes are resource-only and do not rewrite application call sites.
Its closed XLIFF 2.1 text profile losslessly round-trips direct plain resources
for the supported execution contract. Structured declarations, expressions, selectors,
or markup produce the existing semantic-loss report, and the corresponding
text-profile import is refused rather than approximated. The loss entry of a
document message (`XLIFF21-STRUCTURED-MESSAGE`) names the document profile;
segment-level XLIFF for documents is not part of this version.

The version-explicit [RMF2 v1 corpus](../../../specs/translations/corpus/rmf2-v1/README.md)
is the shared release oracle for the execution-v2 boundary. The compiler,
generated C#, .NET artifact-v5 loader, generated ESM, and ESM dynamic-pack
loader consume its common typed execution and rejection expectations. It is
evidence for the implemented contract, not a claim of general Unicode MF2 or
general XLIFF conformance.

The [VS Code extension](../../../tools/vscode-runic-translations/README.md) and
[Visual Studio extension](../../../tools/visualstudio-runic-translations/README.md)
package this language server with native IDE navigation and preview commands.
Their READMEs describe configuration, supported refactor scope and host checks.

## Versions and execution limits

RMF2 resource syntax remains version **1**; the markup contract is version
**2**. The supported execution contract is **rmf2-execution-v2**: normalized AST
and resolved locale artifact **5**, .NET RMF2 ABI requirement **3**, and ESM ABI
**4**. The generator, CLI/MSBuild integration, and Vite plugin use that contract
for both direct `.mf2` and grouped `.rmf2` sources.

Artifact 5 includes the trusted markup registry, slot requirements, and
effective content locales. External pack loading checks these before activation;
payloads never register UI implementations.

The named MF2 baseline is LDML **48.2**; the implemented execution subset is
explicit in [the RMF2 option table](../../../specs/translations/rmf2-execution-v2.json).
The pinned [locale capability matrix](capabilities.md) still applies. Unsupported
function options produce `RTR0065` rather than being silently ignored or clamped.

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

Variable-valued formatter options, expression annotations, formatted literal
operands/locals, and quoted wildcard execution use typed v5 semantics. C++ RMF2,
terms, and group-atomic fallback remain unsupported.
Arbitrary rich XLIFF is outside the supported text-profile
scope; exports report semantic loss. Application-language call-site refactors
are delegated or refused, never implemented as blind text replacement.
Terms, group-atomic fallback, C++ RMF2, rich XLIFF, application-language
call-site rewriting, marketplace distribution, and migration from non-RMF2
legacy formats are outside the frozen RMF2 v1 release boundary. They require a
separately versioned contract and are not prerequisites for this preview.

Retained ABI-1 constructors and compatibility constants are documented in the
[compatibility-retention ledger](compatibility-retention.md). They are read-side
compatibility only: current writers emit the v5/RMF2 ABI-2 contract described
above.

See [validation and measurements](rmf2-validation.md) for reproducible checks
and the recorded native Windows host results, and the
[diagnostics reference](diagnostics.md) for every `RTR` diagnostic.

The [semantic v5 contract](rmf2-semantic-v5.md) defines typed locals, literal
formatting, dynamic options, and precise key selection.
