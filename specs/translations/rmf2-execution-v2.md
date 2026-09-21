# RMF2 execution profile v2 and normalized AST v5

`rmf2-execution-v2` is additive. Its message grammar, normalized AST and locale
artifact versions are **5**. Resource syntax and the declarative markup contract
remain version **1**. The [finite profile table](rmf2-execution-v2.json) is
normative together with this document. The LDML 48.2 baseline and existing locale
capability table remain pinned.

The project compiler, CLI, generated code and external-pack readers continue to
use `rmf2-execution-v1` and artifact v4. The additive .NET `CompiledRmf2Message`
constructors execute explicitly lowered v5 models; they do not accept serialized
v5 artifacts or change project emission. A runtime must explicitly recognize v5
before accepting its artifacts;
changing a version number on a v4 tree is not a conversion.

## Values, declarations and expressions

A value has one of four tags: `input`, `local`, `string-literal`, or
`number-literal`. References use NFC symbol identities. String literals retain
their decoded authored text. Number literals retain both their authored decimal
spelling (`value`) and their exact portable `canonical` value. The compiler also
retains the original shared syntax document, including raw tokens and locations.

An expression contains a required operand, its resolved underlying `valueType`,
an optional formatter function, an ordered option array and an ordered annotation
array. `valueType` is independent of the formatter's accepted input domain;
applying a formatter to an input/local reference never changes its carrier type.
Function-only expressions are
outside this profile. Unknown functions or options are errors. No function,
option, annotation, or local declaration is silently converted to plain text.

Declarations remain ordered and distinguish `.input` from `.local`. An input
declaration with a function annotation establishes its caller type. For an
unannotated input declaration or an undeclared operand, formatter constraints
across the whole message infer its type, following local operand chains back to
the input. With no constraint, the input defaults to string. Integer and decimal
constraints intersect at int64. Conflicting types are errors, independent of
expression order. A declared `:string` input remains string and cannot be
reinterpreted as numeric through a local alias.
All declared inputs and inferred operand inputs appear in the caller contract,
sorted by NFC name using ordinal order. Local names never become caller inputs.
Declaration order is validated before any inference or signature pre-seeding.
Following [LDML 48.2 declarations](https://github.com/unicode-org/cldr/blob/release-48-2/docs/ldml/tr35-messageFormat.md#declarations),
a declaration cannot bind a variable that appeared anywhere in a previous
declaration, whether as a binding, operand or variable-valued option. Names use
NFC identity; quoted literals and annotation text are not variable references.
An `.input` may use its own variable as its operand, but not in its function
options: `.input {$n :number maximumFractionDigits=$n}` and
`.input {$s :string select=$s}` are Duplicate Declaration errors.
Violations report `RTR0067`, `Duplicate declaration 'name'.`, at the later
binding's name span (or the input's own name span for a self-option). This shared
data-model rule applies to both current v4 and
staged v5 compilation. References to a local before its declaration, including
cycles, remain data-model errors as well.

A local holds a **resolved underlying typed value**, independently of its
formatter and selection metadata. A functionless local alias keeps both that
value and its metadata. An explicit function consumes the same underlying typed
value, replacing the formatter/options with the explicit function and that
function's defaults. It never retags the value or reparses localized output.
For example:

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

Type inference also follows a functionless alias:

```text
.input {$n}
.local $a = {$n}
{{{$a :number}}}
```

This declares a decimal caller input `n` and gives the alias a decimal
`valueType`. Omitting the unannotated `.input` produces the same inferred operand
type, although that implicit input still cannot satisfy the explicit-declaration
requirement for dynamic options. An explicit `.input {$n :number}` also works
when it precedes the local. Moving either input declaration after
`.local $a = {$n}` is a Duplicate Declaration error, not a forward input
reference; later formatter or selector metadata cannot retroactively change the
earlier declaration.

Widening the accepted formatter domain does not widen the value's carrier:

```text
.input {$n :integer}
.local $a = {$n :number style=percent}
{{{$a :integer}}}
```

Here `n`, `$a`, and the output expression all retain `valueType: "int64"`.
`$a` inherits a number formatter, while the final expression replaces it with
the integer formatter. This is allowed because the underlying int64 was never
converted into a decimal or formatted string. A caller input explicitly declared
`:number` remains decimal, so an `:integer` override of that input or its aliases
is rejected. Selector and dynamic-option type checks likewise use the underlying
carrier, independently of the inherited formatter.

The function table fixes the following operand types. Int64 can be consumed by a
decimal function without losing its value. Other reference type conversions are
rejected. A literal is initially bound once to the function's accepted literal
type in the table; without a function, number literals bind decimal and string
literals bind string. Subsequent input/local formatter applications preserve
that bound type. Integer numeric literals must be integral and in signed int64
range.

| Functions | Operand type | Literal support |
| --- | --- | --- |
| `string` | string | string literal |
| `integer` | int64 | exact integral number literal in int64 range |
| `number`, `runic:relative-time` | decimal | portable number literal |
| `runic:boolean` | boolean | string literal `true` or `false` |
| `date` | date | string literal exactly `yyyy-MM-dd`, valid calendar date |
| `time` | time | string literal exactly `HH:mm:ss`, valid clock time |
| `datetime` | datetime | string literal exactly `yyyy-MM-ddTHH:mm:ssZ`, UTC |
| `runic:uuid` | guid | exactly 36 decoded characters in UUID D format; no whitespace padding |

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
derived `valueType` consistency,
option constraints, numeric canonical fields, NFC duplicates, key counts,
fallback coverage, or caller and markup contracts.

The compiler's internal `Rmf2SemanticCompilerV5.Compile` reads the existing
lossless syntax, validates data-model/profile constraints and returns the v5 IR.
`Rmf2MessageJsonV5.Serialize` emits only the normalized AST for that IR. The
selection helper accepts already resolved typed selector values and an externally
computed pinned CLDR category; it does not execute declarations or format text.
Limits are 32 caller inputs (or a lower configured limit), 256 declarations,
16 selectors, 256 variants, and 4096 nodes per pattern, plus existing source limits.

The dependent generation and pack-loader work must integrate typed evaluation,
exact decimal/CLDR parity, v5 project and markup linking, caller fingerprint
versioning, code generation, pack semantic validation and explicit version
dispatch. It must retain v4 readers and constructors and must not erase v5 data
through the v4 AST adapter. Only that integrated work may change default emission.

### Additive .NET runtime boundary

The .NET runtime now provides immutable `CompiledRmf2Value`, `Option`,
`Annotation`, `Expression`, `Input`, `Declaration`, `Selector`, `Key`, `Node`,
`Variant`, and `Message` types (each name uses the `CompiledRmf2` prefix).
`CompiledTextMessage.FromRmf2(CompiledRmf2Message)` explicitly dispatches existing snapshot
formatting to that evaluator. Existing constructors retain v4 behavior. Caller
contracts match NFC input name and underlying `TextArgumentType`, independently
of display formats. `TextArgument.CreateRmf2(name, carrier)` and
`CompiledTranslationDefinition.FromRmf2Inputs` provide explicit NFC-name entry
points while legacy constructors retain their ASCII placeholder rules.

The evaluator resolves ordered declarations, inherits or replaces formatter
metadata, validates dynamic options, and ranks exact decimal and pinned CLDR
matches. Declarations cannot bind a variable referenced anywhere in a previous
declaration, whether as an operand or a dynamic option. The runtime rejects such
externally constructed models as duplicate declarations; it does not hoist input
formatters or selection metadata. Input-before-local ordering remains valid.
An explicit input formatter establishes its caller carrier exactly: `:number`
and `:runic:relative-time` declare decimal inputs. Int64 widening into a decimal
formatter is valid only for subsequent local or pattern expressions.
Invalid resolved options raise `TranslationFormatException`; they do
not clamp or fall back. Constructors reject malformed normalized models with
argument exceptions. Authored numeric spelling and canonical fields are checked
exactly before parsing into decimal; no binary floating point is involved.
Number display uses halfExpand rounding and the finite precision table. Platform
globalization still controls localized punctuation and date/time names. Ungrouped
integer output is invariant, matching the pinned exact formatter capability.
V5 uses the closed formatter table directly; a snapshot's optional legacy
`ITextValueFormatter` does not override v5 function semantics.

Annotations remain ordered and inert on expressions and every markup event in
the runtime model. `LocalizedTextContentNode.Annotations` also carries expression
and markup annotations, including closing-event annotations, separately from
renderer `Attributes`. Declaration annotations stay on their declarations; an
alias does not merge them into a use-site expression. Valueless, empty-string and
number-literal annotations retain their distinct representations. Existing
renderers ignore the new annotation collection. Runtime markup constructors
require balanced events and bound option references but assume project contract
linking has already resolved element identities and validated markup option and
slot schemas. That adapter remains dependent work.

`Rmf2RuntimeAbiVersion` is **2**; legacy `RuntimeAbiVersion = 1` and
`MessageGrammarVersion = 2` remain unchanged. Generated v5 consumers must embed
the literal requirement **2**, then call `EnsureRmf2RuntimeAbi(2)` or
`SupportsRmf2RuntimeAbi(2)`. These methods execute against the loaded runtime and
accept requirements 1 and 2. A generated constant that aliases the runtime's
constant is not a compatibility check. Existing v4 emission is intentionally
unchanged in this slice.

Still dependent: C#/ESM v5 generation and project linking, caller fingerprint
versioning, v5 pack semantic validation and explicit loader dispatch, and any
change to default emission. The constructor evaluator is not a v5 pack loader.
The machine profile's project-level executable backend list remains empty until
those backend integrations are complete.

The [golden corpus](corpus/semantic-v5/README.md) is a schema/semantic fixture, not
an activated locale pack. Test-only JsonSchema.Net validation uses Draft 2020-12
and locally registered schema references to check emitted ASTs, complete
envelopes and malformed mutations. Focused verification runs with:

```sh
dotnet run --project tests/dotnet/Runic.Translations.Compiler.Tests -- --rmf2-semantic-v5
```
