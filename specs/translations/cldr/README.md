# Pinned CLDR subset

`runic-subset-48.2.json` is the normalized, reviewable source derived from
Unicode CLDR 48.2 (`cldr-json` tag `48.2.0`). The source archive URL and
SHA-512 digest are recorded in the file. The retained fields are the cardinal and
ordinal plural rules, copied verbatim with their `@integer`/`@decimal` samples
from `supplemental/plurals.json` and `ordinals.json`, relative-time patterns
and the resolved `listPatterns` of `cldr-misc-full` for Runic's explicit target
locales. CLDR has no ordinal rules for `nn`, which therefore inherits the root
rule set (`other` only).

`listPatterns` maps the `Intl.ListFormat` types to the CLDR pattern sets
(`conjunction` to `standard`, `disjunction` to `or`, `unit` to `unit`) and each
style (`long`, `short`, `narrow`) to the patterns `[start, middle, end, 2]`,
copied verbatim; the Czech no-break space is written as `\u00a0`. Every pinned
pattern has the form `{0}…{1}`, so the generator emits only the separators and
fails if a new pin breaks that form.

`eng/cldr-plural-rules.mjs` parses the rules and emits the .NET and ESM
selectors over the CLDR operands (`n`, `i`, `v`, `w`, `f`, `t`, `e`). The
.NET runtime tests and `eng/cldr-plural-rules.test.mjs` check both selectors
against every sample.

The data is licensed under Unicode License v3; the complete notice is in
`LICENSE`. `eng/generate-cldr.mjs` deterministically derives the compiler
registry, runtime locale data, and capability matrix. `eng/render-capabilities.mjs`
then renders the guide from that matrix. Update the pinned source, digest,
normalized subset, generated outputs, and cross-runtime fixtures together. Then run:

```bash
bun eng/generate-cldr.mjs
bun eng/render-capabilities.mjs
bun eng/generate-cldr.mjs --check
bun eng/render-capabilities.mjs --check
```

Generation is offline and deterministic. It never downloads data during a
build. Reviewers should compare normalized values to the tagged
`unicode-org/cldr-json` files before accepting a pin change.
