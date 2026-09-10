# Runic resource format: design proposal

Status: research and proposed direction, 10 September 2026. The syntax, extension,
configuration concepts, and APIs below are illustrative; they are not implemented
Runic features. This document does not change the existing TOML contract.

## Recommendation

Build a small resource language around Unicode MessageFormat, with explicit nested
groups, readable unquoted message entries, attached translator documentation, and
optional directory namespaces. Use `.runic` as a provisional extension.

Keep one logical catalog per locale regardless of its physical file layout. Keep
MF2 as the message language: formatting, declarations, selection, and markup
should retain their standard syntax and semantics. Put Runic's additional value
in resource organization, application contracts, authoring tools, and carefully
specified functions.

Develop the parser and language service together. Precise locations, comment
ownership, and recovery from incomplete edits are design requirements from the
start, not features to reconstruct after compilation discards the source.

## What the research changes

Unicode specifies individual messages and supports embedding them in a container.
The published page inspected identifies itself as LDML 48.2; it distinguishes
stable functionality from draft functions and options. Runic should pin a named
MF2 baseline and list supported functions separately from its resource syntax
version. A parser accepting an expression does not establish that every backend
can execute it. [Unicode MessageFormat specification](https://www.unicode.org/reports/tr35/tr35-messageFormat.html)

There is already a particularly relevant W3C incubation effort: **Message
Resources**. Its explainer proposes hierarchical sections, message entries,
attached comments and properties, and a resource data model. Its text syntax uses
`[section.more]`, `key = value`, and indented continuations. This is a proposal,
not a settled standard. I recommend treating its data model as an interoperability
target while evaluating Runic's recursive group syntax. [W3C Message Resources explainer](https://github.com/w3c/i18n-discuss/blob/gh-pages/explainers/message-resources.md)

Fluent supplies useful precedents for translator-facing features: terms can carry
grammatical variants, and attributes keep related UI text together. These are
useful ideas to evaluate, but adopting Fluent's expression syntax would introduce
another message language. [Fluent terms](https://projectfluent.org/fluent/guide/terms.html),
[Fluent attributes](https://projectfluent.org/fluent/guide/attributes.html)

Runic already has substantial groundwork:

- [The current TOML profile](../../../specs/translations/locale-toml-v1.md)
  supports one file per locale, nested tables and inline groups. It requires
  locale files beside `runic.json`; it joins path segments with underscores and
  rejects resulting collisions.
- [The MF2 parser](../../../tools/Runic.Translations.Compiler/Mf2MessageParser.cs)
  implements a narrower execution profile. For example, expressions must start
  with variables, functions are selected by a fixed switch, and numeric selection
  currently needs an explicit selection option to choose plural behavior.
- [Locale compilation](../../../tools/Runic.Translations.Compiler/TranslationCompiler.Toml.cs)
  maps MF2 diagnostics to the complete containing value, rather than individual
  embedded tokens.
- [Cross-locale contract checking](../../../tools/Runic.Translations.Compiler/TranslationCompiler.cs)
  currently compares placeholder formats, selector count/order/names/functions,
  and whether markup is present.
- [Authoring tools](../../../packages/dotnet/Runic.Translations.Authoring/README.md)
  already support revision-checked edits and grouped physical-file transactions.

The custom format should improve authoring beyond TOML. Enabling broader MF2
execution also requires compiler, contract, and backend work.

## A concrete file

`translations/en.runic`:

```text
common {
  save = Save
  cancel = Cancel
}

checkout {
  title = Checkout

  cart {
    # Displayed above the basket, including when it is empty.
    @param $count - Number of items in the basket.
    summary =
      .input {$count :integer}
      .match $count
      0   {{Your basket is empty.}}
      one {{You have one item.}}
      *   {{You have {$count} items.}}
  }

  payment {
    # The application supplies the link destination and behavior.
    terms = By continuing, you accept the {#app:link}terms{/app:link}.

    submit {
      label = Pay now
      aria_label = Pay for your order
    }
  }
}
```

The parameter documentation supplies context for `$count`; it does not declare a
variable or inject an MF2 `.input` statement. Metadata is described below.

Ordinary messages read as ordinary text. Quotes, apostrophes, `=`, and `#` inside
the value do not need resource-level escaping. `save = Save # now` contains the
literal text `Save # now`; there are no trailing resource comments on entries.

The example uses MF2's default cardinal selection for `:integer`. The current
Runic parser's default behavior must be corrected or explicitly handled by a
new profile before this example has the intended behavior. Numeric selector
functions choose plural categories by default in MF2. [MF2 functions](https://messageformat.unicode.org/docs/reference/functions/)

The resulting path for the plural message is the segment sequence
`[checkout, cart, summary]`, displayed as `checkout.cart.summary`.

### Why explicit group blocks?

| Container choice | Strength | Cost | Assessment |
| --- | --- | --- | --- |
| Existing TOML with MF2 strings | Already implemented; familiar tooling | String delimiters and a second escaping layer; authoring metadata needs conventions | Keep supported |
| W3C-style `[checkout.cart]` sections | Shallow indentation; close to the resource proposal | Repeats full paths; nesting is less visually apparent | Strong alternative |
| `checkout { cart { ... } }` groups | Visible containment and scope endings; local names | Extra braces; message framing still needs precise rules | Preferred custom surface |
| Indentation-only groups | Least punctuation | Moving text can change both namespace and message boundaries | Worth a usability comparison |
| Rewritten plural/select shorthand | May shorten a few examples | A second grammar, documentation, and conversion semantics | Defer |

Use one canonical grouping style initially. Supporting all styles would increase
the cost of formatters, refactors, documentation, and review without adding
message expressiveness. Section syntax remains an import/export candidate; the
block syntax should not be advertised as W3C-compatible serialization.

### Message boundaries and whitespace

The following are proposed framing rules, to make the examples implementable:

1. A group opens with `name {` on a structural line and closes with `}` on its
   own structural line. Group contents use a consistent indentation level;
   two spaces is the formatter default. Tabs in structural indentation are
   rejected. Tabs inside message content remain content.
2. An entry is `name = inline-message`, or `name =` followed by an indented MF2
   body. Inline entries are single-line messages; converting to multiline moves
   the entire body below the assignment. This avoids two continuation rules.
3. For a multiline body, the first nonblank content line establishes a margin
   deeper than the entry. Remove exactly that margin from each content line.
   Additional spaces remain significant. A subsequent nonblank line between the
   entry indentation and that margin is an indentation error, not implicit trim.
4. A nonblank line at the entry's indentation or less ends the body. Resource
   syntax is recognized only outside the body. A `#` or `@` inside the body is
   message content, not a resource comment or property.
5. Blank lines between body content lines are preserved. Blank lines before its
   first content line or after its last are resource trivia. The assignment
   newline and final body-line terminator are framing, not output. CRLF is
   normalized to LF during extraction, with original offsets retained.
6. Preserve MF2 escapes without a second decoding pass. Do not invent JavaScript
   expressions, `${...}`, or a resource-level meaning for `\n` inside messages.
7. Use MF2 quoted patterns when boundary whitespace matters. `empty = {{}}` is
   an explicit empty translation. `name =` without a body is incomplete, not an
   empty translation or a fallback request.

For example:

```text
receipt {
  padded = {{  Total  }}

  lines =
    {{First line
      Indented second line
    }}
}
```

`padded` renders with two spaces on each side. `lines` contains two spaces before
`Indented` and a final LF before the closing quoted-pattern delimiter. MF2 syntax
and container syntax need separate lexer modes; counting every brace as a group
delimiter is insufficient.

Never reflow natural-language lines automatically. Line endings inside patterns
can affect output. A formatter can adjust the structural margin and whitespace
between MF2 declarations or variants without rewriting pattern content. Exact
arbitrary control-character serialization remains a design question for a raw
interchange representation; it should not be conflated with preserving normal
human-authored message text.

The W3C strawman makes different choices: it removes leading continuation
whitespace unless escaped and defines additional resource escapes. Conversion
must therefore encode/decode those differences, rather than just replace group
headers. [Proposed resource grammar](https://github.com/w3c/i18n-discuss/blob/gh-pages/explainers/message-resource.abnf)

## One file, or feature folders, with the same meaning

The default layout stays small:

```text
translations/
  runic.json
  en.runic
  de.runic
```

An optional split layout can coexist with root entries:

```text
translations/
  runic.json
  en.runic
  de.runic
  checkout/
    en.runic
    de.runic
    payment/
      en.runic
      de.runic
```

**Directory segments contribute to the namespace; the locale filename does not.**

| Physical source | Local structure | Logical path |
| --- | --- | --- |
| `translations/en.runic` | `checkout { cart { summary = ... } }` | `checkout.cart.summary` |
| `translations/checkout/en.runic` | `cart { summary = ... }` | `checkout.cart.summary` |
| `translations/checkout/cart/en.runic` | `summary = ...` | `checkout.cart.summary` |

These are alternative homes for the same message, not three definitions to load
simultaneously. An extract-group refactor removes the enclosing groups when it
moves their contents into matching folders. Copying a group unchanged into its
same-named folder would otherwise repeat the prefix; report likely mistakes.

Proposed composition rules:

- Resolve identity as `(catalog, canonical locale, path segments)`. A standalone
  file derives its locale from its filename; exported standalone resources can
  carry locale metadata. If explicit metadata is supported, a mismatch is an
  error rather than an override.
- Recursively discover files under configured source roots. Preserve segments as
  data; do not flatten the tree during discovery. Start with Runic's existing
  identifier-safe segment alphabet and case-sensitive key semantics. Reject
  case-only filesystem aliases for portability and duplicate canonical locales.
- Merge groups by path and reject duplicate message leaves within a locale and
  layer. Report both locations. There is no filesystem-order override behavior.
  A path cannot be both a message and a group; use named children such as
  `label`, `title`, and `aria_label` for related messages.
- Empty groups are legal and create no messages. Reopened groups can contribute
  distinct children. Group metadata has one authoritative declaration per
  locale/layer/path; conflicting declarations are errors.
- Locales may use different physical splits. Completeness checks compare logical
  paths. Missing means absent; an empty message remains present. Keep whole
  message fallback, including its own selectors, as an explicit project policy.
- Preserve existing explicit layer precedence where configured. A more specific
  directory is not a stronger layer. Avoid source-file imports in v1: discovery
  already solves splitting without introducing import order or cycles.

For feature slices colocated with application code, add **explicit mounts in
project configuration**. For example, mount `src/features/checkout/i18n` at
`checkout`; `payment/en.runic` beneath that root adds `payment`. Overlapping
discovery roots should be rejected unless the same physical source is explicitly
deduplicated. Moving the feature directory then preserves its logical namespace
as long as the configured mount stays the same. Config syntax needs its own
schema proposal; these are loader semantics, not supported settings today.

Splitting source files should not automatically split runtime downloads. A
bundler may emit one pack per locale or feature packs independently, with explicit
dependency/fallback handling. That distinction preserves the single-file option
even for applications that later need lazy loading.

### Identity and existing generated APIs

Keep semantic paths separate from generated symbols. Runic currently maps both
`a_b.c` and `a.b_c` to `a_b_c`; adopting dotted paths silently would change its
contracts, generated identifiers, and potentially external packs.

For an initial compatible adapter, retain existing underscore-generated IDs and
collision errors while storing the full path in the authoring model. Existing
flat IDs remain one segment: never infer hierarchy by splitting underscores.
If a later version exposes nested calls such as `m.checkout.cart.summary(...)`,
make that an explicit generation/API decision. Fully admitting paths that collide
under the old mapping needs a versioned identity migration, not just a parser.

## Let translations use the actual power of MF2

Multiple selection dimensions should remain ordinary MF2. For example, this
message combines delivery mode and item count without a new Runic plural syntax:

```text
fulfillment {
  ready =
    .input {$mode :string}
    .input {$count :integer}
    .match $mode $count
    pickup one {{One item is ready for collection.}}
    pickup *   {{{$count} items are ready for collection.}}
    *      one {{One item is ready to ship.}}
    *      *   {{{$count} items are ready to ship.}}
}
```

This visual density is inherent in MF2 when a quoted pattern begins with a
placeholder. Syntax highlighting can distinguish the pattern delimiters from
the expression. A dedicated editor can present variants as rows while preserving
standard source. Multiple selectors, locale-specific plural categories, and
catch-all variants are already part of MF2. Preserve variant order rather than
sorting it as if it were an unordered dictionary. [MF2 matchers](https://messageformat.unicode.org/docs/reference/matchers/)

Likewise, allow `.local` values, literal operands, and variable-valued options
where supported by the selected MF2 baseline. Do not reinterpret `.input` as a
complete application type system: it annotates a message input with function
behavior. [MF2 variables](https://messageformat.unicode.org/docs/reference/variables/)

**Share caller contracts across locales, not grammar trees.** The caller should
supply a compatible set of inputs, with separately specified required inputs and
rich-content slots. A translation should be able to omit an unused input, add a
local binding, change selector order, or introduce a selection using available
inputs. It must not silently require new data from the application. Function
requirements constrain input types, but local display choices need not be
identical across languages. Required-slot rules should be defined independently
of whether a pattern happens to contain any markup.

This is a substantive change to Runic's current `SameContract` checks. It needs
its own compatibility decision. Otherwise, a new syntax could preserve today's
restrictions while appearing to promise unrestricted MF2.

For implementation, keep a lossless syntax representation and full MF2 data
model before lowering to an execution representation. Validate grammar, MF2 data
model rules, resource contracts, and selected backend capabilities as distinct
steps. Unknown functions can remain representable in an editor even when a build
rejects them for an unsupported target. Expand the documented locale-selector
coverage as well as function support; the current public capability matrix is
limited to nine locale families. [Current Runic capabilities](capabilities.md)

## Extensions worth considering

### Attached documentation and examples: first priority

Keep comments and metadata outside MF2 bodies:

```text
checkout {
  # Count excludes items saved for later.
  @param $count - Number of payable items.
  @example {"count": 3}
  item_count =
    .input {$count :integer}
    .match $count
    one {{One item}}
    *   {{{$count} items}}
}
```

Proposed ownership: adjacent `#` comments followed by properties attach to the
next entry or group at that structural level. A blank line breaks attachment;
orphan properties are errors. A comment after a property is rejected to keep the
attachment rule unambiguous. Free comments remain source trivia. `@example`
contains a JSON object checked against the message's caller contract, and may be
repeated. `@param` documents an existing parameter; it does not declare runtime
behavior. Group documentation supplies context; examples attach only to messages.

Allow namespaced metadata for extensions, retain unknown metadata during edits,
and diagnose unknown properties rather than silently executing them. Define a
small versioned vocabulary before adding screenshots, character-budget hints,
or deprecation notes. Keep volatile review history in the existing review system;
translator context belongs close to the message.

### Rich text and related UI messages: build on existing concepts

MF2 markup identifies structured parts; it is not HTML and does not specify a
renderer. Map named slots to application-owned components or native spans, with
explicit permitted options. Show translators which slots are functional and which
are decorative. [MF2 markup](https://messageformat.unicode.org/docs/reference/markup/)

Related messages such as `submit.label` and `submit.aria_label` can already share
a group, documentation, and editor presentation. If they need atomic locale
fallback, define that as a separate group contract. Do not assume all namespace
groups are UI widgets or silently change existing per-message fallback.

### Terms and references: valuable, but a later semantic extension

A namespaced function can use standard MF2 syntax:

```text
about = Learn more about {|brand.product| :runic:term}.
```

The expression syntax is MF2; `runic:term` and its resolution behavior are
proposed Runic functionality. Before implementation, specify absolute term
identity, explicit argument passing, locale resolution, cycles and depth limits,
missing-term diagnostics, dependency tracking, and rich-parts behavior. Choose
one locale for a message and its referenced term graph, or fall back the whole
graph, rather than silently assembling a sentence in several languages.

Terms should allow locale-authored grammatical forms. They should not become an
encouragement to construct sentences by concatenating reusable fragments. Avoid
unrestricted runtime-computed reference keys in the first version: static
references enable useful rename, validation, and feature-pack dependency checks.

Do not initially add arithmetic syntax, loops, general conditionals, implicit
global variables, or a second pattern interpolation language. Evaluate custom
functions against actual application needs and require explicit .NET/ESM/other
target capability declarations.

## Language server and editor architecture

A Runic language server should expose the shared authoring model over LSP. The
CLI and translations editor should use the same parser, diagnostics, and edit
planner. An independent editor-only grammar would drift from compilation.

There is existing MF2 tooling to study: Unicode's editor guide links to
`mf2-tools`, which provides variable completion/rename, diagnostics, formatting,
and semantic highlighting. Its repository describes a Rust parser with error
recovery and native/Wasm server execution, and labels the project GPL-3.0.
It is a useful reference; direct reuse requires evaluating the dependency and
licensing fit rather than assuming it is a drop-in Runic library.
[Unicode editor guide](https://messageformat.unicode.org/docs/lsp/),
[mf2-tools repository](https://github.com/lucacasonato/mf2-tools)

Proposed layers:

```text
Physical UTF-8 files and unsaved buffers
                 |
Resource syntax tree, trivia, and exact source maps
                 |
MF2 syntax/data model + logical catalog index
                 |
Contracts, function registry, locale and target analysis
          /                 |                    \
   LSP services       Editor edit plans       Compiler lowering
```

Start with the features that make the format safe to edit:

| Feature | Expected behavior |
| --- | --- |
| Diagnostics | Mark the exact bad MF2 token; distinguish malformed syntax from unsupported target capability |
| Completion and hover | Inputs, locals, functions, options, plural categories, parameter docs, full mounted key |
| Structure | Fold groups/messages; navigate a logical catalog across physical files |
| Cross-locale navigation | Jump to the same key in another locale and explain fallback provenance |
| Preview | Render supplied examples; identify selected variants and required inputs |
| Refactors | Rename a key/group across locales; extract a group into a folder; inline it back |
| Formatting | Preserve message text, comments, and variant order; format only safe syntax whitespace |

These map to established LSP language and workspace features. Rich previews and
catalog dashboards also need editor UI integration; LSP alone does not provide
a standardized translation preview pane. [Language Server Protocol](https://microsoft.github.io/language-server-protocol/)

Technical requirements:

- Retain both physical UTF-8 spans and the maps into extracted MF2 bodies,
  including removed margins and CRLF normalization. Adapt positions to the
  negotiated LSP encoding; do not assume byte offsets are editor columns.
- Recover at structural entry/group boundaries so one unfinished message does
  not erase the catalog outline. Error recovery is for tools, not permission to
  compile invalid messages.
- Reparse affected regions/files and invalidate dependent keys, locales, or
  reference graphs. A large one-file catalog must remain responsive; do not
  recompile every locale after each keystroke.
- Use revisioned edits and LSP workspace edits, including file operations where
  clients support them. Validate the planned complete catalog before committing
  an extract/move/rename. Check all affected unsaved buffers.
- Distinguish renaming one MF2 local from renaming an application input or message
  key. Updates to C#/TypeScript call sites need language-aware integrations or
  generated-symbol support; text replacement cannot guarantee correctness.
- Build previews from supplied example data and declared formatter capabilities.
  Do not run arbitrary application startup code just to open a locale file.

## Suggested implementation sequence

1. **Validate the authoring experience.** Represent the same small real catalog
   in existing TOML, W3C-style sections, and recursive groups. Include a long
   legal paragraph, whitespace-sensitive text, multiple selectors, rich slots,
   and an RTL translation. Evaluate editing, not just screenshots.
2. **Implement a syntax/authoring slice.** Resource parser with recovery, exact
   source maps, formatter, CLI validation, and an initial LSP using the existing
   execution profile. Introduce an explicit source-layout/version discriminator;
   leave existing layout behavior intact.
3. **Add composition and migration.** Directory mounts, duplicate diagnostics,
   group extraction/inlining, TOML import, and unchanged generated API contracts
   for the compatible subset. Group file edits into existing transactions.
4. **Expand MF2 execution deliberately.** Full data-model representation, refined
   caller contracts, registry-driven validation, broader functions/locales, and
   conformance cases across selected backends. Version changed behavior.
5. **Evaluate semantic additions.** Terms and compound fallback only after real
   consumers establish their requirements.

Useful acceptance cases for that work are: equivalent catalogs from flat/split
layouts; duplicate paths reporting both sources; comments surviving rename and
extraction; invalid MF2 preserving later editor symbols; exact whitespace and
astral/RTL source locations; locale-specific selectors with compatible inputs;
and backend-specific function diagnostics. Import/export must explicitly report
any unsupported metadata or message structure. The existing XLIFF text profile
cannot carry all rich MF2 messages losslessly.
[Current interchange scope](../../../packages/dotnet/Runic.Translations.Tooling/README.md)

This research involved source inspection and comparison with the linked primary
sources. No parser prototype or backend conformance tests were run. The first
decision to validate is whether recursive blocks improve real editing enough to
justify a custom surface over the W3C-style section alternative.
