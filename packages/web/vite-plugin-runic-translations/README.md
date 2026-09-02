# @runic-artifex/vite-plugin-runic-translations

Import generated Runic Translations messages through stable Vite virtual modules, with typed message inputs, production tree shaking, source watching, and HMR. The plugin maps compiler-produced ESM into Vite; it never parses translation authoring JSON in JavaScript.

## Install

```bash
npm install --save-dev @runic-artifex/vite-plugin-runic-translations@<VERSION>
```

Replace `<VERSION>` with the current public preview shown on npm. The package supports Vite 6, 7, and 8. A Vite application and generated `web-module-manifest-v1.json` are required. If the plugin owns generation, install `dotnet-runic-translations` in a project-local .NET 10 tool manifest at that same exact release.

## Configure Vite

```ts
// vite.config.ts
import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [runicTranslations()],
});
```

The no-argument form discovers `translations/runic.json`, compiles its MF2 files
to `.runic/translations`, and watches both config and messages. In a split
frontend/backend layout, use `runicTranslations({ project: "../translations" })`.
When another build owns generation, pass its generated `manifest` and optional
`sourceFiles` instead.

## Render a message

```ts
import { m } from "virtual:runic-translations/app";

document.querySelector("#app")!.textContent = m.application_title();

const greeting = m.greeting({ name: "Ada" }, { locale: "de" });
```

MF2 filenames become identifier-safe message properties. Static ESM re-exports let Vite remove message modules that are not referenced.

Additional entry points are available for generated locale configuration (`/runtime`), request-local SSR (`/server`), cross-process text references (`/transport`), and validated runtime-loaded locale artifacts (`/dynamic`). Wrap SSR rendering with `/server`'s `runWithLocale`; explicit per-call locale options are only needed for intentional overrides.

## When to choose this package

Choose the plugin when Vite should resolve generated virtual modules and invalidate them during development. Generated ESM remains framework- and bundler-independent, so non-Vite consumers can import the emitted modules directly. This package is not a JavaScript compiler and does not replace the .NET tool or MSBuild generation step.

## Compatibility and status

The plugin is a public preview for Vite `>=6 <9`. It accepts the supported web-module manifest and ESM ABI only and reports a clear error for incompatible generated output. Keep the adapter and .NET tool on one release, regenerate ESM during upgrades, and review preview migration notes.

- [Ten-minute Vite workflow](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/quickstart-vite.md)
- [ESM backend and SSR guidance](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/esm.md)
- [Compatibility policy](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/compatibility.md)
- [Plugin tests and production build examples](https://github.com/Runic-Artifex/runic-translations/tree/main/web/test)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE).
