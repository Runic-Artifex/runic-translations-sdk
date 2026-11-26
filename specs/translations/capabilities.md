# Capability matrix

The capability matrix states, for one Translations release, which locales have plural rules, relative time and list
patterns, how equal the implementations' output is for each formatter form (`exact` or `semantic`), and which
implementations there are. `eng/generate-cldr.mjs` generates it from the pinned CLDR subset;
`eng/render-capabilities.mjs` renders it as the [capabilities guide](../../docs/guides/translations/capabilities.md).

| Version | File | Schema | State |
| --- | --- | --- | --- |
| 2 | [`capabilities-v2.json`](capabilities-v2.json) | [`capabilities-v2.schema.json`](schemas/capabilities-v2.schema.json) | current |
| 1 | [`capabilities-v1.json`](capabilities-v1.json) | [`capabilities-v1.schema.json`](schemas/capabilities-v1.schema.json) | frozen |

## Version 2

Version 2 has the members of version 1, except that `implementations` replaces `backends`:

| Member | Content |
| --- | --- |
| `capabilityMatrixVersion` | `2` |
| `releaseTarget` | the release the matrix describes (`RunicTranslationsVersion`) |
| `cldr` | the pinned CLDR release: `version`, `jsonTag`, `source` archive, its `sha512` and `license` |
| `implementations` | the implementations by key, each with `name`, `status`, `dateTimeSource`, and optionally `conformance` and `formatterProfile` (below) |
| `equivalence` | what `exact` and `semantic` mean |
| `formatterProfile` | the equivalence of each formatter form, for every implementation unless its entry overrides it |
| `messageProfile` | the equivalence of plural selection, relative time and lists |
| `locales` | every CLDR language and language-script locale, with whether it has cardinal and ordinal rules, relative time and list patterns |

**Implementation keys are open.** The schema accepts any key of the pattern `^[a-z][a-z0-9]*(-[a-z0-9]+)*$`, so an
implementation such as `rust` is a new key, and a validator of version 2 accepts the matrix that lists it. Version 1's
`backends` is the closed tuple `["dotnet", "esm"]`: adding a backend there would make every version 1 document with
it invalid against the version 1 schema (R2 TR-10).

Each implementation has:

| Member | Content |
| --- | --- |
| `name` | the implementation's packages or runtime, for readers |
| `status` | its maturity: `development`, `preview` or `stable`, the values of its conformance status file. `stable` has no known conformance failures; `development` and `preview` may have some, each with an issue (R7 T1-13) |
| `dateTimeSource` | where its localized date and time patterns and names come from. Localized dates and times are `semantic`: each implementation takes them from its platform or its own data, within the tolerance of the [date and percent corpus](corpus/README.md) |
| `conformance` | optional: its conformance status file, relative to this directory (`conformance/status/<id>.json`) |
| `formatterProfile` | optional: the formatter forms whose equivalence differs for this implementation, such as `{"date": {"localized": "semantic"}}`. Every other form has the matrix's equivalence |

Implementations are listed in the order the generator writes them. The matrix does not say that an implementation is
supported on a platform; a `preview` implementation carries no compatibility promise.

## Version 1 (frozen)

Version 1 stays generated, with the same locales and profiles as version 2 and the backends `dotnet` and `esm`, for
the tools and documents that read it; its schema and content rules do not change, and no implementation is added to
it. New consumers read version 2.
