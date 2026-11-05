# Work with translators and translation tools

The `.rmf2` sources in your repository are the catalog. Translators work on them
in one of three ways:

- in the **Translations Editor**, which edits the sources and the review file
  directly;
- in the sources with an IDE and the **VS Code or Visual Studio extension**,
  which check every edit as `validate` does;
- in a **translation management system** (TMS) or CAT tool, through XLIFF 2.1
  files that `runic-translations xliff export` writes and `xliff import` reads
  back.

This guide follows one translation round through the XLIFF path, then shows how
review state, stale translations, plurals and coverage gates work in all three.
The command reference, with every outcome and code, is in
[the tool's README](../../../tools/dotnet-runic-translations/README.md#exchange-xliff-with-translators-and-tms-tools).

## Before a round

**Add the locale.** `runic-translations locale add fr` declares it in
`runic.json` and creates an empty `fr.rmf2` beside each base file. A regional
variant that only changes a few messages falls back to its language:
`locale add de-CH --fallback de`.

**Give translators context.** A comment above a message, its `@param` lines and
`@maxLength` travel with it into the generated code, the editor and the XLIFF
file:

```rmf2
# The button that saves the current note. Keep it short: it sits in a toolbar.
# @maxLength 12
save = Save

# @param count The number of notes that were deleted.
deleted =
  .input {$count :integer}
  .match $count
  one {{One note deleted}}
  *   {{{$count} notes deleted}}
```

**Check the layout first.** A [pseudo-locale](rmf2.md#pseudo-locales) such as
`en-XA` shows longer, accented text, and `ar-XB` right-to-left text, in a
development build, so truncated labels and concatenated strings show up before anyone
translates them.

**See what is missing.** `runic-translations status` lists, per locale, the
messages that are translated, served by a fallback, missing or stale:

```text
shop (translations/runic.json): 10 message(s), base locale en
  locale      translated  fallback  missing  stale  coverage
  en                  10         0        0      -      100%
  fr                   0         0       10      -        0%
```

## Export

```sh
dotnet tool run runic-translations -- xliff export --project translations --output xliff --locale fr
```

This writes `xliff/shop.fr.xliff`, one XLIFF 2.1 unit per message, with the
message key as the unit's `id`. What a translator sees:

- text as text, and each placeholder as a protected inline code
  (`Hello <ph id="1" equiv="{$name}" disp="name"/>!`);
- the comment and `@param` lines as a note, and `@maxLength` as a size
  restriction (`slr:sizeRestriction`) that a TMS can enforce;
- a plural or other selector message as **one unit**: the `.match` header is a
  protected code and each variant (`one`, `*`) an editable span, so the
  translator writes the variant texts only. When the target language has other
  plural categories (Czech has `one`, `few` and `other`, Japanese only `other`),
  a new translation starts in state `initial` with the target language's
  variants, filled with source text to replace;
- existing translations in state `translated`, approved ones `final` (with a
  review file, below).

Messages with markup (`{#link}terms{/link}`) are exported as `translate="no"`
and reported; translate them in the editor or the sources.

## Settings for your TMS

The export is plain XLIFF 2.1, with nothing tool-specific to install. Set up the
project in your TMS so that:

- **inline codes stay protected**: translators may move a `<ph>` or `<pc>` but
  not edit or delete it (some tools call this "tag protection" or "locked
  placeholders");
- **unit IDs are kept**: they are the message keys;
- **the source is not copied into empty targets** on export ("pre-fill target
  with source"); otherwise an untranslated unit comes back as a translation,
  which import accepts only as needs-review (`XLIFF21-TARGET-COPIES-SOURCE`);
- **segment states are written back** (`translated`, `reviewed`, `final`), so the
  review state round-trips;
- the **XLIFF 2.0 or 2.1** export is used, not XLIFF 1.2.

Import accepts what TMS tools typically change: nested groups, split segments,
`<mrk>` annotations, extra notes and module metadata.

## Import

```sh
dotnet tool run runic-translations -- xliff import --project translations --source xliff/shop.fr.xliff --dry-run
dotnet tool run runic-translations -- xliff import --project translations --source xliff/shop.fr.xliff
dotnet tool run runic-translations -- validate --project translations
```

Import checks every unit against the current sources and applies it on its own:
a broken unit is refused and reported while the others are written. A unit is
refused, for example, when the translator changed a placeholder, dropped a
protected code, or changed a plural message's `.match` header; translators may
add or remove variants, such as an exact `0 {{No notes}}`. A unit whose source
changed since the export is applied as needs-review. The complete list of
outcomes is in
[the tool's README](../../../tools/dotnet-runic-translations/README.md#exchange-xliff-with-translators-and-tms-tools).

For a bot or CI job that imports TMS output, make any problem fail the job and
write nothing:

```sh
dotnet tool run runic-translations -- xliff import --project translations --source xliff/shop.fr.xliff \
  --source-changed refuse --fail-on refused
```

Translations land in the `.rmf2` sources as the editor writes them: an existing
translation keeps its file, comments and formatting, and a new one goes into
the target locale's file beside the base file. Commit the result like any other
change.

## Review state

The optional review file `<catalog>.review.json` beside `runic.json` records,
per message and locale, one of `draft`, `translated`, `needs-review` and
`approved`, together with a fingerprint of the source the translation was made
for. Commit it with the sources. The editor, the language server and the CLI
read and write the same file:

```sh
dotnet tool run runic-translations -- review mark --key save --key deleted --locale fr --state approved
```

- `xliff export` writes `approved` as the XLIFF state `final`, and
  `needs-review` as `translated` with `subState="runic:needs-review"`;
  `xliff import` maps the TMS's states back and updates the file (or creates it
  with `--review`).
- The editor manages review state on the same file; in VS Code, hover shows it
  and **Mark … reviewed** records it.

## Stale translations

When a source message changes after its translation was made or approved, the
translation is **stale**: `validate` and the language server report `RTR0090`
at the translation, `status` counts it in its `stale` column, and the next
`xliff export` exports an approved translation as needs-review. Review the
translation, then mark it again. Only real changes count: spacing, quoting or
option order in the source do not make translations stale.

## Plurals and other selectors

Each locale selects plural variants by its own CLDR categories, so a
translation may have more or fewer variants than the source. `validate` checks
them: a variant the locale never selects is `RTR0094`, and a missing category
that the locale needs is `RTR0095`. `xliff import` applies such a translation
with the notice `XLIFF21-PLURAL-CATEGORIES`, or refuses it when the locale's
`validation.pluralCategories` is `error`. When the editor adds a language, its
starter copies of plural messages get the categories that language needs.

## Coverage in CI

Two ways to keep translations from falling behind:

- **Require them at build time.** By default every locale must translate every
  message (`RTR0010`). Let a locale lag with its own policy in `runic.json`:
  `{ "tag": "fr", "fallback": "en", "validation": { "translationCompleteness": "warning" } }`.
- **Set a minimum.** `status --min-coverage de=100,fr=80` fails (exit code 1,
  `RTR0099`) when a locale is below its threshold; `--format json` gives the
  numbers to a dashboard.

## Coming from another format

`runic-translations import` brings in an existing `.resx` or i18next catalog
([Migrate from .resx](migrate-resx.md), [Migrate from i18next](migrate-i18next.md)).
PO files are not imported: keep their translations in your TMS's translation
memory and pre-translate the XLIFF export with it.

## Translating a library's text

An application that overrides a library's messages keeps those overrides in
`translations/overrides/<catalog>/<locale>.rmf2`
([Build-time overrides](composition.md#build-time-overrides)). They are
ordinary `.rmf2` files, checked against the library's catalog when the
application builds; translate them in the sources or the editor.
