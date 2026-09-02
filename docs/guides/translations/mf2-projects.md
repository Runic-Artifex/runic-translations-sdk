# MF2 projects

The v1 authoring convention keeps configuration and messages separate:

```text
translations/
├── runic.json
├── en/
│   ├── application_title.mf2
│   └── validation_required.mf2
└── de/
    ├── application_title.mf2
    └── validation_required.mf2
```

`runic.json` is the only project declaration. It contains project policy, not
messages:

```json
{
  "$schema": "https://runic-artifex.eu/schemas/translations/project-v1.schema.json",
  "schemaVersion": 1,
  "catalog": "app",
  "code": {
    "namespace": "Example.Translations",
    "className": "AppText"
  },
  "baseLocale": "en"
}
```

Locale directories are discovered from the filesystem. Add `locales` only when
you want the project to reject undeclared locale directories. A message ID comes
from its filename and must be an identifier so the generated ESM API is natural:

```mf2
// translations/en/application_title.mf2
Runic application
```

```mf2
// translations/en/validation_required.mf2
.input {$field :string}
The field {$field} is required.
```

```ts
import { m } from "virtual:runic-translations/app";

m.application_title();
m.validation_required({ field: "email" });
```

The authoring files use MessageFormat 2 syntax. The v1 compiler accepts plain
patterns, `.input`, `.local`, `.match`, quoted patterns, variables, markup, and
the functions `:string`, `:integer`, `:number`, `:date`, `:time`, and
`:datetime`. Runic-specific scalar formats use the explicit `:runic:*`
namespace. Unsupported MF2 constructs are compile errors instead of silently
changing their meaning.

## Build and Vite discovery

`Runic.Translations.Build` automatically discovers `translations/runic.json`
and all `translations/**/*.mf2` files. No MSBuild item list is required.

Vite uses the same project:

```ts
import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";

export default {
  plugins: [runicTranslations()],
};
```

The no-argument form discovers `translations/runic.json`, generates into
`.runic/translations`, watches the config and every MF2 message, and exposes the
generated virtual modules. In a split frontend/backend layout, pass only the
relative project directory: `runicTranslations({ project: "../translations" })`.

The CLI accepts either the directory or config file:

```bash
dotnet tool run runic-translations -- validate --project translations
dotnet tool run runic-translations -- generate \
  --project translations \
  --output .runic/translations \
  --emit-esm
```

MF2 projects are the only supported authoring input. The compiler, CLI, MSBuild,
Vite plugin, and editor all consume this same project layout.

## Locale and SSR runtime

The generated runtime exports `locales`, `baseLocale`, `resolveLocale`, and the
`Locale` type. Browser calls resolve against `<html lang>` unless a call supplies
an explicit locale. Server rendering defaults to the base locale.

The generated `/server` entrypoint adds request-local context:

```ts
import { runWithLocale } from "virtual:runic-translations/app/server";

const html = await runWithLocale("de", () => renderRequest());
```

Calls such as `m.application_title()` inside that operation use `de`, including
across asynchronous work. Concurrent requests do not share mutable locale state.
An explicit `{ locale }` call option remains available for the uncommon case
where one operation intentionally formats another locale.
