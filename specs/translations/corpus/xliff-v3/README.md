# XLIFF profile v3 interchange corpus

Golden cases for `runic-translations xliff import` and `TranslationInterchange.ImportXliff21` against a live
catalog. [`project/`](project) is the `shop` catalog (en, de) with its review file `shop.review.json`; every case
in [`index.json`](index.json) imports one file from [`files/`](files) into a copy of it and states:

- the outcome of every unit (`applied:<review state>`, `skipped:<code>`, `refused:<code>`);
- the notices of the import report (an ignored translator note, a demoted approval, ...);
- the resulting `de.rmf2` in [`expected/`](expected), or `null` when no source changes.

`export` is also an export golden: exporting `project/` with its review file writes
`files/export.de.xliff` byte for byte. Its `farewell` approval was made while the source read "Goodbye", so it
is exported as needs-review, and `validate` reports `RTR0090` for it.

**The TMS files are modeled, not captured.** `crowdin`, `weblate`, `phrase` and `lokalise` were built from a
Runic export of `project/` and changed to show the behaviour this corpus assumes for each tool's XLIFF 2.x
output (each case's `description` in `index.json` says which): which notes and attributes it keeps, how it marks
untranslated, needs-editing and approved segments, its segmentation, `<mrk>` annotations and `<ph>` without
`equiv`. The assumptions follow the tools' published XLIFF descriptions as the authors understood them; they
were not checked against files exported from those services. Replace a modeled file with a captured export
when one is available, and keep both when they differ.

The other cases: `partial` is a Runic export edited in a text editor, `stale-approval` was exported before the
`farewell` source changed (`stale-approval-refuse` imports it with `--source-changed refuse`), `nested-groups`
wraps the cart units in nested `<group>` elements, `selectors` translates the select `order_status` and the
plural `notifications` (header `<ph>` and variant `<pc>` codes, one tool keeping only their ids), and `profile-v2` is the profile 2 format of Runic 0.8. Every
file validates against the pinned OASIS XLIFF 2.1 core schema
(`tests/dotnet/Runic.Translations.Tooling.Tests/Fixtures/xliff-2.1`).

The runners are `XliffCorpusTests` in `tests/dotnet/Runic.Translations.Tooling.Tests` (library and source
writer) and the `xliff import` cases in `tests/dotnet/Runic.Translations.Build.Tests/CliIntegrationTests.cs`.
When a case changes on purpose, update its expectations here and review the new `expected/` file by hand.
