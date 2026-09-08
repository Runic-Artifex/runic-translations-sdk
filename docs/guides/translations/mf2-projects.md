# TOML locale projects with MF2 messages

New projects use one TOML file per locale. Configuration remains separate from
MessageFormat 2 (MF2) message content:

```text
translations/
├── runic.json
├── en.toml
└── de.toml
```

`runic.json` is the only project declaration. It contains project policy, not
messages:

```json
{
  "$schema": "https://runic-artifex.eu/schemas/translations/project-v1.schema.json",
  "schemaVersion": 1,
  "sourceLayout": "locale-toml",
  "catalog": "app",
  "code": {
    "namespace": "Example.Translations",
    "className": "AppText"
  },
  "baseLocale": "en"
}
```

Locale files are siblings of `runic.json`; each filename is its locale tag. Add
`locales` to reject undeclared locales. Keys must be identifier-safe message IDs.
Values are TOML strings containing MF2, for example in `translations/en.toml`:

```toml
application_title = 'Runic application'
validation_required = '''
.input {$field :string}
The field {$field} is required.
'''
```

Use the Runic TOML 1.1 profile: a flat key/value document with keys matching
`[A-Za-z_][A-Za-z0-9_]*`. Tables, dotted keys (including quoted keys containing
dots), arrays, non-string values and duplicate keys are rejected. Literal TOML
strings keep MF2 braces and backslashes readable; basic strings use TOML escaping.
MF2 remains the message
language, including parameters and plural selectors.

```ts
import { m } from 'virtual:runic-translations/app';

m.application_title();
m.validation_required({ field: 'email' });
```

The decoded string values use MessageFormat 2 syntax. The v1 compiler accepts plain
patterns, `.input`, `.local`, `.match`, quoted patterns, variables, markup, and
the functions `:string`, `:integer`, `:number`, `:date`, `:time`, and
`:datetime`. Runic-specific scalar formats use the explicit `:runic:*`
namespace. Unsupported MF2 constructs are compile errors instead of silently
changing their meaning.

## Build and Vite discovery

`Runic.Translations.Build` automatically discovers `translations/runic.json`
and its locale TOML files. No MSBuild item list is required.

Vite uses the same project:

```ts
import { runicTranslations } from '@runic-artifex/vite-plugin-runic-translations';

export default {
  plugins: [runicTranslations()],
};
```

The no-argument form discovers `translations/runic.json`, generates into
`.runic/translations`, watches the config and locale file additions, edits and
removals, and exposes the generated virtual modules. In a split frontend/backend layout, pass only the
relative project directory: `runicTranslations({ project: "./translations" })`.

The CLI accepts either the directory or config file:

```bash
dotnet tool run runic-translations -- validate --project translations
dotnet tool run runic-translations -- generate \
  --project translations \
  --output .runic/translations \
  --emit-esm
```

The compiler, CLI, MSBuild, Vite plugin, and editor consume this shared project
layout. TOML is a compile-time container; generated runtime APIs and message
formatting continue to use the same MF2 compilation model.

## Existing projects

A project without `sourceLayout` retains the historical
`{locale}/{message_id}.mf2` layout. Absence does not opt an existing project into
TOML. New templates and scaffolds set `sourceLayout: "locale-toml"` explicitly.
Do not mix the two layouts in one project or change the discriminator before
converting the resources.

Migration groups each locale's MF2 files into one TOML document while preserving
message IDs and decoded source content. Existing MF2 compilation still normalizes
line endings and trims the message body; container migration does not change
those formatting semantics. Review the migration plan and resolve destination
collisions before applying it:

```bash
dotnet tool run runic-translations -- migrate --project translations --dry-run
dotnet tool run runic-translations -- migrate --project translations
dotnet tool run runic-translations -- validate --project translations
```

The dry run lists planned file creations, replacements and deletions without
writing files. The command without `--dry-run` applies the transaction, including
replacing the project discriminator and removing the migrated MF2 files. Commit
or back up the original inputs before applying the plan, then verify the converted
catalog and regenerate its outputs.

## Locale and SSR runtime

The generated runtime exports `locales`, `baseLocale`, `resolveLocale`, and the
`Locale` type. Browser calls resolve against `<html lang>` unless a call supplies
an explicit locale. Server rendering defaults to the base locale.

The generated `/server` entrypoint adds request-local context:

```ts
import { runWithLocale } from 'virtual:runic-translations/app/server';

const html = await runWithLocale('de', () => renderRequest());
```

Calls such as `m.application_title()` inside that operation use `de`, including
across asynchronous work. Concurrent requests do not share mutable locale state.
An explicit `{ locale }` call option remains available for the uncommon case
where one operation intentionally formats another locale.
