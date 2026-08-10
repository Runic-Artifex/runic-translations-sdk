# @runic-artifex/vite-plugin-runic-translations

This optional adapter exposes Runic compiler output as stable Vite virtual modules.
The generated ESM itself remains framework- and bundler-independent. The test
suite runs a real Vite production build to assert message-level tree-shaking and
exercises source-driven HMR invalidation.

The public package is installed from npm without repository authentication:

```bash
npm install --save-dev @runic-artifex/vite-plugin-runic-translations
```

```js
import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";

export default {
  plugins: [runicTranslations({
    manifest: "obj/translations/app.esm/web-module-manifest-v1.json",
    sourceFiles: ["Resources/app.catalog.json", "Resources/en.json"],
  })],
};
```

For frontend-owned builds, configure the pinned local .NET tool directly in the
plugin. It runs before the manifest is loaded and again before HMR invalidation
after authoring changes:

```js
runicTranslations({
  manifest: "obj/translations/app.esm/web-module-manifest-v1.json",
  sourceFiles: ["Resources/app.catalog.json", "Resources/app.en.json"],
  compiler: {
    catalog: "Resources/app.catalog.json",
    documents: ["Resources/app.en.json"],
    output: "obj/translations",
  },
});
```

Import `virtual:runic-translations/app` for the typed, tree-shakable `m` message
namespace, `/runtime` for locale
configuration, `/transport` for text references, or `/dynamic` for validated
runtime-loaded v2 locale artifacts. The host
build regenerates compiler output; the plugin watches and invalidates the virtual
modules. It never compiles authoring JSON in JavaScript.

```js
import { m } from "virtual:runic-translations/app";

m.Plain();
m["Common.Hello"]({ name: "Ada" });
```
