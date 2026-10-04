# Pinned CLDR subset

`runic-subset-48.2.json` is the normalized, reviewable source derived from
Unicode CLDR 48.2 (`cldr-json` tag `48.2.0`). The source archive URL and
SHA-512 digest are recorded in the file. The retained fields are the cardinal and
ordinal plural rules, copied verbatim with their `@integer`/`@decimal` samples
from `supplemental/plurals.json` and `ordinals.json`, and relative-time patterns
for Runic's explicit target locales. CLDR has no ordinal rules for `nn`, which
therefore inherits the root rule set (`other` only).

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
