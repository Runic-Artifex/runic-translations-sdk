## What changed in the source of a stale review ([#225](https://github.com/Runic-Artifex/runic-translations-sdk/issues/225))

When a source message changes after its translation was approved, the workflow panel of the stale translation shows
the source it was approved for ("Source when marked") next to the current one, with removed words struck through,
added words highlighted and placeholders as chips, beside **Confirm translation** and **Mark needs review**. The text
comes from `<catalog>.review-sources.json`, which the editor, `review mark`, `xliff import` and the language server now
write beside the review file (Runic Translations 0.15.0). An entry recorded by an older tool, without its text, shows
the stale hint without the comparison.
