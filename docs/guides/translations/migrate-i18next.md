# Migrate from i18next

`runic-translations import --from i18next` moves i18next JSON resources
(JSON v4, the format of i18next 21 and later) into the `.rmf2` sources of a Runic
Translations project. Run it once to migrate, or again while both catalogs still
exist: a second run adds the keys that are new since the last run and keeps
every message that already exists, so edits made in the `.rmf2` sources survive.

## Before you start

Create the project and declare every locale you import
([Vite quick start](quickstart-vite.md)):

```sh
runic-translations init --directory translations --catalog app --default-locale en \
  --locale de --namespace MyApp --class-name AppText --no-starter
```

Resources in the v3 format (`key_plural`, `key_0`) must first be converted to v4,
for example with `i18next-v4-format-converter`. For a table keyed by the English
text itself (`keySeparator: false` with sentences as keys), use
[`import-text`](rmf2.md#import-a-text-keyed-catalog) instead.

## Import

```sh
runic-translations import --from i18next public/locales --dry-run
runic-translations import --from i18next public/locales
runic-translations validate
```

`<path>` is a directory or one file:

- a directory holding `<lng>/<namespace>.json` (the `i18next-http-backend`
  layout) or `<lng>.json` files: every file of a locale that `runic.json`
  declares is imported, the base locale first. Files of other locales are
  reported (`IMPORT-LOCALE`) and not imported;
- `<lng>.json` or `<lng>/<namespace>.json`: only that file;
- a file whose name names no locale, such as `src/messages.json`: pass its
  locale with one `--locale`.

`--locale <tag>` (repeatable) imports only those locales, and `--dry-run` reports
the outcome and the files it would write without writing them. New messages go to
`<project>/<baseLocale>.rmf2` and their translations beside them; `--group
<name>` writes new messages to `<project>/<name>/<locale>.rmf2` instead.

A message that already exists, as base text or as a translation, is kept: its
text in the `.rmf2` sources wins, and one whose text differs from the JSON is
reported as `kept` (`IMPORT-EXISTS`). Run with `--dry-run` first to see them.
`--overwrite` replaces their text with the JSON text instead; each such message
keeps its file, group and comments, and only its text changes.


The project may be partly translated, and so may the import: a message whose
translation the import does not bring is left untranslated, and `validate`
reports it (`RTR0010`) until it is translated, as after `locale add`. The import
lists those messages per locale (`untranslated de: 3 message(s) (…)`, and
`interchange.untranslated` with `--format json`). Only errors in `runic.json`, the
base locale or a message's syntax stop the import. Other errors the catalog already
had, such as an obsolete translation (`RTR0011`), neither refuse a message nor fail
the import; each is reported as an `IMPORT-PREEXISTING` notice, and `validate`
still reports it.
The Translations Editor (an optional step of the new-project wizard, and
**Interchange**) and the VS Code command **Runic: Import .resx or i18next
Catalog** run the same importer, with a dry run before anything is written.

## What is mapped

| i18next | RMF2 |
| --- | --- |
| nested keys `{"cart": {"total": ...}}` and `cart.total` | the group `cart { total = ... }`; ESM calls `m.cart_total()` |
| the default namespace `translation` | the top level |
| another namespace, `common.json` | the group `common` |
| `sign-in` | `sign_in` (characters other than letters, digits and `_` become `_`, reported as `IMPORT-KEY-RENAMED`) |
| `{{name}}`, `{{- name}}` | `{$name}` |
| `{{amount, number}}` | `{$amount :number}` |
| `{{when, datetime}}` | `{$when :date style=short}` |
| `{{names, list}}` | `{$names :runic:list}` |
| `items_one`, `items_other`, ... | one message `items` with `.match $count` |
| `place_ordinal_one`, ... | `.match` with `select=ordinal`; named `place_ordinal` when `place` also has cardinal forms |
| `items_zero` | the CLDR `zero` category where the locale has one, otherwise the exact key `0`, which i18next also uses for a count of 0 in every language |

Given `locales/en/translation.json`

```json
{
  "welcome": "Welcome, {{name}}!",
  "cart": {
    "items_zero": "Your cart is empty",
    "items_one": "{{count}} item",
    "items_other": "{{count}} items"
  }
}
```

the base file receives

```rmf2
welcome = Welcome, {$name}!
cart {
  items =
    .input {$count :number}
    .match $count
    0 {{Your cart is empty}}
    one {{{$count} item}}
    * {{{$count} items}}
}
```

and `m.cart_items({ count: 3 })` returns `3 items`. `$count` is formatted as a
number, so 1234 reads `1,234` in English, where i18next printed `1234`. Plural
forms of categories the locale does not use (`_few` in German) are dropped
(`I18NEXT-PLURAL-CATEGORY`), a missing form falls back to `_other`
(`I18NEXT-PLURAL-MISSING`), and a lone `items_one` without `items_other` stays a
plain message (`I18NEXT-PLURAL-INCOMPLETE`), as in i18next.

Resources in the JSON v3 format (`compatibilityJSON: 'v3'`, i18next 20 and
earlier) name plurals `items_plural` or `items_0`, `items_1`, ... These keys are
imported as plain messages with an `I18NEXT-PLURAL-V3` notice. Convert the files
to v4 first, for example with
[i18next-v4-format-converter](https://github.com/i18next/i18next-v4-format-converter),
and import them again.

## What is reported

The command imports what it can and lists the rest. It exits with 0 even when
single messages were skipped or refused; `--format json` lists every message in
the `interchange` member of the payload (`format: "i18next"`, schema
[`translations-tool-v1`](../../../specs/translations/schemas/translations-tool-v1.schema.json)).

| Code | Outcome | Meaning |
| --- | --- | --- |
| `I18NEXT-ARRAY` | skipped | An array for `returnObjects`; split it into keys. |
| `I18NEXT-NON-STRING` | skipped | A number, boolean or `null`. |
| `I18NEXT-NESTING` | refused | `$t(other.key)`: RMF2 messages do not reference each other. Repeat the text, or compose the two messages in code. |
| `IMPORT-NO-BASE` | refused | A translation whose key has no base message. |
| `IMPORT-INVALID` | refused | The message does not compile; the reason names the RTR code, for example a translation that uses `{{user}}` while its base text does not (`RTR0016`). |
| `IMPORT-KEY-COLLISION` | refused | Two keys map to the same RMF2 name, `items` exists both as a message and as plural forms, or a dotted key (`"a.b"`) repeats a nested one (`"a": { "b": ... }`); the first is imported. |
| `IMPORT-EXISTS` | kept | The message already exists with other text and was not changed; `--overwrite` replaces it. |
| `I18NEXT-FORMAT` | notice | A format the importer does not convert, or format options, were dropped: `currency` (write `:currency currency=EUR` with the ISO code, which RMF2 needs in the message), `relativetime` (`:runic:relative-time unit=day`), a custom formatter. |
| `I18NEXT-INTERPOLATION` | notice | `{{user.name}}` became the input `$user_name`; pass it as its own value. |
| `I18NEXT-MARKUP` | notice | HTML or `<Trans>` tags (`<1>terms</1>`) were kept as text. Rewrite them as RMF2 markup (`{#link}terms{/link}`) with a slot ([RMF2 guide](rmf2.md)). |
| `IMPORT-LOCALE` | notice | A file of a locale that `runic.json` does not declare. |
| `I18NEXT-PLURAL-V3` | notice | A v3 plural key (`items_plural`, `items_0`) was imported as a plain message; convert the resources to v4. |

Contexts (`key_male`, `key_female`) are imported as separate messages; join them
into one message with `.match` on a `:string` input if you want a single call.
Refused messages are also `RCLI8216` warnings. A file that is not a UTF-8 JSON
object (`I18NEXT-MALFORMED`; a UTF-8 byte order mark is accepted) stops the command with `RTR0092` and exit code 2,
and so do imported messages that make the project fail to compile for a reason
the importer cannot attribute to one message; nothing is written then.

## Replace the call sites

`t('welcome', { name })` becomes `m.welcome({ name })`, `t('cart.items', { count })`
becomes `m.cart_items({ count })` and `t('common:ok')` becomes `m.common_ok()`
([ESM guide](esm.md)). Remove i18next once nothing calls `t` any more.

## PO files

GNU gettext `.po` files are not imported; see
[Migrate from .resx](migrate-resx.md#po-files) for the two ways to bring them in.
