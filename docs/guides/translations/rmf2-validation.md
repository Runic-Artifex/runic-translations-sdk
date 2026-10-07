# RMF2 validation and measurements

The maintained payment fixture is `specs/translations/examples/rmf2`. Compiler,
external-pack and generated .NET/ESM checks exercise its contracts, conditional
slots and locale variants. Projects use the bounded typed
`rmf2-execution-v2` path. It does not promise every Unicode function or draft
feature merely because its syntax is accepted.

The version-explicit [`rmf2-v1` corpus](../../../specs/translations/corpus/rmf2-v1/README.md)
is the common release oracle for the supported v5 contract. It supplies the same
typed execution and rejection expectations to the compiler, generated C#,
.NET artifact-v5 loader, generated ESM, and ESM dynamic-pack loader. The payment
fixture remains the maintained end-to-end example; neither fixture turns the
bounded profile into full Unicode MF2 conformance.

## Focused checks

Run these in the locked project development shell:

```sh
dotnet run --project tests/dotnet/Runic.Translations.Compiler.Tests
dotnet run --project tests/dotnet/Runic.Translations.Authoring.Tests
dotnet run --project tests/dotnet/Runic.Translations.Build.Tests -- rmf2
dotnet run --project apps/translations-editor -- --smoke-test
cd packages/web/translations-svelte
bun run check
bun run test
bun run test:inline-browser
```

The browser check uses the development shell's Chromium through
`PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH`. It renders a full SSR document, requires
hydration without recovery, checks existing link/button identity, custom badge
options, meaningful icon naming, text escaping and callback teardown. It does
not substitute a DOM emulator for browser parsing of escaped text and hydration
markers.

On Windows, run the maintained WPF payment consumer:

```sh
dotnet run --project tests/dotnet/Runic.Translations.Wpf.Tests
```

The native test passed on Windows 11 build 26200 with .NET SDK 10.0.400
on 2026-09-11, in the signed-in desktop session through the maintained Windows
UI Automation runner. It verifies native controls, custom badge options,
meaningful icon accessibility and stale callback deactivation after replacing
content. Linux cross-compilation also passes.

## IDE and language-service checks

VS Code's isolated extension-host check passes activation, native symbol and
definition providers, example/runtime preview, malformed-input recovery,
versioned resource/configuration rename, unsaved restart and refusal of partial
application renames. The shared LSP integration suite covers all three position
encodings, cancellation, stale requests and new unsaved resources. The editor's
shared preview-model extraction passes Svelte type checking.

```sh
dotnet build tools/dotnet-runic-translations
cd tools/vscode-runic-translations
bun run check
bun run test
bun run build
bun run test:host
bun run package
```

The Visual Studio client supports Visual Studio 2026 (18.x) only. Its VSIX
declares `[18.0,19.0)` for both the installation target and the core-editor
prerequisite. On 2026-10-07 the maintainer narrowed the range from
`[17.14,19.0)`, so Visual Studio 2022 17.14 is no longer a declared host.
The client still builds against its pinned 17.14 SDK, an API surface that
18.x hosts provide, with zero warnings or errors on Linux and with Visual
Studio's native MSBuild on Windows. The official VSSDK packaging targets
generate the installer metadata. The archive verifier checks the declared
assets and range, the TextMate grammar, and the exclusion of host DLLs.
Installation and native host checks passed on Windows 11 build 26200 with
Visual Studio Community 2026 18.8.2 on 2026-09-11, in an isolated `RunicRmf2`
profile that used the default-profile fixture of that time. The maintained
[native interaction test](../../../tools/visualstudio-runic-translations/test/native-host.ps1)
checks:

- registered commands;
- inert rich content;
- invalid-number recovery;
- locale selection;
- unsaved text after a server restart.

The focused RMF2 CLI/LSP suite includes v5 preview and
resource-only refactor coverage, plus mounted multi-project discovery and
diagnostic isolation. See the [Windows integration procedure](../../../tools/visualstudio-runic-translations/README.md)
for build, installation and interactive-session execution.

### Visual Studio 2026 rerun (2026-10-07)

The execution-v2 journey was rerun on Windows 11 Pro build 26200 (the
snapshot-restored `bootstrap-imgui-win11` VM) with Visual Studio Community 2026
18.8.2. The setup was:

- the VSIX from CI run 37644616400 on branch `ide/w200-001-vs2026-range`
  (`53600500`): version `0.6.0.2`, `[18.0,19.0)`, SHA-256
  `490774da289e89aba902282c20d9ab330782747e280369b1eea6c033a43a439d`;
- the language server built on the VM from the same commit with .NET SDK
  10.0.401;
- `RUNIC_TRANSLATIONS_SERVER`, plus `/RootSuffix RunicRmf2`.

Results:

- **Passed:** the semantic project selection, the registered preview command,
  the native inert rich content (`[shop:badge] Ready` with the inert Retry
  action), invalid-number recovery, the German locale and the registered
  restart command. The restart started a new server process.
- **Failed:** the last check, *unsaved buffer after restart*. After the script
  replaced the buffer with `plain = UNSAVED preview marker`, the `plain`
  preview still rendered the saved `Payment details`. This was reproduced in
  two complete runs. A diagnostic copy that waited 20 seconds after the restart
  failed the same way, and so did a copy that skipped the restart. The preview
  therefore did not use the unsaved buffer in this host at all; the restart is
  not the cause. This has to be fixed and rerun before native interaction
  evidence for Visual Studio 2026 is complete.
- During the buffer replacement, Visual Studio's LSP client also logged a
  non-fatal `ArgumentException` in `StructureTagger.GetTags`, raised by
  `NormalizedSnapshotSpanCollection.IntersectsWith`.

Reinstalling the VSIX into the existing `RunicRmf2` profile from an SSH session
left the new MEF part out of the component catalog: preview commands reported
that no `RunicLanguageClient` export was found. Running
`devenv /RootSuffix RunicRmf2 /updateconfiguration` in that disposable profile
fixed it. Note also that `devenv /Log <path>` takes the next argument as the
log file, so pass a document before `/Log`.

## Representative measurements

Reproduce the bounded, warm-loop measurements with:

```sh
dotnet run -c Release --project tests/dotnet/Runic.Translations.Compiler.Tests -- --rmf2-benchmark
```

On this Linux development machine, 2026-09-11, .NET runtime 10.0.11 and Bun 1.4.2:

| Operation                            | Mean µs/op | .NET allocated bytes/op |
| ------------------------------------ | ---------: | ----------------------: |
| Plain .NET message                   |       0.51 |         approximately 0 |
| Rich .NET content                    |       2.14 |                   2,080 |
| Linked .NET rich rendering           |       2.78 |                   3,576 |
| Parse a resource with 1,000 messages |      3,725 |               3,937,249 |
| Plain ESM message                    |       0.34 |            not measured |
| Rich ESM content                     |       2.63 |            not measured |
| Linked ESM rich rendering            |       2.54 |            not measured |

These are observations, not latency guarantees or a cross-runtime ranking.
Formatting/rendering uses 10,000 iterations; parsing uses 30. Registry linking
and pack verification occur outside the timed loops. The plain-message path
returns a string; rich paths construct semantic content and adapter nodes.
Native layout, browser layout, GC pauses and end-to-end language-server transport
are outside these measurements. The syntax cache has a separately tested bounded
capacity and reuses only byte-identical resource snapshots.

The transport-level LSP measurement is opt-in and has its workload and generous
pass/fail limits checked into
`tests/benchmarks/translations/rmf2-lsp/baseline-v2.json`:

```sh
dotnet run -c Release --project tests/dotnet/Runic.Translations.Build.Tests -- --rmf2-lsp-benchmark
```

It measures a v5 2,000-message catalog and a ranged
incremental edit through the real child-process stdio entry point (three samples,
reporting medians). Queued cancellation uses the same framed protocol over
in-process streams with internal worker and cancellation-observed hooks, so the
reader has cancelled the queued target before the worker resumes. The production
stdio entry point supplies neither hook and exposes no test barrier protocol
method.
CI runs the same command as a deterministic, workload-specific regression guard
with generous upper bounds; those bounds are not a latency promise. Host-only
Visual Studio UI/layout time is outside the measurement.

## Interchange boundary

RMF2 source is the lossless representation for markup, declarations, variants
and attached metadata. The existing closed XLIFF 2.1 text profile reports
structured messages as semantic losses. It does not claim lossless interchange
for arbitrary rich MF2. Review its loss report before using a text-profile export
for translation. The import side refuses those structured units rather than
flattening them; an approved review stamp uses the closed-text-profile
fingerprint, while source freshness remains a separate conflict check.

The editor smoke fixture covers mounted
resource mutations, locale/fallback transactions, save/reload, deterministic
plain XLIFF export/import, and the rule that an import never converts an RMF2
project into direct `.mf2` files. Focused interchange tests cover approved-review fingerprints and
structured loss/refusal. Structured preview tests route AST 5 through the
verified .NET runtime with inert link, action, and custom-markup runs.
