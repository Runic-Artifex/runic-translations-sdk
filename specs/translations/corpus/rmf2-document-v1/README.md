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
marker style (bijective letters past `z`, roman numerals past 3999), a `.match`
message with an empty variant, whitespace collapsing, and an inline message
next to documents. Links and actions are bound through typed functional slots.

- `messages` pins each message's content kind and encoded skeletons.
- `executions` give arguments and a locale and expect the effective content
  locale, the block tree with resolved options and occurrences, and the default
  and custom plain-text projections. A projection that needs action labels is
  expected to fail without `allowActionLabels`.
- `rendererRejections` render an inline message with the document renderer and
  a document message with the inline renderer.
- `compilerCases` are small projects with the expected diagnostics
  `RTR0070` to `RTR0078` as `id:locale:severity`.
- `invalidPacks` mutate the `de` locale artifact and expect the same
  `RTR0023/*` rejection in .NET and ESM.

`determinism` pins the caller fingerprint and the generated artifact hashes, so
an unintended change to the contract or to the emitters fails the runners.

Tables, embeds, nested lists, quotes, code blocks, custom blocks, native
adapters and segment-level XLIFF are outside this version.
