# @runic-artifex/vite-plugin-text-resources

This optional adapter exposes Runic compiler output as stable Vite virtual modules.
The generated ESM itself remains framework- and bundler-independent.

```js
import { runicTextResources } from "@runic-artifex/vite-plugin-text-resources";

export default {
  plugins: [runicTextResources({
    manifest: "obj/text-resources/app.esm/web-module-manifest-v1.json",
    sourceFiles: ["Resources/app.catalog.json", "Resources/en.json"],
  })],
};
```

Import `virtual:runic-text-resources/app` for messages or
`virtual:runic-text-resources/app/runtime` for locale configuration. The host
build regenerates compiler output; the plugin watches and invalidates the virtual
modules. It never compiles authoring JSON in JavaScript.
