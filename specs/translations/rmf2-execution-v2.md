# RMF2 execution profile v2 and normalized AST v5

`rmf2-execution-v2` is additive. Its message grammar, normalized AST and locale
artifact versions are **5**. Resource syntax and the declarative markup contract
remain version **1**. The [finite profile table](rmf2-execution-v2.json) is
normative together with this document. The LDML 48.2 baseline and existing locale
capability table remain pinned.

This change supplies a compiler semantic foundation. The project compiler, CLI,
generated code, runtime constructors and external-pack readers continue to use
`rmf2-execution-v1` and artifact v4. No installed runtime advertises v2 execution
yet. A runtime must explicitly recognize v5 before accepting its artifacts;
changing a version number on a v4 tree is not a conversion.

## Values, declarations and expressions

A value has one of four tags: `input`, `local`, `string-literal`, or
`number-literal`. References use NFC symbol identities. String literals retain
their decoded authored text. Number literals retain both their authored decimal
spelling (`value`) and their exact portable `canonical` value. The compiler also
retains the original shared syntax document, including raw tokens and locations.

An expression contains a required operand, an optional function, an ordered
option array and an ordered annotation array. Function-only expressions are
outside this profile. Unknown functions or options are errors. No function,
option, annotation, or local declaration is silently converted to plain text.

Declarations remain ordered and distinguish `.input` from `.local`. Input
declarations establish caller types. An untyped input is a string. For an
undeclared operand, formatter constraints across the whole message infer its
type; an unformatted operand defaults to string. Integer and decimal constraints
intersect at int64. Conflicting types are errors, independent of expression order.
All declared inputs and inferred operand inputs appear in the caller contract,
sorted by NFC name using ordinal order. Local names never become caller inputs.
Duplicate declarations and references to a local before its declaration are
data-model errors.

A local holds a **resolved typed value with its formatter and selection
metadata**, not the formatted display string. A functionless local alias keeps
that value and metadata. An explicit function consumes its underlying typed
value, replacing the formatter/options with the explicit function and that
function's defaults. It never reparses localized output. For example:

```text
.input {$amount :number}
.local $percent = {$amount :number style=percent}
.local $alias = {$percent}
.local $decimal = {$alias :number style=decimal}
{{{$alias} / {$decimal}}}
```

For `amount = 0.5`, `$alias` carries the percent formatter over the number 0.5;
`$decimal` carries the decimal formatter over the same 0.5. Implementations must
not interpret the intermediate display `50%` as a string or the number 50.
Formatting a numeric literal or a constant local creates no caller argument.

The function table fixes the following operand types. Int64 can be consumed by a
decimal function without losing its value. Other reference type conversions are
rejected. Integer numeric literals must be integral and in signed int64 range.

| Functions | Operand type | Literal support |
| --- | --- | --- |
| `string` | string | string literal |
| `integer` | int64 | exact integral number literal in int64 range |
| `number`, `runic:relative-time` | decimal | portable number literal |
| `runic:boolean` | boolean | string literal `true` or `false` |
| `date` | date | string literal exactly `yyyy-MM-dd`, valid calendar date |
| `time` | time | string literal exactly `HH:mm:ss`, valid clock time |
| `datetime` | datetime | string literal exactly `yyyy-MM-ddTHH:mm:ssZ`, UTC |
| `runic:uuid` | guid | string literal in UUID D format |

## Finite function options

The JSON profile records every option's input type, literal/dynamic support,
enum or inclusive range, default, and error behavior. All listed options accept
literals. String options require string literals; integer options require
numeric literals with integral canonical values. No quoted-number coercion is
performed for integer options. `select` is literal-only; every other listed
option permits a typed dynamic value.

| Function | Option | Type / values or range | Default |
| --- | --- | --- | --- |
| `string`, `runic:boolean` | `select` | string: `exact` | `exact` |
| `integer`, `number` | `select` | string: `plural`, `ordinal`, `exact` | `plural` |
| `integer` | `useGrouping` | string: `always`, `never` | `never` |
| `number` | `style` | string: `decimal`, `percent` | `decimal` |
| `number` | `minimumFractionDigits` | int64: 0..6 | 0 |
| `number` | `maximumFractionDigits` | int64: 0..6 | 6 for decimal; 4 for percent |
| `date`, `time`, `datetime` | `style` | string: `iso`, `short`, `long` | `iso` |
| `runic:uuid` | `style` | string: `d`, `n`, `b`, `p` | `d` |
| `runic:relative-time` | `unit` | string: `second`, `minute`, `hour`, `day`, `week`, `month`, `year` | `day` |
| `runic:relative-time` | `numeric` | string: `always`, `auto` | `always` |

For number formatting, `0 <= minimum <= maximum <= 6` for decimal and
`0 <= minimum <= maximum <= 4` for percent. Omitted bounds use the table defaults;
implementations must not adjust another bound to make an invalid pair fit. The
display rounding mode is halfExpand (nearest; halfway away from zero). Percent
display multiplies by 100 without changing the typed value used for selection.
Display rounding does not change the typed value or CLDR selection operands.

A variable used as a dynamic option must refer to an explicitly declared input
of the option's type, or to a previously declared local of that type whose
transitive caller inputs are all explicitly declared. Constant typed locals are
allowed. An implicit operand input does not count as an explicit option input.
Dynamic option dependencies are part of the caller contract. A formatter that
uses `$digits` as a precision option therefore requires an int64 caller input,
even when `$digits` never appears in the output pattern.

Unknown options, bad literal types/values and provably invalid static constraints
are compiler errors (`RTR0065`). Resolved dynamic values use the same enum,
range, and cross-option checks. Invalid dynamic values are evaluation errors;
they never select a different enum, clamp precision, silently use defaults, or
coerce caller types. Defaults apply only to omitted options. The runtime branch
must map this error to its public error API without turning it into output.

## Annotations and markup

Annotations are inert and ordered. Absence of `value` means valueless, while
`{kind: "string-literal", value: ""}` means an explicit empty string. Numeric
annotations use the number-literal tag and canonical decimal field. Annotation
values cannot reference inputs or locals. Annotations do not format values,
participate in selection, add caller arguments, or affect caller fingerprints.

Normalized markup nodes retain open/close/standalone events, ordered typed
options, and ordered annotations, including annotations on closing events. The
balanced-inline constraint is validated separately from grammar. The unchanged
markup contract v1 still owns names, aliases, option schemas, slots and renderer
behavior. The semantic compiler preserves unresolved markup events; project
contract linking and conversion to renderer plans belong to the backend adapter.
Semantic compilation alone is not proof that a project markup contract is met.

## Variant keys and selection

Each variant key is tagged `wildcard` or `literal`; the empty key vector is used
for a simple, non-selector pattern. Only bare `*` is wildcard. `|*|` is a literal
star and can exactly match a string. Literal text is preserved as authored after
escape decoding. NFC normalization is used for comparison and key validity,
never to rewrite authored pattern text or literal values.

String selector literals compare by NFC and ordinal equality. Boolean literals
must be exactly `true` or `false`. Numeric selector keys must be a portable
decimal spelling (quoted or unquoted), or an enabled CLDR category (`zero`,
`one`, `two`, `few`, `many`, `other`). Numeric exact keys store their canonical
decimal value. Int64 selectors additionally require exact keys to fit int64.
Category keys are invalid for `select=exact`. Malformed unquoted number-like
keys such as `01` and `1bad` are invalid even for string selectors.

The rank of one matching key is:

| Selector | Highest to lowest rank |
| --- | --- |
| Decimal/int64 | numeric exact (2), selected plural/ordinal category (1), wildcard (0) |
| String/boolean | exact (1), wildcard (0) |

Nonmatching keys eliminate a variant. Compare remaining rank vectors
lexicographically in authored selector order; an earlier selector wins before
later selector ranks are considered. Equal rank vectors retain authored variant
order. Source order never lets a category beat an exact number on that selector.
The CLDR category uses the canonical unformatted numeric value and the pinned
locale capabilities. Canonical `1`, `1.0`, and `1e0` share CLDR operands; display
precision and percent style do not change them.

Reject duplicate normalized key vectors, including canonically equivalent
Unicode strings and equivalent numeric keys (`1`, `1.0`, `|1e0|`; `-0`, `0`).
Wildcard and literal-star keys remain distinct. Vector equality is structural,
not delimiter concatenation. Every matcher requires an all-wildcard vector with
exactly one key per selector. Selector identities must be unique, and only
string, boolean, int64 and decimal values can select variants.

## Portable exact decimal domain

Source decimal tokens use `-?(0|[1-9][0-9]*)(.[0-9]+)?([eE][+-]?[0-9]+)?`
(the dot is literal). The maximum token length is 4096 and an explicit exponent
must fit a signed 32-bit integer. There is no leading plus, NaN or infinity.

Interpret the digits and exponent exactly. Remove leading coefficient zeros,
remove trailing coefficient zeros while adjusting scale, then expand a negative
scale with trailing zeros. The absolute coefficient must be at most
`79228162514264337593543950335` and the resulting scale must be 0..28. This is
the exact 96-bit decimal coefficient domain, not an approximation to binary64.

Canonical serialization uses plain decimal notation, no exponent, no leading
plus, no redundant leading/trailing zeros, and a leading zero before a decimal
point. All signed zero spellings canonicalize to `0`, after validating the token
and exponent. For example, `1e+2` becomes `100`, `1.2300e-2` becomes `0.0123`, and
`-0.000` becomes `0`.

Overflow, nonzero underflow and excess precision are errors; there is no rounding
while reading literals, annotations or exact keys. `1e29`, `1e-29`, and a
coefficient one larger than the maximum are rejected. Runtime internal decimal
carriers and pack readers must preserve the same exact value; converting a
canonical string through a binary float is not a valid exact-key comparison.

## Serialized boundary and staged implementation

[message-ast-v5.schema.json](schemas/message-ast-v5.schema.json) defines normalized
messages. [locale-artifact-v5.schema.json](schemas/locale-artifact-v5.schema.json)
defines the separate v5 envelope: each message has `contentLocale` and `ast`.
The envelope reuses the unchanged v1 markup-contract serialization from v4.
Schema validation does not replace semantic validation of reference binding,
option constraints, numeric canonical fields, NFC duplicates, key counts,
fallback coverage, or caller and markup contracts.

The compiler's internal `Rmf2SemanticCompilerV5.Compile` reads the existing
lossless syntax, validates data-model/profile constraints and returns the v5 IR.
`Rmf2MessageJsonV5.Serialize` emits only the normalized AST for that IR. The
selection helper accepts already resolved typed selector values and an externally
computed pinned CLDR category; it does not execute declarations or format text.
Limits are 32 caller inputs (or a lower configured limit), 256 declarations,
16 selectors, 256 variants, and 4096 nodes per pattern, plus existing source limits.

The dependent generation/runtime work must implement typed declaration
evaluation, formatter inheritance/replacement, dynamic option error propagation,
exact decimal/CLDR parity, v5 project and markup linking, caller fingerprint
versioning, code generation, pack semantic validation and explicit version
dispatch. It must retain v4 readers and constructors and must not erase v5 data
through the v4 AST adapter. Only that integrated work may change default emission.

The [golden corpus](corpus/semantic-v5/README.md) is a schema/semantic fixture, not
an activated locale pack. Focused verification runs with:

```sh
dotnet run --project tests/dotnet/Runic.Translations.Compiler.Tests -- --rmf2-semantic-v5
```
