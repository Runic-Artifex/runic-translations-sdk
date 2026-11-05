# Runic MF2 Translations for VS Code

**Preview.** This extension is a preview product until the production language
server (2.0): its commands and settings, and the `lsp` protocol it uses, are
outside the 1.0 compatibility rules and may change in a minor release. Every
release carries the Marketplace "Preview" label.

This extension uses the shared Runic language server for direct
`.mf2` and grouped `.rmf2` resources. It supplies highlighting, diagnostics, symbols, folding, completion,
hover, definition/reference navigation, formatting and versioned resource edits.
It synchronizes open `runic.json` buffers so mounted/slot configuration changes
participate in refactors without requiring an intermediate save.

## Install the tool

The extension runs the workspace's own `runic-translations` .NET tool, so the
language server always matches the project's Translations release. It does not
install or restore tools for you. In the repository (or solution) root:

```sh
dotnet new tool-manifest --output .config
dotnet tool install dotnet-runic-translations --version <VERSION>
dotnet tool restore
```

Use the same exact version as the project's Runic Translations packages, and
commit `.config/dotnet-tools.json`; collaborators then run only
`dotnet tool restore`. The tool needs the .NET 10 runtime. Projects created with
the `runic-translations` templates already contain the manifest.

## Starting and status

Open a trusted workspace containing a restored local `runic-translations` .NET
tool. The extension starts `dotnet tool run runic-translations -- lsp` from the
nearest tool-manifest directory.
If the server cannot start (no tool manifest, an unrestored tool, a missing
.NET runtime), a notification says why once per workspace folder, with
**Show output** and **How to install the tool**; **Runic: Restart Language
Server** tries again. The status bar shows the server state for the active
`.mf2`, `.rmf2` or `runic.json` file and, while the server runs, the catalog ID
and the file's locale (`Runic: app · de`). Select it, or run **Runic: Show
Output**, for the server log.

For SDK development, set `runicTranslations.serverAssembly` to the built tool DLL;
`runicTranslations.dotnetPath` selects the installed .NET executable. Separate
workspace folders get separate server instances. Restarting reopens unsaved
buffers; stopping releases watchers and terminates the language-server client.
For grouped projects, declared `sourceRoots` inside the opened workspace are
covered by one canonical recursive watch plan, so mounted add/change/delete/rename
events outside the config directory are reconciled without duplicate nested
watchers. Open a common containing workspace for such mounts; roots outside that
boundary and symlink/reparse-point escapes are not watched.

## Diagnostics, hover and `runic.json`

Diagnostics cover the whole catalog, not only open files: while any file of a
project is open, catalog errors and warnings in its closed translations and in
`runic.json` appear in the Problems view, and they are cleared when the last
file of the project closes. Each `RTR` code links to its entry in the
[diagnostics reference](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/diagnostics.md)
at the release tag of the tool. Hover over a message in a translation shows the
base-locale text with its comments and `@param` descriptions, and its review
state from the project's review file (`<catalog>.review.json` beside
`runic.json`, the file `runic-translations review mark` and the Translations
Editor write), marked stale when the source changed since it was recorded.

A stale review entry is reported as `RTR0090`; its quick fix **Mark … reviewed**
records the entry's state again for the source on disk in the review file. It is
offered, and runs, only while the files with the base message and the translation
have no unsaved changes. Review warnings, hover and quick fixes also work while
the catalog has errors elsewhere, such as a locale that misses keys.
`RTR0095` (plural categories that fall to `*`) has a quick fix that adds the
missing variants as copies of the `*` variant.

`runic.json` files are validated and completed against the project schema
bundled with the extension, copied from the specification on every build. A
file that declares `$schema` uses that schema instead (VS Code downloads the
published URL); templates write the published `project-v1.schema.json` URI.

## Commands and preview

**Export XLIFF** writes one XLIFF 2.1 document per translated locale into a
folder you choose, and **Import XLIFF (dry run)** reports what importing a
translated document would do, unit by unit, without writing anything (the
output goes to **Runic: Show Output**). Both use the project of the active file (the
nearest `runic.json` above it), or ask which project when no file of one is
active and the workspace has several. Both run the workspace's local
`runic-translations xliff` tool, the same code as the Translations Editor; apply
an import with `runic-translations xliff import` or the editor.

The Runic commands in the Command Palette provide inert example previews,
group extraction/inlining for `.rmf2`, and explicitly source-only
input/slot/resource renames. Preview asks the server to execute the selected
semantic contract's verified .NET runtime plan and renders the returned inert runs. Links/actions are
inert, icons are placeholders and custom markup is labeled; no application
callbacks or application startup are involved. Custom renderer code is never
loaded by the extension.

**Preview Message** offers every `@example` sample set plus custom values. The
selected locale and values are remembered per message during the extension
session; reopening a preview reuses them. **Change Preview Samples** lets you
choose another example or edit custom values. **Change Preview Locale** switches
the open panel's language; it also lists the pseudo-locales that `runic.json`
configures (`pseudoLocales`: `en-XA`, `ar-XB`), which the server generates from
the base locale exactly as a `--pseudo-locales` build does (read-only).
**Change Preview Regional Locale** sets the locale a user requests, such as
`de-CH` for `de` text: numbers and dates then format as the runtime formats a
`de-CH` request that resolved to `de`. **Change Preview Time Zone** sets the zone
that `timeZone=local`, the default of `:datetime` styles, formats in (`utc`, an
IANA zone, or the host's). The panel names the requested locale, how it relates
to the shown locale (exact, parent or related), the locale numbers and dates
formatted in and the time zone, and lays the text out in the locale's direction.
The regional locale and time zone stay for the next message. Samples for the
128 most recently previewed message/locale combinations are retained. The panel refreshes from
the latest unsaved resource/configuration buffers and watched source changes.
Out-of-order requests and cancelled selections cannot replace a newer preview.

The LSP owns direct and grouped MF2 plus configuration synchronization. Existing C#, TypeScript,
Svelte language services retain their files. Resource-source edits are not
application-call-site refactors. Standard resource F2 refuses workspaces
containing application or other non-resource sources or unindexable links; the
explicit **Rename Resource in Resource Sources** command intentionally applies
the narrower transaction. Use the native language service
for generated API usages and review source-only input/slot operations against
application bindings. Unsupported language operations must not be treated as a
successful whole-application rename.

## VS Code and Visual Studio

Both extensions use the same language server. Visual Studio keeps a smaller
command set until the production language server (2.0).

| Feature | VS Code | Visual Studio |
| --- | --- | --- |
| Highlighting, completion, symbols, folding, formatting | Yes | Yes |
| Diagnostics for every project file | Yes | Yes |
| Diagnostic documentation links | Yes | Where the IDE shows LSP code descriptions |
| Hover with base text and description | Yes | Yes |
| Definition, references, quick fixes | Yes | Yes |
| F2 rename of resources and MF2 locals | Yes | Yes |
| Unsaved `runic.json` in refactors | Yes | Yes |
| `runic.json` schema validation | Bundled schema, or `$schema` | `$schema` (published schema) |
| Preview Message | Command | Tools menu |
| Change Preview Samples | Command | Sample selector in the preview window |
| Change Preview Locale | Command | Locale selector in the preview window |
| Pseudo-locales (`pseudoLocales`) in the preview | Change Preview Locale | Locale selector in the preview window |
| Change Preview Regional Locale | Command | Regional locale field in the preview window |
| Change Preview Time Zone | Command | Time zone field in the preview window |
| Rename Input in Resource Sources | Command | No (2.0) |
| Rename Slot in Resource Sources | Command | No (2.0) |
| Rename Resource in Resource Sources | Command | No (2.0) |
| Extract Group to Directory | Command | No (2.0) |
| Inline Resource into Ancestor | Command | No (2.0) |
| Export XLIFF | Command | No (2.0) |
| Import XLIFF (dry run) | Command | No (2.0) |
| Review state in hover | Yes | Yes |
| Mark reviewed quick fix (`RTR0090`) | Yes | Where the IDE runs code-action commands |
| Restart Language Server | Command | Tools menu |
| Show Output | Command and status bar | Server stderr in the diagnostic trace |
| Server state, catalog and locale | Status bar | No |
| Start failure | One notification per folder, with install help | IDE notification on initialization failure |

## Build and test

Build and test inside the SDK development shell:

```sh
dotnet build tools/dotnet-runic-translations
cd tools/vscode-runic-translations
bun install --frozen-lockfile
bun run check
bun run test
bun run build
bun run test:host
bun run package
```

The host check uses the installed `code`, or `VSCODE_EXECUTABLE_PATH`, with an
isolated profile, extension directory and fixture. It requires an assertion
report, not merely a zero CLI exit code. Failure logs are retained under
`artifacts/host-failure`. Packaging produces `artifacts/runic-translations.vsix`;
no marketplace upload or user-profile installation is part of these commands.

The `version` in `package.json` is a placeholder. `bun run package` (`package.mjs`)
stamps the release version of `eng/workspace.json`, mapped to `x.y.z`, into a
staged copy and packs that: the plain `0.8.0` becomes `0.8.1000`, `1.0.0-rc.N`
becomes `1.0.(500 + N)` packaged with `--pre-release`, and the final `1.0.0`
becomes `1.0.1000`. It fails if the VSIX does not carry that version, the
pre-release flag of a `-preview.N`/`-rc.N` release, and the Preview label. See
[IDE extension versions](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/eng/release/README.md#ide-extension-versions) for
the full mapping. Each GitHub release attaches the attested VSIX that CI built.

The language client follows Microsoft's [Language Server Extension Guide](https://code.visualstudio.com/api/language-extensions/language-server-extension-guide).
