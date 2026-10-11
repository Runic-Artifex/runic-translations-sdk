# Contributing

Use the locked development environment through `direnv exec . <command>` or `nix develop . -c <command>`. Run focused checks while changing a component; GitHub runs the complete cross-platform matrix. The editor intentionally uses published Runic Application, Platform, and Command Line packages, while Translations components remain project references in this repository.

## Checks

Pull requests must pass the `verify` job in [CI](.github/workflows/ci.yml), which requires every other CI job to succeed. Run the matching command locally before pushing:

| CI job | Local command |
| --- | --- |
| Source checks | `bun eng/generate-cldr.mjs --check`, `bun eng/render-capabilities.mjs --check`, and `actionlint` (available in the runic-sdk development shell) |
| Build and test | `bun run test` (heavy; prefer the focused commands in the README while iterating) |
| Editor / hosted RMF2 E2E | `bun run build`, `dotnet apps/translations-editor/bin/Release/net10.0/Runic.Translations.Editor.dll validate apps/translations-editor/ExampleWorkspace`, then the commands in [the hosted E2E README](apps/translations-editor/tests/HostedBrowserE2E/README.md) |
| Editor / packed Translations packages | `bun run verify-editor-packed` |
| IDE / VS Code extension package | `bun install --frozen-lockfile && bun run --bun check && bun run --bun test && bun run package` in `tools/vscode-runic-translations` |
| WPF / Windows | `dotnet run --project tests/dotnet/Runic.Translations.Wpf.Tests --configuration Release`, then `dotnet run --project tests/dotnet/Runic.Translations.Build.Tests --configuration Release -- --xaml-wpf` |
| IDE / Windows VSIX package | Windows only; see [the Visual Studio extension README](tools/visualstudio-runic-translations/README.md) |

When a `--check` command reports a stale file, run the same script without `--check` and commit the result. `bun eng/generate-cldr.mjs` also rewrites `specs/translations/capabilities-v1.json`, so run `bun eng/render-capabilities.mjs` after it.

`bun run verify-editor-packed` packs the Translations packages under a local-only version (`<version>.editor-packed`), builds the editor against them with `-p:RunicEditorUsePackedTranslations=true`, and checks that NuGet resolved them from `artifacts/packages/nuget`. It removes that version from the NuGet package cache afterwards and leaves the editor restored in packed mode; the next `bun run build` restores project references again. CI also runs the hosted E2E against this packed build.

Packed mode is driven only by this script: it installs `dotnet-runic-translations` from the local feed into `artifacts/editor-packed/tool` and passes it as `TranslationsToolCommand`, so there is no `.config/dotnet-tools.json` and a plain `dotnet build -p:RunicEditorUsePackedTranslations=true` is not supported. Only the NuGet packages are tested in packed form. The editor frontend keeps consuming the npm Translations packages through `workspace:*` links.

The XAML checks can be verified independently with `dotnet run --project
tests/dotnet/Runic.Translations.Generator.Tests --configuration Release --
--treenode-filter "/*/*/GeneratorXamlTests/*"` and `dotnet run --project
tests/dotnet/Runic.Translations.Build.Tests --configuration Release -- --xaml`.
`--xaml-wpf` also compiles real WPF `Page` items and requires the WPF adapter
built in the same configuration first. For locally packed Runtime and Build
candidates, pass `--xaml-packages <feed> <version>` to the Build tests; this
exercises the packaged analyzer without project references.

## Test suites

The managed suites under `tests/dotnet` are executables run with `dotnet run --project <suite> --configuration Release`. Most still use a small suite-local `TestRunner`. `Runic.Translations.Generator.Tests` uses [TUnit](https://tunit.dev) on Microsoft.Testing.Platform (MTP); move further suites to the same pattern one at a time:

- **Project.** Keep `OutputType` `Exe`. Reference `TUnit.Core`, `TUnit.Engine`, `Microsoft.Testing.Extensions.TrxReport` and `Microsoft.Testing.Extensions.CodeCoverage`, whose versions are pinned in `Directory.Packages.props`. The `TUnit` metapackage is not used: it adds TUnit's assertion library and the MTP telemetry extension, and the suites keep their own assertions. Set `TUnitImplicitUsings` to `false` and write `using TUnit.Core;`, as the repository does not use implicit usings. TUnit generates the entry point, so delete `Program.cs`.
- **Lock file.** Set `RestorePackagesWithLockFile` to `true` and `RestoreLockedMode` to `true` when `CI` is `true`, and commit `packages.lock.json`. CI then fails with NU1004 if the lock file is stale, for example after a package version or a referenced project's dependency changes. Update it with `dotnet restore <suite> --force-evaluate` in the development shell.
- **Cases.** Each case is a non-static `[Test]` method in a non-static class. `[DisplayName]` keeps the case's readable name in console output, TRX and the IDE. A case fails when it throws, so suite-local `Assert` helpers keep working.
- **Running and filtering.** Arguments after `--` go to the test application: `--list-tests` lists the cases, and `--treenode-filter "/<assembly>/<namespace>/<class>/<method>"` selects them, with `*` as a wildcard, for example `--treenode-filter "/*/*/GeneratorTests/SynchronousManager"`. `--filter` is not supported. The exit code is 0 when all selected cases pass, 2 when one fails and 8 when none ran. `dotnet test` is not supported yet: `global.json` does not opt in to the MTP mode of `dotnet test`, and the VSTest mode fails on .NET 10.
- **Reports.** `bun eng/run.mjs test` passes `--report-trx --coverage --coverage-output-format cobertura` and writes `<suite>.trx` and `<suite>.cobertura.xml` to `artifacts/test-results/<suite>`, which CI uploads as `runic-translations-test-results`. Without `--results-directory`, TUnit writes its HTML and JSON reports to `bin/<configuration>/net10.0/TestResults`.
- **Parallelism.** TUnit runs cases in parallel. Before you allow this for a suite, make sure no case shares mutable static state, the current directory, environment variables, ports or files with another case. The generator cases are isolated and CPU-bound, so `AssemblyInfo.cs` limits them to one case per processor with `ParallelLimiter<ProcessorCountParallelLimit>`. Pass `--maximum-parallel-tests 1` to run cases one at a time while debugging; mark a case or class that cannot run alongside others with `[NotInParallel]`.
- **IDE.** Visual Studio, Rider and the C# Dev Kit discover MTP test projects when their Microsoft.Testing.Platform support is enabled. See [running TUnit tests](https://tunit.dev/docs/getting-started/running-your-tests/).

## Public API

The libraries that NuGet packages expose under `lib/` set `RunicTrackPublicApi`: `Runic.Translations`, `Runic.Translations.Wpf`, and `Runic.Translations.Tooling` with its bundled `Runic.Translations.Authoring` and `Runic.Translations.Compiler`. [`eng/build/public-api.targets`](eng/build/public-api.targets) then references `Microsoft.CodeAnalysis.PublicApiAnalyzers`, which compares the compiled public surface with the project's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. These files are analyzer inputs and are not packed. A new or changed public member without a matching entry reports RS0016, a removed one RS0017; `Verification` builds treat both as errors. Add new API to `PublicAPI.Unshipped.txt` (the IDE code fix does this) and mark removals with `*REMOVED*` there. `PublicAPI.Shipped.txt` holds the API of the last published release.

Packing `Runic.Translations`, `Runic.Translations.Tooling` and `Runic.Translations.Wpf` also runs package validation against `RunicTranslationsPackageValidationBaselineVersion` from `eng/Versions.props`, the last published release. It compares every assembly under `lib/`, including the bundled ones. Record an intentional binary break in the project's `CompatibilitySuppressions.xml`; regenerate it with `dotnet pack <project> -p:ApiCompatGenerateSuppressionFile=true`.

## Dependencies

`bun run dependencies:audit` reports known vulnerabilities in every tracked `bun.lock` and in the NuGet graphs of the solution and the editor. It runs weekly in [Dependency audit](.github/workflows/dependency-audit.yml) and never upgrades packages. Pin GitHub Actions to a full commit SHA with a `# vX.Y.Z` comment.

Report security issues as described in [SECURITY.md](SECURITY.md).
