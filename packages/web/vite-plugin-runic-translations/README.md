# @runic-artifex/vite-plugin-runic-translations

Import generated Runic Translations messages through stable Vite virtual modules, with typed message inputs, production tree shaking, source watching, and HMR. The plugin maps compiler-produced ESM into Vite; it never parses translation authoring JSON in JavaScript.

## Install

```bash
npm install --save-dev @runic-artifex/vite-plugin-runic-translations@<VERSION>
```

Replace `<VERSION>` with the current public preview shown on npm. The package supports Vite 6, 7, and 8 and accepts only the RMF2 `web-module-manifest-v3.json` contract (ESM ABI 4, runtime ABI 2). If the plugin owns generation, install `dotnet-runic-translations` in a project-local .NET 10 tool manifest at that same exact release.

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

Manifest mode treats the supplied generated directory as externally owned.
Changes to `sourceFiles` trigger a Vite refresh, but the plugin cannot recompile
the project or recompute `sourceHash` without a compiler command. The owning
build must regenerate the ESM package and manifest before the refresh is served;
otherwise the plugin will reload the still-current manifest bytes, not detect
that authoring sources are newer than them.

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

Additional entry points are available for generated locale configuration (`/runtime`), request-local SSR (`/server`), cross-process text references (`/transport`), and validated runtime-loaded locale artifacts (`/dynamic`). Wrap SSR rendering with `/server`'s `runWithLocale`; explicit per-call locale options are only needed for intentional overrides.

## When to choose this package

Choose the plugin when Vite should resolve generated virtual modules and invalidate them during development. Generated ESM remains framework- and bundler-independent, so non-Vite consumers can import the emitted modules directly. This package is not a JavaScript compiler and does not replace the .NET tool or MSBuild generation step.

## Compatibility and status

The plugin is a public preview for Vite `>=6 <9`. It accepts the supported web-module manifest and ESM ABI only and reports a clear error for incompatible generated output. Keep the adapter and .NET tool on one release, regenerate ESM during upgrades, and review preview migration notes.

- [Ten-minute Vite workflow](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/quickstart-vite.md)
- [ESM backend and SSR guidance](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/esm.md)
- [RMF2 guide](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/rmf2.md)
- [Plugin tests and production build examples](https://github.com/Runic-Artifex/runic-sdk/tree/main/packages/web/vite-plugin-runic-translations/test)
- [Issues and support](https://github.com/Runic-Artifex/runic-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-sdk/blob/main/LICENSE).

## RMF2

Project mode recursively discovers direct `.mf2` and grouped `.rmf2` sources and watches `runic.json`, the project directory, and all explicit `sourceRoots` for additions, edits and removals. It emits the typed grammar 5 / ESM ABI 4 `web-module-manifest-v3.json` output, invokes the shared compiler, and retains its diagnostic failures, including mixed-source rejection. See the [RMF2 guide](../../../docs/guides/translations/rmf2.md) for the generated inline renderer API.
