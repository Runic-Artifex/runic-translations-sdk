# Vite quick start

This workflow keeps the .NET compiler authoritative while making compilation,
watching, HMR, and production bundling part of the normal Vite lifecycle.

## 1. Install and pin the tools

```bash
dotnet new tool-manifest
dotnet tool install dotnet-runic-translations --version <VERSION>
npm install --save-dev @runic-artifex/vite-plugin-runic-translations@<VERSION>
```

Commit `.config/dotnet-tools.json` and the npm lockfile. A clean checkout then
restores the same compiler with `dotnet tool restore`.

## 2. Create the project

```text
translations/
├── runic.json
├── en/application_title.mf2
└── de/application_title.mf2
```

Declare the catalog, C# names, and base locale once in `translations/runic.json`.
Locale folders are inferred by default. See the [MF2 project convention](mf2-projects.md)
for the complete config and supported authoring syntax. Add `.runic/` to
`.gitignore` when Vite owns generation.

## 3. Configure Vite

```ts
// vite.config.ts
import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [runicTranslations()],
});
```

The plugin discovers `translations/runic.json`, runs the pinned local tool before
Vite loads generated modules, and watches the config and all `.mf2` files. A
watched authoring change is compiled before the virtual modules are invalidated.

## 4. Render a message

```ts
import { m } from "virtual:runic-translations/app";

document.querySelector("#app")!.textContent = m.application_title();
```

Message filenames are identifier-safe, so normal calls use property access.

## 5. Validate in CI

```bash
dotnet tool restore
dotnet tool run runic-translations -- verify \
  --project translations \
  --output .runic/translations \
  --emit-esm
npm run build
```

`verify` renders to an isolated location and byte-compares expected output. Exit
code `0` is valid and current, `1` represents catalog or generated-output
diagnostics, and `2` represents invalid invocation or an operational failure.

For SvelteKit locale routing and request-scoped SSR, pass the generated locale
metadata and `/server` context to the Runic SvelteKit adapter.
