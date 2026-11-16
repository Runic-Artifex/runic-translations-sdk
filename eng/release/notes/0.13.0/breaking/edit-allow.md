- `@runic-artifex/vite-plugin-runic-translations` no longer reads
  `RUNIC_TRANSLATIONS_EDIT_ALLOW`, the former name of the in-page editing API's
  allow list. Set `RUNIC_DEV_ALLOW` instead, which takes the same entries and
  which vite-plugin-runic also reads for Runic Inspect. When the old variable is
  set, the dev server logs a warning and ignores it.
