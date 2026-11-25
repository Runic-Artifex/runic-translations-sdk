## `dotnet runic translations` runs this tool

From runic-sdk 0.24, `dotnet runic translations …` runs `runic-translations …` as an extension of the Runic core tool,
with the arguments unchanged and this tool's exit code. For the core's `doctor`, the tool answers
`runic-translations --extension-info` with one JSON line: `name` (`translations`), `version` (the release that
`--version` reports) and `extensionProtocol` 1, plus `command` and `package`. Nothing else changes: the package
`dotnet-runic-translations` and the command `runic-translations` keep their names in this release. See
[Run under `dotnet runic`](../../../tools/dotnet-runic-translations/README.md#run-under-dotnet-runic).
