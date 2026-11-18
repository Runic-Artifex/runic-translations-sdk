## Dotted message keys in the CLI ([#225](https://github.com/Runic-Artifex/runic-translations-sdk/issues/225))

A message has one name for people: its logical key, the dotted path of its groups and name (`common.cancel`), as
`validate` and the editor show it. `review mark --key` now takes it as well as the canonical key that generated code
and the review file use (`common_cancel`). Both name the same message (`RTR0018` keeps them unambiguous), and the
review file keeps storing the canonical key. A key that names no message is reported with the closest logical key
(`Did you mean 'common.cancel'?`), here and in `overrides add --key` and the compile server's `message` and
`set-message`.

The Translations Editor's bridge contract adds, without changing existing members, the logical path of each message
(`EditorMessageEntry.path`, `["common", "cancel"]`) and of the message a diagnostic belongs to
(`EditorDiagnostic.messagePath`), for a namespace tree in the message list.
