# RMF2 semantic v5 foundation

The additive `rmf2-execution-v2` profile has a typed compiler model,
resolved-locale artifact writer, strict .NET external-pack loading, and published
v5 message/artifact schemas. Add `executionProfile: "rmf2-execution-v2"` to an
`rmf2-v1` project to activate typed v5 C#/.NET and ESM output. Omit it to retain
the existing v4/v1 behavior. C++ and standalone v5 TypeScript/template contracts
remain unsupported and are refused deterministically.
An empty project is valid as an authoring scaffold; generation and verification
require at least one canonical key in the effective default locale.

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
An input's own operand is allowed, but its function options cannot refer to
itself: `.input {$n :number maximumFractionDigits=$n}` is also `RTR0067`, at the
input binding's `$n`.

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
schema distribution alone does not activate a project; the explicit selector does.
Resource syntax and markup contracts remain version 1.

The v5 pack path is explicitly dispatched by artifact version, grammar and
`rmf2-execution-v2` profile. It validates bounded strict JSON/UTF-8, the caller
contract, declaration graph, exact decimals, selectors and fallback vectors,
effective content locales, and linked markup/slot obligations before composing
through the existing immutable snapshot path. The caller supplies integrity
verification when authenticity is required; a matching fingerprint proves
compatibility, not provenance. Legacy v1/v2/v4 constructors and readers remain
unchanged and reject v5 artifacts.

The CLI, source generator/MSBuild integration, and Vite plugin dispatch the
selected profile to the v5 C#/ESM backends. This does not claim package-only
consumer, cross-backend corpus, NativeAOT, or published-release evidence.
