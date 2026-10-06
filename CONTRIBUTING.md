# Contributing

Use the locked development environment through `direnv exec . <command>` or `nix develop . -c <command>`. Run focused checks while changing a component; GitHub runs the complete cross-platform matrix. The editor intentionally uses published Runic Application, Platform, and Command Line packages, while Translations components remain project references in this repository.

## Checks

Pull requests must pass the `verify` job in [CI](.github/workflows/ci.yml), which requires every other CI job to succeed. Run the matching command locally before pushing:

| CI job | Local command |
| --- | --- |
| Source checks | `bun eng/generate-cldr.mjs --check`, `bun eng/render-capabilities.mjs --check`, and `actionlint` (available in the runic-sdk development shell) |
| Build and test | `bun run test` (heavy; prefer the focused commands in the README while iterating) |
| Editor / hosted RMF2 E2E | `bun run build`, then the commands in [the hosted E2E README](apps/translations-editor/tests/HostedBrowserE2E/README.md) |
| Editor / packed Translations packages | `bun run verify-editor-packed` |
| IDE / Windows VSIX package | Windows only; see [the Visual Studio extension README](tools/visualstudio-runic-translations/README.md) |

When a `--check` command reports a stale file, run the same script without `--check` and commit the result. `bun eng/generate-cldr.mjs` also rewrites `specs/translations/capabilities-v1.json`, so run `bun eng/render-capabilities.mjs` after it.

`bun run verify-editor-packed` packs the Translations packages under a local-only version (`<version>.editor-packed`), builds the editor against them with `-p:RunicEditorUsePackedTranslations=true`, and checks that NuGet resolved them from `artifacts/packages/nuget`. It leaves the editor restored in packed mode; the next `bun run build` restores project references again.

## Dependencies

`bun run dependencies:audit` reports known vulnerabilities in every tracked `bun.lock` and in the NuGet graphs of the solution and the editor. It runs weekly in [Dependency audit](.github/workflows/dependency-audit.yml) and never upgrades packages. Pin GitHub Actions to a full commit SHA with a `# vX.Y.Z` comment.

Report security issues as described in [SECURITY.md](SECURITY.md).
