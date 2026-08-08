# ESM backend

`--emit-esm` emits `{catalog}.esm/` beneath the selected output directory. Each
canonical message has an injectively named module under `messages/`; `messages.js`
provides aggregate named exports and `messages.d.ts` supplies typed inputs.
`runtime.js` owns locale canonicalization, fallback, explicit locale overrides,
input validation, and portable scalar formatting. Generated messages consume the
compiler AST and never parse authoring patterns or fetch JSON.

For SSR, pass `{ locale }` on each call. The configurable resolver is synchronous
host state intended for browser applications, not a request-global SSR locale.
The optional Vite package maps `virtual:runic-text-resources/{catalog}`, `/runtime`,
and `/transport` to these ordinary modules and invalidates them on watched changes.

JavaScript `number` is accepted only for safe integer inputs; `bigint` covers the
full signed integer contract. Decimal inputs use finite JavaScript numbers and
therefore promise semantic, not full .NET-decimal precision, equivalence.
