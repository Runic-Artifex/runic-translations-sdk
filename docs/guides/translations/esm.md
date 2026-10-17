# ESM backend

`--emit-esm` emits `{catalog}.esm-v5/` beneath the selected output directory.
Its module inventory is `web-module-manifest-v3.json` and its ESM ABI is 4. Each
canonical message has an injectively named internal module under `messages/`;
`messages.js` exposes the tree-shakable application namespace `m` and
`messages.d.ts` supplies exact-key, typed inputs.
`runtime.js` owns locale canonicalization, fallback, explicit locale overrides,
input validation, and portable scalar formatting. Generated messages consume the
compiler AST and never parse authoring patterns or fetch JSON.

A message module holds each locale's message compactly. A message that is text
and plain placeholders (`{$name}`, with no function) is written as its parts,
such as `["Hello ", ["name"]]`; any other message is minified AST JSON. A locale
whose message comes from its fallback shares the fallback's object instead of a
copy. The runtime rebuilds the same normalized AST from the parts and validates
every message when its module loads, so the encoding does not change what a
message formats. For a catalog of about 1,300 messages in two locales, the
minified bundle of all messages shrank from 2.0 MB to 0.93 MB, of which
0.32 MB is `runtime.js`.

`server.js` owns an `AsyncLocalStorage` request context. Wrap an SSR operation in
`runWithLocale(locale, operation)` and ordinary message calls inside it resolve
request-locally. Concurrent renders do not share mutable locale state. An
explicit `{ locale }` option still overrides the context for an individual call.
`server.js` imports `AsyncLocalStorage` from `node:async_hooks`, so it needs
Node.js or another server runtime that implements that module, such as Bun, Deno,
or Cloudflare Workers with `nodejs_compat` enabled. Runtimes without
`AsyncLocalStorage` are not supported. Only
server code imports `server.js`; `messages.js` and `runtime.js` have no
Node.js dependency and run in the browser.
The optional Vite package maps `virtual:runic-translations/{catalog}`, `/runtime`,
`/transport`, and `/dynamic` to these ordinary modules and invalidates them on
watched changes.

`transport.js` writes, decodes and resolves [text references](transport.md):
`createTextReference`, `decodeTextReference`, `formatTextReference` and
`resolveTextReference`, which shows a stored reference after the catalog has
changed.

## Without Vite

The generated directory is a set of ordinary ES modules with relative imports and
`.d.ts` declarations; it uses no `import.meta.glob`, virtual module, file read or
bundler transform. The `virtual:runic-translations/...` names exist only in the
Vite plugin, which re-exports these files. Bun imports the generated files
directly, for example `import { m } from "./generated/app.esm-v5/messages.js"`,
from JavaScript or TypeScript, and `bun build` bundles them; Node.js also loads
them as ES modules. Generate the directory with `dotnet tool run
runic-translations -- generate --emit-esm` or the build package, and point the
Vite plugin's `manifest` option at its `web-module-manifest-v3.json` so a
frontend, a server and other processes share one catalogue. The
[Bun operation-history example](../../../examples/bun/operation-history/README.md)
runs a Bun server and a separate process this way; CI runs it.

## Messages

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
mutable locale source with `getLocale`, `subscribe`, and `setLocale`.
`runtime.d.ts` declares a `Locale` union of the catalog's locales, for example
`"de" | "en"`. `locales`, `baseLocale`, `getLocale`, the source returned by
`createLocaleSource`, and `getRequestLocale` in `server.d.ts` use it, so a
source fits a typed `MutableLocaleSource<Locale>` without a cast. Functions
that resolve a tag, such as `resolveLocale`, `runWithLocale` and the
`initialLocale` option, still accept any string; an empty string, such as an
unset `lang` attribute, resolves like an unsupported locale (the base locale
unless `unsupportedLocale` is `exact`). Framework adapters consume this
structural contract and create one source per browser root. SSR uses the
generated server entrypoint rather than a mutable global source.

`localeDirection(locale)` returns `"rtl"` or `"ltr"` for any valid BCP 47
tag, not only the catalog's locales, so the page can set its direction with
the language:

```ts
import { getLocale, localeDirection } from 'virtual:runic-translations/app/runtime';

const locale = getLocale();
document.documentElement.lang = locale;
document.documentElement.dir = localeDirection(locale);
```

The script decides: an explicit script subtag, otherwise the likely script
that `Intl.Locale.prototype.maximize()` finds in CLDR's likely-subtags data. A
script is right-to-left when CLDR's script metadata marks it so (Arabic,
Hebrew, Syriac, Thaana, N'Ko, Adlam and others; the list is pinned in
`specs/translations/cldr/runic-subset-48.2.json`). So `ar`, `he`, `fa`, `ur`,
`pa-PK` and `ku-Arab` are right-to-left, and `ku`, `he-Latn` and `sd-Deva` are
left-to-right. `Intl.Locale.prototype.getTextInfo()` is not used: it is missing
in older engines, and JavaScriptCore answers `ltr` for tags such as `en-Arab`
and `az-IR`. Without `Intl.Locale`, only an explicit script subtag counts. An
invalid tag throws a `RangeError`, as in `resolveLocale`. In .NET, use
`CultureInfo.TextInfo.IsRightToLeft`; it is always `false` with
`InvariantGlobalization`.

Dynamic loading consumes locale artifact 5 and validates the
closed artifact envelope, profile and grammar 5, catalog, caller fingerprint,
locale and content-locale mappings, linked markup/slot contract, and bounded
typed message semantics. Runtime ABI 3 and the source hash are metadata of the
separate `web-module-manifest-v3.json`, where the Vite plugin validates them;
they are not locale-artifact members. Import `/dynamic`, call
`decodeLocaleArtifact` once after loading JSON, then call
`formatDynamicMessage`. The decoder enforces artifact, grammar, catalog,
fingerprint, key, input, selector, node, depth, and size contracts. A decoded
artifact formats only its own locale. Compiled and dynamic modes return the same
plain or structured result shapes. A document message returns a
`LocalizedDocument` (`kind: "localized-document"`); render it with
`createDocumentRenderer`, which adds a `block` factory to the inline factories,
render it as semantic HTML with `createDomDocumentRenderer` (see
[web document adapters](rmf2.md#web-document-adapters)), or project it with `toPlainText(content, {slots, listMarker})`. Both runtimes
reject a pack whose document message has a block structure outside the
generated contract as `RTR0023/document-structure-mismatch`.

An integer input (`:integer`) takes a safe-integer JavaScript `number` or a
`bigint`, which covers the full signed integer contract. A decimal input
(`:number`, `:runic:relative-time`) takes a finite JavaScript `number`, a
decimal string such as `"12.50"`, or the exact coefficient/scale carrier that
`decimal("...")` returns: `m.hours({ count: 1.5 })`. A `number` stands for the
decimal that `String(value)` writes, its shortest round-trip form, so `0.1` is
`0.1` and `1e-7` is `0.0000001`; `NaN` and the infinities are rejected. Pass a
string or `decimal("...")` for more digits than a `number` holds, such as
`"0.10000000000000000001"`. `createTextReference` accepts the same values.
