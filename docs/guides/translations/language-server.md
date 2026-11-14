# Language server

`runic-translations lsp` runs the Runic Translations language server for direct
`.mf2` and grouped `.rmf2` resources and their `runic.json` project files. It is
part of the `dotnet-runic-translations` .NET tool and is released with every
Translations release. The editor extensions in
[runic-editors](https://github.com/Runic-Artifex/runic-editors) (VS Code and
Visual Studio 2026) are clients of this server; they do not bundle a server of
their own. The VS Code extension is becoming one Runic extension for Translations
and the Runic SDK
([runic-editors#2](https://github.com/Runic-Artifex/runic-editors/issues/2)).

The command is hidden from `runic-translations --help`. See
[RMF2](rmf2.md#cli-editor-and-language-service) for what its refactors and previews do.

## Compatibility

- The server is the project's own tool, so it always matches the project's
  Translations release. Install `dotnet-runic-translations` with the same exact
  version as the project's `Runic.Translations` packages.
- Until the production language server (2.0), the `lsp` command, its protocol
  subset, its custom methods and commands, and the `initializationOptions` it reads
  are **outside the 1.0 compatibility rules** and may change in a minor release
  (see [Versioning and support from 1.0](../../../eng/release/README.md#versioning-and-support-from-10)).
  `serverInfo` reports `Runic RMF2` with protocol revision `1`.
- The editor extensions are versioned and released by runic-editors. Its
  [README](https://github.com/Runic-Artifex/runic-editors#language-server-and-supported-translations-versions)
  lists the Translations releases each extension release is tested with. Up to
  0.11, the extensions were also attached to the Translations GitHub release; from
  0.12.0 install them from the
  [runic-editors releases](https://github.com/Runic-Artifex/runic-editors/releases).

## How editors launch it

Install the tool as a local tool in the repository or solution root and commit
the manifest; collaborators then run only `dotnet tool restore`. The tool needs
the .NET 10 runtime. Projects created with the `runic-translations` templates
already contain the manifest.

```sh
dotnet new tool-manifest --output .config
dotnet tool install dotnet-runic-translations --version <VERSION>
dotnet tool restore
```

A client starts one server per workspace folder, from the directory of the
nearest tool manifest:

```sh
dotnet tool run runic-translations -- lsp
```

The server speaks LSP over standard input and output (`Content-Length` framing,
JSON-RPC 2.0) and takes no other arguments. It reads the workspace folders (or
`rootUri`) from `initialize`, finds each `runic.json` below them, and follows
declared `sourceRoots`. The editors run it only in trusted workspaces, because it
runs the workspace's own tool. For development against a build of this
repository, the extensions accept the path of the built
`dotnet-runic-translations.dll` instead (see their READMEs).

## Protocol

The server negotiates the position encoding from the client's
`general.positionEncodings` (`utf-8`, `utf-16` or `utf-32`; `utf-16` when the
client offers none) and uses incremental text synchronization. Unsaved buffers,
including new RMF2 files inside a configured source root and open `runic.json`
files, take part in every request. Cancellation (`$/cancelRequest`) is processed
while a request runs; cancelled and stale requests return protocol errors instead
of obsolete results.

| Area | Methods |
| --- | --- |
| Lifecycle | `initialize`, `initialized`, `shutdown`, `exit` |
| Documents | `textDocument/didOpen`, `didChange`, `didClose`, `didSave`; `workspace/didChangeWatchedFiles`, `workspace/didChangeConfiguration` |
| Diagnostics | `textDocument/publishDiagnostics` for the whole catalog while a file of the project is open, with links to the [diagnostics reference](diagnostics.md) when the client supports `codeDescription` |
| Language features | `completion` (trigger characters `$ : # / =`), `hover`, `definition`, `references`, `documentSymbol`, `foldingRange`, `semanticTokens/full`, `formatting`, `rename` |
| Quick fixes | `textDocument/codeAction` (`quickfix`) with `codeAction/resolve` |
| Commands | `workspace/executeCommand`: `runic.extractGroup`, `runic.inlineResource`, `runic.markReviewed`, `runic.preview`, `runic.renderPreview`, `runic.renameInput`, `runic.renameSlot`, `runic.renameResource` |
| Runic requests | `runic/documentInfo` (project, catalog, base locale and locale of a file), `runic/message` (the resource at the cursor and its inputs, slots and locales), `runic/libraryCatalogs` and `runic/libraryDocument` (installed library catalogs, for [override sources](composition.md)) |

Refactors and commands return `WorkspaceEdit` results that the client applies.
Edits that create or delete files need the client's `create` and `delete`
resource operations. Edits that change `runic.json` need
`initializationOptions.runicConfigurationSync: true`, by which the client
promises to send its unsaved `runic.json` buffers; without it the server refuses
such refactors rather than overwrite unsaved configuration.

## Tests

The protocol is covered by the `Rmf2*LspTests` suites in
`tests/dotnet/Runic.Translations.Build.Tests` (see
[RMF2 validation](rmf2-validation.md#language-service-checks)). runic-editors
runs its extension-host and native Visual Studio tests against the released tool
from NuGet.
