# Translations conformance: case list and status files

The language-neutral conformance driver ([`eng/conformance`](../../../eng/conformance/README.md#translations)) runs the
[Translations corpus](../corpus/README.md) against an implementation through an adapter. This directory holds what the
driver commits:

| File | Content |
| --- | --- |
| [`cases.json`](cases.json) | the case list (`rax.conformance-cases/1`): one case per line, keyed `<file stem>/<id>` as the corpus README's [Case ids](../corpus/README.md#case-ids) name them, with the hash of its content. `bun eng/conformance/cli.ts cases` fails when it is out of date; `cases --write` rewrites it |
| [`status/dotnet-translations.json`](status/dotnet-translations.json) | the expected outcome of each case for the .NET runtime |
| [`status/esm-translations.json`](status/esm-translations.json) | the same for the generated ESM runtime |
| [`differences.md`](differences.md) | the intentional differences (D011) that `deviation` entries cite |

## Cases

| Set | Cases | Kind | What the driver checks |
| --- | ---: | --- | --- |
| `rmf2-list-conformance`, `rmf2-grouping-conformance`, `rmf2-date-percent-conformance`, `rmf2-bidi-conformance`, `rmf2-currency-unit-conformance` | 4,410 | `format` | the case's message, read from the committed artifact of its compiled group, formats as `expected` in the requested locale. The date and percent corpus's space and apostrophe tolerance applies; its `alternates` do not (below) |
| `rmf2-format-data-invalid`, `rmf2-v1.packs`, `rmf2-document-v1.packs` | 127 | `pack` | the [pack recipe](../corpus/README.md#pack-recipes), applied by the driver to the `packBase` artifact, is rejected with the case's reason (`RTR0023/malformed` for the format-data cases), or loads (`validPacks`) |
| `transport-v2` | 53 | `reference` | the wire text resolves against the reader catalog's artifact of the case's locale with the expected outcome, text and diagnostics |
| `transport-v2.rejected` | 29 | `reference-rejected` | the wire text is rejected; an implementation that names a reason must name the case's |
| `transport-v2.written` | 13 | `reference-written` | the reference written from the case's description is byte for byte `expected`; the two version 1 cases require the capability `transport-v1-writer` |
| `locale-negotiation` | 25 | `negotiate` | every preference list of the group negotiates the expected locale among the group's locales |

The relative-time, plural, `rmf2-v1` execution and document-rendering cases, and the compiler-side corpora
(`semantic-v5`, `v5-project`, `xliff-v3`, `migration`), are not in the case list yet.

## Status files

A status file (`rax.conformance-status/1`) gives each case one of:

- `pass`;
- `unsupported`: needs a capability the adapter does not list (`requires`), or an issue;
- `excluded`: a role the implementation does not have;
- `known-failure`: a case it fails today. It needs an issue, and only a `development` or `preview` implementation may
  have one (R7 T1-13);
- `deviation`: an intentional difference, citing its row of [`differences.md`](differences.md).

`implementation.maturity` is the status the [capability matrix](../capabilities.md) gives the implementation.

**Dates and the TR-1 tolerance.** Localized dates and times come from each implementation's own data
(`dateTimeSource` in the matrix). Three date cases list an `alternates` text that some platform ICU writes. The driver
reports such output as a failure that names the alternate. The status files record those cases as the deviation
[`platform-date-data`](differences.md#platform-date-data), which accepts a pass or that failure. So a platform with other
date data needs a status entry, not another alternate in the corpus.

Current statuses: both implementations are `stable`. Each has 4,652 `pass`, the 3 date cases as `deviation`, and the
2 version 1 writer cases as `unsupported` (`requires: ["transport-v1-writer"]`): neither public API writes version 1
references.
