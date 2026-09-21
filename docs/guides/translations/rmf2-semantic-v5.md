# RMF2 semantic v5 foundation

The additive `rmf2-execution-v2` profile has a typed compiler model and published
v5 message/artifact schemas. It is not enabled in project output or installed
runtimes yet. Existing RMF2 projects continue to emit v4 and use the v1 execution
profile, with its existing supported features and refusal diagnostics.

The new model preserves input/local references, formatted literal operands,
formatter metadata through local chains, ordered typed options, and inert
annotations. Expressions carry an underlying `valueType` independently of their
formatter: an int64 input formatted by `:number` remains int64 and can later use
`:integer`. Type inference follows aliases back to unannotated or implicit inputs;
explicitly annotated caller types remain fixed. UUID literal binding requires
exactly 36 UUID D-format characters without padding.

Declarations cannot bind a variable already mentioned in an earlier declaration,
including operands and dynamic options. For example, `.local $a = {$n}` followed
by `.input {$n :number}` is a Duplicate Declaration (`RTR0067` at the later `$n`),
not a forward input reference. Put the input first or leave it implicit where
the profile permits. Local forward references and cycles remain invalid. This
shared validation also protects the current v4 compiler.

Variant keys distinguish bare wildcard `*` from literal `|*|`.
Selection compares per-selector ranks in order: numeric exact, plural/ordinal
category, then wildcard. String and boolean exact matches outrank wildcard.
Unicode comparisons use NFC while keeping authored text.

Dynamic function options require declared inputs of the option's type (or typed
locals rooted in declared inputs). Their types participate in caller contracts.
The finite option table defines enums, precision limits, defaults and errors.
Invalid values are rejected without clamping. Decimal canonicalization preserves
exact values, including exponent notation and negative zero, within a bounded
96-bit coefficient / 0..28 scale domain.

The normative [execution specification](../../../specs/translations/rmf2-execution-v2.md)
and [machine-readable option table](../../../specs/translations/rmf2-execution-v2.json)
describe these rules and the integration boundary. Public schema mirrors are
[message AST v5](/schemas/translations/message-ast-v5.schema.json) and
[locale artifact v5](/schemas/translations/locale-artifact-v5.schema.json).
The CLI `schema` command also exports both v5 schemas for offline validation;
schema distribution does not enable v5 project generation or runtime loading.
Resource syntax and markup contracts remain version 1.

The next integration slice must evaluate typed locals/options, link project
markup contracts, generate v5 code, validate external v5 packs and implement
explicit version dispatch in .NET and ESM. No rendering or backend parity claim
follows from this semantic foundation alone.
