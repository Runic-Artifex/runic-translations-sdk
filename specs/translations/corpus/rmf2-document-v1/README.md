# RMF2 document profile v1 conformance corpus

This directory is the shared oracle for
[document profile v1](../../rmf2-document-profile-v1.md): markup contract v2,
runtime ABI 3 and locale artifact v5 with `content` and `skeletons` members.
`index.json` is language-neutral. The compiler, generated C#, the .NET pack
loader, generated ESM and the ESM dynamic-pack runner consume the same cases and
expectations.

The project has the base locale `en`, `de`, and `fr` with an explicit fallback
to `de`. Its messages cover paragraphs, headings with integer literal levels,
bullet lists with a custom list marker, ordered lists with `start` and every
marker style (bijective letters past `z`, roman numerals past 3999) and default
options, a `.match` message with an empty variant, whitespace collapsing, U+3000
at a leaf edge, bidi marks between blocks, empty blocks, a nested inline
occurrence, placeholder values that contain markup syntax or a line feed, custom
`explicit` and `omit` tags, and an inline message next to documents. Links and
actions are bound through typed functional slots. `project.diagnostics` lists
the warnings the project itself is expected to report.

- `messages` pins each message's content kind and encoded skeletons.
- `executions` give arguments and a locale and expect the effective content
  locale, the block tree with resolved options and occurrences, and the default
  and custom plain-text projections. A projection that needs action labels or a
  custom `explicit` projection is expected to fail without one. In `custom`,
  each tag maps to a template in which `{text}` stands for the projected
  children; runners supply it as `Rmf2PlainTextOptions.Custom` in .NET and as
  `custom` bindings in ESM. Supplied projections for tags with any other policy
  are ignored.
- `rendererRejections` render an inline message with the document renderer and
  a document message with the inline renderer.
- `compilerCases` are small projects with the expected diagnostics
  (`RTR0070` to `RTR0078`, and `RTR0061`/`RTR0067` for integer literal
  forms) as `id:locale:severity`.
- `invalidPacks` mutate the `de` locale artifact and expect the same
  `RTR0023/*` rejection in .NET and ESM. `validPacks` apply the same mutations
  with equivalent encodings that both loaders must accept. Mutations edit the
  first variant (or `variant`) of `key`: `editText` adds a `prefix` or
  `suffix` to the `text`-th text node, `insertNodes` inserts `nodes` at node
  index `at`, and `setOption` replaces the value of the first `option`.

`html.json` is the oracle for DOM-based adapters: the canonical semantic HTML
of every execution with heading base 2, attributes in ordinal name order, no
hydration comments or framework scoping classes, and custom tags rendered as
`<span data-runic-markup="<name>" data-runic-occurrence="...">`. The generated
DOM adapter (compiler corpus tests) and the Svelte `LocalizedDocument`
(`packages/web/translations-svelte`) must both produce it. Update it together with
any intended change to the adapters.

`determinism` pins the caller fingerprint and the generated artifact hashes, so
an unintended change to the contract or to the emitters fails the runners.

Tables, embeds, nested lists, quotes, code blocks, custom blocks and
segment-level XLIFF are outside this version.
