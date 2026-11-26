# Contributing

Use the locked development environment through `direnv exec . <command>` or `nix develop . -c <command>`. Run focused checks while changing a component; GitHub runs the complete cross-platform matrix. The editor intentionally uses published Runic Application, Platform, and Command Line packages, while Translations components remain project references in this repository.

## Checks

Pull requests must pass the `verify` job in [CI](.github/workflows/ci.yml), which requires every other CI job to succeed; the macOS jobs may instead be skipped when a pull request or an ordinary push to `main` changes none of their paths ([CI triggers and macOS jobs](#ci-triggers-and-macos-jobs)). Run the matching command locally before pushing:

| CI job | Local command |
| --- | --- |
| Source checks | `bun eng/generate-cldr.mjs --check`, `bun eng/render-capabilities.mjs --check`, `bun test --timeout 180000 eng/ci/apt-install.test.mjs` (Linux; see [Ubuntu packages in CI](#ubuntu-packages-in-ci)), `bun test eng/ci/macos-changes.test.mjs`, `bun test eng/conformance/vendor.test.mjs` (the vendored conformance core; see [its README](eng/conformance/README.md)), `bun eng/conformance/cli.ts cases` and `bun eng/conformance/cli.ts lint` (the Translations case list and status files), and `actionlint` (available in the runic-sdk development shell) |
| Build and test | `bun run test` (heavy; prefer the focused commands in the README while iterating; it includes the compiled corpus check, `bun eng/generate-corpus.mjs --check`), then `bun examples/bun/operation-history/verify.ts`, `bun eng/conformance/cli.ts dotnet-translations` and `bun eng/conformance/cli.ts esm-translations` ([conformance driver](eng/conformance/README.md#translations)) |
| Vite in-place message updates / Chromium | `bun run --bun build` in `packages/web/vite-plugin-runic-translations` and in `packages/web/translations-svelte`, then `bun run --bun test:hmr` in `packages/web/translations-svelte` (builds the CLI; set `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` to a Chromium, such as the `editor` dev shell's) |
| RMF2 NativeAOT / linux-x64 | `bun eng/run.mjs native-aot` (NativeAOT publishes of the RMF2 suites and examples, then the size budgets: `bun tests/aot-size/measure.mjs`, see [tests/aot-size](tests/aot-size/README.md)) |
| Language server / generic client | `bun eng/run.mjs lsp-smoke` (packs the tool under a local-only version, installs it as a local tool in a temporary project and runs `tests/lsp-generic-client/client.mjs` against `dotnet tool run runic-translations -- lsp`) |
| Editor / hosted RMF2 E2E | `bun run build`, `dotnet apps/translations-editor/bin/Release/net10.0/Runic.Translations.Editor.dll validate apps/translations-editor/ExampleWorkspace`, then the commands in [the hosted E2E README](apps/translations-editor/tests/HostedBrowserE2E/README.md) |
| Editor / packed Translations packages | `bun run verify-editor-packed` |
| Editor / native window / win-x64, osx-arm64 | On that platform: `bun eng/run.mjs publish-editor <rid>`, then `bash eng/ci/editor-native-canary.sh artifacts/editor-<rid> webview2\|wkwebview artifacts/editor-canary "$(bun eng/release/editor-release.mjs version)"` |
| Release editor (the editor's packages; [distribution guide](apps/translations-editor/docs/editor-distribution.md)) | Linux: `bun eng/run.mjs build-web`, `bash eng/ci/install-dotnet-runic.sh <tool path>`, then `<tool path>/dotnet-runic package deb\|rpm\|appimage --project apps/translations-editor/Runic.Translations.Editor.csproj` with `dpkg-deb`, `rpmbuild` or `appimagetool` installed; `macos` on a Mac. Open an extracted or installed package with `editor-native-canary.sh <folder with the executable> gtk4-webkitgtk6\|wkwebview <logs> <editor version>` |
| Localized formatting / Windows, macOS | `bash eng/ci/localized-formatting.sh` (the localized formatting tests, the date/percent corpus and the all-locale comparison, ESM in Bun and Node); on Windows also `dotnet run --project tests/dotnet/Runic.Translations.Authoring.Tests --configuration Release -- --treenode-filter "/*/*/(PathContainmentTests)\|(Rmf2AuthoringTests)/*"` (Windows path rules) |
| WPF / Windows | `dotnet run --project tests/dotnet/Runic.Translations.Wpf.Tests --configuration Release`, `dotnet build examples/wpf/document-sample --configuration Release`, then `dotnet run --project tests/dotnet/Runic.Translations.Build.Tests --configuration Release -- --treenode-filter "/*/*/XamlBuildTests/*" --test-parameter xaml-wpf=true --minimum-expected-tests 3` |

When a `--check` command reports a stale file, run the same script without `--check` and commit the result. `bun eng/generate-corpus.mjs` rewrites `specs/translations/corpus/compiled`, the compiled conformance corpus; run it after a change to the compiler output or to a corpus source ([Compiled artifacts](specs/translations/corpus/README.md#compiled-artifacts)). `bun eng/generate-cldr.mjs` also rewrites the capability matrices `specs/translations/capabilities-v1.json` (frozen) and `capabilities-v2.json` ([capability matrix](specs/translations/capabilities.md)), so run `bun eng/render-capabilities.mjs` after it.

`bun run verify-editor-packed` packs the Translations packages under a local-only version (`<version>-editor-packed` for a plain release such as `0.8.0`, `<version>.editor-packed` for a prerelease), builds the editor against them with `-p:RunicEditorUsePackedTranslations=true`, and checks that NuGet resolved them from `artifacts/packages/nuget`. It removes that version from the NuGet package cache afterwards and leaves the editor restored in packed mode; the next `bun run build` restores project references again. CI also runs the hosted E2E against this packed build.

Packed mode is driven only by this script: it installs `dotnet-runic-translations` from the local feed into `artifacts/editor-packed/tool` and passes it as `RunicTranslationsToolCommand`, so there is no `.config/dotnet-tools.json` and a plain `dotnet build -p:RunicEditorUsePackedTranslations=true` is not supported. Only the NuGet packages are tested in packed form. The editor frontend keeps consuming the npm Translations packages through `workspace:*` links.

The XAML checks can be verified independently with `dotnet run --project
tests/dotnet/Runic.Translations.Generator.Tests --configuration Release --
--treenode-filter "/*/*/GeneratorXamlTests/*"` and `dotnet run --project
tests/dotnet/Runic.Translations.Build.Tests --configuration Release --
--treenode-filter "/*/*/XamlBuildTests/*"`. Adding `--test-parameter xaml-wpf=true`
also compiles real WPF `Page` items and requires the WPF adapter built in the
same configuration first. For locally packed Runtime and Build candidates, add
`--test-parameter xaml-package-feed=<feed> --test-parameter
xaml-package-version=<version>` instead; this exercises the packaged analyzer
without project references.

## CI triggers and macOS jobs

CI runs on pull requests, on pushes to `main`, nightly (03:17 UTC) and when dispatched. A pull request branch runs only
through its pull request, not a second time as a branch push, and a newer push to it cancels the running CI.

macOS runners are shared with runic-sdk and Runic Command Line (at most 5 macOS jobs at once across the three
repositories), so the macOS jobs (`Localized formatting / macOS` and `Editor / native window / osx-arm64`) run only
where they test something:

- **Pull requests and ordinary pushes to `main`:** only when the change touches a path in
  [`eng/ci/macos-paths.txt`](eng/ci/macos-paths.txt): the formatting runtime, the generated ESM runtime, the CLDR data
  and conformance corpora and their tests, the editor's native host, the workflow and CI scripts, and the toolchain
  and dependency pins, including the NuGet lock files. A push compares with the previous `main` commit; a push that
  moved `main` by more than one commit runs them.
- **Version and release commits on `main`:** always. A push that changes a path in
  [`eng/ci/full-run-paths.txt`](eng/ci/full-run-paths.txt) runs every job: `eng/Versions.props`,
  `eng/workspace.json`, an assembled release note `eng/release/notes/<version>.md` and Publish preview's gate pin,
  which the release pull request and the post-release pull request change.
- **Nightly and dispatched runs:** always.

The `Platform paths` job decides this with `eng/ci/macos-changes.sh --event <event>`; otherwise the two jobs report as
skipped and `verify` accepts that. Check a branch locally with
`bash eng/ci/macos-changes.sh "$(git merge-base origin/main HEAD)"` (the pull request rule), or a pushed commit with
`bash eng/ci/macos-changes.sh --event push <commit>^ <commit>`. To run the macOS jobs for a pull request that does not
need them, dispatch CI on its branch (`gh workflow run ci.yml --ref <branch>`). Add a path to the list when a macOS
job starts to exercise it.

**Full runs.** A run that runs the macOS jobs runs every job, since they are the only jobs a run skips. Outside pull
requests such a run uploads the `ci-full-run` artifact. Publish preview (`eng/release/ci-artifact.mjs find-ci`) takes a
release's packages only from a green full run on `main` of the dispatched commit: its push run when it is a version
or release commit, or a nightly or dispatched run of it. The runic-sdk train (gate and train feed, runic-sdk
`eng/train/README.md`, "Run selection by commit") takes only a green full run at or after its pinned commit. Neither
dispatches CI itself. When a commit has only an ordinary push run, both fail and name the command that starts a full
run: `gh workflow run ci.yml -R Runic-Artifex/runic-translations-sdk --ref main` (while `main` is still at that commit).
The macOS jobs upload only their failure logs, so no consumer reads an artifact that an ordinary push run lacks.

## Ubuntu packages in CI

Linux jobs install Ubuntu packages through [`.github/actions/apt-install`](.github/actions/apt-install/action.yml), which runs [`eng/ci/apt-install.sh`](eng/ci/apt-install.sh); both are ported from runic-sdk, whose `eng/ci/README.md` ("Ubuntu packages") documents them in full. No workflow step runs `apt-get` directly. The runners' Ubuntu mirrors can crawl (an EC2 mirror delivered about 40 kB/s in #258), so the script does not give apt a fixed time per attempt:

- **Probe.** Before the first download it measures each candidate mirror (the runner's own Ubuntu mirrors plus `azure.archive.ubuntu.com` and `archive.ubuntu.com`) with a ranged 2 MB GET of the largest archive to fetch, and puts the fastest first.
- **Progress bounds.** `apt-get install --download-only` is stopped after 60 seconds without growth of the archives, or, while an untried mirror is left, when its average rate after 45 seconds is below 1 MB/s. The next attempt uses the fastest untried mirror and resumes the partial downloads. All attempts (at most five) share a 20-minute budget, so a slow last mirror that keeps delivering still finishes. The install then runs with `--no-download`. The `APT_*` variables at the top of the script set these limits.
- **Archive cache.** The action first runs `apt-install.sh --resolve` (`apt-get update`, then a hash of the archive versions the packages need) and keys an `actions/cache` entry of the downloaded `.deb` files by Ubuntu release, architecture, package list, runner image and that hash. On `main` it saves the entry right after the install; other branches only restore. apt verifies every cached archive against the signed indexes before it uses it.
- **Chromium.** [`.github/actions/install-chromium`](.github/actions/install-chromium/action.yml) lists the packages Playwright's Chromium needs that the image lacks (`playwright-core install-deps --dry-run`), installs them through the action without recommended packages, and then downloads the browser with [`eng/ci/install-chromium.sh`](eng/ci/install-chromium.sh) (5 minutes per attempt, three attempts, through `eng/ci/retry.sh`). Playwright's own `--with-deps` is not used, as it runs `apt-get` without a time limit or mirror fallback.

[`eng/ci/apt-install.test.mjs`](eng/ci/apt-install.test.mjs) runs the script against a stubbed `sudo`, `apt-get` and `curl` in the `Source checks` job: the probe, the rate floor, a slow last mirror, a stalled mirror, plain sources, `ports.ubuntu.com`, the budget and `--resolve`.

## Test suites

The managed suites under `tests/dotnet` are executables run with `dotnet run --project <suite> --configuration Release`. The Authoring, Build, CommandLine, Compiler, Generator, Runtime, Tooling and WPF suites use [TUnit](https://tunit.dev) on Microsoft.Testing.Platform (MTP). The AOT, RMF2 AOT and package suites are single-purpose probe executables that check a published or packed build; a new suite of independent cases follows the TUnit pattern:

- **Project.** Keep `OutputType` `Exe`. Reference `TUnit.Core`, `TUnit.Engine`, `Microsoft.Testing.Extensions.TrxReport` and `Microsoft.Testing.Extensions.CodeCoverage`, whose versions are pinned in `Directory.Packages.props`. The `TUnit` metapackage is not used: it adds TUnit's assertion library and the MTP telemetry extension, and the suites keep their own assertions. Set `TUnitImplicitUsings` to `false` and write `using TUnit.Core;`, as the repository does not use implicit usings. TUnit generates the entry point, so delete `Program.cs`.
- **Lock file.** Set `RestorePackagesWithLockFile` to `true` and `RestoreLockedMode` to `true` when `CI` is `true`, and commit `packages.lock.json`. CI then fails with NU1004 if the lock file is stale, for example after a package version or a referenced project's dependency changes. Update it with `dotnet restore <suite> --force-evaluate` in the development shell.
- **Cases.** Each case is a non-static `[Test]` method in a non-static class. `[DisplayName]` keeps the case's readable name in console output, TRX and the IDE. A case fails when it throws, so suite-local `Assert` helpers keep working. A case that had no readable name keeps its method name, which TUnit uses as the display name. Give a case that ran once per value its own `[Arguments]` rows and name it with the parameter, as in `[DisplayName("formatter renders fixed$precision")]`. Where `System.ComponentModel` is imported, alias `DisplayNameAttribute` to `TUnit.Core.DisplayNameAttribute`.
- **Running and filtering.** Arguments after `--` go to the test application: `--list-tests` lists the cases, and `--treenode-filter "/<assembly>/<namespace>/<class>/<method>"` selects them, with `*` as a wildcard, for example `--treenode-filter "/*/*/GeneratorTests/SynchronousManager"`. A `[Category]` selects a group across classes with `[Category=<name>]`: `--treenode-filter "/*/*/*/*[Category=rmf2-semantic-v5]"` runs the Compiler suite's RMF2 semantic v5 cases. `--test-parameter <name>=<value>` passes a setting to the cases, which read it from `TestContext.Parameters`; the Build suite uses it for its XAML modes. An `[Explicit]` case, such as the Build suite's RMF2 LSP benchmark, runs only when a filter selects it. `--filter` is not supported. The exit code is 0 when all selected cases pass, 2 when one fails and 8 when none ran. `dotnet test` is not supported yet: `global.json` does not opt in to the MTP mode of `dotnet test`, and the VSTest mode fails on .NET 10.
- **Reports.** `bun eng/run.mjs test` passes `--report-trx --coverage --coverage-output-format cobertura` and writes `<suite>.trx` and `<suite>.cobertura.xml` to `artifacts/test-results/<suite>`, which CI uploads as `runic-translations-test-results`. Without `--results-directory`, TUnit writes its HTML and JSON reports to `bin/<configuration>/net10.0/TestResults`.
- **Parallelism.** TUnit runs cases in parallel. Before you allow this for a suite, make sure no case shares mutable static state, the current directory, environment variables, ports or files with another case. The cases of the other TUnit suites are isolated, so their `AssemblyInfo.cs` limits them to one case per processor with `ParallelLimiter<ProcessorCountParallelLimit>`. Pass `--maximum-parallel-tests 1` to run cases one at a time while debugging; mark a case or class that cannot run alongside others with `[NotInParallel]`, as the timed editor-state scale case in `Runic.Translations.Authoring.Tests` is. The Build suite's cases start dotnet builds, the CLI, serve mode and the language server with fixed response timeouts, so its `AssemblyInfo.cs` runs the whole suite `[NotInParallel]`. The Compiler suite's cases that start `bun` to run generated ESM, the Vite plugin or `tsc` are `[NotInParallel]`; they need `bun install --frozen-lockfile` and the web packages built by `bun run build`, as in CI.
- **WPF.** `Runic.Translations.Wpf.Tests` targets `net10.0-windows` and runs only on Windows; `EnableWindowsTargeting` lets it build on Linux. Its `WpfDispatcherExecutor` runs every case on one STA thread with a running WPF dispatcher, as the former `[STAThread]` `Main` did, without creating a WPF `Application`. The binding cases share `TranslationSource.Default`, the `Catalogs` sources, their managers' locale and, last, a permanent design-mode override, so every case keeps the former order with `[NotInParallel(WpfSuite.Key, Order = n)]`. The CI `WPF / Windows` job writes its TRX and Cobertura reports to `artifacts/test-results/Runic.Translations.Wpf.Tests` and uploads them as `runic-translations-wpf-test-results`. The interactive document sample is a separate project, `examples/wpf/document-sample`.
- **IDE.** Visual Studio, Rider and the C# Dev Kit discover MTP test projects when their Microsoft.Testing.Platform support is enabled. See [running TUnit tests](https://tunit.dev/docs/getting-started/running-your-tests/).

## Public API

The libraries that NuGet packages expose under `lib/` set `RunicTrackPublicApi`: `Runic.Translations`, `Runic.Translations.CommandLine`, `Runic.Translations.Wpf`, and `Runic.Translations.Tooling` with its bundled `Runic.Translations.Authoring` and `Runic.Translations.Compiler`. [`eng/build/public-api.targets`](eng/build/public-api.targets) then references `Microsoft.CodeAnalysis.PublicApiAnalyzers`, which compares the compiled public surface with the project's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. These files are analyzer inputs and are not packed. A new or changed public member without a matching entry reports RS0016, a removed one RS0017; `Verification` builds treat both as errors. Add new API to `PublicAPI.Unshipped.txt` (the IDE code fix does this) and mark removals with `*REMOVED*` there. `PublicAPI.Shipped.txt` holds the API of the last published release.

Packing `Runic.Translations`, `Runic.Translations.Tooling` and `Runic.Translations.Wpf` also runs package validation against `RunicTranslationsPackageValidationBaselineVersion` from `eng/Versions.props`, the last published release. It compares every assembly under `lib/`, including the bundled ones. Record an intentional binary break in the project's `CompatibilitySuppressions.xml`; regenerate it with `dotnet pack <project> -p:ApiCompatGenerateSuppressionFile=true`. A package that has not been published yet, such as `Runic.Translations.CommandLine` before its first release, clears `RunicTranslationsPackageValidationBaselineVersion` in its project; remove that line once the package is published.

## Release notes

A pull request with a user-facing change adds its release notes as fragments under `eng/release/notes/<version>/`, `<version>` being the candidate on `main`: one file per entry, in the directory of its section, named by the issue or pull request, for example `features/176.md` (a `## Title` section with its issue), `breaking/176.md` and `fixes/180.md` (list items). Never edit `eng/release/notes/<version>.md`: the release pull request assembles it from the fragments. `bun eng/release/notes.mjs check` validates them; [the release guide](eng/release/README.md#release-notes) lists the sections, names and order. A note about the Translations Editor alone goes to the editor's own notes, `eng/release/notes/editor/<editor version>/`, in the same layout; the editor's version is `RunicTranslationsEditorVersion` in `apps/translations-editor/Directory.Build.props`, not the Translations version (D078.3). A change to both, such as a review file the CLI and the editor write, keeps its note in the Translations notes and adds a short one for the editor.

## Building against an unpublished Runic Command Line

During a release train (see [Release trains](eng/release/README.md#release-trains)),
`RunicCommandLineVersion` in `eng/Versions.props` can name a Command Line release
that is not on nuget.org yet. CI then builds against the train feed. Locally,
pack runic-cli-sdk with `eng/pack.sh <version> <dir>`, run
`bun eng/train-feed.mjs prepare --from <dir>`, and set
`RUNIC_TRAIN_FEED=$PWD/artifacts/train-feed/nuget` for every command. Restores then
run unlocked against a lock file in `obj/` and leave the committed
`packages.lock.json` files alone, since two candidates of one version differ. Clear the
candidate from `.cache/nuget` (`runic.commandline*/<version>`) once the release is
on nuget.org.

## Dependencies

The `bun.lock` files of `examples/component-library/web` and `examples/shared-catalog/Frontend` lock only third-party packages: `bun eng/verify-packages.mjs` (the `Build and test` job) installs the example with the current commit's packed Runic packages and fails when the rest of the install differs from the committed lock. Update them with `RUNIC_UPDATE_EXAMPLE_LOCKS=1 bun eng/verify-examples.mjs` after `bun eng/run.mjs pack`.

`bun run dependencies:audit` reports known vulnerabilities in every tracked `bun.lock` and in the NuGet graphs of the solution and the editor. It runs weekly in [Dependency audit](.github/workflows/dependency-audit.yml) and never upgrades packages. Pin GitHub Actions to a full commit SHA with a `# vX.Y.Z` comment.

Report security issues as described in [SECURITY.md](SECURITY.md).
