- **Runic Command Line 0.15.** `Runic.Translations.CommandLine` 0.16 pairs with
  Runic.CommandLine 0.15: its package depends on `Runic.CommandLine`
  `[0.15.0, 0.16.0-0.0)`, so update both together; 0.15.x of the adapter stays
  on Command Line 0.14. Command Line 0.15 adds and changes no text keys, so the
  `runic-command-line` catalog and its English and German text are unchanged,
  and your overrides and packs keep working. Help and usage errors change in
  every language: a command's and a group's help no longer list `--version`
  (`Version anzeigen` in German), which only root help keeps; a root command's
  usage error ends with root help's usage line and
  `Weitere Informationen erhalten Sie mit "<application> --help".`; and a root
  command's name is no longer suggested for a mistyped command. Update tests
  that compare such help or standard error text. See the
  [Command Line 0.15 release notes](https://github.com/Runic-Artifex/runic-cli-sdk/releases/tag/v0.15.0).
