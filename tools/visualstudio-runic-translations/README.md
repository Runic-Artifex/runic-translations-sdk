# Runic RMF2 Translations for Visual Studio

Development preview for Visual Studio 2022 17.14, Windows x64. The MEF client
registers `.rmf2` with Visual Studio's remote-code content type and uses the same
stdio language server as VS Code. TextMate highlighting, diagnostics, completion,
hover, symbols, definition/references, formatting and bounded F2 resource/local
rename use the shared compiler and language service.

Restore the solution's local `runic-translations` .NET tool before opening an
RMF2 document. Activation finds the nearest `.config/dotnet-tools.json` from the
active document (or solution), respecting `isRoot`, and starts
`dotnet tool run runic-translations -- lsp`. The installed tool requires its
matching .NET runtime; it runs outside Visual Studio's .NET Framework process.
No tool download or restore occurs during activation. Use trusted projects.

For development, set `RUNIC_TRANSLATIONS_SERVER` to an absolute built tool DLL
before starting Visual Studio. `RUNIC_DOTNET` optionally selects the installed
.NET executable. Initialization failures appear in an IDE notification; server
stderr is drained to diagnostic trace output. One server serves the current IDE
session. Restart it after switching solutions or changing the selected tool.

Within an RMF2 editor:

- **Ctrl+Alt+P** opens the native message preview. Select a locale, edit example
  values and choose **Render preview**. The server executes its verified runtime
  plan. Links and actions are inert, icons use slot labels, and custom components
  use labeled spans. The extension never loads application renderers.
- **Ctrl+Alt+R** stops and restarts the server.
- Standard IDE navigation, Find All References, Format Document and Rename use
  Visual Studio's LSP support. F2 refuses a resource rename when application or
  legacy files make whole-workspace correctness unsupported. MF2 local rename
  stays within its lexical scope. Explicit resource-only input/slot/structural
  transactions are available in the CLI, Editor and VS Code; this client does
  not implement a second workspace-edit engine for those commands.

`runic.json` retains Visual Studio's JSON language service. Save configuration
changes before invoking preview. Configuration-changing refactors are refused
because this client cannot safely include unsaved JSON buffers; resource buffers remain synchronized
through LSP. C#, TypeScript, Svelte, TOML and legacy MF2 retain their existing
language services. Runic does not silently rename their application call sites.

## Build and package

From the SDK development shell:

```sh
dotnet build tools/dotnet-runic-translations
dotnet build tools/visualstudio-runic-translations
python3 tools/visualstudio-runic-translations/package.py
```

The VS host dependency set is pinned separately from the SDK's modern runtime
packages: in-process dependencies must match Visual Studio 17.14. The VSIX
contains only the extension assembly, grammar and metadata; VS-owned DLLs are
resolved from the host. `packages.lock.json` records the graph. Packaging checks
asset paths, grammar, managed payload and archive integrity without Windows-only
build tasks. Output: `artifacts/runic-translations-visualstudio.vsix`.

## Windows integration check (pending execution)

Cross-compilation succeeds on Linux; it does not establish MEF activation or
native Visual Studio UI behavior. On a Windows development machine with the
Visual Studio extension workload, use an experimental instance:

1. Build both projects, package, and install the VSIX into the experimental hive
   with `VSIXInstaller.exe /rootSuffix:Exp artifacts/runic-translations-visualstudio.vsix`.
2. Set `RUNIC_TRANSLATIONS_SERVER` to the tool DLL. Open a temporary copy of
   `specs/translations/examples/rmf2` with `devenv.exe /RootSuffix Exp /Log`.
3. Open `en.rmf2`; check highlighting, completion, a syntax-error diagnostic,
   definition navigation to both locales, and recovery after undo.
4. Put the cursor inside `payment`, press Ctrl+Alt+P and render count `1`, tone
   `positive`, then change locale. Verify styled text, disabled action, labeled
   icon/custom component, editable samples and invalid-number error recovery.
5. Close the preview, press Ctrl+Alt+R, edit an unsaved message and verify
   diagnostics/preview reflect the current buffer. Close the IDE and confirm its
   Runic server process exits. Add `App.cs` and verify F2 refuses a resource rename
   without changing either file.

This is a known platform verification limitation, not a claim of a passing
Windows host test. No marketplace publication is performed.

Implementation follows Microsoft's [LSP extension guide](https://learn.microsoft.com/en-us/visualstudio/extensibility/adding-an-lsp-extension?view=vs-2022)
and [VSIX schema](https://learn.microsoft.com/en-us/visualstudio/extensibility/vsix-extension-schema-2-0-reference?view=vs-2022).
