# Runic Translations conformance corpora

The current release contract is exercised by sixteen complementary fixtures:

- [`rmf2-v1/index.json`](rmf2-v1/index.json) is the cross-backend release oracle
  for resource syntax `rmf2-v1` executed as `rmf2-execution-v2`, grammar/AST and
  artifact 5, runtime ABI 3, ESM ABI 4, and generated-name mapping 1;
- [`rmf2-document-v1`](rmf2-document-v1/README.md) is the cross-backend oracle
  for document profile v1: block structure, skeletons, occurrences, plain-text
  projection and pack rejection;
- [`semantic-v5`](semantic-v5/README.md) isolates normalized AST and semantic
  validation cases;
- [`v5-project`](v5-project/README.md) isolates project linking, caller and
  freshness hashes, generated backends, and strict pack decoding;
- [`rmf2-list-conformance.json`](rmf2-list-conformance.json) is the
  cross-backend oracle for `:runic:list`: the 0.8 locale families and locales
  whose patterns differ in kind (`pt-PT`, scripts, `mi` text after `{1}`), each
  type and style, the Spanish `y`/`e` and `o`/`u` forms and the Hebrew `ו-`.
  Each case must format exactly as expected in a linked .NET message, a loaded
  .NET pack, generated ESM and a dynamically decoded ESM artifact. The
  expectations are `Intl.ListFormat`'s output;
- [`rmf2-relative-time-conformance.json`](rmf2-relative-time-conformance.json)
  is the cross-backend oracle for `:runic:relative-time`: CLDR plural
  categories, past and future, `numeric=auto` texts and localized digits. The
  expectations are `Intl.RelativeTimeFormat`'s output; .NET and the ESM
  runtime must format each case exactly;
- [`locale-negotiation.json`](locale-negotiation.json) is the cross-backend
  oracle for locale negotiation with CLDR likely subtags and parent locales:
  .NET `LocaleNegotiation.Negotiate` and the ESM `negotiateLocale` must choose
  the expected declared locale for each preference list. The .NET runner
  (`CldrLocaleConformanceTests`) also compares both runtimes' lists, relative
  times, plural categories and negotiation for every CLDR locale, and their
  canonicalization and negotiation of non-canonical inputs (case, `_`, POSIX
  code sets, extensions, grandfathered and malformed tags) and of 1,500
  generated two- to four-entry preference lists;
- [`rmf2-grouping-conformance.json`](rmf2-grouping-conformance.json) is the
  cross-backend oracle for `useGrouping` on `:integer` and `:number`: the
  `auto` default in every supported locale family, each value in `en`, `de`,
  `es` and `it`, dynamic and inherited options. The expectations are
  `Intl.NumberFormat`'s output for the same option and must format exactly in
  the same four routes as the list corpus;
- [`rmf2-date-percent-conformance.json`](rmf2-date-percent-conformance.json) is
  the cross-backend oracle for [localized formatting](../rmf2-execution-v2.md#localized-formatting):
  every supported locale family's `:date`, `:time` and `:datetime` styles and
  percentages, IANA time zones, and the formatting locale of a requested tag
  (`requested`, default `locale`) over the case's content `locale`, including the
  implied-script cases (`sr-Latn` over `sr`, `zh-TW` over `zh`, `zh-CN` over
  `zh-Hant` and back, `de-Latn-CH` over `de`), dropped extensions and the kept
  `-u-nu-latn`, the Arabic and Thai date rules, native digits (CLDR's default
  numbering system in `bn fa mr my ne ps ar-EG`, Latin in `ar`) in numbers,
  percentages, dates, times and relative time, format-context weekdays (`lv mn
  km tk yo`), `zh-Hant`'s flexible day periods, and pinned percent patterns
  (`oc`, `fa`). The expectations are plain `Intl`
  output under the documented rules and must format in the same four routes as
  the list corpus. The only tolerance is for punctuation that platform ICU
  versions write differently: U+0020, U+00A0 and U+202F count as the same space,
  and U+0027 and U+2019 as the same apostrophe (the `de-CH` group separator, `'`
  in ICU 78 and `’` in older builds). A case may also list `alternates`, the
  exact output of a platform ICU whose locale data differs, with a `note` naming
  that platform (Apple's ICU writes `2026年11月4日 水曜日` for the Japanese full
  date; ICU 72 to 77, which CI's Linux libicu74 and Windows load, write `sv-AX`'s
  medium date as `4 nov. 2026`, and ICU 72 to 75 `yo`'s full date with the
  month `Èrèl`); each
  route must produce the expected string or one of them. Every other case is
  strict on every platform: the expectations of the other cases were checked
  with .NET on ICU 72, 74, 76 and 78. CI runs it on Linux, Windows and macOS, with
  the ESM half in Bun and Node;
- [`localized-format-divergences.json`](localized-format-divergences.json) lists
  the localized dates and times that .NET and ESM write differently for a CLDR
  locale both runtimes' ICU covers, with the reason (`shn-TH`'s Buddhist era name,
  `haw`'s Roman month numerals). `LocalizedFormatComparisonTests` formats numbers,
  percentages, dates and times in both runtimes for every CLDR locale (and with
  `-u-nu-latn` where the default digits are not Latin): numbers must agree
  exactly, and with `Intl.NumberFormat` where the engine carries CLDR 48; dates
  and times are compared where .NET and the engine load the same ICU version,
  and every difference beyond the tolerance above must be listed here. A listed
  entry that no longer occurs fails too. `--test-parameter
  comparison-report=<file>` writes every difference;
- [`localized-format-engine-coverage.json`](localized-format-engine-coverage.json)
  lists, per JavaScript engine and ICU version, the CLDR locales whose dates the
  comparison cannot compare because the engine's `Intl` has no data for them
  (ESM then uses the nearest CLDR parent it has, .NET its ICU's fallback). The
  comparison writes it with `--test-parameter update-engine-coverage=true` and
  fails when Node, or Bun on Linux, covers a different set under the recorded
  ICU version;
- [`rmf2-currency-unit-conformance.json`](rmf2-currency-unit-conformance.json)
  is the cross-backend oracle for `:currency`, `:unit` and `:number
  runic:notation=compact` (D055.4): 3,794 cases over 41 requested locales
  (regional ones such as `de-CH` and `en-IN`, native digits in `ar-EG`, `bn`,
  `fa`, `my` and `mr`, right-to-left `ar`, `fa`, `he` and `ur`, and rich plurals
  in `ru`, `pl`, `cs`, `ga` and `cy`), with every currency display, accounting,
  fraction digits, run-time codes from `formats`, every unit width, CLDR and
  composed `-per-` units, compact patterns around each magnitude, selection on
  a compact number, compact fraction digits and `fractionDigits=auto`. Expected
  values are `Intl.NumberFormat` output on ICU 78 except where a `note` records
  ICU departing from CLDR; all four routes must match exactly, with isolation off
  (the bidi corpus covers it);
- [`rmf2-format-data-invalid.json`](rmf2-format-data-invalid.json) holds crafted
  `formatData` members of a locale artifact that both runtimes reject as
  `RTR0023/malformed` in exact packs and overlays
  (`Rmf2FormatDataValidationTests`, the
  [embedded format data rules](../rmf2-execution-v2.md#embedded-format-data));
- [`currency-unit-divergences.json`](currency-unit-divergences.json) lists, by
  locale and kind, where `CurrencyUnitComparisonTests` (every CLDR locale, .NET
  and ESM agreeing exactly) differs from `Intl.NumberFormat` on ICU 78, with the
  reason; the test fails when a listed difference no longer occurs;
- [`rmf2-bidi-conformance.json`](rmf2-bidi-conformance.json) is the cross-backend
  oracle for [placeholder isolation](../rmf2-execution-v2.md#bidirectional-text):
  MF2's default bidi strategy in left-to-right (`en`) and right-to-left (`ar`,
  `he`) messages, with the catalog's `bidiIsolation` on, off and default (decided
  per call, including a right-to-left request on a left-to-right catalog through
  `requestedLocale`), every `u:dir` value, lists with isolated items, relative
  time, percentages, right-to-left dates, negative numbers in native digits
  (`ar-EG`'s ALM and `fa`'s LRM before the minus sign, inside the isolate),
  literals, local aliases and explicit functions. Expectations use `\u2066`
  to `\u2069` escapes and must format exactly in the same four routes as the list
  corpus;
- [`transport-v2-persisted-references.json`](transport-v2-persisted-references.json)
  is the cross-backend oracle for [persisted text references](../transport-v2.md):
  references written against the `writer` catalog are resolved by the changed
  `reader` catalog, with the expected outcome, text and diagnostics. `$writer` and
  `$reader` stand for the two catalogs' contract fingerprints. The .NET
  `TranslationReferenceResolver` and the generated ESM `resolveTextReference`
  must agree with every case, and every `rejected` reference must fail to read.
  `written` references must be written byte for byte as `expected` by .NET and
  by `JSON.stringify` of the ESM `createTextReference` result. `json` gives a
  case as raw text (duplicate members, a `2.0` version); `$text`, `$items` and
  `$arguments` macros spell the large limit cases (see the .NET runner).

[`xliff-v3`](xliff-v3/README.md) holds XLIFF profile v3 interchange cases: modeled TMS
files, partial and stale files, placeholders, nested groups and the expected `.rmf2` sources after
`xliff import`.

The name `rmf2-v1` denotes the resource-file syntax. It does not select a retired
runtime, artifact, or emitter. Current runners compile every fixture through the
typed v5 project model.

## Published 0.3 historical fixture boundary

The root [`index.json`](index.json), `valid/`, `invalid/`, `shared/`, and `import/`
trees are frozen evidence for the published 0.3 JSON catalog/resource compiler.
They remain executable only where a test explicitly verifies that historical
release boundary. They are not current authoring examples, are not accepted by
the v5 project compiler, and must not be used to reintroduce an old reader or
writer into the current package graph.

The root index carries explicit `fixtureStatus` and `publishedReleaseBoundary`
metadata so tooling cannot mistake that evidence for the current release oracle.
Its paths are relative to this directory; its diagnostic locations are one-based,
start-inclusive, end-exclusive, and count UTF-16 code units.
