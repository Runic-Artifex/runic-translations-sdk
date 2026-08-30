# Runic Translations compiler conformance corpus

This directory is the language-neutral Wave A corpus for the version 1 catalog
and resource source contracts. `index.json` is the machine-readable entry point.

All paths in the index are relative to this directory and use `/` separators.
Source files are UTF-8 JSON. Diagnostic locations are one-based, start-inclusive,
and end-exclusive. Columns count UTF-16 code units. Property diagnostics include
the JSON property-name token (including quotes); value diagnostics include the
JSON value token (including quotes for strings).

Valid cases can declare semantic facts and a `fingerprintGroup`. Members of the
same fingerprint group MUST produce byte-identical canonical IR fingerprints,
even when file names, input order, or document partitioning differ. Invalid cases
declare the complete ordered diagnostic set expected from the compiler kernel.

`RTR0020` is intentionally excluded from the compiler-kernel corpus because
output path containment and collision validation belongs to the later build/CLI
surface. The exclusion is explicit in `index.json`; all other diagnostics from
`RTR0001` through `RTR0022` have at least one Wave A case.

`locale-pack-v2-parity.json` is the bytes-first decoder parity corpus. It fixes
the shared bounds and normalized `RTR0023/<reason>` IDs used by .NET and
generated ESM decoders. Runtime and generated-module tests exercise the
integrity-before-parse, expected-locale, fingerprint, and immutable-snapshot
paths; broader malformed-document permutations remain in the existing Wave B
external-pack corpus.

The `rejectionParity` entries extend that corpus into byte-for-byte rejection
parity. Each `template` is ASCII text with `%TOKEN%` identity placeholders
(`VERSION`, `GRAMMAR`, `BAD_VERSION`, `BAD_GRAMMAR`, `CATALOG`, `LOCALE`,
`FINGERPRINT`, plus fixed foreign counterparts prefixed with `OTHER_`) that each
runner binds to its own contract before UTF-8 encoding, so both decoders observe
identical bytes despite differing compiled fingerprints. The optional operators
are applied identically on both sides: `truncateFromEnd` drops trailing bytes,
`padTo` right-pads with spaces past the configured document limit,
`expectedLocale` substitutes the requested-locale argument instead of mutating
bytes, and `verifier: "reject"` decodes under a rejecting integrity policy.
Every entry must surface exactly its `expected` token on both runtimes;
`accepted` marks the positive control that must decode successfully.
