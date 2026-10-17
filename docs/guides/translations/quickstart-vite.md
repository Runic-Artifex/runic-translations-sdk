# Vite quick start

This workflow keeps the .NET compiler authoritative while making compilation,
watching, HMR, and production bundling part of the normal Vite lifecycle.
It requires the .NET 10 SDK and Node 24.18 or later in the Node 24 series.
Use one exact published Translations version for both the tool and adapter;
replace `<VERSION>` below with that release. No SDK source checkout is needed.

## 1. Install and pin the tools

Start in an empty application directory:

```bash
npm init -y
npm pkg set type=module scripts.dev="vite" scripts.build="vite build"
npm install --save-dev --save-exact vite@8.3.2
dotnet new tool-manifest --output .config
dotnet tool install dotnet-runic-translations --version <VERSION>
npm install --save-dev --save-exact @runic-artifex/vite-plugin-runic-translations@<VERSION>
```

Commit `.config/dotnet-tools.json` and the npm lockfile. A clean checkout then
restores the same compiler with `dotnet tool restore`.

## 2. Create the project

```text
translations/
├── runic.json
├── en.rmf2
└── de.rmf2
```

Create `translations/runic.json`:

```json
{
  "schemaVersion": 1,
  "catalog": "app",
  "code": { "namespace": "Example", "className": "AppText" },
  "baseLocale": "en",
  "locales": ["en", "de"]
}
```

Locale tags come from the RMF2 filenames; grouped `.rmf2` resources are
recognized directly. Create `translations/en.rmf2`:

```rmf2
application {
  title = Runic application
}
```

Create `translations/de.rmf2` with the same group and
`title = Runic-Anwendung`.

See the [RMF2 guide](rmf2.md)
for the complete config and supported authoring syntax. Add `.runic/` to
`.gitignore` when Vite owns generation; also ignore `node_modules/` and `dist/`.

## 3. Configure Vite

```js
// vite.config.js
import { runicTranslations } from '@runic-artifex/vite-plugin-runic-translations';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [runicTranslations()],
});
```

The plugin discovers `translations/runic.json`, runs the pinned local tool before
Vite loads generated modules, and watches the config and locale `.rmf2` files,
including file additions and removals. A
watched authoring change is compiled before the virtual modules are invalidated.
During `vite dev` the plugin keeps one `runic-translations serve` compiler
running, so a save recompiles only the edited files instead of starting .NET
again; production builds run the tool once.

## 4. Render a message

Create `index.html`:

```html
<!doctype html>
<html lang="en">
  <meta charset="utf-8">
  <title>Runic Translations example</title>
  <div id="app"></div>
  <script type="module" src="/main.js"></script>
</html>
```

Create `main.js`:

```js
import { m } from 'virtual:runic-translations/app';

document.querySelector('#app').textContent = m.application_title();
```

RMF2 resource names produce `m.application_title()`. Run `npm run dev` and open
the printed URL to see `Runic application`. The production command is
`npm run build`; the plugin generates `.runic/translations` before bundling.

For TypeScript, the plugin creates `.runic/translations/virtual.d.ts`. Include
that file in your TypeScript configuration. The plugin writes it when Vite runs,
and a type checker does not run Vite, so on a clean checkout run Vite first:
`vite build && tsc --noEmit`, `vite build && svelte-check` or
`vite build && vue-tsc --noEmit`. The opposite order fails with "Cannot find
module 'virtual:runic-translations/app'" until the first Vite run. See the
[adapter guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/packages/web/vite-plugin-runic-translations/README.md)
for all virtual entry points and options.

## Svelte without SvelteKit

A Vite and Svelte app, such as a Runic application's frontend, uses
`@runic-artifex/translations-svelte` beside the plugin:

```bash
npm install --save-dev --save-exact @runic-artifex/translations-svelte@<VERSION>
```

When the application owns the locale, for example a ViewModel's language
setting, `useMessageLocale` makes the `m.*()` calls follow it. Templates,
`$derived` and `$effect` that call a message update in place when the locale
changes; the page does not need `{#key locale}`. `LocalizedInline` renders a
message with markup, here a link slot and a project tag, as Svelte elements:

```svelte
<script lang="ts">
  import { LocalizedInline, inlineFactory, type InlineElement } from "@runic-artifex/translations-svelte/inline";
  import { useMessageLocale } from "@runic-artifex/translations-svelte/translations";
  import { m } from "virtual:runic-translations/app";
  import { configureLocaleResolver, createInlineRenderer, linkBinding } from "virtual:runic-translations/app/runtime";

  let locale = $state("en"); // or a ViewModel's state, such as settings.state?.locale
  useMessageLocale(configureLocaleResolver, () => locale);

  const inline = createInlineRenderer(inlineFactory);
  const terms = $derived(inline.render(m.app_terms(), { slots: { terms: linkBinding({ href: "/terms" }) } }));
  const pending = $derived(inline.render(m.app_pending({ count: 1234 }), { slots: {} }));
</script>

{#snippet highlight(node: InlineElement)}<mark><LocalizedInline nodes={node.children} /></mark>{/snippet}

<h1>{m.app_title()}</h1>
<p>{m.app_hours({ count: 1.5 })}</p>
<p><LocalizedInline nodes={terms} /></p>
<p><LocalizedInline nodes={pending} custom={{ "app:highlight": highlight }} /></p>
<button onclick={() => (locale = locale === "en" ? "de" : "en")}>{m.app_switch()}</button>
```

with `app_terms = Read the {#link ref=terms}terms{/link} before you continue.`,
`app_hours = About {$count :number} hours left` and
`app_pending = {#highlight}{$count :integer}{/highlight} updates are waiting.`.
`highlight` is a tag the project declares in `runic.json`:

```json
"markup": {
  "contracts": [
    { "name": "app:highlight", "kind": "paired", "children": "inline", "interactive": false, "plainText": "children" }
  ],
  "aliases": { "highlight": "app:highlight" }
}
```

The renderer passes it to `inlineFactory` like a built-in tag, and
`LocalizedInline` renders it with the snippet of the same name; the contract is
not restated in TypeScript. To render a tag without a snippet, bind its
declared contract with `bindMarkup(projectMarkup["app:highlight"], render)`
from the same runtime module. A `:number` input takes a
JavaScript `number`, a decimal string or `decimal("...")`; see
[ESM inputs](esm.md). Call `useMessageLocale` once, while the root component
initialises; it installs the resolver for every component of the page and
removes it when that component is destroyed. With server-side rendering, use
the request-scoped [SvelteKit quick start](quickstart-sveltekit.md) instead. The
`svelte-check` and `vite build` order above applies here too.

## 5. Validate in CI

```bash
npm ci
dotnet tool restore
dotnet tool run runic-translations -- validate --project translations
npm run build
```

Commit `package.json`, `package-lock.json`, `.config/dotnet-tools.json`, the Vite
configuration, application files, and translations. This sequence works when
ignored `.runic/` output is absent: validation checks the sources, then Vite
generates and bundles them. There is no retained output to byte-verify before
that first build.

## Retain generated output when another build owns it

If you deliberately commit compiler artifacts, generate a dedicated output tree:

```bash
dotnet tool run runic-translations -- generate \
  --project translations --output generated/translations --emit-esm
```

Commit `generated/translations`, and replace the plugin options with:

```js
runicTranslations({
  manifest: 'generated/translations/app.esm-v5/web-module-manifest-v3.json',
  typeDeclarations: '.runic/translations/virtual.d.ts',
  sourceFiles: ['translations/runic.json', 'translations/en.rmf2', 'translations/de.rmf2'],
})
```

Keep the adapter's ambient declarations outside the compiler-owned tree; CLI
verification detects extra files as well as changed or missing compiler output.
The owning build must regenerate these retained artifacts after authoring changes.
On CI, restore dependencies and the tool, then byte-verify the retained tree:

```bash
dotnet tool run runic-translations -- verify \
  --project translations --output generated/translations --emit-esm
npm run build
```

`verify` renders to an isolated location and byte-compares expected output; it
does not create a missing expected-output tree. Exit
code `0` is valid and current, `1` represents catalog or generated-output
diagnostics, and `2` represents invalid invocation or an operational failure.

For SvelteKit locale routing and request-scoped SSR, continue with the
[SvelteKit quick start](quickstart-sveltekit.md). It passes the generated
`/server` context to the Runic SvelteKit adapter.

## Troubleshooting

- **An edited `.rmf2` file does not change the page in manifest mode.** With
  `manifest`, the plugin treats the generated directory as owned by another
  build. A change to `sourceFiles` refreshes Vite, but the plugin cannot
  recompile, so it reloads the old manifest. Run the `generate` command from
  [Retain generated output](#retain-generated-output-when-another-build-owns-it)
  again, or remove `manifest` so the plugin compiles the project itself.
- **The build reports an `RTR` diagnostic.** See its entry in
  [Diagnostics](diagnostics.md).
