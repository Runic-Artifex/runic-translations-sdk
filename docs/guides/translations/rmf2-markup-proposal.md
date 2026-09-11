# RMF2 markup: defaults, bindings, and extensions

Status: accepted design direction, 10 September 2026. All APIs below are illustrative, not
implemented APIs. This extends the [RMF2 proposal](runic-resource-format-proposal.md).

Canonical delivery scope and state are maintained in
[roadmap W200](../../../../local-planning/content/records/initiatives/W200.md)
and [decision D014](../../../../local-planning/content/records/decisions/D014.md).

## Direction

Use MF2 markup syntax with three layers: a small Runic vocabulary, declarative
markup contracts, and renderer implementations supplied by each UI integration.
Applications can add named elements using the same mechanism as the defaults.
Translation files describe localized content; application bindings provide routes,
actions, assets, and component implementations.

MF2's playground supplies `link`, `bold`, and `star-icon`; the standard does not
assign these names a built-in rendering behavior. It distinguishes opening,
closing, and standalone markup. [MF2 markup guide](https://messageformat.unicode.org/docs/reference/markup/)

Keep the MF2 data model before applying a renderer profile. MF2 permits unpaired
and overlapping markup; Runic's default inline UI profile should require balanced
nesting, paired/standalone kinds appropriate to each registered tag, and no nested
interactive elements. These are Runic renderer constraints, not MF2 syntax rules.
Keep options separate from `@attributes`: options carry resolved values, whereas
MF2 attributes are annotations without formatting effects. Custom canonical tag
identities should be namespaced. [Unicode MessageFormat specification](https://www.unicode.org/reports/tr35/tr35-messageFormat.html)

## Defaults

Ship the following with first-party rich-text adapters. A headless formatter
produces structured parts and does not depend on a UI toolkit.

| Short RMF2 spelling | Kind | Meaning | Web adapter | Native adapter |
| --- | --- | --- | --- | --- |
| `strong` | Paired | Strong importance | `strong` | Importance semantic plus emphasized text style |
| `em` | Paired | Stress emphasis | `em` | Emphasis semantic plus text style |
| `bold` | Paired | Visual bold treatment | Styled span | Bold text span |
| `italic` | Paired | Visual italic treatment | Styled span | Italic text span |
| `code` | Paired | Inline code | `code` | Monospace/code text span |
| `br` | Standalone | Explicit line break | `br` | Line break |
| `link` | Paired | Application-bound navigation | Anchor/router integration | Native navigation span/control |
| `action` | Paired | Application-bound action | Button integration | Native action control |
| `icon` | Standalone | Application-bound inline asset | Asset component | Native image/icon run |

`bold` should not be an alias for semantic importance, nor `italic` for emphasis.
Renderer themes control appearance; translation files do not supply CSS classes,
font sizes, style strings, or toolkit properties. Native semantic support varies:
adapters must report their actual accessibility capabilities rather than claim
identical platform behavior.

`link`, `action`, and `icon` are available slot types, but do not invent a route,
callback, or image when none is supplied. An `icon` primitive does not imply a
bundled icon collection. `star-icon` is a useful application-defined tag or alias,
not a universal Runic asset.

Keep `kbd`, subscript/superscript, highlighting, and deletion text in an optional
extended inline vocabulary initially. Defer paragraphs, headings, lists, tables,
and arbitrary layout components to a separate document profile. Their content
models and native renderers are substantially different from an inline label.

### Short names and canonical identity

The above short names are explicit RMF2 aliases, such as `bold` to `runic:bold`.
Canonical names appear in compiled contracts and exported MF2. The language
server shows the binding on hover. Export also needs to carry the registry
contract; namespacing alone does not make another formatter implement the tag.

An application can use `shop:badge` directly, or register `badge` as an alias at a
catalog boundary. Aliases are locale-independent, cannot shadow defaults, and
must resolve unambiguously. Feature folders do not silently rebind names. An
alias is resource-level name binding; it does not modify MF2 expression grammar.

## Application-bound slots

```text
checkout {
  terms = Read the {#link ref=terms}terms{/link} and {#link ref=privacy}privacy policy{/link}.
  retry = Payment failed. {#action ref=retry}Try again{/action}.
  favorite = {#icon ref=star/} Add to favorites
}
```

Illustrative application usage, with generated slot requirements:

```ts
render(m.checkout.terms(), {
  slots: {
    terms: linkBinding({ href: routes.terms }),
    privacy: linkBinding({ href: routes.privacy })
  }
});

render(m.checkout.retry(), {
  slots: { retry: actionBinding({ onActivate: retryPayment }) }
});

render(m.checkout.favorite(), {
  slots: { star: iconBinding({ asset: icons.star, decorative: true }) }
});
```

The translator controls wording and can move a link as a whole within a sentence.
The application controls its destination and behavior. `ref` is a static slot ID,
not a URL, import name, DOM ID, or arbitrary expression. For the simple
`{#link}here{/link}` form, infer `ref=link`; multiple distinct links use explicit
references. `action` can similarly default to `ref=action`. Generic `icon`
requires an explicit reference.

Generated message types expose required slot names and kinds. A binding for
`terms` cannot accidentally be an icon. Derive the initial slot set from source
messages, with optional developer-authored constraints for conditional presence
and multiplicity. Calls provide the union of potentially needed slots; formatting
only uses the selected variant. Application input types and slot contracts remain
separate from translator-authored display choices.

Validate required functional slots per variant, not just per locale file. Contract
defaults can require each always-present source slot exactly once; authors must
specify requirements for slots that appear only in some source variants. A target
translation may add/rearrange decorative emphasis without matching the source's
markup tree. New functional slots require a caller-contract change. Slot IDs must
remain stable across translations, and nested links/buttons are rejected.

An icon binding must declare whether it is decorative or provide a localized
accessible name through the application. Decorative icons produce no accessible
name; meaningful icons cannot silently disappear in an accessible/plain-text
projection. If a label is supplied by another localized message, resolve it in the
effective content locale, including fallback. Prefer visible translated words
beside an icon when that expresses the same meaning naturally.

## Custom markup without a special plugin system

Provide a small declaration API that creates a serializable contract. For example:

```ts
export const badge = defineMarkup({
  name: "shop:badge",
  kind: "paired",
  options: {
    tone: enumOption(["neutral", "positive", "warning"], "neutral")
  },
  children: "inline",
  interactive: false,
  plainText: "children"
});
```

A message can then use:

```text
inventory {
  available = Status: {#shop:badge tone=positive}In stock{/shop:badge}
}
```

Register a renderer implementation separately:

```ts
const inventoryRenderer = appRenderer.extend([
  bindMarkup(badge, ({ children, options }) =>
    ui.badge({ tone: options.tone, children })
  )
]);
```

Here `ui.badge` stands for the consuming framework's component/snippet/native
element factory. Framework adapters provide idiomatic bindings and retain normal
component lifecycle semantics. The callback produces a UI value, never an HTML
string. Each renderer binds the same contract to its own implementation.

For a fixed standalone icon, declare `shop:star-icon` with `kind: "standalone"`,
no children, and a renderer that uses the application's star asset. An optional
`star-icon` alias allows exactly `{#star-icon/}`. Asset choice and decorative status
belong to that binding or its developer-controlled contract, not arbitrary markup
properties supplied by a translator.

Option schemas describe types, defaults, allowed values, literal-only constraints,
and translator documentation. Permit variable-valued options when the schema
allows them; type-check the input and validate its value at runtime. Use ordinary
message text/children for prose needing MF2 formatting. MF2 option values are not
nested MF2 programs. Do not forward a markup's option map wholesale into UI props.
Adapter code explicitly maps allowed options; URLs, callbacks, assets, credentials,
and framework services come from typed application bindings.

The source of truth for contracts is language-neutral data. A TS helper can export
that data at build time; JSON-first or .NET declaration tooling can produce the
same manifest. Only one form is authoritative per definition. The compiler, LSP,
and other backends read the manifest without loading application components or
executing project startup code. A library can distribute contracts and optional
renderer adapters together, using an owned namespace such as `acme:badge`.

## Performance and scale

These are design targets, not measured performance claims:

- Resolve aliases, validate literal options, and check content models at build
  time. Precompile messages into a bounded instruction stream. Preserve distinct
  open, close, and standalone operations in the source/interchange model.
- Link the renderer once to the catalog's required tag contracts. It can use
  compact numeric dispatch indices internally; external packs carry canonical
  names and contract fingerprints, not unvalidated process-local numeric IDs.
- Select the message variant first. Resolve dynamic values and allocate UI nodes
  only for that variant. Keep plain-message formatting on its existing fast path.
- Share immutable message plans and option constants. Avoid cloning all constant
  attributes or recursively freezing them on every invocation. Per-render values,
  callbacks, and request context stay local; never cache rendered UI globally.
- Offer direct rendering from a plan for normal use, with a materialized parts
  representation for inspection, transport, or editor preview. Avoid requiring
  several intermediate trees for every label.
- Compose immutable registries at application and feature scope; resolve them
  once, not by repeatedly walking parent scopes per markup token. Overrides must
  be explicit and contract-compatible. Defaults are not silently replaced.
- Emit dependencies per feature pack and tree-shake unused renderer bindings.
  Load a feature's markup adapters with its UI chunk, before rendering that
  feature. Do not dynamically import code for each tag encountered in a message.
- Keep SSR and client contract versions aligned. Use stable occurrence IDs for
  nodes within compiled variants. Preserve component state only where logical
  identity matches; never use translated text as a reconciliation key.

For .NET, an adapter can consume a bounded token/span stream with a visitor or
builder rather than reflectively resolve a component type per node. The exact
allocation strategy should be selected after profiling a realistic consumer.

External packs must be checked against allowed tags/options, slot requirements,
nesting/node limits, and available renderer contracts before activation. Unknown
tags are build or pack-validation errors, not automatic HTML element names.
Missing required bindings are initialization/render errors. Explicit decorative
fallback can unwrap a style tag; functional markup cannot silently become inert.

Define an explicit `toPlainText` policy for email, logs, native surfaces without
rich support, and accessibility projections: inline styles preserve children,
`br` inserts LF, links preserve labels with optional destination annotation,
decorative icons disappear, and meaningful icons require alternate text. An
interactive action has no equivalent behavior in plain text; a consuming surface
must deliberately choose a label-only projection or a separate message. Do not
automatically present a label-only projection as a working interactive UI.

## Tooling and current implementation gaps

The LSP should complete tags, closing tags, slots, and allowed option values;
show renderer availability and plain-text behavior on hover; diagnose illegal
nesting and missing functional slots; and preview with safe placeholder adapters
without executing application callbacks. A slot rename updates the message
contract and all locales, with application code integration where available.

Current source inspection shows reusable structured-output infrastructure, but
several changes are required:

- [MF2 parsing](../../../tools/Runic.Translations.Compiler/Mf2MessageParser.cs)
  checks markup names against an ASCII identifier regex that rejects both
  `star-icon` and namespaced tags. Its option reader stores strings and does not
  preserve the full options/attributes distinction.
- [The compiled message model](../../../tools/Runic.Translations.Compiler/MessageAst.cs)
  stores a markup name, string attributes, and children; standalone tags become
  indistinguishable from empty paired tags. Retain that distinction before
  renderer-specific lowering.
- [The .NET content model](../../../packages/dotnet/Runic.Translations/Formatting/LocalizedTextContent.cs)
  already exposes structured nodes without implicit HTML conversion. Its public
  accessors clone arrays; measure allocations before selecting a replacement API.
- [ESM generation](../../../tools/Runic.Translations.Compiler/Generation/EsmOutputRenderer.cs)
  emits structured elements and has runtime paths that copy/freeze attributes.
  A linked rendering plan is a possible optimization to compare against it.
- Existing tree/pack validation assumes narrower identifier and property models.
  Parser changes alone will not provide cross-runtime compatibility. Version any
  changed pack/ABI contracts and extend shared conformance tests.

Prototype a real payment message containing two links, one action, and a custom
badge in two locales. Verify variant-dependent slots, standalone icons, literal
and variable options, native/web behavior, SSR hydration, external-pack rejection,
and meaningful plain-text projection. Benchmark repeated simple and rich message
rendering for allocations and time, with one representative feature split. This
design has not yet been implemented or benchmarked.
