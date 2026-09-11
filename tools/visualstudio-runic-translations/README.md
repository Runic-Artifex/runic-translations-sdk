# Runic RMF2 Translations for Visual Studio

Development preview targeting Visual Studio 2022 17.14 and compatible newer
hosts, Windows x64. Native checks use Visual Studio 2026 18.8.2. The MEF client
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

Commands appear in the Tools menu. Assign preferred shortcuts in Visual Studio’s
keyboard options; the extension does not override existing IDE bindings.

Within an RMF2 editor:

- **Tools → Runic: Preview Message** opens the native message preview. Select a locale, edit example
  values and choose **Render preview**. The server executes its verified runtime
  plan. Links and actions are inert, icons use slot labels, and custom components
  use labeled spans. The extension never loads application renderers.
- **Tools → Runic: Restart Language Server** stops and restarts the server.
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

Cross-compilation uses the SDK development shell:

```sh
dotnet build tools/dotnet-runic-translations
dotnet build tools/visualstudio-runic-translations
```

Create the installable VSIX with Visual Studio's full-framework MSBuild on
Windows (for example, from its Developer PowerShell):

```powershell
MSBuild tools/visualstudio-runic-translations/Runic.Translations.VisualStudio.csproj /restore /m:1
python tools/visualstudio-runic-translations/package.py
```

The pinned `Microsoft.VSSDK.BuildTools` package supplies the packaging targets;
no global extension-development workload is required. Packaging includes the
installer's generated `manifest.json` and `catalog.json`, the registered command
package, TextMate registration and grammar. The verifier rejects missing assets
and redistributed host-owned DLLs. Output:
`tools/visualstudio-runic-translations/artifacts/runic-translations-visualstudio.vsix`.
A native-built archive copied to Linux can be verified with `package.py --input
/path/to/native.vsix`. Cross-compilation alone does not produce an installable
archive.

The VS host dependency set is pinned separately from the SDK's modern runtime
packages: in-process dependencies must match Visual Studio 17.14.
`packages.lock.json` records the graph. The VSIX contains only the extension's
own managed assembly; VS-owned DLLs resolve from the host.

## Windows integration check

Use a dedicated experimental instance; the maintained test expects
`/RootSuffix RunicRmf2` and a signed-in interactive Windows desktop:

1. Build the language server and VSIX, then install with
   `VSIXInstaller.exe /rootSuffix:RunicRmf2 /quiet <native-built.vsix>`.
2. Set `RUNIC_TRANSLATIONS_SERVER` to the absolute tool DLL before starting
   `devenv.exe /RootSuffix RunicRmf2 /Log`. Open an RMF2 document to activate LSP.
3. Run the native interaction check from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/native/Runic.Desktop.WebViewSmoke/run-windows-ui-automation.ps1 `
  -Executable "C:/Program Files/Microsoft Visual Studio/18/Community/Common7/IDE/devenv.exe" `
  -AutomationScript tools/visualstudio-runic-translations/test/native-host.ps1 `
  -ReceiptPath artifacts/rmf2-vs-host.json
```

The runner schedules the test in the signed-in desktop session and removes its
scheduled task afterward. The test creates and removes its own temporary payment
fixture, exercises registered commands and native preview controls, and checks
invalid-number recovery, locale switching and unsaved text after server restart.
The shared LSP integration suite separately covers diagnostics, navigation,
versioned rename, cancellation and refusal of partial application renames.

When repeatedly reinstalling the same development version, Visual Studio can
retain stale extension/component metadata. Close the experimental instance and
refresh only that disposable profile's extension metadata and component cache,
or use a fresh experimental profile. Do not clear a user's normal IDE profile.
No marketplace publication is performed.

Implementation follows Microsoft's [LSP extension guide](https://learn.microsoft.com/en-us/visualstudio/extensibility/adding-an-lsp-extension?view=vs-2022)
and [VSIX schema](https://learn.microsoft.com/en-us/visualstudio/extensibility/vsix-extension-schema-2-0-reference?view=vs-2022).
