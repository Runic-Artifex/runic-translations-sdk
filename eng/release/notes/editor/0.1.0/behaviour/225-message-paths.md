- Messages are named by their dotted path (`common.cancel`), as `validate` and the diagnostics name them, and the
  message tree and the list group them by namespace
  ([#225](https://github.com/Runic-Artifex/runic-translations-sdk/issues/225)). The editor's bridge contract adds
  `EditorMessageEntry.path` and `EditorDiagnostic.messagePath` for it, without changing existing members.
