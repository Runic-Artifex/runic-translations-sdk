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
- From 1.0.0 the language server is covered by the 1.0 compatibility rules
  ([Versioning and support from 1.0](../../../eng/release/README.md#versioning-and-support-from-10)),
  as the tool's other commands are: a minor release only adds, and a breaking
  change needs a major release. Until 1.0 a minor release may still change it,
  with upgrade steps in its release notes
  ([Versions before 1.0](../../../eng/release/README.md#versions-before-10)).
- The editor extensions are versioned and released by runic-editors. Its
  [README](https://github.com/Runic-Artifex/runic-editors#language-server-and-supported-translations-versions)
  lists the Translations releases each extension release is tested with. Up to
  0.11, the extensions were also attached to the Translations GitHub release; from
  0.12.0 install them from the
  [runic-editors releases](https://github.com/Runic-Artifex/runic-editors/releases).

### What the promise covers

- **The command.** `runic-translations lsp`, run with no arguments and speaking
  LSP over standard input and output. It is hidden from `--help`, but covered.
- **The standard LSP surface.** Every capability the `initialize` result
  advertises, the LSP methods behind it, and their behaviour as the LSP
  specification defines it for `.rmf2`, `.mf2` and `runic.json` documents: the
  methods listed under [Protocol](#protocol), the position encodings, the
  semantic token legend, the code-action kinds, the completion trigger
  characters, and the error codes in [Protocol revisions](#protocol-revisions).
- **The `experimental.runic` protocol.** The capability key
  `capabilities.experimental.runic` is covered, with its members `protocol`,
  `commands` and `requests`. So are each listed `runic.*` command and `runic/*`
  request, with their arguments, results and errors, the meaning of protocol
  revision 2, and the `initializationOptions` the server reads
  (`runicConfigurationSync`, and `runicProtocol` for choosing a later revision).

The `initialize` result a client receives is pinned by a golden snapshot test
(`tests/dotnet/Runic.Translations.Build.Tests/Fixtures/lsp-initialize-result.json`).

### What a minor release may add

New capabilities and capability options, completion trigger characters,
code-action kinds, semantic token types appended to the legend, commands and
requests listed in `experimental.runic`, optional arguments, result members,
diagnostics and quick fixes. A client ignores what it does not know: unknown
capabilities, members, commands, requests and code-action kinds.

### What is not covered

- Text written for people: hover and completion details, diagnostic messages,
  code-action titles and the messages of refusals. The diagnostics carry the
  compiler's IDs (`RTR0001`), which the [diagnostics reference](diagnostics.md)
  documents.
- The order of completion items, symbols, references and code actions, and
  which quick fixes are offered for a diagnostic.
- Timing, batching and performance. The large-catalog budgets that CI checks
  ([Performance](#performance)) guard against regressions and are not a
  contract.
- The value of `serverInfo.version`, which is the tool's version, and the file
  patterns the server watches, which may grow.

### What a breaking change requires

- **Standard LSP surface.** Removing a capability or a method, or changing its
  behaviour beyond what the LSP specification allows, waits for a major
  release, with upgrade steps in its release notes.
- **`experimental.runic`.** A change to an existing command, request, result
  or initialization option is a new protocol revision, never a silent change.
  The server offers a new revision only to a client that asks for it with
  `initializationOptions.runicProtocol` and advertises it in
  `experimental.runic.protocol`. Every other client keeps getting revision 2
  for the rest of the major version. Removing revision 2, or renaming or
  removing the capability key, waits for a major release.

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
declared `sourceRoots`. Because it runs the workspace's own tool, open only
repositories you trust (see [Workspace trust](#workspace-trust)). For development against a build of this
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

A request is stale, and answered with `ContentModified` (-32801), only when a
change the client sent while it was pending affects its result:

- The outline, folding ranges, semantic tokens, formatting and `prepareRename`
  depend on the request's document only.
- Hover, completion, the code-action listing, `runic/message` and
  `runic/documentInfo` also depend on the project: `runic.json`, the review
  files, the restore and install state of library catalogs and
  `workspace/didChangeConfiguration`.
- Requests that return edits or locations in other files (`rename`,
  `definition`, `references`, `codeAction/resolve`, `workspace/executeCommand`,
  and a code-action listing with refactor edits) also depend on every file
  below the directories of the catalogs they read, so an edit is never computed
  from outdated text.

A watched-file event for an open document whose file holds the document's text,
as after the document is saved, changes nothing.

The catalog is validated in the background. A burst of changes is validated
once, at its last change, and a newer change cancels a validation that is still
running, so `textDocument/publishDiagnostics` arrives after the changes it
describes, carries the document's latest version, and never follows a
publication of a newer version. Requests do not wait for validation:

- Completion, the outline, folding, semantic tokens, formatting,
  `prepareRename`, `runic/message` and `runic/documentInfo` never need it.
- Hover shows the message's compiled contract and review state from the latest
  finished validation, which can be a few changes old while a newer one runs,
  and none of them before the first validation of the catalog finishes.
- The code-action listing offers the catalog's own quick fixes (RTR0095,
  **Mark … reviewed**) once the validation of the current text has finished; a
  client asks again when the diagnostics change.
- Rename, refactorings, `runic.*` commands and `codeAction/resolve` compute
  their edits from the current text, and compile the catalog when they need to.

| Area | Methods |
| --- | --- |
| Lifecycle | `initialize`, `initialized`, `shutdown`, `exit` |
| Documents | `textDocument/didOpen`, `didChange`, `didClose`, `didSave`; `workspace/didChangeWatchedFiles`, `workspace/didChangeConfiguration` |
| Diagnostics | `textDocument/publishDiagnostics` for the whole catalog while a file of the project is open, with links to the [diagnostics reference](diagnostics.md) when the client supports `codeDescription` |
| Language features | `completion` (trigger characters `$ : # / =`), `hover`, `definition`, `references`, `documentSymbol`, `foldingRange`, `semanticTokens/full`, `formatting`, `rename` with `prepareRename` |
| Code actions | `textDocument/codeAction` with `codeAction/resolve`: `quickfix`, `refactor.extract` and `refactor.inline` |
| Server to client | `client/registerCapability` (file watchers), `window/showMessageRequest` (a confirmation), `workspace/applyEdit` (command edits), `$/cancelRequest` (a cancelled confirmation or edit) |
| Commands | `workspace/executeCommand`: `runic.extractGroup`, `runic.inlineResource`, `runic.markReviewed`, `runic.preview`, `runic.renderPreview`, `runic.renameInput`, `runic.renameSlot`, `runic.renameResource` |
| Runic requests | `runic/documentInfo` (project, catalog, base locale and locale of a file), `runic/message` (the resource at the cursor and its inputs, slots and locales), `runic/libraryCatalogs` and `runic/libraryDocument` (installed library catalogs, for [override sources](composition.md)) |

Edits that create files (Extract Group, Inline Resource) need the client's
`create` resource operation; without it the server offers no Extract or Inline
actions and refuses those commands. A deletion the client cannot apply is
replaced by an edit that empties the file (see [Client compatibility](#client-compatibility)). Edits that change `runic.json` need
`initializationOptions.runicConfigurationSync: true`, by which the client
promises to send its unsaved `runic.json` buffers; without it the server refuses
such refactors rather than overwrite unsaved configuration. A client that does
not open `runic.json` with this server, as most generic clients, leaves it out.

## Protocol revisions

The server speaks revision 2 of the Runic protocol and advertises it in
`capabilities.experimental.runic.protocol`. A client needs no initialization
option for it: `initializationOptions.runicProtocol` is ignored, so a client that
sends none, `1` or a higher number gets revision 2. A later revision will be
negotiated through `runicProtocol` and `experimental.runic`, never changed
silently.


| Area | Revision 2 |
| --- | --- |
| Negotiation | `capabilities.experimental.runic` is `{ "protocol": 2, "commands": [...], "requests": [...] }`, listing the `runic.*` commands and `runic/*` requests; `serverInfo.version` is the tool's version (`0.15.0`) |
| Rename (F2) | `renameProvider.prepareProvider`. `prepareRename` returns the name and its range for a resource or group name, an MF2 local or caller input (without its `$`) and a slot reference (`ref=` of `#link`, `#action` or `#icon`), and `null` elsewhere. `rename` renames inputs and slots in every locale's resource, and updates slot contracts in `runic.json` |
| Rename in a mixed workspace | A resource rename in a workspace that contains application sources (`.cs`, `.ts`, `.svelte` and others) asks with `window/showMessageRequest` whether to rename in resource sources only, since Runic does not update call sites. The server asks whether or not the client advertises `window.showMessage`, because Visual Studio 2026 advertises no `window` capabilities but shows the request. Declining, or answering `null`, returns no edit. A client that answers the request with an error (such as `MethodNotFound`) gets the refusal. A client that never answers leaves the rename waiting until the client cancels it |
| Refactor code actions | `refactor.extract` on a group's name: "Extract group 'shop' to shop/en.rmf2". `refactor.inline` in a resource: one "Inline into ../en.rmf2" per resource of the same locale in an ancestor directory of its source root. A client with `textDocument.codeAction.resolveSupport` gets them without their edit, whatever properties it lists, and resolves them with `codeAction/resolve` against the document version and source revision they were offered for, like quick fixes; they answer `ContentModified` when the document changed since. A client without `resolveSupport` gets the edit in the action. Extract needs the client's `create` resource operation. Inline deletes the inlined file; for a client without `delete` it empties the file instead and says so in the title: "Inline into ../en.rmf2 (leaves en.rmf2 empty)". An empty resource adds nothing to the catalog; delete it when convenient |
| Commands | `runic.renameResource`, `runic.renameInput`, `runic.renameSlot`, `runic.extractGroup` and `runic.inlineResource` apply their edit through `workspace/applyEdit` and return `null`. A client without `workspace.applyEdit` receives the `WorkspaceEdit`. A refused edit fails the command with the client's `failureReason`. Like the code action, `runic.inlineResource` empties the inlined file for a client without `delete` |
| File watchers | With `workspace.didChangeWatchedFiles.dynamicRegistration`, the server registers watchers for `runic.json`, `*.rmf2`, `*.mf2`, `*.review.json` and the restore and install state of library catalogs (`obj/project.assets.json` and the npm, pnpm, Yarn and Bun lock and state files). A client without dynamic registration, or one that refuses it, gets the server's own watcher for the same files in its workspace folders (`rootUri`), with changes collected for 200 ms and handled as one `workspace/didChangeWatchedFiles`; events the client still forwards are handled as well. Source roots outside the workspace folders are not covered by either |
| Errors | `MethodNotFound` (-32601) for an unknown request, `InvalidParams` (-32602) for malformed parameters, `RequestFailed` (-32803) for a request that cannot be carried out |

**Revision 1 was removed in 0.15.** Translations 0.13 spoke only revision 1,
and 0.14 kept it for a client without `runicProtocol: 2`. In revision 1 the
`runic.*` refactoring commands returned their `WorkspaceEdit` for the client to
apply, there was no `prepareRename` and there were no refactor code actions, every
failure was `InvalidParams`, an unknown request answered `null`, and
`serverInfo.version` was `1`. From 0.15 every client gets revision 2:

- Runic DevKit 0.22 and later (VS Code and Visual Studio 2026) always ask for
  revision 2 and are unaffected.
- The Runic extensions of runic-editors 0.21 and earlier expect a refactoring
  command (Rename Input, Rename Slot, Rename Resource, Extract Group, Inline
  Resource from the command palette or menu) to return its edit. Against a 0.15
  tool the server applies the edit itself through `workspace/applyEdit` and the
  command returns `null`, which these extensions report as a failure after the
  edit was made. Update the extension to 0.22 or later.
- Other LSP clients get the standard refactorings, error codes and file watching
  of revision 2 without configuration (see [Other editors](#other-editors)).

## Client compatibility

Revision 2 degrades by what the client advertises, and works around what
Visual Studio 2026 does differently from what it advertises. Measured with
Visual Studio 18.12.0's LSP client:

| Client capability | VS Code | Visual Studio 2026 (18.12) | Revision 2 |
| --- | --- | --- | --- |
| `workspace.applyEdit` | yes | yes | Commands apply their edit; without it they return the `WorkspaceEdit` |
| `workspaceEdit.resourceOperations` | `create`, `rename`, `delete` | `create`, `rename` | Extract needs `create`. Without `delete`, Inline empties the inlined file instead of deleting it |
| `window.showMessage` (`showMessageRequest`) | yes | not advertised, but shown as an InfoBar | Always asked in a mixed workspace; an error answer refuses the rename |
| `textDocument.rename.prepareSupport` | yes | yes | `prepareRename` |
| `codeAction.resolveSupport` | `edit` listed | listed without `edit`, but resolved | Any `resolveSupport` gets unresolved refactor actions; none gets the edit inline |
| `didChangeWatchedFiles.dynamicRegistration` | yes | no | The server registers its watchers, or watches the workspace itself |
| `workspaceEdit.changeAnnotationSupport` | yes | no | Not used |

Visual Studio opens the documents an edit changes and leaves them unsaved, so
after Extract or Inline save the changed files (File > Save All).

## Other editors

Any LSP client can run the server, with no initialization options; the
refactorings work through the editor's own rename and code-action UI. Start the editor (or
the server) from a directory below the tool manifest, since `dotnet tool run`
looks for the manifest from its working directory.

**Neovim** (0.11 or later):

```lua
vim.filetype.add({ extension = { rmf2 = "rmf2", mf2 = "mf2" } })
vim.lsp.config("runic", {
  cmd = { "dotnet", "tool", "run", "runic-translations", "--", "lsp" },
  filetypes = { "rmf2", "mf2" },
  root_markers = { ".config/dotnet-tools.json", "runic.json" },
})
vim.lsp.enable("runic")
```

**Helix** (`languages.toml`):

```toml
[language-server.runic]
command = "dotnet"
args = ["tool", "run", "runic-translations", "--", "lsp"]

[[language]]
name = "rmf2"
scope = "source.rmf2"
file-types = ["rmf2", "mf2"]
roots = [".config/dotnet-tools.json", "runic.json"]
language-servers = ["runic"]
```

**JetBrains IDEs** with [LSP4IJ](https://github.com/redhat-developer/lsp4ij):
add a new language server with the command
`dotnet tool run runic-translations -- lsp`, map the file name patterns `*.rmf2`
and `*.mf2` to it.

**Zed** adds language servers through extensions, and there is no Runic
extension for Zed yet. An extension would declare a language for `rmf2` and `mf2`
files and run the command above.

These clients do not open `runic.json` with the server, so they leave out
`runicConfigurationSync` and the server refuses refactors that would change
`runic.json` (such as renaming a mounted namespace or a slot with a contract).
Save `runic.json` and use the Translations Editor or VS Code for those. Preview
(`runic.preview`, `runic.renderPreview`) is a custom command that needs editor
UI, which only the Runic extensions provide.

### Workspace trust

`dotnet tool run runic-translations -- lsp` runs the tool that the repository's
`.config/dotnet-tools.json` names, in the version it pins, restored from the
package sources the repository's `NuGet.config` may add. Opening a repository
with a client that starts the server therefore runs code the repository chose,
as any project-local language server does (rust-analyzer, OmniSharp).

- Runic DevKit for VS Code starts the server only in a trusted workspace
  (VS Code's workspace trust).
- Runic DevKit for Visual Studio and generic clients (Neovim, Helix, LSP4IJ,
  Zed) have no such gate: they start the server whenever a resource file is
  opened. Open only repositories you trust with them, or start the editor where
  it does not run language servers automatically.

The server itself starts no processes, loads no code from the workspace and
makes no network requests. It reads the workspace's catalogs and the restore
state of library catalogs. It writes files only when a command asks it to:
`runic.markReviewed` writes the review file, and refactorings send their edits
to the client, which applies them.

## Performance

CI measures the server on a catalog of **20,000 messages in 10 locales** (200
resource files of 1,000 messages), once as it is and once with a review file,
on its Linux runner (ubuntu-24.04, 4 processors), and fails when a measurement
exceeds its budget ([tests/lsp-budget](../../../tests/lsp-budget/README.md)).
The budgets are the slowest of six runs on 4 processors times a margin for a
shared runner's variance: 2× for what takes seconds, 3× for hover and
completion while typing and 6× for idle requests, but at least 25 ms for an
idle request and 50 ms while typing.

| Measurement | Budget (4 processors) | Measured, 4 processors | Measured, CI runner |
| --- | ---: | ---: | ---: |
| Open to the first diagnostics | 9.3 s | 4.0–4.6 s | 6.0 s |
| An edit to its diagnostics | 1.4 s | 0.38–0.68 s | 0.61 s |
| The last of five keystrokes to its diagnostics | 242 ms | 37–121 ms | 98 ms |
| Hover after five keystrokes | 50 ms | 3–9 ms | 4 ms |
| Completion after five keystrokes | 50 ms | 7–14 ms | 7 ms |
| Hover and completion, idle | 25–31 ms | 1–5 ms | 1–6 ms |
| Rename of a resource (10 files) | 1.5 s | 0.56–0.75 s | 0.82 s |
| Resident memory after open | 968 MiB | 754–774 MiB | 809 MiB |
| Peak resident memory | 2,086 MiB | 1,308–1,604 MiB | 1,420 MiB |

The 4-processor column is six runs pinned to four processors of a 24-thread
workstation; the CI runner's column is one run of the budget job, both at the
same commit. On 12 processors, open takes about 3.7 s, an edit about 0.45 s and
a rename about 0.65 s (#271).

- **Scaling.** Smaller catalogs are faster: at 2,000 messages × 10 locales an
  edit's diagnostics take about 0.1 s and open under 1 s.
- **Review files.** A catalog with a review file (`<catalog>.review.json`)
  computes the review state with every validation. At 20,000 × 10 on 4
  processors, open takes 4.3–8.7 s and an edit's diagnostics 0.4–0.7 s; hover,
  completion, rename and memory are as without one. Its budgets are in
  `tests/lsp-budget/budgets.json` under `review`.

## Tests

The protocol is covered by the `Rmf2*LspTests` suites in
`tests/dotnet/Runic.Translations.Build.Tests` (see
[RMF2 validation](rmf2-validation.md#language-service-checks)). CI's
*Language server / generic client* job (`bun eng/run.mjs lsp-smoke`) packs the
tool, installs it as a local tool in a new project and drives it with a minimal
stdio LSP client (`tests/lsp-generic-client/client.mjs`) through the command
above, without initialization options: `initialize`, diagnostics, hover,
completion, `prepareRename` and rename, a quick fix, and shutdown. runic-editors
runs its extension-host and native Visual Studio tests against the released tool
from NuGet.
