- **Runic Command Line 0.14.** `Runic.Translations.CommandLine` 0.15 pairs with
  Runic.CommandLine 0.14: its package depends on `Runic.CommandLine`
  `[0.14.0, 0.15.0-0.0)`, so update both together; 0.14.x of the adapter stays
  on Command Line 0.13 (#243). Command Line 0.14 retires the `faults.RCLI4001`
  key: the `RCLI4001` fault (a prompt that cannot be asked) always shows its
  `diagnostics.prompt-*` text, so every framework key now has a message in the
  built-in English and German catalogs. To change that text, override
  `diagnostics.promptRequiresTerminal`, `diagnostics.promptJsonOutput` and
  `diagnostics.promptDisabled` (input `prompt`) in
  `translations/overrides/runic-command-line/<locale>.rmf2`, and delete a
  `faults.RCLI4001` message. Its strict JSON reader (`CommandJsonEnvelopeReader`
  and the `Runic.CommandLine.Testing` helpers built on it) now rejects a frame
  with a duplicate member name anywhere, also inside members it does not know,
  with `duplicate-property`; write each member name once per object. See the
  [Command Line 0.14 release notes](https://github.com/Runic-Artifex/runic-cli-sdk/releases/tag/v0.14.0).
