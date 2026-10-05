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
that file in your TypeScript configuration. If your build runs `tsc` before Vite,
run Vite generation first (for example, `vite build && tsc --noEmit`) so the
declarations exist on a clean checkout. See the
[adapter guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/packages/web/vite-plugin-runic-translations/README.md)
for all virtual entry points and options.

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

For SvelteKit locale routing and request-scoped SSR, pass the generated locale
metadata and `/server` context to the Runic SvelteKit adapter.
