# Migration import corpus

Inputs and expected output of `runic-translations import --from resx|i18next`
([Migrate from .resx](../../../../docs/guides/translations/migrate-resx.md),
[Migrate from i18next](../../../../docs/guides/translations/migrate-i18next.md)).

- `project/`: the target project (`en`, `de`) with existing messages, so the import shows that they keep their place
  and comments.
- `resx/`: `Strings.resx`, its German satellite and a French one for an undeclared locale. It covers positional and
  named holes, `N0`/`P0` formats, a dropped date format, escaped braces, quoted patterns, a comment, a typed entry, a
  designer entry, a translation that adds an input and one without a base message.
- `i18next/locales/`: `<lng>/<namespace>.json` files with nested keys, a second namespace, interpolation with formats,
  cardinal (with `_zero`) and ordinal plurals, a form German does not use, nesting, `<Trans>` tags, arrays and numbers.
- `expected/`: the `.rmf2` files after importing `resx/Strings.resx` or `i18next/locales` into `project/`.

`MigrationCliTests` in `tests/dotnet/Runic.Translations.Build.Tests` runs both imports and checks the files and the
reported entries and notices.
