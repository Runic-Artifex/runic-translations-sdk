# Migrate from .resx

`runic-translations import --from resx` moves the strings of a .NET resource
file and its satellites into the `.rmf2` sources of a Runic Translations
project. Run it once to migrate, or again while both catalogs still exist: a
second run adds the strings that are new since the last run and keeps every
message that already exists, so edits made in the `.rmf2` sources survive.

## Before you start

Create the project and declare every locale you import, as for any catalog
([.NET quick start](quickstart-dotnet.md)):

```sh
runic-translations init --directory translations --catalog app --default-locale en \
  --locale de --namespace MyApp --class-name AppText --no-starter
```

The base locale is the language of the neutral file (`Strings.resx`). The
importer only writes `.rmf2` resources, so a project with direct `.mf2` files is
rejected.

## Import

```sh
runic-translations import --from resx Properties/Strings.resx --dry-run
runic-translations import --from resx Properties/Strings.resx
runic-translations validate
```

Given the neutral `Strings.resx`, the command imports it into the base locale and
every `Strings.<locale>.resx` beside it whose locale `runic.json` declares
(`Strings.de.resx`, `Strings.pt-BR.resx`). A satellite of an undeclared locale is
reported (`IMPORT-LOCALE`) and not imported. Given a satellite such as
`Strings.de.resx`, only that file is imported, so the base messages must exist
already. `--locale <tag>` (repeatable) imports only those locales; `--dry-run`
reports the outcome and the files it would write without writing them.

New messages go to `<project>/<baseLocale>.rmf2` and their translations beside
them. `--group <name>` writes new messages to `<project>/<name>/<locale>.rmf2`
instead, so the generated API names them `<name>_<key>`.

A message that already exists, as base text or as a translation, is kept: its
text in the `.rmf2` sources wins, and one whose text differs from the `.resx`
is reported as `kept` (`IMPORT-EXISTS`). Run with `--dry-run` first to see
them. `--overwrite` replaces their text with the `.resx` text instead; each such
message keeps its file, group and comments, and only its text changes.

The project may be partly translated, and so may the import: a message whose
translation the import does not bring is left untranslated, and `validate`
reports it (`RTR0010`) until it is translated, as after `locale add`. Only
errors in `runic.json`, the base locale or a message's syntax stop the import.
The Translations Editor (an optional step of the new-project wizard, and
**Interchange**) and the VS Code command **Runic: Import .resx or i18next
Catalog** run the same importer, with a dry run before anything is written.

## What is mapped

| `.resx` | RMF2 |
| --- | --- |
| `<data name="Greeting">` | `Greeting = ...`; the key is the generated member name |
| `Errors.NotFound` | `Errors_NotFound` (a `.` becomes `_`; other characters that are not letters, digits or `_` also become `_`, reported as `IMPORT-KEY-RENAMED`) |
| `<comment>` in the neutral file | a `#` comment above the message, which documents the generated member and the XLIFF export |
| `{0}`, `{1}` | `{$arg0}`, `{$arg1}` |
| `{userName}` (named holes, as with SmartFormat) | `{$userName}` |
| `{0:N0}`, `{0:N2}` | `{$arg0 :number minimumFractionDigits=0 maximumFractionDigits=0}`, with 2 |
| `{0:P0}` | `{$arg0 :number style=percent minimumFractionDigits=0 maximumFractionDigits=0}` |
| `{0:F2}` | `:number` with 2 fraction digits and `useGrouping=never` |
| `{{`, `}}` | a literal `{`, `}` |

Given

```xml
<data name="Greeting" xml:space="preserve">
  <value>Hello, {0}!</value>
  <comment>Shown on the start page. {0} is the user's name.</comment>
</data>
<data name="ItemsInFolder" xml:space="preserve">
  <value>{0:N0} items in {1}</value>
</data>
```

the base file receives

```rmf2
# Shown on the start page. {0} is the user's name.
Greeting = Hello, {$arg0}!
ItemsInFolder = {$arg0 :number minimumFractionDigits=0 maximumFractionDigits=0} items in {$arg1}
```

Text with outer whitespace or line breaks is written as a quoted pattern
(`{{  Indented}}`). Comments of satellite files are not imported, since they
describe the same message.

Positional inputs keep their order but not their meaning. After the import,
rename them in the base file (for example `$arg0` to `$name`, with the editor's
or language server's rename) and in the translations; the generated C# then
takes `name:`.

## What is reported

The command imports what it can and lists the rest. It exits with 0 even when
single messages were skipped or refused; `--format json` lists every message in
the `interchange` member of the payload (`format: "resx"`, schema
[`translations-tool-v1`](../../../specs/translations/schemas/translations-tool-v1.schema.json)).

| Code | Outcome | Meaning |
| --- | --- | --- |
| `RESX-NON-STRING` | skipped | A file reference, image or other typed entry: a `type` other than `System.String` (with or without its assembly), or a `mimetype`. |
| `RESX-DESIGNER` | skipped | Windows Forms designer metadata (`>>button1.Name`, `$this.Text`). |
| `RESX-NO-VALUE` | skipped | A `<data>` entry without a `<value>` element. |
| `IMPORT-EXISTS` | kept | The message already exists with other text and was not changed; `--overwrite` replaces it. |
| `IMPORT-NO-BASE` | refused | A translation whose key has no base message. |
| `IMPORT-INVALID` | refused | The message does not compile; the reason names the RTR code, for example a translation that uses `{2}` while its base text has only `{0}` (`RTR0016`). |
| `IMPORT-KEY-COLLISION` | refused | Two keys map to the same RMF2 name. |
| `RESX-FORMAT` | notice | A format such as `{0:d}`, `{0:C}` or an alignment `{0,10}` was dropped: add `:date`, `:time`, `:datetime` or `:number` yourself. |
| `RESX-BRACE` | notice | A `{` or `}` that is not part of a format item was kept as text. |
| `IMPORT-LOCALE` | notice | A satellite of a locale that `runic.json` does not declare. |

Refused messages are also `RCLI8216` warnings. A file that is not well-formed XML
(`RESX-MALFORMED`) stops the command with `RTR0092` and exit code 2, and so do
imported messages that make the project fail to compile for a reason the
importer cannot attribute to one message; nothing is written then.

## Replace the call sites

With `var text = new AppText(manager)` ([.NET quick start](quickstart-dotnet.md)),
`Strings.Save` becomes `text.Messages.Save`, `string.Format(Strings.Greeting, name)`
becomes `text.Messages.Greeting(arg0: name)` (or the name you gave the input), and
`string.Format(Strings.ItemsInFolder, count, folder)` becomes
`text.Messages.ItemsInFolder(arg0: count, arg1: folder)`. In WPF,
`{x:Static p:Strings.Greeting}` becomes `{rt:Message Greeting, Arg0={Binding UserName}}`
([WPF adapter](../../../packages/dotnet/Runic.Translations.Wpf/README.md)). When
nothing references the `.resx` files any more, delete them.

## PO files

GNU gettext `.po` files are not imported. Either:

- convert them to i18next JSON with `i18next-conv` (`i18next-conv -l de -s de.po -t locales/de/translation.json`); check that its
  plurals come out as JSON v4 keys (`_one`, `_other`), not `_plural` or `_0`,
  and follow
  [Migrate from i18next](migrate-i18next.md); or
- keep the PO translations in your TMS's translation memory, upload the files
  of [`xliff export`](../../../tools/dotnet-runic-translations/README.md#exchange-xliff-with-translators-and-tms-tools),
  let the TMS pre-translate them, and bring them back with `xliff import`.
