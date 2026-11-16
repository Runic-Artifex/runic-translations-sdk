- `runic-translations xliff import` takes the XLIFF document with `--input <path>`
  instead of `--source <path>`, which read like a filter on the source-language
  text next to `--source-changed`. `--source` still works as a hidden alias until
  2.0; giving both is an invocation error (exit code 2).
