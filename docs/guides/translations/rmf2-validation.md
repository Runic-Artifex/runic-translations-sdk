# RMF2 validation and measurements

The maintained payment fixture is `specs/translations/examples/rmf2`. Compiler,
external-pack and generated .NET/ESM checks exercise its contracts, conditional
slots and locale variants. RMF2 execution remains the bounded
`rmf2-execution-v1` profile; accepting syntax does not promise execution of every
Unicode function or draft feature.

## Focused checks

Run these in the locked project development shell:

```sh
dotnet run --project tests/dotnet/Runic.Translations.Compiler.Tests
dotnet run --project tests/dotnet/Runic.Translations.Authoring.Tests
dotnet run --project tests/dotnet/Runic.Translations.Build.Tests -- rmf2
dotnet run --project apps/translations-editor -- --smoke-test
cd packages/web/svelte
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

The Visual Studio client builds against its pinned 17.14 SDK with zero
warnings/errors on Linux and with Visual Studio's native MSBuild on Windows.
The official VSSDK packaging targets generate installer metadata; the archive
verifier checks declared assets, TextMate grammar and exclusion of host DLLs.
Installation and native host checks passed on Windows 11 build 26200,
Visual Studio Community 2026 18.8.2, on 2026-09-11 in an isolated `RunicRmf2`
profile. The maintained [native interaction test](../../../tools/visualstudio-runic-translations/test/native-host.ps1)
checks registered commands, inert rich content, invalid-number recovery, locale
selection and unsaved text after server restart. The VSIX declares
`[17.14,19.0)` because the client targets the 17.14 API and the tested host is
in the 18.x line. This range declaration is not a claim that every host is
validated: native evidence currently covers only Visual Studio Community 2026
18.8.2; the Visual Studio 2022 17.14 host remains platform-only validation.

The focused RMF2 CLI/LSP suite passes 5/5 on Windows and Linux, including plain
and rich German runtime previews. The runtime suite passes 175/175, including a
zero-input compiled variant regression. See the [Windows integration procedure](../../../tools/visualstudio-runic-translations/README.md)
for build, installation and interactive-session execution.

## Representative measurements

Reproduce the bounded, warm-loop measurements with:

```sh
dotnet run -c Release --project tests/dotnet/Runic.Translations.Compiler.Tests -- --rmf2-benchmark
```

On this Linux development machine, 2026-09-11, .NET runtime 10.0.11 and Bun 1.4.2:

| Operation | Mean µs/op | .NET allocated bytes/op |
| --- | ---: | ---: |
| Plain .NET message | 0.51 | approximately 0 |
| Rich .NET content | 2.14 | 2,080 |
| Linked .NET rich rendering | 2.78 | 3,576 |
| Parse a resource with 1,000 messages | 3,725 | 3,937,249 |
| Plain ESM message | 0.34 | not measured |
| Rich ESM content | 2.63 | not measured |
| Linked ESM rich rendering | 2.54 | not measured |

These are observations, not latency guarantees or a cross-runtime ranking.
Formatting/rendering uses 10,000 iterations; parsing uses 30. Registry linking
and pack verification occur outside the timed loops. The plain-message path
returns a string; rich paths construct semantic content and adapter nodes.
Native layout, browser layout, GC pauses and end-to-end language-server transport
are outside these measurements. The syntax cache has a separately tested bounded
capacity and reuses only byte-identical resource snapshots.

The transport-level LSP measurement is opt-in and has its workload and generous
pass/fail limits checked into
`tests/benchmarks/translations/rmf2-lsp/baseline-v1.json`:

```sh
dotnet run -c Release --project tests/dotnet/Runic.Translations.Build.Tests -- --rmf2-lsp-benchmark
```

It measures a 2,000-message catalog, a ranged incremental edit and queued
request cancellation over stdio (three samples, reporting medians). The
benchmark enables a test-only worker barrier before the cancellation workload,
so the cancellation request is observed before the queued target can execute.
CI runs the same command as a deterministic, workload-specific regression guard
with generous upper bounds; those bounds are not a latency promise. Host-only
Visual Studio UI/layout time is outside the measurement.

## Interchange boundary

RMF2 source is the lossless representation for markup, declarations, variants
and attached metadata. The existing closed XLIFF 2.1 text profile reports
structured messages as semantic losses. It does not claim lossless interchange
for arbitrary rich MF2. Review its loss report before
using a text-profile export for translation.
