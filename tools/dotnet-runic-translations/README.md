# dotnet-runic-translations

Create, import, validate, generate, and verify Runic Translations MF2 projects, and exchange their translations as XLIFF, from a project-local .NET tool. Use it in developer workflows and CI to catch message errors before generated C# or ESM reaches an application.

## Install locally

```bash
dotnet new tool-manifest --output .config
dotnet tool install dotnet-runic-translations --version <VERSION>
```

Replace `<VERSION>` with the current version shown on NuGet. The tool targets .NET 10. Commit `.config/dotnet-tools.json`, restore it with `dotnet tool restore`, and keep the tool on the same exact release as the runtime, build package, and Vite adapter.

## Validate an MF2 project

```bash
dotnet tool run runic-translations -- validate \
  --project translations
```

The project path may name the conventional directory or its `runic.json`.
Without `--project`, `validate`, `generate` and `verify` use `./runic.json`, then
`./translations/runic.json`, so `runic-translations validate` works from the
project directory. `runic-translations help <command>` describes every option.
New projects use grouped RMF2 resources in sibling files such as `en.rmf2` and
`de.rmf2`. Direct locale-directory `.mf2` messages are also supported; the
representation is inferred from source files and cannot be mixed.

`--project` is repeatable: `validate --project src/App/translations --project src/Lib/translations` validates
each catalog and reports their diagnostics together (exit 2 when any fails). `--sarif <file>` also writes every
diagnostic as a SARIF 2.1.0 report (one rule per RTR code, linked to its reference entry; UTF-16 columns), for
GitHub code scanning or other CI annotations. Artifact URIs are percent-encoded and relative to `%SRCROOT%`, the
repository root (the nearest directory with `.git` above the working directory, else the working directory),
which `originalUriBaseIds` declares; a file outside it gets an absolute `file:` URI. So the report is the same
wherever in the repository `validate` runs. It is written when validation fails too, and when one of several
projects cannot be read: the others are still validated, and the run's `invocations` record the failure.

```bash
dotnet tool run runic-translations -- validate --project src/App/translations --project src/Lib/translations --sarif artifacts/translations.sarif
```

## Report translation coverage

```bash
dotnet tool run runic-translations -- status
dotnet tool run runic-translations -- status --min-coverage de=100,fr=80 --format json
```

`status` lists, per catalog and locale, the messages the locale translates, those a fallback locale other than
the base locale serves (`de-CH` from `de`), those missing (shown in the base locale), and with a review file
(below) the stale ones, translations whose source changed since they were recorded, counted as `validate`
reports them (`RTR0090`; an unreadable review file is the warning `RTR0091` and leaves the column empty):

```text
shop (translations/runic.json): 10 message(s), base locale en
  locale      translated  fallback  missing  stale  coverage
  en                  10         0        0      -      100%
  de                   3         0        7      1       30%  below 80%
```

Coverage is translated plus fallback as a percentage of the base locale's messages. `--min-coverage
<locale>=<percent>` (repeatable or comma-separated; `*` for every translated locale) makes the command exit
with 1 and the fault `RTR0099` when a locale is below it; the payload, `runic.translations.status/1`
(schema `translations-status-v1.schema.json`, written by `schema`), is then in
`fault.data.payload`. `--project` is repeatable. To require completeness at build time instead, use the
locale's own policy in `runic.json`: `{ "tag": "fr", "fallback": "en", "validation": { "translationCompleteness":
"warning" } }` lets `fr` lag while the project's `error` still applies to the other locales.

## Add or remove a locale

```bash
dotnet tool run runic-translations -- locale add fr
dotnet tool run runic-translations -- locale add de-CH --fallback de
dotnet tool run runic-translations -- locale remove fr --dry-run
```

`locale add <tag>` adds the locale to `runic.json`'s `locales` (keeping the file's formatting; an object entry
with `--fallback` or when the list has object entries) and creates an empty `<tag>.rmf2` beside every
base-locale `.rmf2` file. It writes nothing when the project would not compile with it. The new locale is
untranslated, so `validate` reports `RTR0010` until it is translated, unless the locale entry relaxes
`validation.translationCompleteness`. `locale remove <tag>` deletes the locale's `.rmf2` files (or its `<tag>/`
`.mf2` files and the directories that become empty), then removes the entry and its review entries; if a file
cannot be deleted, the locale stays declared and the command can run again. It refuses the base locale and a
locale that another locale falls back to. Both accept `--project` and `--dry-run`, which lists the files and
directories and marks those outside the `runic.json` directory (from `sourceRoots`). Edits to `runic.json` keep
its line endings.

## Override library catalogs

```bash
dotnet tool run runic-translations -- overrides list
dotnet tool run runic-translations -- overrides add acme.grid --locale fr
dotnet tool run runic-translations -- overrides add acme.grid --locale de --key grid.title --key grid.empty
```

An application overrides the text of a library catalog that a package it uses packs (see
[Build-time overrides](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/composition.md#build-time-overrides)).
Both commands find the library in the restore output (`obj/project.assets.json` of the project that holds
`runic.json`) or in `node_modules` (`package.json#runicTranslations`), as the build and the Vite plugin do, so
restore first. `overrides list` lists the installed library catalogs and, for each catalog `runic.json` overrides,
its locales (the library's and those the application adds, with their fallback and the messages shown in each
locale's own text), the overridden messages, overrides whose library text changed, and the build's diagnostics.

`overrides add <catalog> --locale <tag> --key <key>` adds each message to
`translations/overrides/<catalog>/<tag>.rmf2`, seeded with the library's text in that locale (for a locale the
library lacks, the text of the locale it falls back to) and with the library's base text as comments above it, and
records the library text it was written for (the base text and the text that locale shows) in
`translations/overrides/<catalog>/<catalog>.review.json`, so `validate`, `status`, the language server, the
Translations Editor and `overrides list` report the override (`RTR0090`) when a library update changes either. Keys are
logical (`grid.title`) or canonical (`grid_title`). Without `--key`, it creates `<tag>.rmf2` with every library
message as a commented-out template. A locale the library lacks is added with its first message and falls back by
the build's rule (an added locale with no message yet is not a fallback target of other locales). An installed catalog
that `runic.json` does not list yet is added to `"overrides"`. The request is checked first; `runic.json`, the override
source and the review file are then written together, each only while it is unchanged since it was read, or nothing is
written. An unreadable review file (`RTR0091`) stops the command until it is fixed or deleted. `--dry-run` reports the
change without writing it. A custom `BaseIntermediateOutputPath`, `MSBuildProjectExtensionsPath` or `ArtifactsPath`
and Yarn Plug'n'Play are not searched for library catalogs.

## Generate C# and ESM

```bash
dotnet tool run runic-translations -- generate \
  --project translations \
  --output obj/translations \
  --emit-csharp \
  --emit-esm
```

When no emit option is present, projects default to typed C#, locale-v5 JSON plus
its asset manifest, and the cohesive ESM-v5 package. When any option is present,
only that group is rendered. V5 supports `--emit-csharp`, `--emit-json`, and
`--emit-esm`; the retired `--emit-typescript`, `--emit-template-manifest` and
`--emit-cpp` options were removed and are rejected as unknown options.
Validation permits an empty v5 project as a scaffold, but `generate` and `verify`
fail with `RTR0009` until the effective default locale defines a canonical key.

`--pseudo-locales` on `validate`, `generate` and `verify` also generates the development pseudo-locales that
`runic.json` declares in `pseudoLocales` (`en-XA`, `ar-XB`); without it they are left out, as release builds should.
See [Pseudo-locales](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md#pseudo-locales).

`generate --overrides <directory>` compiles the project, a library catalog, with an application's override sources (the
`<locale>.rmf2` files of `translations/overrides/<catalog>/`), each checked against the library's key (`RTR0103`).
`--locales <list>` keeps only the library locales that these application locales resolve to as the runtimes resolve a
requested locale (the locale itself, else by CLDR likely subtags and parent locales: `zh-TW` keeps `zh-Hant`), with their
fallbacks and the base locale; an invalid tag is a usage error. `--shared-runtime` writes ESM whose `runtime.js` takes
its code from `runtime-core.js` beside the catalog directory, shared by every catalog generated into the same output
directory. With any of the three, `generate --emit-esm` owns `<catalog>.esm-v5/` and removes files there that it did not
write. The Vite plugin's `dependencies: "auto"` uses all three.

`--contract <file>` with `--emit-json` (and no other `--emit-*` option) builds
translation packs for a release that has already shipped. `<file>` is a locale
artifact of that release, such as its `<catalog>.en.locale-v5.json`. Each pack
then holds only the messages that release accepts and carries its contract
fingerprint, and the command prints each message it leaves out with the reason.
See [Ship translation updates without a release](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/ota.md).

Use `Runic.Translations.Build` for generated C# or when MSBuild should invoke this local tool. Use this CLI directly when Vite, CI, or a custom script owns artifact generation.

## Import a text-keyed catalog

```bash
dotnet tool run runic-translations -- import-text \
  --project translations \
  --group stored \
  --base locales/untranslated.json \
  --table de=locales/de.json
```

`import-text` turns a catalog keyed by base-language text, such as
`{ "Remove user {name}": "Benutzer {name} entfernen" }`, into the RMF2 group
`<project>/<group>/`. Each `--table <locale>=<path>` (repeatable) is a JSON object
of string values for a locale declared in `runic.json` other than the base locale.
`--base` is an optional JSON array of base texts without a translation yet. The
base texts are the `--base` entries and then each table's keys, in order of first
appearance.

It writes `<baseLocale>.rmf2`, one `<locale>.rmf2` per table (an untranslated
text is omitted) and `base-text.json`, then validates the project as `validate`
does. `{name}` placeholders become `{$name}`; other `{`, `}` and `\` are escaped,
and text that an unquoted pattern would change (outer whitespace, a leading `.`,
a newline) is written as a quoted pattern. A translation that uses a placeholder
its base text lacks, or a table value that is not a string, fails with `RTR0088`
(one `RCLI8214` diagnostic per problem) and exit code 2, and nothing is written. `--dry-run` reports and validates the
files without writing them.

Keys are the first seven ASCII words of the text without placeholders
(`Remove user {name}` becomes `remove_user`), with `_2`, `_3` and so on for
collisions. A re-import reads the existing `<baseLocale>.rmf2` back: a text that is
still present keeps its key, new texts get new keys, and removed texts are dropped
and listed in the output. `base-text.json` maps each base text to its generated
message key, `{ "Remove user {name}": "stored_remove_user" }`, for an application
that must look up text it stored in the base language. See
[Import a text-keyed catalog](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md#import-a-text-keyed-catalog).

## Migrate a .resx or i18next catalog

```bash
dotnet tool run runic-translations -- import --from resx Properties/Strings.resx --dry-run
dotnet tool run runic-translations -- import --from i18next public/locales --project translations
```

`import --from resx|i18next <path>` writes an existing catalog into the project's `.rmf2` sources through the same
writer as `xliff import`: a new message goes to `<project>[/<group>]/<locale>.rmf2`, and an existing one (base text or
translation) is kept and, when its text differs, reported as `kept` (`IMPORT-EXISTS`). `--overwrite` replaces the text
of existing messages instead; they keep their place and comments. A neutral `Strings.resx` brings its declared `Strings.<locale>.resx` satellites;
an i18next directory holds `<lng>.json` or `<lng>/<namespace>.json` files. Keys become RMF2 names (nested i18next keys
and namespaces become groups), `{0}` becomes `{$arg0}`, `{name}` and `{{name}}` become `{$name}`, i18next plural suffixes
(`_zero`, `_one`, ..., `_other`, `_ordinal_*`) become `.match $count` on the locale's CLDR categories, and `.resx`
comments become descriptions. `--locale` (repeatable) selects locales, `--group` places new messages in a group
directory and `--dry-run` writes nothing.

What cannot be mapped is skipped or refused per message, with a code such as `RESX-NON-STRING`, `I18NEXT-NESTING`,
`IMPORT-NO-BASE` or `IMPORT-INVALID`, and lossy mappings are notices; the rest is imported and the command exits with 0.
The `interchange` member of the JSON payload (`operation: "import"`, `format: "resx"` or `"i18next"`) lists them per
locale. An unreadable source fails with `RTR0092`. PO files are not supported: convert them to i18next JSON or go
through a TMS and XLIFF. See
[Migrate from .resx](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/migrate-resx.md)
and [Migrate from i18next](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/migrate-i18next.md).

## Exchange XLIFF with translators and TMS tools

```bash
dotnet tool run runic-translations -- xliff export --project translations --output xliff
dotnet tool run runic-translations -- xliff import --project translations --source xliff/app.de.xliff --dry-run
dotnet tool run runic-translations -- xliff import --project translations --source xliff/app.de.xliff
```

`xliff export` writes `<catalog>.<locale>.xliff` (XLIFF 2.1, Runic profile 3) for every locale except the base
locale, or only for the `--locale` ones (repeatable). Each message is one unit whose `id` is the message key:

- Text is text, and every placeholder is a `<ph>` whose `equiv` is its exact MF2 expression, such as
  `Hello <ph id="1" equiv="{$name}" disp="name"/>!`. TMS tools protect these codes; import restores the
  expression, so a placeholder round-trips losslessly.
- The message's comment and its `@param` lines are a readable `<note category="description">`. The
  `runic:unit` and `runic:review` notes beside it are machine data (Base64 JSON) for the import.
- A message with `@maxLength N` has `slr:sizeRestriction="N"` (the XLIFF Size and Length Restriction module,
  profile `xliff:codepoints` with NFC), so a TMS can stop a longer translation; placeholders are not counted.
  Import ignores the attribute, and `validate` checks the length (`RTR0098`).
- A message with selectors (plurals, selects) or declarations is one unit with its MF2 syntax protected: one
  `<ph>` holds the declarations and `.match`, and each variant is a `<pc>` whose `equivStart` is its keys, so
  the translator edits only the variant texts:

  ```xml
  <source><ph id="h" canCopy="no" canDelete="no" equiv=".input {$n :integer}&#xA;.match $n" disp=".match $n"/>
    <pc id="v1" equivStart="one {{" equivEnd="}}" dispStart="one">One item</pc>
    <pc id="v2" equivStart="* {{" equivEnd="}}" dispStart="*"><ph id="1" equiv="{$n}" disp="n"/> items</pc></source>
  ```

  A missing translation into a locale whose plural categories differ from the source's gets a target in state
  `initial` with that locale's variants (Czech `:integer`: `one`, `few`, `*`, and `many` too for a decimal
  `:number`; Japanese: `*`), filled with the source text for the translator to replace. `@maxLength` becomes a
  size restriction on each variant.
- Text holds characters that XML 1.0 forbids (such as U+0001) as `<cp hex="0001"/>`, which import reads back. A
  placeholder, header or key that holds one cannot be an attribute, so its unit is exported as `translate="no"`
  and reported (`XLIFF21-CONTROL-CHARACTER`).
- A message with markup is exported as `translate="no"` with its MF2 source and reported as loss
  (`XLIFF21-STRUCTURED-MESSAGE`); translate it in the `.rmf2` sources or the editor.
- An existing translation is in state `translated`, a missing one has no target. With a review file (below),
  `approved` is `final` and `needs-review` is `translated` with `subState="runic:needs-review"`. An approval
  whose source changed since it was made is exported as needs-review and reported (`REVIEW-STALE`); the files
  are not changed. `--review <path>` reads another review file.

`xliff import --source <file>` checks the document against the project and applies it **per unit**. It accepts
XLIFF 2.0 and 2.1, the profile 2 files of 0.8, and what TMS tools write from an export: nested `<group>`s,
several segments with `<ignorable>`s, `<mrk>` annotations, module metadata, unknown attributes and foreign
notes (ignored; a dropped translator note is reported as `XLIFF21-NOTE-IGNORED`). For each unit:

| Unit | Outcome |
| --- | --- |
| no target, or state `initial` | skipped, `XLIFF21-UNTRANSLATED` |
| empty target, unless its state is `translated` or later | skipped, `XLIFF21-EMPTY-TARGET`; the base text stays the fallback |
| markup message, a selector message from an export before selector units, or `translate="no"` | skipped, `XLIFF21-STRUCTURED-SKIPPED` |
| a selector target that changes the declarations or `.match` | refused, `XLIFF21-SELECTOR-CHANGED`; a translation may change variant texts and add or remove variants, such as an exact `0 {{…}}` |
| a selector target with an empty variant (unless the source's variant with those keys is empty) | refused, `XLIFF21-EMPTY-VARIANT` |
| an added variant whose `equivStart` is not only MF2 keys (`*`, a number, a category or a literal, one per selector) and `{{` | refused, `XLIFF21-VARIANT-KEY`; the composed message must read back as the same header, keys and variant texts, else `XLIFF21-PLACEHOLDER-CHANGED` |
| a plain target (no `<pc>`) for a message with variants, unless the current translation is plain or the locale has one plural category | refused, `XLIFF21-SELECTOR-CHANGED`; a tool that strips every code joins the variant texts |
| plural keys the target locale never selects or categories it lacks (`RTR0094`, `RTR0095`) | applied with the notice `XLIFF21-PLURAL-CATEGORIES`, or refused with that code when the locale's `validation.pluralCategories` is `error` |
| a placeholder outside the message's caller contract, an unknown inline code, invalid MF2 | refused, `XLIFF21-PLACEHOLDER`, `XLIFF21-INLINE` or `XLIFF21-TARGET-INVALID` |
| a target `<ph>` whose `equiv` changes a placeholder (another function or options, or a literal for a variable) | refused, `XLIFF21-PLACEHOLDER-CHANGED`; a target may only use the expressions of the current source and translation, compared in canonical form |
| a target identical to the source, with no state and no Runic review note | applied as needs-review, reported as `XLIFF21-TARGET-COPIES-SOURCE` (some tools copy the source into untranslated units); with an explicit state it keeps that state |
| a key the catalog does not have, a duplicate unit | refused, `XLIFF21-UNKNOWN-KEY`, `XLIFF21-DUPLICATE-UNIT` |
| its source changed since export | applied as needs-review, or refused (`XLIFF21-SOURCE-CHANGED`) with `--source-changed refuse` |
| otherwise | applied |

Applied translations are written into the sources as the editor writes them: an existing translation keeps its
`.rmf2` file, comments and formatting and only its body changes; a missing one is added to the target locale's
`.rmf2` beside the base file, inside its existing groups, or as the same file name in the target locale's
directory for direct `.mf2` sources. A key added to an existing group copies the indentation of the group's
other entries. Equivalent spellings of the same text count as unchanged. Every file is staged and the project is
compiled before anything is written; `--dry-run` reports without writing. If a source or the review file changed
on disk since the import read it (an editor save, a second import), nothing is written and the command fails
with `RTR0092`; run it again. Refused units are warnings (`RCLI8216`) and the rest is applied; the command exits
with 0. A document that cannot be used at all (another catalog or base locale, an undeclared target locale, not
XLIFF 2, more than 8 MiB or elements nested deeper than 64 levels) fails with `RTR0092` (`RCLI8215` names the
problem), exit code 2, and nothing is written.

In CI, or for a bot that imports TMS output, add `--fail-on refused`: when any unit is refused, the command
writes nothing and fails with `RTR0092` and exit code 2, so a refused unit (including one refused by
`--source-changed refuse`) fails the pipeline. The JSON payload is the same, with `files` empty and the payload
in the failure data:

```bash
dotnet tool run runic-translations -- xliff import --source xliff/app.de.xliff --source-changed refuse --fail-on refused
```

The review state comes from the segment `state` (`translated`, `translated` + `subState="runic:needs-review"`,
`reviewed` or `final` as approved) or from the unit's `runic:review` note when both agree; a TMS state change
wins. The `interchange` member of the JSON payload lists the counts (`added`, `changed`, `unchanged`,
`skipped`, `refused`, `sourceChanged`), one entry per unit that was not applied unchanged with its code, the
notices and the files written.

## Track review state and stale translations

```bash
dotnet tool run runic-translations -- review mark --key greeting --locale de --state approved
dotnet tool run runic-translations -- validate
```

The optional review file `<catalog>.review.json` beside `runic.json`
(`runic.translations.interchange-review/2`, schema `interchange-review-v2.schema.json`) records the review
state of translations. The Translations Editor and the language server read and write the same file (the
editor's notes and terminology stay in its own state). Commit it with the sources. Each entry stores the
per-unit fingerprint of the source its translation was made or approved for (a hash of the key, the source text
and the caller contract), so editing one message never touches the others. The source text is hashed in canonical
MF2 form: spacing inside an expression or between a `.match`'s parts, literal quoting, option order and equivalent
escapes do not change it; different text, placeholders, functions, options or selectors do.

- `review mark --key <key>... --locale <locale> --state draft|translated|needs-review|approved [--note <text>]`
  creates or updates entries with the fingerprint of the current source. `--key` is repeatable.
- `xliff import` updates the file when it exists, or creates it with `--review`. A translation of a changed
  source keeps the fingerprint of the source the translator saw.
- `validate` warns with `RTR0090` at the translation when its entry's fingerprint is not the current one:
  the translation predates a source change. Review it, then run `review mark`. An unreadable review file is
  the warning `RTR0091`. Without the file nothing is reported.
- `xliff export` reads it for the XLIFF review state (see above).
- The language server (`lsp`) reports the same `RTR0090` warnings, shows the review state in hover and offers
  **Mark … reviewed**, which records the entry's state again for the source on disk. It refuses while the file with
  the base message or the translation has unsaved changes (save first), and like the editor it works while another
  locale misses keys. All three writers replace the review file only if it did not change since they read it.

## Other commands

```text
runic-translations verify  --project <directory|runic.json> --output <directory>
runic-translations schema  --output <directory>
runic-translations serve
```

- `verify` renders in isolation and byte-compares the selected expected output, including extra-file detection.
- `schema` copies the bundled source, artifact, manifest, normalized-AST, capability, review-file and tool payload schemas.
- `serve` is a persistent compiler for development servers, used by `@runic-artifex/vite-plugin-runic-translations`. It keeps unchanged sources parsed and compiled between requests, so a request after an edit recompiles only the edited files. See [serve protocol](#serve-protocol).
- `lsp` runs the RMF2 language server over standard input/output for the VS Code and Visual Studio extensions. It is hidden from help, and its protocol subset and custom methods are outside the 1.0 compatibility rules until the production language server (2.0).

`generate` leaves output files whose bytes are unchanged untouched, so file watchers only see artifacts that changed.

Arguments can be placed in a UTF-8 response file and passed as `@arguments.rsp`;
help lists it as `@<file>`. The tool uses Runic Command Line's response files: a
line whose first non-blank character is `#` is a comment, arguments are quoted
as on the Windows command line, `@file` inside a response file is relative to
that file's directory, files nest at most 16 levels deep, and `@@` stands for a
literal leading `@`. A response file that cannot be read, is not valid UTF-8,
has an unterminated quote, is nested too deeply or includes itself is a usage
error (exit code 2) with the code `RCLI1017`, `RCLI1018`, `RCLI1019`, `RCLI1020`
or `RCLI1021`.

## Output and exit codes

Every command writes human text by default. `--format json` (or
`RUNIC_COMMANDLINE_OUTPUT=json`) writes one `runic.commandline/1` envelope to
standard output instead. `--runic-output` is the former spelling of
`--format`; it is still accepted until 2.0 but no longer listed in help or
completions.

The envelope's payload is `runic.translations.tool/1` for every command except
`support`, whose payload is `runic.translations.support/1`. Their JSON schemas
are published as `translations-tool-v1.schema.json` and
`translations-support-v1.schema.json` (written by `schema`). The `output` and
`error` members of `runic.translations.tool/1` are display text, not a format to
parse; read `diagnostics`, `interchange` (for `xliff`, `import` and `review`) and the envelope's `exitCode` instead.
Later releases may add members; readers ignore members they do not know.

The exit codes are the same in every Runic tool:

| Exit code | Meaning |
| --- | --- |
| `0` | Success. |
| `1` | The command failed or a resource was unavailable, for example a `verify` difference or a file that cannot be read or written. |
| `2` | Invalid invocation, or input that failed validation, such as translation (`RTR`) diagnostics. |
| `3` | Internal or host failure. |
| `130` | The command was cancelled, for example with Ctrl+C. |

`serve` reports the same exit code in each response's `exitCode`.

## Support envelope

```text
runic-translations support preview --editor-diagnostics <zip>
runic-translations support collect --editor-diagnostics <zip> --destination <path>
runic-translations support remove  --destination <path>
```

`support` reads the diagnostic ZIP that the Translations Editor's `diagnostics`
command exports (`runic.translations.editor-diagnostics/1`) and builds a local,
unsigned `runic.support-envelope/1` from it. `preview` reports what the envelope
would contain, `collect` writes it to a new file, and `remove` deletes a file
only after checking that `collect` wrote it. It accepts only bounded,
path-free summary fields and never collects source, translation or review
text, workspace paths, sessions, cookies or tokens. Nothing is uploaded. It
moved here from `dotnet runic support --mode <mode>`, which forwards to it
until 2.0.

With `--format json` the payload is `runic.translations.support/1`:

```json
{"mode":"collect","destination":"/home/me/support-envelope.json","digest":"sha256:...","collectors":["runic.translations.editor-diagnostics"],"omissions":["automatic-capture","network","workspace-roots","relative-paths","source-text","translation-text","review-text","sessions-cookies-tokens"],"outboundTransportAttempts":0}
```

`destination` is the full path of the envelope, or `null` for `preview`;
`digest` is the SHA-256 of the envelope's bytes; `collectors` and `omissions`
are empty for `remove`. A failure reports an `RTRSUP` code from the
[diagnostics reference](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/diagnostics.md#runic-translations-support-codes)
with exit code `2`, or `RTRSUP025` with exit code `1` when a file cannot be
read or written.

## Diagnostics

Translation diagnostics are printed to standard error, one per line, in the
form editors and MSBuild recognize:

```text
translations/de.rmf2(2,3,2,8): error RTR0011: Locale 'de' defines 'commands.remov', which base locale 'en' does not define. Did you mean 'commands.remove'?
```

With `--format json` they are structured entries rather than text. A
success carries them in `payload.diagnostics`; a failure carries the same
`runic.translations.tool/1` payload in `fault.data.payload`:

```json
{"code":"RTR0011","severity":"error","message":"Locale 'de' defines ...","path":"translations/de.rmf2","line":2,"column":3,"endLine":2,"endColumn":8,"helpUri":"https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/diagnostics.md#rtr0011"}
```

The envelope's own `diagnostics` array holds only Runic.CommandLine `RCLI`
diagnostics, such as usage errors. See the
[diagnostics reference](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/diagnostics.md).

The [Translations Editor](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/apps/translations-editor/README.md)
is built from source; the editor is a separate product with its own releases.
Its `validate <path>` takes the workspace as a positional argument; the
equivalent tool command is `runic-translations validate --project <path>`.

## Serve protocol

`serve` speaks line-delimited JSON (`runic-translations-serve/1`, schema `translations-serve-v1.schema.json`) over standard input and output. The protocol is a contract between the Vite plugin and the tool of the same release, so a minor release may change both together; diagnostics never go to standard output. It first writes `{"protocol":"runic-translations-serve/1","event":"ready","version":"..."}`. Each request is one line and gets exactly one response line with the same `id`, in order:

```text
{"id":1,"method":"generate","project":"translations","output":".runic/translations","emit":["esm"]}
{"id":1,"ok":true,"exitCode":0,"output":"generated 12 artifact(s).","message":"","diagnostics":[],"elapsedMs":9}
```

Methods are `generate` (`project`, `output`, optional `emit` with `csharp`, `json` or `esm`; another name fails the request), `validate` (`project`) and `shutdown`. `exitCode` and `diagnostics` match the one-shot command, and a translation diagnostic (an `RTR` code) also carries `path`, `line`, `column`, `endLine`, `endColumn` and `helpUri`; `message` is the text it would print on failure. Paths resolve against the server's working directory. The server exits after `shutdown` or when standard input closes. A request line longer than 1 MiB is discarded without being buffered; its reply has `"id":null` and answers the oldest outstanding request, because requests are answered in order.

Trust model: `serve` has the same authority as the one-shot commands run by the same user. It reads requests only from its standard input, so only the parent process that started it can send them; it opens no socket or port. A request may name any `project` and `output` the process can access, and `generate` writes there, exactly like `runic-translations generate --project ... --output ...`. Start it only from trusted tooling, and do not forward untrusted input into its standard input.

## Compatibility and status

This tool targets .NET 10. Before 1.0 a minor release may contain breaking changes, each listed with upgrade steps in the release notes (see [Versions before 1.0](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/eng/release/README.md#versions-before-10)). Pin one exact version in the local manifest and coordinate upgrades with all consumers of its generated artifacts.

- [Vite quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-vite.md)
- [RMF2 project guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md)
- [CLI source and examples](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/tools/dotnet-runic-translations)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/specs/translations/THIRD-PARTY-NOTICES.md) for bundled data attribution.

## RMF2

Grouped `.rmf2` sources enable recursive resources and markup contracts; direct
`.mf2` sources are also supported. `validate`, `generate`, and `verify` use
grammar 5/runtime ABI 2 consistently. Generated JSON is
`{catalog}.{locale}.locale-v5.json`; ESM output is under `{catalog}.esm-v5/`
with `web-module-manifest-v3.json`.
`lsp` starts the bounded stdio language service. See the [RMF2 guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md) for capabilities and limits.

The LSP's standard resource rename is deliberately resource-only: it refuses
workspaces containing application or other non-translation source files rather than emitting a
partial application refactor. Use the explicit source transaction in an IDE or
the editor, then update application call sites with the native language service.
