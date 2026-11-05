# Diagnostics

The Runic Translations compiler reports `RTR` diagnostics. The same IDs and
messages appear in every host: the C# source generator in
`Runic.Translations.Build` (as compiler errors and warnings), the
`runic-translations` tool (`validate`, `generate`, `verify`, `serve`), the
language server and the editor. Each message names the specific problem; this
page explains what each ID covers and how to fix it. Locations use the source
file, a one-based line and column, and UTF-16 columns. The generator's help
links open this page at the release tag of the package you use.

Most diagnostics are errors and stop generation. `RTR0010`, `RTR0011`, `RTR0021`
and the quality checks `RTR0094`–`RTR0098` follow the project's `validation`
policies (`allow`, `warning` or `error`), which a locale entry in `locales` can
override for that locale (`{ "tag": "fr", "fallback": "en", "validation": {
"translationCompleteness": "warning" } }`). The quality checks are warnings by
default, so a project that treats warnings as errors sees them as errors; and some `RTR0051` reports are warnings, as are `RTR0068`, `RTR0069`, `RTR0071`,
`RTR0076`, `RTR0077`, `RTR0078`, `RTR0084`, `RTR0090`, `RTR0091` and `RTR0093`. `RTR0085` is informational.

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
files after canonical tags. For `pseudoLocales`: a tag other than `en-XA` or
`ar-XB`, a duplicate, a tag that is also in `locales`, or source files of a
pseudo-locale while pseudo-locales are generated
([Pseudo-locales](rmf2.md#pseudo-locales)).

## RTR0006

Catalog ID or generated code name is invalid. The catalog ID, `code.namespace`
or `code.className` cannot be used. Catalog IDs are lowercase identifiers with
dots or dashes; namespaces and class names must be ASCII C# identifiers.

## RTR0009

Base locale defines no messages. The base locale has no source files, or it
defines no message, so no typed catalog can be generated. Add at least one
message to the base locale.

## RTR0010

Locale lacks a translation for a base-locale message. For example: `Locale 'de'
does not translate 'commands.remove' from base locale 'en'.` The diagnostic
points at the locale's file beside the base-locale file of the message, or at
the locale in `runic.json` when the locale has no such file. A translation that
exists but fails to compile reports its own error instead. Translate the
message, or relax `validation.translationCompleteness` to `warning` or `allow`
to fall back. A locale entry's own `validation` applies to that locale only, so
`de` can be required to be complete while `fr` may lag:
`"locales": ["en", "de", { "tag": "fr", "fallback": "en", "validation": { "translationCompleteness": "warning" } }]`.

## RTR0011

Locale defines a message the base locale does not have. For example: `Locale
'de' defines 'commands.remov', which base locale 'en' does not define. Did you
mean 'commands.remove'?` Translations may only use keys of the base locale; the
closest base-locale key is suggested when one is similar. Remove or rename the
message, add it to the base locale, or set `validation.extraLocaleKeys`
(`allow` turns the check off).

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
references a `Runic.Translations` runtime that does not run the generated code's
ABI, or no runtime at all. Code generated by release N runs on runtime N or later
within the same minor before 1.0 (the same major from 1.0), so reference
`Runic.Translations` at the version of `Runic.Translations.Build` or later. Generated code also checks the ABI of the
runtime that is loaded and throws `InvalidOperationException` with this ID when
an older runtime runs it.

## RTR0031

Built-in formatter does not support the content locale. A plural, ordinal,
relative-time or list formatter is used for a locale that the pinned CLDR data
does not cover. Every CLDR locale is covered, including regional and script
variants such as `pt-PT`, `sr-Latn` and `zh-TW`; a locale with CLDR data but no
plural rules of its own (`agq`, `rw`, `mi`) uses CLDR's root rule, which selects
`other`. So the diagnostic only reports languages without CLDR data, such as
`tlh`; see [capabilities](capabilities.md). Use a supported locale or avoid the
formatter for that locale.

## RTR0041

MF2 variable or function is invalid. A variable name is invalid, a variable has
conflicting declarations, or a message calls an unsupported function. See the
[RMF2 guide](rmf2.md) for the supported functions.

## RTR0050

RMF2 resource syntax is invalid. A grouped `.rmf2` file has a structural error,
or a direct `.mf2` file name is not an identifier.

## RTR0051

Message metadata is invalid or unknown. `@param` documents a missing input,
`@example` is not a JSON object matching the message inputs, or `@maxLength` is
not one positive whole number (errors). Unknown
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
skips `<ClassName>.Readable.g.cs`, so `text.Messages` is unavailable. Without
`TreatWarningsAsErrors`, the build stays green and code that does not use
`Messages` keeps building. With `TreatWarningsAsErrors`, RTR0068 fails the build
even when the code does not use `Messages`. Reference `Runic.Translations` at the
same version as `Runic.Translations.Build`. Only the source generator reports
this; `runic-translations generate --emit-csharp` cannot inspect references and
always writes the readable file.

RTR0068 is reported at no source location, so an `.editorconfig` section cannot
suppress it. Use one of these instead:

- `<NoWarn>$(NoWarn);RTR0068</NoWarn>` removes the warning.
- `<WarningsNotAsErrors>$(WarningsNotAsErrors);RTR0068</WarningsNotAsErrors>`
  keeps the warning but stops it from failing the build.
- A `.globalconfig` file (`is_global = true`) with
  `dotnet_diagnostic.RTR0068.severity = none` (or `suggestion`), added to the
  project as a `GlobalAnalyzerConfigFiles` item.

## RTR0069

Readable C# name is reserved or clashes. A message is left out of the readable
C# surface (`text.Messages`) because its key, or one of its slot IDs, is
reserved, or because a verbatim input name or slot ID equals the encoded name
of another input (or another slot) of the same message. When
`code.className` is `Messages` or `__readable`, which name members the facade
adds to the generated class, the whole readable surface is skipped and RTR0069
is reported once at `code.className`. Reserved message keys are the
`object` members (`Equals`, `GetHashCode`, `ToString`, `GetType`,
`MemberwiseClone`, `Finalize`, `ReferenceEquals`), `ReadableNameVersion`,
`__text`, and the class names `<ClassName>`, `<ClassName>Messages` and
`<ClassName>Slots`. A slot ID is reserved when it is an `object` member or equal
to the message key. The message stays available through its encoded member.
Rename the key, input or slot to fix it.

RTR0069 is also reported when a message key is the encoded name of another
message's reference factory: key `ToString` is reserved, so
`Class.References.r_546f537472696e67` is its factory, and a key
`r_546f537472696e67` cannot use that name too. Its factory keeps its own
encoded name; rename the key to fix it.

RTR0069 is reported at the `.rmf2` or `runic.json` location of the name, not in
a `.cs` file, so an `.editorconfig` `[*.cs]` section does not apply. To change
its severity, use one of the same mechanisms as RTR0068:

- `<NoWarn>$(NoWarn);RTR0069</NoWarn>` removes the warning;
- `<WarningsNotAsErrors>$(WarningsNotAsErrors);RTR0069</WarningsNotAsErrors>`
  keeps the warning but stops it from failing the build;
- a `.globalconfig` file (`is_global = true`) with
  `dotnet_diagnostic.RTR0069.severity = none` to remove the warning, or `error`
  to fail the build on it, added as a `GlobalAnalyzerConfigFiles` item. Put the
  key at the top level of the file, not inside a section.

The compiler cannot detect two collisions, because they are C# declarations in
the consumer's own code and produce ordinary C# errors instead of RTR0069:

- a user `partial` declaration of `<ClassName>` that already declares a member
  named `Messages`;
- user types named `<ClassName>Messages` or `<ClassName>Slots` in the same
  namespace as the generated class.

Rename the conflicting type or member, or move it to another namespace.

## RTR0070

Message content kind is invalid. A message is either inline (text and inline
markup) or a document (block elements such as `p`, `h`, `ul` and `ol` at the
top level). The kind comes from the base locale; a translation must have the
same kind, and every non-empty variant of one message must have the same kind.
Text, placeholders and inline markup cannot sit directly beside blocks at the
top level of a document; wrap them in `{#p}...{/p}`. A variant that holds only
whitespace or bidi marks counts as empty and fits either kind. See the
[RMF2 document profile](../../../specs/translations/rmf2-document-profile-v1.md).

## RTR0071

Variant uses another variant's document structure. A translated variant has a
valid document structure, but it matches the source structure of a different
variant rather than the one with the same keys. The translation still builds;
check that the variants were not swapped.

## RTR0072

Document element is in an invalid position. `li` appears outside `ul` or `ol`,
something other than `li` appears inside a list, a list is nested inside `li`
(nested lists are not part of document profile v1), a block element appears
inside `p`, `h`, `li` or inline markup, or a block element appears in an inline
message below its top level.

## RTR0073

Document exceeds a structure limit. A document nests more than 16 element
levels (blocks and inline markup together) or has more than 4096 nodes.

## RTR0074

Translated document structure does not match the source. Translators can
change text and inline markup, but the block structure of a translated variant
(the block and list elements, their order and their options) must equal the
structure of one of the source variants. The message names the first difference,
for example `ul[1]/li[3]`, against the closest source variant.

## RTR0075

Reserved for a later document profile. The current compiler does not report it.

## RTR0076

Document block or list is empty. A `p`, `h` or `li` has no content after
whitespace normalization, or a `ul` or `ol` has no items. Empty blocks still
render, but usually indicate a mistake.

## RTR0077

Document heading skips a level. The first heading of a document is not level 1,
or a heading is more than one level deeper than the heading before it. Only the
base locale reports it; translations keep the source heading levels.

## RTR0078

Line break between Southeast Asian characters became a space. In documents a
line break inside text becomes one space, except next to CJK wide characters or
U+200B ZERO WIDTH SPACE. Thai, Lao, Khmer and Myanmar text does not separate
words with spaces, so join the lines or end the first line with U+200B.

## Other hosts

The C# source generator does not report `RTR0020` and `RTR0023`.

## RTR0020

Generated output path is invalid. The `runic-translations` tool and MSBuild
report it when a generated output path is invalid or escapes the output root,
or when a translation source, the output directory or the staging directory is
a symbolic link or reparse point. Keep generated paths inside the output root
(for MSBuild, `RunicTranslationsOutputPath` beneath `IntermediateOutputPath`) and
replace links with real files or directories.

## RTR0023

External translation pack is rejected. This is the runtime's classification of
a rejected external translation pack.
`RTR0023/markup-contract-version-mismatch` means the pack was built for another
Runic markup contract version (for example by an earlier compiler release);
rebuild the pack with the current compiler.
`RTR0023/document-structure-mismatch` means a document message in the pack has
a block structure that no base-locale variant of the generated contract has;
rebuild the pack from the same sources as the application. A document message
that breaks the block rules themselves (text outside a block, a misplaced
element, or a leaf that starts or ends with whitespace) is
`RTR0023/malformed-pattern`.

## RTR0080

Translation XAML declaration is invalid. The optional XAML validator found malformed XML, an invalid Message declaration (missing/duplicate Key or properties), or a catalog assertion that names neither the local RunicTranslationProject nor a directly referenced project's catalog. Fix the declaration. External catalogs are not checked: remove the assertion, or set `RunicTranslationsValidateXaml="false"` on the file's `Page` or `RunicTranslationXaml` item to skip the file. Runtime-valued keys and sources are allowed; they are not evaluated during the build.

## RTR0081

Translation XAML message key is unknown. A static key does not exist on the generated readable surface of the catalog the file is checked against. The message suggests the closest readable key when one differs only in case, in `.`/`-` separators or by a few characters. Use the flattened readable name (for example, `application_title`), including its exact case. Encoded-only reserved names are not available to Message. A file without its own catalog assertion is checked against `RunicTranslationsXamlCatalog`; if its source is inherited at run time from another file, an `App.xaml` style or code-behind, skip it with `<Page Update="Views/OrderSummary.xaml" RunicTranslationsValidateXaml="false" />`. Design-time (`mc:Ignorable`) and `mc:AlternateContent` content is not checked.

## RTR0082

Translation XAML inputs do not match. The message names the problem: positional inputs with a gap (`Arg1` without `Arg0`), positional and named inputs mixed, a `MessageInput` name set twice or without a Name and Value, a name the message does not have, a missing input, or the wrong number of positional inputs. It lists the inputs the message takes; for the positional form it shows which `Arg` binds which input, for example `Arg0=email, Arg1=name`. The names are the generated method's readable C# parameter names; non-identifiers use their encoded fallback. Binding expressions are accepted as values and are checked by WPF at runtime.

## RTR0083

Translation XAML message kind does not match. `{rt:Message}` produces a plain string, so a message with inline markup needs `rt:TranslationProperties.RichMessage` on a `TextBlock`, and a plain message used with `RichMessage` should use `{rt:Message}` instead. Neither renders document messages; use `WpfDocumentRenderer` for those. The message names the adapter to use.

## RTR0084

Translation XAML binds several inputs by position. A message with two or more inputs is bound with `Arg0`..`Arg3`. Positions follow the generated method's parameters, which are sorted by input name, not by their order in the message text: for `edit_heading = Editing {$name} ({$email})`, `Arg0` is `email` and `Arg1` is `name`. The warning shows this mapping. Swapped bindings compile and show the wrong values, so use the named form, which binds each value by name and is checked against the catalog:

```xml
<rt:Message Key="edit_heading">
  <rt:MessageInput Name="name" Value="{Binding Name}"/>
  <rt:MessageInput Name="email" Value="{Binding Email}"/>
</rt:Message>
```

A message with a single input can keep `Arg0`. To keep positional bindings you have checked, suppress the warning with `NoWarn` or `.editorconfig`.

## RTR0085

Translation XAML keys under an explicit source are not checked. Static keys are checked against `RunicTranslationsXamlCatalog` only where that catalog applies. An explicit source may select another catalog at run time, so keys under it are skipped, and this information is reported once at the source's declaration with the first skipped key. A `Source=` on a `Message` affects only that message. An attached `rt:TranslationProperties.Source` affects its element's content, and also the file's templates, styles and resources, which may be instantiated under it. A `Setter` for `TranslationProperties.Source` can apply to any element, so it affects the whole file. If the source provides the local catalog, assert it for the file to check every key, including those with an explicit source:

```xml
<Page Update="Views/Checkout.xaml" Catalog="app" />
```

Keys of another catalog cannot be checked by the build; the WPF adapter still checks them when the XAML loads. Informational diagnostics appear in the IDE's error list but not in command-line build output; to see them there, raise the severity with `dotnet_diagnostic.RTR0085.severity = warning` in `.editorconfig` or a global analyzer config.

## RTR0086

An MSBuild name of `Runic.Translations.Build` was renamed or removed. The build
fails instead of ignoring the setting. Since 0.8 every public property, item and
metadata name starts with `Runic`:

| Old name | New name |
| --- | --- |
| `TranslationsGenerateOnBuild`, `TranslationsEmitJson`, `TranslationsEmitEsm` | `RunicTranslationsGenerateOnBuild`, `RunicTranslationsEmitJson`, `RunicTranslationsEmitEsm` |
| `TranslationsOutputPath`, `TranslationsOutputStamp`, `TranslationsWebOutputPath` | `RunicTranslationsOutputPath`, `RunicTranslationsOutputStamp`, `RunicTranslationsWebOutputPath` |
| `TranslationsToolCommand`, `TranslationsToolAdditionalArguments` | `RunicTranslationsToolCommand`, `RunicTranslationsToolAdditionalArguments` |
| `TranslationsXamlCatalog`, `TranslationsXamlReferencedCatalogs`, `TranslationsValidateXaml` (property and item metadata) | `RunicTranslationsXamlCatalog`, `RunicTranslationsXamlReferencedCatalogs`, `RunicTranslationsValidateXaml` |
| items `TranslationProject`, `TranslationMf2`, `TranslationXaml`, `TranslationsToolInput` | `RunicTranslationProject`, `RunicTranslationMf2`, `RunicTranslationXaml`, `RunicTranslationsToolInput` |

`TranslationsEmitTypeScript`, `TranslationsEmitTemplateManifest` and
`TranslationsEmitCpp` were removed: they selected emitters that do not exist.

**Fix:** rename the property, item or metadata as the message says, or delete a
removed property.

## runic-translations tool codes

The `runic-translations` tool (`dotnet-runic-translations`) reports these codes
in the `runic.commandline/1` envelope. The fault of a failed command is an `RTR`
code; its diagnostics use the tool's block `RCLI8200`–`RCLI8299` of the
Runic.CommandLine application range. Exit code `1` means a command failure,
such as a `verify` difference or an inaccessible file; `2` an invalid
invocation or translation diagnostics; `3` an internal failure; `130` a
cancelled command.

Before 0.8 the tool reported these meanings with Runic.CommandLine's generator
codes: `RCLI9000` is now `RTR0087`, `RCLI9014` is `RTR0088` with `RCLI8214`
diagnostics, and `RCLI9001`–`RCLI9006`, `RCLI9011` and `RCLI9013` are `RCLI8201`–`RCLI8206`,
`RCLI8211` and `RCLI8213`. `RCLI9012` is no longer reported: translation
diagnostics keep their own `RTR` IDs.

## RTR0087

The translations command could not be completed. The fault of a failed command;
its diagnostics name the cause.

## RTR0088

`import-text` cannot import its input. The fault of a rejected import; one
`RCLI8214` diagnostic names each problem. The command exits with 2 and writes
nothing.

## RTR0089

`Runic.Translations.Build` could not write the `Runic.Translations` dependency
range into a packed library. Packing a project that uses the Build package
gives its `Runic.Translations` dependency the range `[N, next minor-0)` before
1.0, for example `[0.9.0, 0.10.0-0)`, and `[N, next major-0)` from 1.0: code
generated by release N runs on runtime N or later within the same minor before
1.0 and the same major from 1.0. The range is written into the `.nupkg`, the
`.snupkg` and a written `.nuspec`. The error names the I/O problem; fix it and
pack again, or set `RunicTranslationsStampRuntimeRange` to `false` to keep
NuGet's open range.

## RTR0090

A translation predates a change to its source. `validate` reports this warning
at the translation when the project has a review file, `<catalog>.review.json`
beside `runic.json`, and the file's entry for the key and locale records the
fingerprint of an older source: the base text or its placeholders changed after
the translation was made, imported or approved. The C# source generator does not
report it. Review the translation, then record it with
`runic-translations review mark --key <key> --locale <locale> --state approved`
(or `needs-review`). The Translations Editor reads the same file: it marks the
row **Source changed**, and setting the state again records the current source. See
[review state](../../../tools/dotnet-runic-translations/README.md#track-review-state-and-stale-translations).

## RTR0091

The review file cannot be read. `validate` reports this warning at
`<catalog>.review.json` when it is not valid
`runic.translations.interchange-review/2` (or `/1`) JSON, or belongs to another
catalog. The message names the interchange code, such as `REVIEW-MALFORMED`.
Fix or delete the file; `review mark` refuses to overwrite it.

## RTR0092

An `xliff` or `review` command cannot use its document. The fault of an
`xliff export`, `xliff import` or `review mark` that stops before writing
anything; one `RCLI8215` diagnostic names the problem. The command exits with 2.
`xliff import` also stops with this fault when `--fail-on refused` is given and
a unit was refused (each refusal is an `RCLI8216` warning), and when a source or
the review file changed on disk between reading and writing, for example through
an editor save or a second import; run the import again.
## RTR0093

A packed library's `Runic.Translations` dependency has a version format that
the version rule does not handle, such as a floating version (`0.9.*`), so the
package keeps it as an open minimum version and a later breaking release
satisfies it. This warning names the version. Reference `Runic.Translations`
with a plain version such as `0.9.0`, which the Build package turns into
`[0.9.0, 0.10.0-0)` (see [RTR0089](#rtr0089)), or set
`RunicTranslationsStampRuntimeRange` to `false`. A dependency that already names
a range is left as it is without this warning.

## RTR0094

Plural key is never selected in its locale. For example:
`Variant key 'few' of 'items' is never selected in locale 'de', whose plural
categories are one, other.` The key is checked against the CLDR categories of
the message's own locale (cardinal for `:integer`/`:number`, ordinal for
`select=ordinal`), from the same generated rules that both runtimes select with,
so the variant is dead text. An integer selector (`:integer`) is checked against
the categories integers reach: Czech `many` is for decimals only, so `many` under
`:integer` is reported (`whose plural categories for integers are one, few,
other`). It usually comes from copying another language's
variants. Remove the variant, or use an exact number key such as `0` if a value
was meant. A warning by default; `validation.pluralCategories` sets the policy.

## RTR0095

Plural categories of the locale fall to the catch-all variant. For example:
`'items' has no variant for the plural categories few, many of locale 'cs'; they
fall to the '*' variant.` Czech selects `few` for 2–4 and `many` for decimals, so
a translation with only `one` and `*` shows the `*` text, often with the wrong
grammar. Under `:integer` only the categories integers reach are expected:
`one`, `few` and `*` are a complete Czech integer plural, while French still
needs `many` (1000000). Add a variant for each listed category; for a message
with one selector, the Translations Editor offers a quick fix that adds them as
copies of the `*` variant. A selector whose keys are all
exact numbers and `*` chose not to use categories and is not checked. A warning
by default; `validation.pluralCategories` sets the policy (with `RTR0094`).

## RTR0096

Translation drops a placeholder the base displays. For example: `The 'de' translation of
'greeting' displays no $name, which base locale 'en' displays.` An input that
the base locale shows in a pattern, directly or through a `.local`, appears in
no variant of the translation. Translations may omit inputs, so this is a
warning, not an error; the union over variants counts, so `one {{Ein Element}}`
beside `* {{{$n} Elemente}}` is fine, and an input the base only selects on is
not required. Add the placeholder, or set `validation.inputParity` (`allow`
turns the check off, for a language that needs no count).

## RTR0097

Boolean is formatted as text. For example: `'flag' in locale 'en' formats the
boolean $on as text, which reads 'true' or 'false' in every locale.` A
`:runic:boolean` value, or an input declared boolean, printed in a pattern is
the English word in every locale. Select on it instead and write the words in
each variant: `.input {$on :runic:boolean}`, `.match $on`, `true {{On}}`,
`* {{Off}}`. A warning by default; `validation.formattedBooleans: "error"`
forbids it.

## RTR0098

Message is longer than its @maxLength. For example: `The 'de' text of 'save'
is 16 characters long, more than its @maxLength of 10; placeholder values are
not counted.` `@maxLength N` on a base-locale message applies to every locale.
A locale's own `@maxLength` can only lower it for that locale; a larger value is
an `RTR0051` warning and the base limit applies. The length is the
number of Unicode code points of the literal text after NFC normalization, in
the longest variant, without placeholder values and markup, so a text that
passes can still overflow at run time. XLIFF export writes the same per-locale limit as
`slr:sizeRestriction` with the `xliff:codepoints` profile, so translation tools
can enforce it. Shorten the translation or raise the limit. A warning by
default; `validation.maxLength` sets the policy. A `@maxLength` that is not a
positive whole number, or appears twice, is an `RTR0051` error.

## RCLI8201

The requested output could not be written, for example because of an
`RTR0020` path problem. Fix the reported path and run the command again.

## RCLI8202

The translations operation reported diagnostics. Fix the `RTR` diagnostics it
lists.

## RCLI8203

The command arguments are invalid. The message names the argument, and human
output prints the usage.

## RCLI8204

A translations project could not be created, for example because the project
already exists; no files were written. The message names the problem.

## RCLI8205

A translations input or output could not be accessed. Check that the paths
exist and that you can read and write them.

## RCLI8206

The command failed internally. Report it with the command and project that
trigger it.

## RCLI8211

`verify` found a difference between the retained output and freshly generated
output. Regenerate the output with `generate` and commit it.

## RCLI8213

The project cannot produce the requested output, for example an emit switch
the project's sources do not support. The message names the output.

## RCLI8214

A problem that keeps `import-text` from importing its input: a table is not a
JSON object of string values, `--base` is not a JSON array of strings, a
translation uses a placeholder that its base text lacks, or the existing
base-locale file of the group cannot be read back. The message names the file
or the base text. The fault is `RTR0088`.

## RCLI8215

An XLIFF document or review file cannot be used as a whole. The message starts
with its interchange code: `XLIFF21-MALFORMED` (not well-formed XML),
`XLIFF21-ROOT` or `XLIFF21-VERSION` (not XLIFF 2.0 or 2.1, no `trgLang`),
`XLIFF21-FILE` (not exactly one `<file>`, no units), `XLIFF21-CATALOG-MISMATCH`,
`XLIFF21-SOURCE-LOCALE` or `XLIFF21-TARGET-LOCALE` (another catalog, base locale
or an undeclared target locale), `XLIFF21-CONTRACT` or `XLIFF21-PROFILE`
(another execution contract or Runic profile), `XLIFF21-LIMIT` (more than 8 MiB,
50,000 units, or elements nested deeper than 64 levels, counting `<xliff>` as
level 0), or a `REVIEW-*` code for the review file. It also names a source or
review file that changed on disk during `xliff import`. The fault is `RTR0092`
and nothing is written.

## RCLI8216

An XLIFF unit was refused and the rest of the document was applied. This
warning names the key and the unit code: `XLIFF21-PLACEHOLDER` (a `<ph>` that
is not one MF2 placeholder, or a placeholder the message's caller contract does
not have), `XLIFF21-PLACEHOLDER-CHANGED` (a target `<ph>` whose expression is
not one of the message's placeholders: another function or options, or a literal
in place of a variable), `XLIFF21-INLINE` (an inline element other than `<ph>`, `<mrk>` and
`<cp>`), `XLIFF21-TARGET-INVALID` (the target does not compile against the base
message), `XLIFF21-UNKNOWN-KEY`, `XLIFF21-DUPLICATE-UNIT`, `XLIFF21-UNIT-ID`,
`XLIFF21-STATE` (an unknown segment state), `XLIFF21-SOURCE-CHANGED` (with
`--source-changed refuse`) or `XLIFF21-METADATA` (an unreadable Runic note).
Fix the unit in the TMS or the file and import it again. With
`--fail-on refused` a refused unit fails the whole import instead (`RTR0092`).
Skipped units
(`XLIFF21-UNTRANSLATED`, `XLIFF21-EMPTY-TARGET`, `XLIFF21-STRUCTURED-SKIPPED`)
are listed in the `interchange` payload only.

## runic-translations support codes

`runic-translations support preview|collect|remove` reports these codes as the
fault of a failed command. They replace the `RAPPSUP` codes of the former
`dotnet runic support --mode`, with the same numbers and meanings.
`RTRSUP025` exits with `1`; every other code is a usage error and exits with
`2`.

## RTRSUP001

`support` received a mode other than `preview`, `collect` or `remove`.

**Fix:** run `runic-translations support preview`, `collect` or `remove`.

## RTRSUP002

`support collect` has no `--destination`.

**Fix:** pass the envelope file with `--destination`.

## RTRSUP003

The `--destination` file of `collect` already exists.

**Fix:** choose a new file, or remove the old envelope with `support remove`.

## RTRSUP004

The support envelope would exceed its size limit.

**Fix:** collect from a smaller Editor diagnostic ZIP.

## RTRSUP005

`support remove` was given a collector source such as
`--editor-diagnostics`.

**Fix:** pass only `--destination`.

## RTRSUP006

`support remove` has no `--destination`.

**Fix:** pass the envelope file with `--destination`.

## RTRSUP007

The `--destination` file of `remove` does not exist.

**Fix:** pass the path of an envelope that `collect` wrote.

## RTRSUP008

`preview` or `collect` has no collector.

**Fix:** select the Editor collector with `--editor-diagnostics <zip>`.

## RTRSUP009

The Editor collector source is not a `.zip` file.

**Fix:** pass the diagnostic ZIP that the Editor exported.

## RTRSUP010

The Editor diagnostic ZIP is missing or exceeds its size limit.

**Fix:** export the diagnostics from the Editor again and pass that file.

## RTRSUP011

The Editor diagnostic ZIP has an invalid number of entries.

**Fix:** pass the ZIP exactly as the Editor exported it.

## RTRSUP012

The Editor diagnostic ZIP does not contain a bounded `diagnostics.json`.

**Fix:** pass the ZIP exactly as the Editor exported it.

## RTRSUP013

The Editor diagnostic source is not a valid ZIP file.

**Fix:** export the diagnostics from the Editor again.

## RTRSUP014

The Editor diagnostic summary is not valid JSON.

**Fix:** export the diagnostics from the Editor again.

## RTRSUP015

The diagnostic summary uses a schema that this version of `runic-translations`
does not support.

**Fix:** use a `runic-translations` from the same release as the Editor.

## RTRSUP016

The diagnostic groups in the summary are invalid or exceed their limit.

**Fix:** export the diagnostics from the Editor again.

## RTRSUP017

The `--destination` file of `remove` is not a support envelope created by
`support collect`.

**Fix:** pass the envelope that `collect` wrote; `remove` deletes nothing else.

## RTRSUP018

The diagnostic summary omits a required field.

**Fix:** export the diagnostics from the Editor again, with the Editor and
`runic-translations` from the same release.

## RTRSUP019

A text field of the diagnostic summary is not a string.

**Fix:** export the diagnostics from the Editor again.

## RTRSUP020

A count in the diagnostic summary is not a non-negative integer.

**Fix:** export the diagnostics from the Editor again.

## RTRSUP021

A flag in the diagnostic summary is not `true` or `false`.

**Fix:** export the diagnostics from the Editor again.

## RTRSUP022

A section of the diagnostic summary is not a JSON object.

**Fix:** export the diagnostics from the Editor again.

## RTRSUP023

The diagnostic summary contains fields that the envelope does not support.

**Fix:** use a `runic-translations` from the same release as the Editor.

## RTRSUP024

The diagnostic summary contains a path or other sensitive text, which a
support envelope must not include.

**Fix:** export the diagnostics from the Editor again; it removes paths from
the summary.

## RTRSUP025

`support` could not read or write a required local file.

**Fix:** check that the ZIP and the destination folder exist and that you can
read and write them.
