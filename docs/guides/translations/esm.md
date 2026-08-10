# ESM backend

`--emit-esm` emits `{catalog}.esm/` beneath the selected output directory. Each
canonical message has an injectively named module under `messages/`; `messages.js`
provides aggregate named exports and `messages.d.ts` supplies typed inputs.
`runtime.js` owns locale canonicalization, fallback, explicit locale overrides,
input validation, and portable scalar formatting. Generated messages consume the
compiler AST and never parse authoring patterns or fetch JSON.

For SSR, pass `{ locale }` on each call. The configurable resolver is synchronous
host state intended for browser applications, not a request-global SSR locale.
The optional Vite package maps `virtual:runic-translations/{catalog}`, `/runtime`,
`/transport`, and `/dynamic` to these ordinary modules and invalidates them on
watched changes.

Dynamic mode is explicit. Schema-v2 locale output uses
`{catalog}.{locale}.locale-v2.json` and carries validated lowered AST rather than
v1 pattern strings. Import `/dynamic`, call `decodeLocaleArtifact` once after
loading JSON, then call `formatDynamicMessage`. The decoder enforces artifact,
grammar, catalog, fingerprint, key, input, selector, node, depth, and size
contracts. A decoded artifact formats only its own locale. Compiled and dynamic
modes return the same plain or structured result shapes.

JavaScript `number` is accepted only for safe integer inputs; `bigint` covers the
full signed integer contract. Decimal inputs use finite JavaScript numbers and
therefore promise semantic, not full .NET-decimal precision, equivalence.
