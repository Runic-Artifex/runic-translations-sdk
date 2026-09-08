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
`locales` to reject undeclared locales. Group related messages with standard TOML
tables, nested tables, dotted keys or inline tables. Values remain MF2 strings, for example in
`translations/en.toml`:

```toml
[application]
title = 'Runic application'

[validation]
required = '''
.input {$field :string}
The field {$field} is required.
'''

[documents.actions]
save = 'Save document'

[documents]
count = '''
.input {$count :integer select=plural}
.match $count
one {{One document}}
* {{{$count} documents}}
'''
```

The Runic TOML 1.1 profile joins key-path segments with underscores for the logical
message ID. `[application] title` becomes `application_title`, and
`[documents.actions] save` becomes `documents_actions_save`. The dotted assignment
`documents.actions.save = 'Save document'` at the document root is an equivalent
alternative to the nested table. Inline tables offer compact groups with the same
mapping, for example:

```toml
[documents]
actions = { save = 'Save', cancel = 'Cancel' }
```

This produces `documents_actions_save` and `documents_actions_cancel`, just as
`[documents.actions]` with `save` and `cancel` entries would. Table containers may
nest recursively; every message leaf must be an MF2 string. Prefer section tables
for long catalogs and use inline tables for optional compact groups. Existing flat
keys remain supported.

Each segment must match `[A-Za-z_][A-Za-z0-9_]*`. Flattened IDs must be unique:
`a_b.c`, `a.b_c` and the flat key `a_b_c` all produce `a_b_c`, so a document
containing more than one of them is rejected. Duplicate TOML keys, scalar or mixed
arrays and non-string message leaves are rejected. A quoted segment
containing a literal dot is not an identifier-safe segment.

Prefer named section tables for authoring and generated catalogs. Arrays of tables
are optional and use a Runic
mapping rule in addition to TOML syntax:

```toml
[[notifications]]
_id = 'saved'
title = 'Document saved'
```

This maps the message to `notifications_saved_title`. Every row requires an `_id`
string matching the identifier-segment rule, unique within its array. Keep this
stable ID identical across locales; row reordering does not change message IDs.
Only an immediate array-row `_id` is metadata, excluded from message payloads and
translation; `_id` inside an ordinary table remains a message key. Nested arrays
of tables belong to their actual enclosing parent row. Inline arrays of tables
use the same mapping: `notifications = [{ _id = 'saved', title = 'Document saved' }]`
at the document root is equivalent to the example above. An empty array `[]`
represents an empty group. The same flattened-ID collision rules apply. This
profile does not treat arbitrary TOML values as messages; scalar and mixed arrays
are not message groups.

Prefer multiline literal strings for readable MF2 parameters and plurals. Literal
strings preserve MF2 braces and backslashes; basic strings use TOML escaping.
TOML groups messages; MF2 supplies formatting and selection. There is no separate
TOML matching language.

```ts
import { m } from 'virtual:runic-translations/app';

m.application_title();
m.validation_required({ field: 'email' });
m.documents_actions_save();
m.documents_count({ count: 2 });
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
