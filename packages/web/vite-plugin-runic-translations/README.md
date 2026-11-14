# @runic-artifex/vite-plugin-runic-translations

Import generated Runic Translations messages through stable Vite virtual modules, with typed message inputs, production tree shaking, source watching, and HMR. The plugin maps compiler-produced ESM into Vite; it never parses translation authoring JSON in JavaScript.

## Install

```bash
npm install --save-dev @runic-artifex/vite-plugin-runic-translations@<VERSION>
```

Replace `<VERSION>` with the current version shown on npm. The package supports Vite 8.3.3 and later 8.x releases on Node.js 24 or 26 and accepts only the RMF2 `web-module-manifest-v3.json` contract (ESM ABI 4, runtime ABI 3). If the plugin owns generation, install `dotnet-runic-translations` in a project-local .NET 10 tool manifest at that same exact release.

## Configure Vite

```ts
// vite.config.ts
import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [runicTranslations()],
});
```

The no-argument form discovers `translations/runic.json`, compiles either its
direct locale `.mf2` files or grouped `.rmf2` files to `.runic/translations`,
and watches config and message edits, including added, removed, and renamed
source files. A project may select additional source trees with `sourceRoots`;
mixing the two source forms is rejected by the shared compiler. In a split
frontend/backend layout, use `runicTranslations({ project: "../translations" })`.
When another build owns generation, pass its generated `manifest` and optional
`sourceFiles` instead.

## Development server

In project mode, `vite dev` starts one `runic-translations serve` process and
sends it every regeneration, so a save does not pay for starting .NET and
unchanged sources are not recompiled. The process restarts after a crash and
stops with the dev server. When the installed tool has no `serve` command, or the
process crashes three times in a row, the plugin logs a warning and runs
`runic-translations generate` for each change instead. Set
`persistentCompiler: false` to always run one-shot generation. A regeneration
that takes longer than `persistentCompilerTimeout` (default 120000 ms) stops the
process and everything it started, generates that change one-shot, and restarts
the compiler on the next change. `vite build`
always runs the tool once.

### Edited messages update in place

A save is compiled once for every Vite environment (client, SSR), and the plugin
compares the regenerated catalog with the previous one:

- **Text edits update the page in place.** When only message text changed (a
  message's text in any locale, a plural or selector variant, the text inside
  inline markup or a document, a pseudo-locale), the plugin sends Vite one update
  for each edited message's module and nothing else: the browser fetches those
  modules, which hand their new text to the modules the page already holds, so
  the runtime, the catalog's resolvers and installed overlays stay, and the page
  keeps its state. The server's environment gets the same update, so the next
  server render shows the new text.
- **The page then formats again.** The plugin reports each update on the
  [text-change channel](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/esm.md#text-changes-during-development),
  once per update, and whatever listens re-renders its messages, as a locale
  switch does. `useMessageLocale` from `@runic-artifex/translations-svelte`
  listens, and so do the runic-sdk text helpers (`@runic-artifex/views`, and the
  React, Vue, Svelte and Angular packages) from their release that supports it.
  A page with no listener reloads, so it never shows stale text.
- **Everything else reloads the page,** with the reason in the dev server's log,
  for example `[runic-translations] page reload: messages were added, removed or
  renamed in catalog 'app'`: an added, removed or renamed message (`m` is a module
  namespace and cannot gain members), a changed input, markup or content kind
  (`dynamic.js changed (the catalog's contract changed)`), added or removed source
  files, and changes to runic.json.
- **A compile error** shows Vite's error overlay over the last good text. The
  next good save is compared with that last good catalog and updates in place.
- **Rapid saves** (format-on-save, auto-save, a checkout, a tool writing several
  files) are compiled in turn, and the last save always wins. Vite's watcher
  drops a second change of a file within 50 ms, so once a change is handled the
  plugin checks the file again and compiles a save the watcher did not report.

Values computed once rather than while rendering, such as `const title =
m.app_title()` at module level, data returned from a SvelteKit `load` function,
or `document.title` set imperatively, keep their text until they run again, as on
a locale switch. A production build is unchanged: the code that makes message
modules update themselves exists only under the dev server, and the generated
ESM is the same.

The dev server does not watch the generated declarations (`virtual.d.ts`,
`<output>/**/*.d.ts` and the generated directory's `.d.ts` files): the plugin
writes them only when their content changes, and needs no event for them. This
keeps framework plugins that reload on any `.d.ts` change, such as Analog's,
from turning each edit into a reload. Editors and `tsc` still read them from disk.

In project mode the plugin also generates the development pseudo-locales that
`runic.json` declares in `pseudoLocales` (`en-XA`, `ar-XB`) when Vite's mode is
`development`, so `vite dev` shows them and `vite build` leaves them out. Set
`pseudoLocales: true` or `false` to override. In manifest mode another build
generates the directory, and a Debug `dotnet build` includes the pseudo-locales;
unless they are on, the plugin takes them out of the bundle (the runtime's
`locales` and their messages), so a production `vite build` never ships them. See
[Pseudo-locales](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md#pseudo-locales).

Manifest mode treats the supplied generated directory as externally owned.
Changes to `sourceFiles` trigger a Vite refresh, but the plugin cannot recompile
the project or recompute `sourceHash` without a compiler command. The owning
build must regenerate the ESM package and manifest before the refresh is served;
otherwise the plugin will reload the still-current manifest bytes, not detect
that authoring sources are newer than them. Edits that the owning build
regenerates, such as a `dotnet build` that rewrites the directory, update in
place by the same comparison: the compiler rewrites only the files whose content
changed, and the plugin compares the validated manifest with the previous one.
While the build is still writing, the manifest and its files can disagree; that
save then shows an integrity error until the build finishes and its last file
change arrives.

### In-page editing API

Under `vite dev` in project mode, the dev server offers a small HTTP API for
editing a translation from the page itself, such as runic-sdk's Runic Inspect:
read a message in every locale, then write one locale's text back to its source.
The write goes through the same compile and comparison as a save in an editor,
so the page shows the new text in place (see above). The API exists only on the
dev server: `vite build` and `vite preview` never mount it, and a production
build is byte for byte the same with or without it.

It is on by default and accepts requests from the same machine only. Turn it off
with `inPageEditing: false`; allow other hosts and networks with
`inPageEditing: { allow: [...] }` or the `RUNIC_DEV_ALLOW` environment variable
(see [Who may edit](#who-may-edit)).

Reading and writing go through `runic-translations serve`, which reuses the
Translations Editor's source-preserving writer: only the message's own text
changes, and the file keeps its formatting, comments, line endings and the order
of everything else. A message is named by its catalog and key, never by a path.

#### `GET /__runic-translations/message`

Query parameters: `key` (the canonical key `shop_cart` or the logical key
`shop.cart`) and optionally `catalog` (the application's catalog when omitted, or
a library catalog regenerated with `dependencies: "auto"`). The path is below
Vite's `base`.

```jsonc
// 200
{
  "message": {
    "catalog": "app", "kind": "catalog",        // "library" for a library catalog (with "package": "@acme/grid@1.2.0")
    "key": "clicks", "logicalKey": "clicks", "baseLocale": "en",
    "content": "inline", "structured": false,    // structured: the message has markup in some locale
    "inputs": [{ "name": "count", "type": "int64" }], "markup": [],
    "locales": [{
      "locale": "de", "fallback": "en",
      "origin": "catalog",                       // "catalog" | "override" | "library" | "missing"
      "text": ".input {$count :integer}\n.match $count\none {{Ein Tipp}}\n* {{{$count} Tipps}}",
      "source": { "path": "/app/translations/de.rmf2", "line": 2, "column": 1 },
      "file": { "path": "/app/translations/de.rmf2", "exists": true, "hash": "sha256:9f2c…" },
      "editable": true,
      "structure": {
        "supported": true,
        "inputs": [{ "name": "count", "type": "int64", "function": "integer", "declared": true }],
        "selectors": ["count"], "selectorFunctions": ["plural"],
        "variants": [{ "id": "0", "keys": ["one"], "pattern": "Ein Tipp" }, { "id": "1", "keys": ["*"], "pattern": "{$count} Tipps" }],
        "pluralCategories": ["one", "other"]
      }
    }]
  }
}
```

- `locales` lists the base locale first, then the others; pseudo-locales are
  generated and not listed.
- `text` is the message as authored: the whole MF2 message, with its
  declarations, selectors, variants and markup (`{#strong}…{/strong}`), without
  the RMF2 entry's key and indentation. It is `null` when the locale has no text of
  its own and falls back (`origin: "missing"`).
- `source` is where `text` is defined (1-based line and column). `file` is the
  file a write of this locale changes, with its SHA-256 `hash` (`none` for a file
  that does not exist yet). The two differ for a library's own text (`origin:
  "library"`): it is read from the installed package, and a write creates the
  application's override. Paths are absolute.
- `editable` is false, with a `reason`, when a locale cannot be written, such as
  a locale without a source file beside the base locale's.
- `structure` is the Translations Editor's projection of `text`, for showing the
  plural or selector variants and inputs. When `supported` is false the message
  still reads and writes as text.

#### `POST /__runic-translations/message`

`Content-Type: application/json`, with:

```json
{ "catalog": "app", "key": "clicks", "locale": "de",
  "text": ".input {$count :integer}\n.match $count\none {{Ein Tipp}}\n* {{{$count} Tipps insgesamt}}",
  "expectedHash": "sha256:9f2c…" }
```

- `text` replaces the locale's whole message text. Plural, selector, markup and
  document messages are edited as their whole source text, as `GET` returns it.
- `expectedHash` is `locales[].file.hash` of the message as read (`"none"` or
  `null` for a file that does not exist yet). The write is refused when the file
  changed since, for example when it was saved in an editor.
- The new text is compiled with the rest of the catalog first. A text that does
  not compile is refused with its diagnostics, and nothing is written.

```jsonc
// 200
{ "written": ["/app/translations/de.rmf2"], "message": { /* the message after the write, as GET returns it */ } }
```

The dev server then sees the change as it sees a save: a text edit updates the
page in place, and a change of the message's inputs or markup reloads it with the
reason logged.

A locale that has no text of its own (`origin: "missing"`, allowed by
`validation.translationCompleteness`) gets the message inserted into its source
beside the base locale's: the locale's `.rmf2` in the same directory, or the same
file name in the locale's directory for direct `.mf2` sources.

**Library catalogs** (`dependencies: "auto"`): a write never touches the installed
package. It goes to the application's override,
`translations/overrides/<catalog>/<locale>.rmf2`, by the rules of
`runic-translations overrides add --key`: a first override of a key is inserted
with the library's base text as comments above it, the library text it was
written for is recorded in `<catalog>.review.json` (so `RTR0090` reports it when
the library changes), and a catalog `runic.json` does not list in `overrides` yet
is added there, from the package that ships it. That runic.json change reloads the
page once; later writes of the override update in place. A further write of an
existing override changes only its text. `written` lists every file the write
changed.

#### Errors

Every error is JSON: `{ "error": "<code>", "detail": "<text>" }`, plus
`diagnostics` and the current `message` where noted.

| Status | `error` | When |
| --- | --- | --- |
| 400 | `bad-request` | A missing or malformed `key`, `locale`, `text`, `expectedHash` or body. |
| 403 | `forbidden` | The client is not loopback or allowed, or the request is not same-origin. |
| 404 | `not-found` | An unknown catalog, key or locale, or another path below `/__runic-translations/`. |
| 405 | `method-not-allowed` | A method other than `GET`, `HEAD` or `POST`. |
| 409 | `conflict` | The file changed since it was read (`expectedHash`). `message` is the current message; read the new hash from it and reapply the edit. |
| 413 | `too-large` | A body over 512 KiB. |
| 415 | `unsupported-media-type` | A body that is not `application/json`. |
| 422 | `invalid` | The text does not compile; `diagnostics` has the compiler's diagnostics (`code`, `severity`, `message`, `path`, `line`, `column`, `endLine`, `endColumn`, `helpUri`) located in the edited source. Nothing was written. |
| 422 | `unsupported` | The locale cannot be written (no source file for it), or the library catalog cannot be overridden (it does not compile, its review file is unreadable). |
| 501 | `unavailable` | Manifest mode: the plugin does not compile the catalog itself. |
| 503 | `unavailable` | `runic-translations serve` cannot answer (the installed tool predates it, or it keeps crashing). |

#### Who may edit

The API writes source files, so it refuses anything but the developer's own
browser by default:

- **Client address.** A request is accepted when it comes from loopback
  (`127.0.0.0/8`, `::1`), or from an address in the allow list. The address is
  the TCP connection's; `X-Forwarded-For` and similar headers are ignored.
- **Same origin.** `Sec-Fetch-Site`, when the browser sends it, must be
  `same-origin` (or `none`), and `Origin`, when present, must name the dev server
  (its host and port as in the `Host` header). A write must carry `Origin`, which
  every browser sends with `fetch` `POST`; scripts and tools must send it too.
  Another site, or another dev server on a different port of the same machine,
  cannot read or write through the API.
- **Vite's checks still apply first.** The API runs after Vite's CORS and host
  checks, so `server.allowedHosts` refuses a request whose `Host` it does not
  allow (DNS rebinding) before the API sees it.

To edit from another machine, such as a phone or a laptop on a
[Tailscale](https://tailscale.com/kb/1015/100.x-addresses) network, or a browser
that an agent drives from a local server, start Vite on a reachable address
(`server.host`) and allow its clients:

```ts
runicTranslations({ inPageEditing: { allow: ["100.64.0.0/10", "fd7a:115c:a1e0::/48", "192.168.1.20", "laptop.tailnet.ts.net"] } })
```

```sh
RUNIC_DEV_ALLOW="100.64.0.0/10, 192.168.1.20" vite dev --host
```

Entries are IPv4 or IPv6 addresses, CIDR networks, or host names (resolved when a
request arrives, and cached for 30 seconds). The environment variable takes the
same entries separated by commas or spaces, and adds to the option.
`RUNIC_DEV_ALLOW` is the setting vite-plugin-runic (`@runic-artifex/vite-plugin-runic`)
also reads for Runic Inspect's dev endpoints, so one variable admits a client to
both; `RUNIC_TRANSLATIONS_EDIT_ALLOW` keeps working, and the two add up. An invalid
entry stops the dev server with an error naming it. When the list is not empty,
the dev server logs it at start. Vite must also accept the `Host` those clients
use (`server.allowedHosts`, which allows IP addresses and `localhost` by
default).

Allow only networks whose members you trust to change your sources: anyone the
list admits can write the catalog's sources and its override files. A reverse
proxy on the same machine makes every client it forwards look like loopback; do
not expose the dev server through one while the API is on.

## Library catalogs from dependencies

The [Ship translations in a library](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/libraries.md)
guide and the [component-library example](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/examples/component-library)
show a library and an application end to end.

A library package ships its catalog for applications with three things in its `package.json` and files:

```json
{
  "name": "@acme/grid",
  "exports": {
    ".": "./index.js",
    "./runic-translations": "./translations/acme.grid.esm-v5/web-module-manifest-v3.json"
  },
  "runicTranslations": { "catalog": "acme.grid", "project": "./runic/acme.grid/runic.json" }
}
```

- `runic/acme.grid/` holds the catalog's `runic.json` and sources, so the application can regenerate it;
- `./runic-translations` exports the prebuilt catalog's web module manifest (`runic-translations generate --emit-esm`);
- the library's own code imports its catalog by a relative path (`./translations/acme.grid.esm-v5/messages.js`), not a
  virtual ID. Catalog IDs that start with `runic-` are reserved for the `@runic-artifex` packages.

`runicTranslations({ dependencies: "auto" })` finds the application's `package.json` (the nearest at or above Vite's
`root`), every dependency it declares that declares `runicTranslations`, and, transitively, the dependencies those
libraries declare (`dependencies`, `peerDependencies`, `optionalDependencies`) that declare it too. A package without
`runicTranslations` is not followed, so a catalog library that the application reaches only through such a package is
not found: add it to the application's dependencies. A declared dependency that is not installed is reported as a
warning. Each catalog is regenerated into `<output>/dependencies/` with the same compiler command as project mode:

- with the application's overrides: runic.json `"overrides": [{ "catalog": "acme.grid", "from": "@acme/grid" }]` and
  `translations/overrides/acme.grid/<locale>.rmf2`, validated against the library's catalog key by key, which may add
  locales the library lacks. `from` may list the library's NuGet package too (`["Acme.Grid", "@acme/grid"]`), so a
  runic.json that a .NET build shares serves both. As in a .NET build, an override of a catalog that no discovered
  dependency ships, or from another package than `from` lists, fails the build (`RTR0104`), and a directory under `translations/overrides/` that
  runic.json does not list is ignored with a warning. A locale the overrides add falls back as on .NET: to its nearest
  CLDR parent among the library's and the added locales (`de-CH` to an added `de`), else to the library locale that CLDR
  likely subtags match, else to the library's base locale;
- filtered to `locales` (default: the locales of the project's runic.json): only the library locales the runtimes resolve
  these to (the locale itself, else by CLDR likely subtags and parent locales, so `zh-TW` keeps a library's `zh-Hant` and
  `en-GB` its `en-001`), their fallbacks and the library's base locale are bundled. An invalid tag is an error;
- over one `runtime-core.js` that all regenerated catalogs share, so the runtime code is bundled once.

The regenerated catalog must keep the contract fingerprint of the library's prebuilt one; if the package's `runic/`
sources do not match its prebuilt catalog, the build fails instead of loading the prebuilt modules with a second
runtime. An import of a prebuilt module that the regenerated catalog lacks is an error for the same reason. Regenerating
replaces the catalog's directory under `<output>/dependencies/` completely, so no stale module remains.

**Copies of one library.** Catalogs are deduplicated by catalog ID. Copies of the same package and version (for example
one nested in another library's `node_modules`) share one regenerated catalog, and the imports of every copy that
discovery reaches are redirected to it. Two versions of a package with one catalog, or two packages with one catalog ID,
fail the build: install one version (`npm dedupe`, or `overrides`/`resolutions` in the application's `package.json`). A
copy that discovery does not reach keeps its prebuilt catalog and runtime; the plugin warns when such a copy is imported.

**Package managers.** Discovery reads `node_modules`, which npm, pnpm (through its symlinks), Bun and Yarn with
`nodeLinker: node-modules` create. Yarn Plug'n'Play creates none, so `dependencies: "auto"` fails there with an error;
set `nodeLinker: node-modules` in `.yarnrc.yml`, or leave `dependencies` off, and the libraries keep their prebuilt
catalogs without the application's overrides and locale filtering.

The library's imports of its own catalog modules are redirected to the regenerated ones, the catalog is also available as
`virtual:runic-translations/acme.grid`, and its declarations are written to `<output>/dependencies/acme.grid.d.ts`, which
the application's `virtual.d.ts` references; nothing is written into `node_modules`. These packages are excluded from
Vite's dependency pre-bundling and are not externalized for server rendering, so the plugin sees their imports.

**Development server.** Editing an override regenerates the dependency catalogs and compares them with their previous
generation, as for the application's catalog: an edited override's text, or a library's text in a workspace-linked
library, updates in place, and an added or removed override message, an added or removed override file, or a change to
runic.json `overrides` or `locales` reloads the page with its reason. A new directory under `translations/overrides/` is
picked up as well. While runic.json
is invalid in the middle of an edit, the plugin reports it once and keeps its last valid overrides and locales. Library
sources are watched only outside `node_modules` (a workspace-linked library); Vite does not watch `node_modules`, so after
changing an installed package, restart the development server.

The plugin ignores manifest members, entrypoints and asset members that a later compiler adds within ESM ABI 4, so a
library built by a newer compiler patch still loads.

## TypeScript virtual modules

After validating the manifest and every generated declaration asset, the plugin
writes an ambient `virtual.d.ts` that exposes the exact catalog message keys,
inputs, and return types through all five virtual entry points. Project mode
writes the file to `<output>/virtual.d.ts`; an explicit manifest writes it beside
that manifest. TypeScript projects with a narrow `include` must add that stable
generated file, for example:

```json
{
  "include": ["src", ".runic/translations/virtual.d.ts"]
}
```

When the catalog has a `messages.d.ts.map`, as `generate --emit-esm` writes,
the plugin also writes `virtual.d.ts.map`, so Go to Definition on `m.key()` opens
the message's `.rmf2` or `.mf2` source line (see
[Editor navigation](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/esm.md#editor-navigation)).

The file exists once Vite has run the plugin (`vite build`, or `vite` for the
dev server). A type checker does not run Vite, so on a clean checkout check
after building: `vite build && svelte-check`, `vite build && tsc --noEmit`.

No hand-written `declare module`, catalog-specific path mapping, or client
compatibility module is needed. Use `typeDeclarations` to select another stable
path, or set it to `false` when another tool owns virtual-module declarations.

## Render a message

```ts
import { m } from "virtual:runic-translations/app";

document.querySelector("#app")!.textContent = m.application_title();

const greeting = m.greeting({ name: "Ada" }, { locale: "de" });
```

RMF2 resource names become identifier-safe message properties. Static ESM
re-exports let Vite remove message modules that are not referenced.

`reportUnused: true` logs, after a production build, the messages that no chunk
of the build contains, so they are candidates for removal:

```text
[runic-translations] 2 of 120 messages of catalog "app" are not used by this build: legacy_banner, settings_old_title.
```

It reports each build environment that includes the catalog. A message read only
through the `/dynamic` entry, by a key computed at run time or by another build
(such as the .NET side, see `RTR0100`) is reported too.

Additional entry points are available for generated locale configuration (`/runtime`), request-local SSR (`/server`), cross-process text references (`/transport`), and validated runtime-loaded locale artifacts (`/dynamic`). Wrap SSR rendering with `/server`'s `runWithLocale`; explicit per-call locale options are only needed for intentional overrides.

## When to choose this package

Choose the plugin when Vite should resolve generated virtual modules and invalidate them during development. Generated ESM remains framework- and bundler-independent, so non-Vite consumers can import the emitted modules directly. This package is not a JavaScript compiler and does not replace the .NET tool or MSBuild generation step.

## Compatibility and status

The plugin supports Vite `>=8.3.3 <9`. Before 1.0 a minor release may contain breaking changes, each listed with upgrade steps in the release notes (see [Versions before 1.0](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/eng/release/README.md#versions-before-10)). It accepts the supported web-module manifest and ESM ABI only and reports a clear error for incompatible generated output. Keep the adapter and .NET tool on one release, regenerate ESM during upgrades, and review the release notes.

- [Ten-minute Vite workflow](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-vite.md)
- [SvelteKit quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-sveltekit.md)
- [ESM backend and SSR guidance](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/esm.md)
- [RMF2 guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md)
- [Plugin tests and production build examples](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/packages/web/vite-plugin-runic-translations/test)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/LICENSE).

## RMF2

Project mode recursively discovers direct `.mf2` and grouped `.rmf2` sources and watches `runic.json`, the project directory, and all explicit `sourceRoots` for additions, edits and removals. It emits the typed grammar 5 / ESM ABI 4 `web-module-manifest-v3.json` output, invokes the shared compiler, and retains its diagnostic failures, including mixed-source rejection. See the [RMF2 guide](../../../docs/guides/translations/rmf2.md) for the generated inline renderer API.
