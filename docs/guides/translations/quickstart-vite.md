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

## 2. Create a catalog

```bash
dotnet tool run runic-translations -- init \
  --directory Resources \
  --catalog app \
  --default-locale en \
  --locale de \
  --namespace Example.Translations \
  --class AppText
```

The command creates a schema-v2 manifest and locale documents. Add the generated
output directory to `.gitignore` when generation is owned by local builds and CI.

## 3. Configure Vite

```ts
// vite.config.ts
import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [
    runicTranslations({
      manifest: "obj/translations/app.esm/web-module-manifest-v1.json",
      sourceFiles: [
        "Resources/app.catalog.json",
        "Resources/app.en.json",
        "Resources/app.de.json",
      ],
      compiler: {
        catalog: "Resources/app.catalog.json",
        documents: ["Resources/app.en.json", "Resources/app.de.json"],
        output: "obj/translations",
      },
    }),
  ],
});
```

The plugin runs the pinned local tool before Vite loads generated modules. A
watched authoring change is compiled before the virtual modules are invalidated.
No JavaScript parser interprets Runic authoring syntax.

## 4. Render a message

```ts
import { m } from "virtual:runic-translations/app";

document.querySelector("#app")!.textContent = m["Application.Name"]();
```

Use exact dotted keys through bracket access:

```ts
m["Common.Hello"]({ name: "Ada" }, { locale: "de" });
```

## 5. Validate in CI

```bash
dotnet tool restore
dotnet tool run runic-translations -- verify \
  --catalog Resources/app.catalog.json \
  --documents Resources/app.en.json Resources/app.de.json \
  --output obj/translations \
  --emit-esm
npm run build
```

`verify` renders to an isolated location and byte-compares expected output. Exit
code `0` is valid and current, `1` represents catalog or generated-output
diagnostics, and `2` represents invalid invocation or an operational failure.

For SvelteKit locale routing and request-scoped SSR, use the Runic SvelteKit
adapter rather than installing a request-global locale resolver.
