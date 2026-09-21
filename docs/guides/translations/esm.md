# ESM backend

`--emit-esm` emits `{catalog}.esm/` beneath the selected output directory for
projects that omit the RMF2 execution-v2 selector. Its module inventory is
`web-module-manifest-v2.json` and its ESM ABI is 3. An `rmf2-v1` project with
`executionProfile: "rmf2-execution-v2"` instead emits `{catalog}.esm-v5/`,
`web-module-manifest-v3.json`, and ESM ABI 4. Each
canonical message has an injectively named internal module under `messages/`;
`messages.js` exposes the tree-shakable application namespace `m` and
`messages.d.ts` supplies exact-key, typed inputs.
`runtime.js` owns locale canonicalization, fallback, explicit locale overrides,
input validation, and portable scalar formatting. Generated messages consume the
compiler AST and never parse authoring patterns or fetch JSON.

`server.js` owns an `AsyncLocalStorage` request context. Wrap an SSR operation in
`runWithLocale(locale, operation)` and ordinary message calls inside it resolve
request-locally. Concurrent renders do not share mutable locale state. An
explicit `{ locale }` option still overrides the context for an individual call.
The optional Vite package maps `virtual:runic-translations/{catalog}`, `/runtime`,
`/transport`, and `/dynamic` to these ordinary modules and invalidates them on
watched changes.

Import `m` from the generated or virtual messages entrypoint. A single-segment
key is available through normal property access. Dotted keys retain their exact
catalog identity and use bracket access:

```ts
import { m } from 'virtual:runic-translations/app';

m.application_title();
m.greeting({ name: 'Ada' }, { locale: 'de' });
```

Names such as `m$Common$Hello` are deterministic implementation and filename
details, not public ESM exports. The namespace is backed by static ESM re-exports,
so Vite can remove message modules whose properties are not referenced.

`runtime.js` exports `locales`, `baseLocale`, and `resolveLocale`, as well as
`createLocaleSource`, which creates an explicitly scoped
mutable locale source with `getLocale`, `subscribe`, and `setLocale`. Framework
adapters consume this structural contract and create one source per browser root
or browser root. SSR uses the generated server entrypoint rather than a mutable
global source.

Dynamic mode is explicit. Schema-v2 locale output uses
`{catalog}.{locale}.locale-v2.json` and carries validated lowered AST rather than
v1 pattern strings. Import `/dynamic`, call `decodeLocaleArtifact` once after
loading JSON, then call `formatDynamicMessage`. The decoder enforces artifact,
grammar, catalog, fingerprint, key, input, selector, node, depth, and size
contracts. A decoded artifact formats only its own locale. Compiled and dynamic
modes return the same plain or structured result shapes.

RMF2 execution-v2 dynamic loading consumes locale artifact 5 and validates the
closed artifact envelope, profile and grammar 5, catalog, caller fingerprint,
locale and content-locale mappings, linked markup/slot contract, and bounded
typed message semantics. Runtime ABI 2 and the source hash are metadata of the
separate `web-module-manifest-v3.json`, where the Vite plugin validates them;
they are not locale-artifact members. V4 RMF2 dynamic loading remains on the
manifest-v2 path when the selector is omitted.

JavaScript `number` is accepted only for safe integer inputs; `bigint` covers the
full signed integer contract. V5 decimal inputs use the exact coefficient/scale
carrier returned by `decimal("...")` (and also accept an exact decimal string),
not a JavaScript `number`. Other numeric contracts may use bounded finite
JavaScript numbers where their documented precision is sufficient.
