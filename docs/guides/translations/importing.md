# One-way catalog import

`runic-translations import` is a migration aid, not a compatibility layer. It
reads locale JSON files once, emits ordinary Runic schema-v2 files, writes a
key-by-key conversion report, and validates the result with the canonical Runic
compiler before any output is committed. The generated project has no Paraglide,
inlang, or source-format dependency.

Use an explicit locale mapping for every input file:

```bash
dotnet tool run runic-translations -- import \
  --source en=messages/en.json \
  --source de=messages/de.json \
  --catalog app \
  --default-locale en \
  --namespace Product.Localization \
  --class AppText \
  --output Translations
```

`--format auto` is the default. It recognizes an inlang `$schema` marker or a
complex-message array; otherwise it treats the input as conventional JSON and
preserves braces as ordinary text. Use `--format inlang` for simple inlang files
that omit their schema marker, or `--format json` to forbid inlang constructs.

The output directory contains `app.catalog.json`, one `app.<locale>.json` file
per source, and `runic-import-report.json`. Flat dotted keys become nested Runic
keys. Non-identifier key segments are made valid deterministically, and every
changed mapping appears in the report. Mapping collisions are errors.

## Preview before writing

Add `--dry-run` to perform the full parse, conversion, contract comparison, and
canonical compilation without creating or changing a file. The deterministic
JSON report is written to standard output. Diagnostics go to standard error and
the command returns exit code `1` when a lossless conversion is not possible.

`--allow-partial` is explicit consent to omit messages outside the supported
subset. A message is retained only when it is convertible in every locale and
its input/selector contract agrees across locales. Omitted and rejected keys are
still reported. If no portable messages remain, the importer refuses to write an
empty migration.

## Losslessly converted subset

The importer accepts conventional flat or nested JSON objects whose leaves are
strings. It also recognizes the human-readable JSON files used by the inlang
message-format plugin:

- `{name}` input expressions;
- exact built-in `{value: string|number|integer|date|time|datetime}` formatters;
- balanced identifier-only semantic markup such as `{#strong}text{/strong}`;
- complex messages represented by one descriptor in an array;
- `input name` declarations;
- `local category = count: plural|ordinal` selectors;
- direct literal selectors and complete `selector=value` variant matches;
- an explicit all-`other` catch-all variant.

The imported shapes correspond to the documented inlang message format, but its
project database, runtime API, compiler output, and package model are deliberately
not accepted or exposed as Runic contracts.

## Diagnosed instead of guessed

The importer rejects or, in partial mode, omits anything it cannot preserve
exactly. This includes scalar JSON leaves, malformed or duplicate JSON members,
unknown declaration syntax, formatter options without an exact Runic mapping,
markup options or attributes, unbalanced markup, missing catch-all variants,
per-message metadata, inconsistent locale coverage, and differing input or
selector contracts. It never flattens selectors into text or discards variants,
markup, inputs, or metadata silently.

Keep the report with the migration review. Re-running an import with identical
inputs and options produces identical Runic files and report bytes.
