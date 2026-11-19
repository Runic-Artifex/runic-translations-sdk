- CI checks the language server's performance on a catalog of 20,000 messages in 10 locales
  ([#262](https://github.com/Runic-Artifex/runic-translations-sdk/issues/262)): the new *Language server / large-catalog
  budgets* job measures open to diagnostics, an edit to its diagnostics, hover and completion while typing, a rename and
  memory over stdio, with and without a review file, and fails when one exceeds its budget (`tests/lsp-budget`,
  `bun eng/run.mjs lsp-budget`). It replaces the opt-in 2,000-message benchmark, which no workflow ran. The language
  server guide lists the budgets and the measured values
  ([Performance](https://github.com/Runic-Artifex/runic-translations-sdk/blob/v0.15.0/docs/guides/translations/language-server.md#performance)).
