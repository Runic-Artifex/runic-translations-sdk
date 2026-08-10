# ESM backend

`--emit-esm` emits `{catalog}.esm/` beneath the selected output directory. Each
canonical message has an injectively named internal module under `messages/`;
`messages.js` exposes the tree-shakable application namespace `m` and
`messages.d.ts` supplies exact-key, typed inputs.
`runtime.js` owns locale canonicalization, fallback, explicit locale overrides,
input validation, and portable scalar formatting. Generated messages consume the
compiler AST and never parse authoring patterns or fetch JSON.

For SSR, pass `{ locale }` on each call. The configurable resolver is synchronous
host state intended for browser applications, not a request-global SSR locale.
The optional Vite package maps `virtual:runic-translations/{catalog}`, `/runtime`,
`/transport`, and `/dynamic` to these ordinary modules and invalidates them on
watched changes.

Import `m` from the generated or virtual messages entrypoint. A single-segment
key is available through normal property access. Dotted keys retain their exact
catalog identity and use bracket access:

```ts
import { m } from "virtual:runic-translations/app";

m.Plain();
m["Common.Hello"]({ name: "Ada" }, { locale: "de" });
```

Names such as `m$Common$Hello` are deterministic implementation and filename
details, not public ESM exports. The namespace is backed by static ESM re-exports,
so Vite can remove message modules whose properties are not referenced.

`runtime.js` also exports `createLocaleSource`, which creates an explicitly scoped
mutable locale source with `getLocale`, `subscribe`, and `setLocale`. Framework
adapters consume this structural contract and create one source per browser root
or SSR request. Message calls still receive `{ locale }` explicitly during SSR;
the source does not install request-global state.

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
