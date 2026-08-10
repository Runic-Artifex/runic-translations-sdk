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
