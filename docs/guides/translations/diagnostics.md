# Diagnostics

The Runic Translations compiler reports `RTR` diagnostics. The same IDs and
messages appear in every host: the C# source generator in
`Runic.Translations.Build` (as compiler errors and warnings), the
`runic-translations` tool (`validate`, `generate`, `verify`, `serve`), the
language server and the editor. Each message names the specific problem; this
page explains what each ID covers and how to fix it. Locations use the source
file, a one-based line and column, and UTF-16 columns.

Most diagnostics are errors and stop generation. `RTR0010`, `RTR0011` and
`RTR0021` follow the project's `validation` policies (`allow`, `warning` or
`error`), and some `RTR0051` reports are warnings, as are `RTR0068` and
`RTR0069`.

## RTR0001

Translation source is unreadable or malformed. A source file could not be read,
is not valid UTF-8, or `runic.json` contains a duplicate JSON property. Save the
file as UTF-8 and remove the duplicate property.

## RTR0002

Translation inputs are duplicated or ambiguous. The same normalized source path
was supplied twice, or the generator received zero or several `runic.json`
projects. A C# project compiles exactly one translation project; check the
`AdditionalFiles` marked `RunicTranslationKind="Project"`.

## RTR0003

Translation project schema is not supported. `runic.json` declares a
`schemaVersion` or `$schema` this compiler does not support. Use
`"schemaVersion": 1` and the published `project-v1.schema.json` URI, or upgrade
the tool and packages together.

## RTR0004

Locale is invalid or not declared. A locale tag is not a valid BCP 47 tag, a
locale appears twice, the base locale is not declared, or a source file uses a
locale missing from `locales`. Declare every locale in `runic.json` and name
files after canonical tags.

## RTR0006

Catalog ID or generated code name is invalid. The catalog ID, `code.namespace`
or `code.className` cannot be used. Catalog IDs are lowercase identifiers with
dots or dashes; namespaces and class names must be ASCII C# identifiers. A class
name cannot be `Messages`, `__readable` or `__translationManager`, which name
members of the generated class.

## RTR0009

Base locale defines no messages. The base locale has no source files, or it
defines no message, so no typed catalog can be generated. Add at least one
message to the base locale.

## RTR0010

Locale lacks a translation for a base-locale message. A locale does not
translate a message of the base locale. Translate it, or relax
`validation.translationCompleteness` to `warning` or `allow` to fall back.

## RTR0011

Locale defines a message the base locale does not have. Translations may only
use keys of the base locale. Remove or rename the message, add it to the base
locale, or set `validation.extraLocaleKeys`.

## RTR0012

Locale fallback is invalid. The base locale declares a fallback, or a fallback
names an undeclared locale. Only non-base locales have fallbacks, and they must
name declared locales.

## RTR0013

Locale fallback does not reach the base locale. A chain of fallbacks forms a
cycle or ends without reaching the base locale. Make every chain end at the base
locale.

## RTR0014

Message pattern is malformed. A pattern has an unmatched `{` or `}` or an
invalid placeholder. Quote literal braces and close every expression.

## RTR0016

Translation changes the message's caller inputs. A translation introduces an
input the base-locale message does not declare, or gives an input another type.
Callers use the base locale's inputs, so translations must keep them.

## RTR0018

Generated name collides or is reserved. Two resource paths map to the same
generated key, a generated hint or class name collides, or a name is a reserved
Windows file name such as `CON`. Rename one of the resources or the class.

## RTR0019

Project member or source encoding is invalid. A `runic.json` member has the
wrong JSON type or an invalid value (for example `visibility` or a validation
policy), or a message is not valid UTF-8.

## RTR0021

Message has an empty variant. A variant of a message has no content. This is
allowed by default; `validation.emptyValues` turns it into a warning or an error.

## RTR0022

Compiler limit exceeded. A document, message, nesting depth, input count, key
count or locale count exceeds the compiler's configured limit. Split large
resources or reduce the catalog.

## RTR0024

Referenced Runic.Translations runtime ABI is incompatible. The project
references a `Runic.Translations` runtime whose ABI does not match the generated
code, or no runtime at all. Reference `Runic.Translations` at the same version as
`Runic.Translations.Build`.

## RTR0031

Built-in formatter does not support the content locale. A plural, ordinal or
relative-time formatter is used for a locale the built-in data does not cover.
Use a supported locale or avoid the formatter for that locale.

## RTR0041

MF2 variable or function is invalid. A variable name is invalid, a variable has
conflicting declarations, or a message calls an unsupported function. See the
[RMF2 guide](rmf2.md) for the supported functions.

## RTR0050

RMF2 resource syntax is invalid. A grouped `.rmf2` file has a structural error,
or a direct `.mf2` file name is not an identifier.

## RTR0051

Message metadata is invalid or unknown. `@param` documents a missing input, or
`@example` is not a JSON object matching the message inputs (errors). Unknown
`@` metadata is preserved but reported as a warning.

## RTR0052

Translation source layout is invalid. A source is outside every source root,
mixes direct `.mf2` and grouped `.rmf2` files, or does not follow the
`{locale}/{message}.mf2` or `{locale}.rmf2` layout.

## RTR0054

Resource is declared more than once or conflicts. The same resource is declared
twice in one locale, or a path is a message in one locale and a group in another.

## RTR0060

Markup contract is invalid. A `markup` declaration in `runic.json` has an
invalid shape, unknown member, name or option.

## RTR0061

Inline markup is invalid. Markup in a message is unknown, unbalanced,
overlapping, nested too deeply, or uses the wrong standalone or paired form.

## RTR0062

Markup slot or project markup reference is invalid. A message does not satisfy
its declared markup slots, or `runic.json` refers to an unknown message or slot.

## RTR0065

Message is not executable in the RMF2 profile. The message is valid MF2 but
uses a construct the `rmf2-execution-v2` runtime does not execute, such as
repeated selectors or formatting a constant local.

## RTR0066

MF2 syntax error. The message is not valid MF2 syntax.

## RTR0067

MF2 data model is invalid. The message parses but violates the MF2 data model:
for example a matcher without a catch-all variant, duplicate variant keys, or a
local used before its declaration.

## RTR0068

Referenced runtime lacks the readable C# surface. The C# source generator
found a `Runic.Translations` reference that predates the readable surface (its
`TranslationsCompatibility` has no `TypedSlotBindingsVersion`). The generator
still emits the encoded accessors, keys, catalog data and registration, but
skips `<ClassName>.Readable.g.cs`, so `text.Messages` is unavailable. Code that
does not use `Messages` keeps building. Reference `Runic.Translations` at the
same version as `Runic.Translations.Build`. Only the source generator reports
this; `runic-translations generate --emit-csharp` cannot inspect references and
always writes the readable file.

## RTR0069

Readable C# name is reserved or clashes. A message is left out of the readable
C# surface (`text.Messages`) because its key, or one of its slot IDs, is
reserved, or because a verbatim input name or slot ID equals the encoded name
of another input or slot of the same message. Reserved message keys are the
`object` members (`Equals`, `GetHashCode`, `ToString`, `GetType`,
`MemberwiseClone`, `Finalize`, `ReferenceEquals`), `ReadableNameVersion`,
`__text`, and the class names `<ClassName>`, `<ClassName>Messages` and
`<ClassName>Slots`. A slot ID is reserved when it is an `object` member or equal
to the message key. The message stays available through its encoded member.
Rename the key, input or slot, or change the severity in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.RTR0069.severity = error # or none
```

## Other hosts

`RTR0020` is reported by the `runic-translations` tool and MSBuild when a
generated output path is invalid or escapes the output root. `RTR0023` is the
runtime's classification of a rejected external translation pack.
