# Runic MF2 Translations for VS Code

This development extension uses the shared Runic language server for direct
`.mf2` and grouped `.rmf2` resources. It supplies highlighting, diagnostics, symbols, folding, completion,
hover, definition/reference navigation, formatting and versioned resource edits.
It synchronizes open `runic.json` buffers so mounted/slot configuration changes
participate in refactors without requiring an intermediate save.

Open a trusted workspace containing a restored local `runic-translations` .NET
tool. The extension starts `dotnet tool run runic-translations -- lsp` from the
nearest tool-manifest directory. It does not install or restore tools for you.
For SDK development, set `runicTranslations.serverAssembly` to the built tool DLL;
`runicTranslations.dotnetPath` selects the installed .NET executable. Separate
workspace folders get separate server instances. Restarting reopens unsaved
buffers; stopping releases watchers and terminates the language-server client.
For grouped projects, declared `sourceRoots` inside the opened workspace are
covered by one canonical recursive watch plan, so mounted add/change/delete/rename
events outside the config directory are reconciled without duplicate nested
watchers. Open a common containing workspace for such mounts; roots outside that
boundary and symlink/reparse-point escapes are not watched.

The Runic commands in the Command Palette provide inert example previews,
group extraction/inlining for `.rmf2`, and explicitly source-only
input/slot/resource renames. Preview asks the server to execute the selected
semantic contract's verified .NET runtime plan and renders the returned inert runs. Links/actions are
inert, icons are placeholders and custom markup is labeled; no application
callbacks or application startup are involved. Custom renderer code is never
loaded by the extension.

The LSP owns direct and grouped MF2 plus configuration synchronization. Existing C#, TypeScript,
Svelte language services retain their files. Resource-source edits are not
application-call-site refactors. Standard resource F2 refuses workspaces
containing application or other non-resource sources or unindexable links; the
explicit **Rename Resource in Resource Sources** command intentionally applies
the narrower transaction. Use the native language service
for generated API usages and review source-only input/slot operations against
application bindings. Unsupported language operations must not be treated as a
successful whole-application rename.

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

The language client follows Microsoft's [Language Server Extension Guide](https://code.visualstudio.com/api/language-extensions/language-server-extension-guide).
