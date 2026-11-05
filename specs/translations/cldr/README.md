# Pinned CLDR subset

`runic-subset-48.2.json` is the normalized, reviewable source derived from
Unicode CLDR 48.2 (`cldr-json` tag `48.2.0`). The source archive URL and
SHA-512 digest are recorded in the file, with the npm `dist.integrity` of the
five `cldr-json` packages the pin is extracted from: `cldr-core`,
`cldr-misc-full`, `cldr-dates-full`, `cldr-numbers-full` and `cldr-units-full`,
all version `48.2.0`. `runic-formats-48.2.json`, extracted in the same run, holds
the currency, unit and compact-notation data (see below).

It covers every CLDR locale (`availableLocales.full`) and keeps:

- `plurals`: the cardinal and ordinal plural rules of every CLDR plural locale,
  copied verbatim with their `@integer`/`@decimal` samples from
  `supplemental/plurals.json` and `ordinals.json`, keyed by CLDR data ID;
- `likelySubtags`: CLDR's likely subtags for the languages with CLDR data,
  without the entries that shorter keys already imply;
- `parentLocales`: CLDR's explicit parent locales (the main component) as data
  IDs, without the parents the default rules give;
- `languageAliases`: deprecated and legacy language codes and their CLDR
  replacements, which may add a script or region (`iw` → `he`, `tl` → `fil`,
  `sh` → `sr-Latn`, `cnr` → `sr-ME`), and macrolanguage codes of languages
  without data of their own (`cmn` → `zh`). Codes such as `tl`, `mo`, `sh` and
  `jw` still have CLDR plural rules; the extractor checks that they equal the
  replacement's, so aliasing never changes a plural;
- `rightToLeftScripts`: the scripts whose `rtl` is `YES` in
  `cldr-core/scriptMetadata.json`, which the ESM runtime's `localeDirection` reads;
- `numberingSystems`: the ten digits of each numbering system a locale uses,
  from `supplemental/numberingSystems.json`;
- `availableLocales`: every CLDR locale (`availableLocales.full`) as a data
  ID, which the cross-runtime comparison of localized formatting iterates;
- `locales`: per CLDR data ID, `minimumGroupingDigits` and `numbers` from
  `cldr-numbers-full` (`main/<locale>/numbers.json`): the
  `defaultNumberingSystem` and, for it and for `latn` (`-u-nu-latn`), the
  `decimal`, `group`, `minusSign` and `percentSign` symbols and the standard
  decimal and percent patterns, verbatim; from `cldr-dates-full`
  (`main/<locale>/ca-gregorian.json`) the format-context wide `weekdays`
  (Sunday first) where a date pattern uses `EEEE` and they differ from the
  stand-alone names, and the `dayPeriods` of a time pattern with `B` as
  `[minute of the day, abbreviated name]` from `supplemental/dayPeriods.json`
  (both only for .NET, whose platform data lacks them; an empty value overrides
  an ancestor's); the resolved
  `listPatterns` of `cldr-misc-full`, and the long-form `relativeTime` of
  `cldr-dates-full` (`main/<locale>/dateFields.json`) for the units second to
  year: the past and future patterns per plural category and the
  `relative-type` texts from -2 to 2. Each value is stored only where it differs
  from the value the locale's parent chain gives.

`runic-formats-48.2.json` holds the data of `:currency`, `:unit` and compact
notation. Unlike the subset, no runtime ships it: the compiler embeds the
entries a catalog uses (D055.4), and `eng/generate-cldr.mjs` deflates it into the
compiler's `GeneratedFormatData.bin`. It keeps:

- `currencies`: the ISO 4217 codes that `supplemental/currencyData.json` lists
  as current tender of some region (no `_to`, not `_tender: false`), each with
  its CLDR fraction digits (`DEFAULT` where none are listed); the extractor
  checks that no code has a non-cash rounding increment;
- `units`: the 45 simple units ECMA-402 sanctions for `Intl.NumberFormat` and
  CLDR's own compound units of two of them (`kilometer-per-hour`, …);
- `locales`: per data ID, stored per member where it differs from the parent
  chain's value (a currency, a unit, a numbering system or a width):
  `currencyFormats` per numbering system (the default one and `latn`, each
  falling back to `latn`'s members as ICU reads them) as `[standard, accounting,
  unit patterns other, zero, one, two, few, many, currency decimal, currency
  group]`; `currencies` as `[symbol, narrow symbol, display names other … many,
  the currency's own pattern, decimal and group]`; `units` as `[long, short,
  narrow]`, each `[unit patterns other … many, perUnitPattern]`; `per`, the
  compound pattern of each width; and `compact`, the short and long decimal
  patterns per numbering system as one record per magnitude from 10³ to the
  locale's largest, `[other … many, the explicit "1" pattern]`. A long table the
  locale lacks falls back to its short one, as ICU does.

Records drop trailing nulls, a plural form equal to `other` is null (lookups
fall back to `other`, as ICU's do), and a unit width equal to the wider one is
null. Every locale uses CLDR root's currency spacing, which the extractor
checks, so the runtimes implement that rule (a no-break space between a digit
and a currency symbol whose adjacent character is neither a symbol nor a
separator) rather than reading it.

A **data ID** is a CLDR locale with its likely script added and then omitted
again when it is the language's default script, so `zh-TW` is `zh-Hant-TW`,
`zh-Hans-SG` is `zh-SG` and `sr-Latn` stays `sr-Latn`. Lists, relative time and
grouping are looked up along CLDR's parent chain: an explicit parent locale,
else the ID without its last variant or region; a language-script ID such as
`sr-Latn` has the root as parent (CLDR's `nonlikelyScript` rule). Plural rules
are looked up by removing subtags only, as CLDR's empty `plurals` parent-locale
component and ICU do, so `pt-PT` has its own rules while `pt-AO` uses `pt`'s.
`eng/cldr-locales.mjs`, `CldrLocales.cs` (.NET) and the generated ESM runtime
implement the same lookup and the locale matching built on it.

`listPatterns` maps the `Intl.ListFormat` types to the CLDR pattern sets
(`conjunction` to `standard`, `disjunction` to `or`, `unit` to `unit`) and each
style (`long`, `short`, `narrow`) to the patterns `[start, middle, end, 2]`,
copied verbatim; no-break spaces and bidi marks are written as escapes. Every
pattern has `{0}` before `{1}`; a few add text after `{1}` (`mi`, `mn`, `ti`), so
both runtimes compose the patterns as CLDR specifies, from the end.

`eng/cldr-plural-rules.mjs` parses the rules and emits the .NET and ESM
selectors over the CLDR operands (`n`, `i`, `v`, `w`, `f`, `t`, `e`). The
.NET runtime tests and `eng/cldr-plural-rules.test.mjs` check both selectors
against every sample.

The data is licensed under Unicode License v3; the complete notice is in
`LICENSE`. `eng/generate-cldr.mjs` deterministically derives the compiler
registry, the runtime locale tables (`GeneratedLocaleTables.cs`, shared by the
runtime and the compiler), the plural selectors (`GeneratedLocaleData.cs`), the
deflated number-format, weekday, day-period, list and relative-time resource
(`GeneratedLocaleData.bin`), and the
capability matrix. The ESM compiler embeds only the data of a catalog's
languages, and not the weekdays and day periods, which `Intl` already applies. `eng/render-capabilities.mjs` then renders the guide from that matrix.

To update the pin, download the five packages, check them against the recorded
integrity, unpack each into `<directory>/<package>/`, and run:

```bash
bun eng/extract-cldr.mjs <directory>
bun eng/generate-cldr.mjs
bun eng/render-capabilities.mjs
bun eng/generate-cldr.mjs --check
bun eng/render-capabilities.mjs --check
```

Update the cross-runtime fixtures in `specs/translations/corpus` with them.
Generation is offline and deterministic; it never downloads data during a
build. Reviewers should compare normalized values to the tagged
`unicode-org/cldr-json` files before accepting a pin change.
