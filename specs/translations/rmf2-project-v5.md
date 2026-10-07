# RMF2 v5 project linking

The compiler uses the `Rmf2ProjectV5` carrier for the supported
`rmf2-execution-v2` contract. It contains v5 messages directly and never
constructs a `CompiledMessagePattern` or passes messages through an older
parser/adapter.

Generator, CLI, MSBuild, and Vite hosts compile projects through the typed v5
carrier. `CompileRmf2ProjectV5` is the dedicated typed entry point. Failed
compilations return diagnostics without a project that could accidentally reach
emission.

Project schema v1 remains unchanged apart from the additive markup `placement`
member and `integer` option type; artifact v5 does not imply a project schema
version bump. The exported markup contract is version 2 (see
[execution v2](rmf2-execution-v2.md)). The linker infers direct `.mf2`
versus grouped `.rmf2` sources and rejects mixed representations. It uses the
existing locale, mount, completeness, extra-key, empty-value and runtime policies.

## Caller contracts and executable content

`CanonicalMessages` is sorted by canonical underscore key and supplies stable
IDs, resource paths, NFC caller names and carrier types, slot requirements, and
structured-output/markup obligations. Locale payloads retain their full v5
declarations, typed options, ordered annotations, tagged selector keys and
variants. A translation can omit canonical inputs, introduce locale-specific
locals and selectors, and choose different compatible formatters. It cannot
introduce caller inputs or change their carriers. Unannotated target references
inherit canonical carriers; explicit input declarations must agree exactly.
Int64 values may use decimal formatters in local/pattern expressions.
Allowed noncanonical keys live in `ExtraMessages` with ID -1 and retain validated
dynamic caller/markup contracts; they do not add generated canonical members or
participate in the caller fingerprint. The first direct definition in sorted
source order supplies each extra key's caller contract.

Each locale has direct and fallback-resolved resource lists. Every resource
retains its `ContentLocale`, independently of the requested locale. Validation
of locale-dependent functions uses that content locale. Source diagnostics map
back through resource framing into physical UTF-8 locations.

Markup aliases resolve to registered identities in every variant. The linker
validates paired/standalone form, nesting and interactive ancestors; supplies
typed defaults; and checks literal and input/local option types. Numeric
markup literals are numeric MF2 values, not quoted strings; project-v1 numeric
defaults remain strings and are converted to exact canonical decimal literals.
Integer options (`"type": "integer"`, optional `minimum`/`maximum` within the
int32 range, default the full int32 range) accept a quoted or unquoted literal
only when its text matches `0|-?[1-9][0-9]*` and lies within the bounds, so
`01`, `1.0`, `1e1`, `+1` and `-0` are rejected; non-literal-only integer options
accept `:integer` inputs. `markup.structure` is reserved and rejected with
RTR0060.
Closing-event annotations remain intact. Functional refs are static slot IDs.
Source slots default to exactly one occurrence per variant. Conditional source
slots require explicit min/max constraints. Every direct locale must obey those
requirements, and cannot introduce a slot or change its kind. Fallback retains
the already-validated resource.

## Compatibility and freshness

`CallerFingerprint` is SHA-256 over a deterministic, versioned caller contract:

- caller contract version 1, execution profile, grammar 5, runtime ABI 2, and
  generated-name mapping version 1;
- catalog identity, sorted canonical keys, their logical path segments, and
  sorted NFC caller names/types;
- canonical slot kinds/cardinalities and app-facing structured/markup contracts.

It excludes translated text, locals, formatter choices, annotations, selector
trees, descriptions, source organization, and content-locale/fallback mappings.
Only registered markup contracts used by canonical messages participate. Alias
spellings, enum declaration ordering and equivalent numeric defaults do not
change compatibility. Adding a markup obligation in any direct locale can
change the canonical message's structured return/renderer contract.

`SourceHash` is a separate SHA-256 hash of the profile and length-framed project
bytes, sorted source identities and complete source bytes. It detects actual
v5 content changes, including changes intentionally excluded from caller
compatibility. Source paths inside the project are relative to its directory.
The exported markup v1 JSON retains effective content-locale mappings for runtime
renderers, but those mappings are excluded from the caller fingerprint.

## Resolved locale artifact and pack loading

The locale writer emits artifact/grammar 5 with the explicit
`rmf2-execution-v2` profile. It serializes the linked v5 AST directly and never
converts through the v4 carrier. Each resolved message carries its effective
content locale. Its input array is expanded to the canonical caller contract, so
a target translation may omit an unused input while snapshot invocation remains
stable across locales and fallback sources.

The .NET pack contract uses `TranslationPackMessageContract.FromRmf2Inputs` and
`TranslationPackContract.CreateRmf2V5`. Those factories accept validated NFC
RMF2 argument names; the existing constructors retain their ASCII validation.
The loader validates the closed envelope and AST, version/profile identity,
catalog/locale/fingerprint, caller input ordering and types, declaration graph,
exact decimal canonical values, selectors, fallback vector, content locale,
markup and functional slots, and all runtime limits before activation. Verified
messages overlay through the existing immutable external-snapshot composition.
Fingerprint equality is compatibility evidence only. Applications that require
authenticity still provide an integrity verifier over the exact pack bytes.

## Generated-name mapping version 1

`Rmf2GeneratedNamesV1.Identifier(name)` maps the NFC UTF-8 bytes of a name to
`r_` followed by lowercase hexadecimal. All names are encoded, including ASCII
names and language keywords. This is injective over NFC names, portable in C#
and JavaScript identifiers, independent of input order, and immune to accidental
collisions with names that already resemble an encoded name. For example,
`a` maps to `r_61` and `r_61` maps to `r_725f3631`.

`Path(segments)` joins encoded segments with `_`, which cannot occur inside the
hex payload. Segment boundaries are preserved. This contract is tested for
keywords, punctuation, Unicode, composed/decomposed aliases and segment
collisions. Generator emission uses this mapping and separate generated helper
scopes. RMF2 resource keys retain their existing ASCII path rules and
underscore-key collision rejection.

## Readable C# name policy version 1

Generated C# also exposes a readable surface beside the encoded members
(decision D016). It is additive: the generated-name mapping version 1 above,
the encoded members, the `r_args_*` members, keys, the caller fingerprint and
all ABIs are unchanged, and no readable name is part of the caller fingerprint,
the web module manifest, ESM or the corpus index. `<ClassName>Messages.ReadableNameVersion`
is always `1` for this policy. The readable names below are guaranteed for
version 1.

Facade and types:

- The facade is the top-level sealed class `<ClassName>Messages`, reached
  through the instance property `<ClassName>.Messages`.
- The slot types of structured messages are nested in the top-level static
  class `<ClassName>Slots` as `<ClassName>Slots.<key>`. Every structured message
  has a slot type, which is empty when the message has no functional slots.
- Visibility follows `code.visibility`.

Message members:

- A canonical message's member name is its canonical key, verbatim (keys are
  already ASCII identifiers and unique, see RTR0018). There is no case change,
  PascalCase or grouping by path.
- The member is a property when the message has no inputs and a method
  otherwise. A plain message returns `string`; a structured message returns
  `LocalizedTextContent<<ClassName>Slots.<key>>`.
- Extra (non-canonical) keys are not part of the readable surface.

Input parameters and slot IDs:

- A name matching `^[A-Za-z_][A-Za-z0-9_]*$` is used verbatim.
- Any other name uses its generated-name v1 encoding (`r_<hex>` of the NFC
  UTF-8 bytes) for that parameter or slot property only; the message stays on
  the readable surface. There is no Unicode verbatim name, transliteration,
  suffixing or rewriting of `-` to `_`.
- Slot properties and slot constructor parameters use the same names.
- Input parameters follow the canonical contract's input order. Slot
  constructor parameters follow the contract's slot order (ordinal by slot ID).

Reserved names and clashes. Names are compared ordinally after the verbatim or
encoded rule. A message that matches one of these rules is left out of the
readable surface, stays available through its encoded member, and is reported
as warning RTR0069 at its base-locale name:

- Message keys `Equals`, `GetHashCode`, `ToString`, `GetType`,
  `MemberwiseClone`, `Finalize`, `ReferenceEquals`, `ReadableNameVersion`,
  `__text`, `<ClassName>`, `<ClassName>Messages` and `<ClassName>Slots`.
- Slot names equal to one of the `object` members above, or to the message key.
  `MessageKey`, `CopyTo`, `d`, `destination` and `__destination` are allowed.
- A verbatim input name equal to the encoded name of another input of the same
  message, or a verbatim slot ID equal to the encoded name of another slot of
  the same message. Inputs and slots are separate scopes.

If `code.className` is `Messages` or `__readable`, which name members the
facade adds to the generated class, no readable surface is generated and
RTR0069 is reported once at `code.className`; the encoded surface is unchanged.

A message's readable names depend only on its own key, input names, slot IDs,
the class name and the fixed lists above, so adding or removing another message
never renames, adds or drops a readable member.

Emission details that are not part of the policy: every user-derived identifier
is emitted with an `@` prefix, types are `global::`-qualified and members
`this.`-qualified. Changing them does not change a name and does not bump the
version. A change to the identifier rule or encoded fallback, the reserved list
or clash rule, the facade or slot type names, or parameter order bumps
`ReadableNameVersion`. `GeneratedNameVersion` is never changed by this policy.

The C# source generator emits the readable file `<ClassName>.Readable.g.cs`
only when the referenced runtime declares
`TranslationsCompatibility.TypedSlotBindingsVersion`; otherwise it reports
warning RTR0068 and emits the other generated files unchanged.

The cross-locale fixture in [corpus/v5-project](corpus/v5-project/README.md)
exercises the carrier, generated backends, exact ESM runtime, and strict pack
decoders. It is an internal fixture, not proof that an installed package or a
published release contains the profile; host activation is tested separately.
