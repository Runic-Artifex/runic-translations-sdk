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

Its Linux cross-compilation passes. WPF control execution and automation-peer
assertions have not been run on Linux; they require Windows. The test verifies
native controls, custom badge options, meaningful icon accessibility and stale
callback deactivation after replacing content. No Windows runtime result is
implied by a successful cross-build.

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

## Interchange boundary

RMF2 source is the lossless representation for markup, declarations, variants
and attached metadata. The existing closed XLIFF 2.1 text profile reports
structured messages as semantic losses. It does not claim lossless interchange
for arbitrary rich MF2, as scoped by W200-008. Review its loss report before
using a text-profile export for translation.
