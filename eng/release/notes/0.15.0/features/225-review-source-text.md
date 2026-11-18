## What changed in the source of a stale review ([#225](https://github.com/Runic-Artifex/runic-translations-sdk/issues/225))

When a source message changes after its translation was approved, the Translations Editor now shows what changed: the
workflow panel of the stale translation shows the source it was approved for ("Source when marked") next to the
current one, with removed words struck through, added words highlighted and placeholders as chips, beside **Confirm
translation** and **Mark needs review**.

The text comes from a new companion of the review file, `<catalog>.review-sources.json`
(`runic.translations.review-sources/1`, schema `review-sources-v1.schema.json`, packed with Runic.Translations.Tooling
and written by `runic-translations schema`). `review mark`, `xliff import`, the editor and the language server's
**Mark … reviewed** record the MF2 source text of each entry they write there: one text per message key and source
fingerprint, shared by all locales. `xliff import` records the source the XLIFF file was translated from, also when it
has changed since the export. Texts no entry refers to are dropped on the next write.

`<catalog>.review.json` keeps the `runic.translations.interchange-review/2` format unchanged, because the 0.14 readers
refuse unknown members and schemas; 0.14 and 0.15 tools keep working on the same files. An entry recorded without a
text (by an older tool) shows the stale hint without the comparison.

The editor's bridge contract adds, without changing existing members, `EditorReviewSnapshot.recordedSources` (the
recorded text of each stale entry, by key and source fingerprint).
