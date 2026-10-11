# Releasing Runic Translations SDK

Runic Translations releases seven NuGet packages together: `Runic.Translations`,
`Runic.Translations.Build`, `Runic.Translations.CommandLine`, `Runic.Translations.Tooling`,
`Runic.Translations.Wpf`, `Runic.Translations.Templates`, and
`dotnet-runic-translations`. It also releases the Vite, Svelte, and SvelteKit
packages listed in `eng/workspace.json`, and attaches the VS Code and Visual Studio
extensions (`runic-translations.vsix`, `runic-translations-visualstudio.vsix`) that
CI builds from the same commit to the GitHub release; they are not published to an
extension marketplace. The editor is verified with the release source but is not
released by this workflow.

## Release notes

Add user-facing notes to `eng/release/notes/<version>.md` as changes merge,
including upgrade steps for breaking changes. `ci-artifact.mjs release` passes that
file to `gh release create --notes-file`, and gh puts it above the list of merged
pull requests that `--generate-notes` adds. Without a notes file the release has
only the generated list.

## IDE extension versions

Neither VSIX format accepts a SemVer prerelease label, so CI stamps each extension
with a version mapped from the `eng/workspace.json` release version when it packages
it. The committed manifests carry no release number (`tools/vscode-runic-translations/package.json`
keeps a `0.0.1` placeholder; `source.extension.vsixmanifest` carries the
`|%CurrentProject%;GetRunicVsixVersion|` build token), so changing the release
version touches only `eng/workspace.json` and `eng/Versions.props`.

| Release | Visual Studio (`N.N.N.N`) | VS Code (`x.y.z`) | VS Code pre-release |
| --- | --- | --- | --- |
| `0.6.0-preview.N` | `0.6.0.N` | `0.6.N` | yes |
| `0.6.0` | `0.6.0.1000` | `0.6.1000` | no |
| `0.6.1-preview.N` | `0.6.1.N` | `0.6.(1000 + N)` | yes |
| `0.6.1` | `0.6.1.1000` | `0.6.2000` | no |

Visual Studio uses the same mapping as the generator's `AnalyzerReleases` headers:
the fourth component is the preview number, or 1000 for the final release. VS Code
folds that component into the patch (`patch × 1000 + N`, or `+ 1000` for the final
release) and packages a preview with `vsce package --pre-release`. Both orders match
the release order, and since a final release is a multiple of 1000 and a preview never
is, no preview can take a later final release's number; mapping preview N to
`0.6.N` alone would collide with the final `0.6.N`.

Only two release version forms can pass CI: `x.y.z-preview.N` with N from 1 to 999,
and a final `x.y.z`. IDE packaging rejects every other prerelease label (`-rc.1`,
`-beta`, `-preview.0`), build metadata (`+build`) and, because Visual Studio reads
each component as a 16-bit `System.Version` part, any major, minor or patch above
65534; the folded VS Code patch is then at most 65535000. The **Validate version**
step of **Publish preview** applies the same pattern before it looks for a CI run,
so a dispatch with another version fails there with that message. [`ide-versions.mjs`](ide-versions.mjs)
defines the mapping and `ide-versions.test.mjs` checks that the Visual Studio
project (`GetRunicVsixVersion`) and `package.py` agree with it.

- `bun run package` in `tools/vscode-runic-translations` (`package.mjs`) copies the
  files `vsce ls` would package to a staging directory, stamps the copied
  `package.json` and packs that, so tracked files are never modified. The stamp
  sets `version` and `preview`: the committed manifest keeps `"preview": true`, but
  a final release is packaged with `"preview": false`, so it does not carry the
  Marketplace "Preview" label (`GalleryFlags`) of a preview. It then checks the
  identity, `extension/package.json` version and `preview` field, the pre-release
  flag and the Preview gallery flag of the VSIX; `prepare` checks them again.
  `node package.mjs <release> [output]` packages another release; CI packages
  `0.6.0` this way to check the final-release flags.
- The Visual Studio project replaces the manifest token with its
  `GetRunicVsixVersion` result during the Windows build, and `package.py` fails
  unless the built VSIX has the mapped version. Set `-p:RunicIdeReleaseVersion=<version>`
  to build another version locally.

Set the intended preview version in both `eng/Versions.props` and
`eng/workspace.json`, merge to `main` and wait for that commit's CI push run to
succeed. Then dispatch **Publish preview** from `main` with that exact version.
Check **dry-run** first to run every check without publishing.

The workflow does not test or pack again; CI already verified the isolated consumers
of these exact files. Its `candidate` job (`contents: read`, `actions: read`, no
environment) runs `eng/release/ci-artifact.mjs`:

- `find-ci` finds the newest successful `ci.yml` push run on `main` for the
  dispatched commit, its `runic-translations-packages` artifact and its
  `rmf2-vscode-vsix` and `rmf2-visualstudio-vsix` artifacts. It fails clearly
  when there is no such run, the run is still in progress or failed, or an artifact
  expired (CI keeps them 30 days; rerun all jobs of that CI run to upload them again).
- `actions/download-artifact` downloads them by id (`artifact-ids`, `run-id`,
  `github-token`); a re-upload under the same name gets a new id, so it cannot swap the bytes.
- `prepare` requires exactly the `eng/workspace.json` packages at the requested
  version, packed from the dispatched commit for this repository, and the two VSIX
  files with their expected identities, the IDE versions mapped from the requested
  version and, for VS Code, the matching pre-release flag (`scanVsix`). It records
  their hashes.
- `describe` writes `runic-translations-sdk-<version>.cdx.json`, a CycloneDX 1.6
  SBOM of exactly those files (`sbom.py`, standard library only; it reads package metadata
  and executes nothing). Every file must declare the release version, except that
  each VSIX must declare its mapped version, passed as `--artifact-version
  <file>=<version>`. It is deterministic for a commit. The inventory and SBOM
  are uploaded as the `release-candidate-<run id>` artifact, and their `sha256sum`
  lines are passed to `publish` as the job output `release-sha256`.
- `publish.mjs publish --dry-run` reports which versions are missing and fails if a
  published version has different contents; `release-check` fails if the tag (looked
  up exactly, annotated tags followed) or an existing GitHub release points at
  another commit, or a lookup fails for any reason other than not found (a release
  or draft of this exact commit is kept); `publish.mjs tag-latest
  --dry-run` reports where npm `latest` would move.

A dry run ends there, without OIDC, an attestation, a registry write, a tag or a
release. It cannot detect missing trusted-publisher configuration, such as the first
publication of a new npm package name (W120-023). Otherwise the `publish` job, the
only one in the `preview` environment and the only one with `id-token: write`,
`attestations: write` and `contents: write`, downloads the same artifacts and
candidate files, checks the files against `release-sha256` and the packages and
VSIX files against the candidate inventory, requested version and CI run
(`ci-artifact.mjs verify`). Before anything is published it attests those verified
bytes: `actions/attest-build-provenance` covers every `.nupkg`, npm `.tgz`, both
VSIX files and the SBOM, and `actions/attest` attaches the SBOM to the packages and
VSIX files. It then publishes missing package versions (npm also with
`--provenance`), creates the GitHub prerelease at the dispatched commit from those
same packages, the VSIX files and the SBOM (`ci-artifact.mjs release`; a rerun after a partial
publication keeps a release of this tag and commit, and for a draft uploads only
missing assets and publishes it), and then,
because every release is a preview until 1.0, moves npm `latest` to it, never to
an older version, waiting while npm still answers 401 or 404 for a just-published
name. A published release is never modified, since immutable releases reject new
assets: if it lacks an asset (for example a release created before the SBOM
existed), the release step keeps it unchanged and reports the missing files as a
workflow warning instead of failing, so `latest` still moves. The attestations still
cover those files. `eng/release/ci-artifact.test.mjs` and `sbom.test.mjs` pin this contract.

## Verifying a release

Every package, VSIX file and the SBOM of a release have a signed build-provenance
attestation from `publish-preview.yml` on `main`; the packages and VSIX files also
have an SBOM attestation. Verify with the GitHub CLI (`gh auth login` first):

```sh
version=0.7.0-preview.1
gh release download "v$version" -R Runic-Artifex/runic-translations-sdk
gh attestation verify "Runic.Translations.$version.nupkg" -R Runic-Artifex/runic-translations-sdk \
  --signer-workflow Runic-Artifex/runic-translations-sdk/.github/workflows/publish-preview.yml
gh attestation verify "runic-translations-sdk-$version.cdx.json" -R Runic-Artifex/runic-translations-sdk
gh attestation verify runic-translations.vsix -R Runic-Artifex/runic-translations-sdk
gh attestation verify runic-translations-visualstudio.vsix -R Runic-Artifex/runic-translations-sdk
# The SBOM attestation (CycloneDX); the release asset runic-translations-sdk-<version>.cdx.json is the same document.
gh attestation verify "Runic.Translations.$version.nupkg" -R Runic-Artifex/runic-translations-sdk \
  --predicate-type https://cyclonedx.org/bom
# npm serves the published bytes unchanged, so a registry tarball verifies directly.
npm pack "@runic-artifex/translations-svelte@$version"
gh attestation verify "runic-artifex-translations-svelte-$version.tgz" -R Runic-Artifex/runic-translations-sdk
```

NuGet.org adds its repository signature (`.signature.p7s`) to every package it
accepts, so a `.nupkg` downloaded from NuGet.org has different bytes from the
attested one and does not verify by itself. Verify the copy attached to the GitHub
release; every entry of the NuGet.org package except `.signature.p7s` is identical
to it, and `dotnet nuget verify --all <package>` checks the NuGet.org signature.

After publication, update the documentation catalog in `runic-site` (see
"Release catalogs" in its `docs/README.md`). The workflow does not push to other
repositories; the publish summary repeats this reminder.

Run `bun run verify:candidate [version]` to pack a fresh candidate and verify its
isolated NuGet and npm consumers once. Packing builds the set in a staging directory
beside `artifacts/packages` and replaces it only when every package succeeded, so a
failed or interrupted pack keeps the previous set. The version and `gitHead` are
written into the packed npm archives; tracked `package.json` files are not modified.
It does not run `bun run verify-editor-packed`, which CI runs separately.

Packed READMEs, and npm `homepage` fields, link to the release tag `v<version>`
instead of `main`. This repository's `blob/main` and `tree/main` links are
rewritten, relative links resolve against the tag, and a remaining main-branch
link to any Runic Artifex repository fails the pack. Link to another repository
through its package page or the documentation portal.
`eng/build/release-links.targets` does this for NuGet packages and
`eng/release/readme-links.mjs` for npm packages; runic-sdk keeps the same files.
The C# generator's diagnostic help links also name the tag
(`RunicDiagnosticsCatalog` in `Directory.Build.props`).

Because the npm archives are now re-gzipped after stamping, their bytes differ from
archives packed by the earlier flow. Rerunning publish (or a dry run) for a version that was
already published with that flow reports a content mismatch; new versions are unaffected.

After publishing, move each library's `PublicAPI.Unshipped.txt` entries into
`PublicAPI.Shipped.txt`, set `RunicTranslationsPackageValidationBaselineVersion`
in `eng/Versions.props` to the published version, and delete any
`CompatibilitySuppressions.xml` files, which describe breaks from the old
baseline. See [Public API](../../CONTRIBUTING.md#public-api).

Before the first standalone release, configure NuGet and npm trusted publishers
for `Runic-Artifex/runic-translations-sdk`, `publish-preview.yml`, and the
`preview` environment. Each npm trusted publisher also needs **Allow npm dist-tag**
enabled. New repository ownership does not inherit trusted publisher
configuration from the former SDK repository.
