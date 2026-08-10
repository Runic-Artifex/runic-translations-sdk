# @runic-artifex/vite-plugin-runic-translations

This optional adapter exposes Runic compiler output as stable Vite virtual modules.
The generated ESM itself remains framework- and bundler-independent. The test
suite runs a real Vite production build to assert message-level tree-shaking and
exercises source-driven HMR invalidation.

```js
import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";

export default {
  plugins: [runicTranslations({
    manifest: "obj/translations/app.esm/web-module-manifest-v1.json",
    sourceFiles: ["Resources/app.catalog.json", "Resources/en.json"],
  })],
};
```

Import `virtual:runic-translations/app` for messages, `/runtime` for locale
configuration, `/transport` for text references, or `/dynamic` for validated
runtime-loaded v2 locale artifacts. The host
build regenerates compiler output; the plugin watches and invalidates the virtual
modules. It never compiles authoring JSON in JavaScript.
