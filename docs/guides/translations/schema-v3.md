# Source schema v3 and MF2 profile policy

Schema v3 is the preview authoring interchange for the closed
`runic-mf2-subset/1` profile. A v3 resource document keeps the v2 catalog,
locale, layer, grouping, and metadata model, but structured `$value`s are
explicitly enveloped:

```json
{
  "$value": {
    "mf2": {
      "profile": "runic-mf2-subset/1",
      "ast": { "astVersion": 3, "profile": "runic-mf2-subset/1", "inputs": {}, "declarations": [], "selectors": [], "variants": [{ "matches": {}, "pattern": [{ "kind": "text", "value": "Hello" }] }] }
    }
  }
}
```

The envelope is deliberately a closed extension boundary. `profile` and
`astVersion` are required, unknown members are rejected, and a future parser
must lower MF2 syntax into this normalized AST before compiler backends see it.
The v3 schema does not claim broad MessageFormat 2 conformance.

The supported subset has typed inputs; input/local operands; `literal`,
`plural`, and `ordinal` selectors; exact and `*` variant matches; scalar and
relative-time formatting; and semantic markup with string attributes. Format
options are closed to `format`, `unit`, and `numeric` (with documented unit and
numeric vocabularies); annotations and extension options are rejected. AST maps
are ordinal-canonicalized and all generated backends consume normalized nodes,
not source syntax. Parse locations, comments, quoting rules, private-use MF2
extensions, functions outside this set, and any unrecognized annotations remain
outside this milestone.

The profile limits each message to 32 inputs, 16 selectors, 256 variants, and
4,096 normalized nodes per variant; text nodes are at most 65,536 characters
and markup nesting is at most 16. Selector/declaration names are unique,
variant matches are nonempty and cover exactly the declared selectors, and tags
are unique. These bounds and function/input compatibility are enforced for the
v2 input and for the emitted v3 AST before migration succeeds.

`Runic.Translations.Tooling.MigrateV2ToV3` is deterministic and emits compact
UTF-8 plus a machine-readable report. It materializes v2 implicit input format
and relative-time numeric defaults as non-semantic-loss report events. It does
validate v2 input before migration and does not silently discard unsupported
source: malformed, unknown-member, invalid-locale, or non-v2 input is rejected
with a stable `MIGV3-*` code. A legacy `$schema` value is always replaced by the
canonical v3 schema URI. Migration finishes by validating the emitted document
against the closed v3 profile gate; it does not return a partially conforming
v3 document.

Version policy: v3 is the only preview writer target. v1 and v2 are frozen
reader and migration inputs; they are not extended. The current compiler still
compiles the established v1/v2 execution grammar. Connecting the v3 parser to
that compiler is a subsequent, separately versioned milestone.

The tooling package now ships a closed XLIFF 2.1 text interchange profile for
compiler-valid v1/v2 execution resources. It preserves plain patterns,
placeholder contracts, standard resource metadata, and target review
state/notes deterministically; `runic.translations.interchange-review/1` is the portable,
Git-friendly review sidecar. It is intentionally not a general XLIFF dialect or
MF2 parser: inline codes, extra XLIFF metadata, source-layer provenance, and
selector/formatter/markup messages are rejected on import or reported as a
deterministic loss on export. The v3 `mf2` envelope remains the only extension
point for a future rich-message interchange milestone.
