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
| `0.8.0` | `0.8.0.1000` | `0.8.1000` | no |
| `0.8.1` | `0.8.1.1000` | `0.8.2000` | no |
| `1.0.0-preview.N` | `1.0.0.N` | `1.0.N` | yes |
| `1.0.0-rc.N` | `1.0.0.(500 + N)` | `1.0.(500 + N)` | yes |
| `1.0.0` | `1.0.0.1000` | `1.0.1000` | no |

Releases before 1.0 are plain `0.y.z` versions (see
[Versions before 1.0](#versions-before-10)), packaged like a final release: no VS
Code pre-release flag. `-preview.N` and `-rc.N` are for previews and release
candidates of a coming version from 1.0 on. Visual Studio uses the same mapping as
the generator's `AnalyzerReleases` headers: the fourth component is the preview
number, 500 plus the release-candidate number, or 1000 for the final release. VS
Code folds that component into the patch (`patch × 1000 + revision`) and packages a
prerelease with `vsce package --pre-release`. Both orders match the release order:
every preview sorts below every release candidate of the same version, and since a
final release is a multiple of 1000 and a prerelease never is, no prerelease can
take a later final release's number.

Only three release version forms can pass CI: a plain `x.y.z`, and `x.y.z-preview.N`
or `x.y.z-rc.N` with N from 1 to 499. IDE packaging rejects every other prerelease
label (`-beta`, `-preview.0`, `-rc.500`), build metadata (`+build`) and, because
Visual Studio reads each component as a 16-bit `System.Version` part, any major,
minor or patch above 65534; the folded VS Code patch is then at most 65535000. The
**Validate version** step of **Publish preview** applies the same pattern before it
looks for a CI run, so a dispatch with another version fails there with that
message. [`ide-versions.mjs`](ide-versions.mjs) defines the mapping and
`ide-versions.test.mjs` checks that the Visual Studio project (`GetRunicVsixVersion`)
and `package.py` agree with it.

- `bun run package` in `tools/vscode-runic-translations` (`package.mjs`) copies the
  files `vsce ls` would package to a staging directory, stamps the copied
  `package.json` and packs that, so tracked files are never modified. The stamp
  sets `version` and `preview`. Every release keeps `"preview": true`, the
  Marketplace "Preview" label (`GalleryFlags`), because the extensions are a
  preview product until 2.0 (see [Versioning and support from 1.0](#versioning-and-support-from-10));
  the label is independent of the pre-release flag. It then checks the identity,
  `extension/package.json` version and `preview` field, the pre-release flag and the
  Preview gallery flag of the VSIX; `prepare` checks them again.
  `node package.mjs <release> [output]` packages another release; CI packages
  `0.8.0` this way to check the flags of a plain release.
- The Visual Studio project replaces the manifest token with its
  `GetRunicVsixVersion` result during the Windows build, and `package.py` fails
  unless the built VSIX has the mapped version. Set `-p:RunicIdeReleaseVersion=<version>`
  to build another version locally.

Set the intended release version in both `eng/Versions.props` and
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
  version and, for VS Code, the matching pre-release flag and the Preview label (`scanVsix`). It records
  their hashes.
- `describe` writes `runic-translations-sdk-<version>.cdx.json`, a CycloneDX 1.6
  SBOM of exactly those files (`sbom.py`, standard library only; it reads package metadata
  and executes nothing). Every file must declare the release version, except that
  each VSIX must declare its mapped version, passed as `--artifact-version
  <file>=<version>`. It is deterministic for a commit. The inventory and SBOM
  are uploaded as the `release-candidate-<run id>` artifact, and their `sha256sum`
  lines are passed to `publish` as the job output `release-sha256`.
- `publish.mjs publish --dry-run` reports which versions are missing and fails if a
  published version has different contents or if the NuGet trusted-publishing key
  could not push a missing package (see [New NuGet package IDs](#new-nuget-package-ids));
  `release-check` fails if the tag (looked
  up exactly, annotated tags followed) or an existing GitHub release points at
  another commit, or a lookup fails for any reason other than not found (a release
  or draft of this exact commit is kept) and reports whether it would create a
  prerelease or a release; `publish.mjs tag-latest --dry-run` reports where npm
  `latest` would move, or that a prerelease from 1.0 leaves it alone.

A dry run ends there, without OIDC, an attestation, a registry write, a tag or a
release. Apart from the NuGet push check, it cannot detect missing trusted-publisher
configuration, such as the first publication of a new npm package name (W120-023). Otherwise the `publish` job, the
only one in the `preview` environment and the only one with `id-token: write`,
`attestations: write` and `contents: write`, downloads the same artifacts and
candidate files, checks the files against `release-sha256` and the packages and
VSIX files against the candidate inventory, requested version and CI run
(`ci-artifact.mjs verify`). Before anything is published it attests those verified
bytes: `actions/attest-build-provenance` covers every `.nupkg`, npm `.tgz`, both
VSIX files and the SBOM, and `actions/attest` attaches the SBOM to the packages and
VSIX files. It repeats every registry lookup and the NuGet push check before the
first push, then publishes missing package versions (npm also with
`--provenance`), creates the GitHub release at the dispatched commit from those
same packages, the VSIX files and the SBOM (`ci-artifact.mjs release`; a rerun after a partial
publication keeps a release of this tag and commit, and for a draft uploads only
missing assets and publishes it), and then moves npm `latest` to it when the
version may have it, never to an older version, waiting while npm still answers 401
or 404 for a just-published name. The version decides both (`githubPrerelease` in
`ci-artifact.mjs`, `latestEligible` in `publish.mjs`):

| Version | Example | npm `preview` | npm `latest` | GitHub release |
| --- | --- | --- | --- | --- |
| `0.y.z` (any 0.x version) | `0.9.0` | yes | moves to it if higher | prerelease |
| prerelease from 1.0 | `1.0.0-rc.1` | yes | unchanged | prerelease |
| stable from 1.0 | `1.0.0`, `1.0.1` | yes | moves to it if higher | release (not a prerelease) |

Every npm package is published with `--tag preview`; during the release
candidates npm `latest` stays at the last `0.y.z` release until 1.0.0 (D043.5). A published release is never modified, since immutable releases reject new
assets: if it lacks an asset (for example a release created before the SBOM
existed), the release step keeps it unchanged and reports the missing files as a
workflow warning instead of failing, so `latest` still moves. The attestations still
cover those files. `eng/release/ci-artifact.test.mjs` and `sbom.test.mjs` pin this contract.

## New NuGet package IDs

NuGet packages are pushed with the short-lived key that `NuGet/login` gets from the
nuget.org trusted-publishing policy of the `runic-artifex` account. The key can push
only IDs that match the policy's package glob, a new ID only if the policy may push
new packages, and an existing ID only if `runic-artifex` owns it. Otherwise nuget.org
answers 403 during `dotnet nuget push`: 0.6.0-preview.6 stopped that way after two
of its packages were published, because the glob did not cover the new
`Runic.Translations.CommandLine`.

[`nuget-policy.json`](nuget-policy.json) mirrors the policy: the owner, its package
globs (`*` matches any run of characters, case-insensitively, as on nuget.org) and
whether it may push new packages. `publish.mjs publish`, in the dry run and in the
publish job before the first push, checks every NuGet package that is not published
yet: its ID must match a glob; a new ID (one nuget.org does not know) needs
`pushNewPackages`; an existing ID must list `runic-artifex` among its owners. These
lookups use the public service index, search and flat-container endpoints and need
no key. A failure names each package ID and the remedy, and nothing is published.
The dry run notes every new ID it lets through. `publish.test.mjs` also fails when a
package in `eng/workspace.json` matches none of the mirrored globs.

Before releasing a new NuGet package ID:

1. Sign in to nuget.org as an administrator of `runic-artifex`, open
   **Trusted Publishing**, and edit the policy for `Runic-Artifex/runic-translations-sdk`,
   `publish-preview.yml` and the `preview` environment: allow pushing new packages and
   make a package glob match the new ID. Since 2026-10-12 the globs are
   `Runic.Translations*` and `dotnet-runic-translations`, which cover every ID the
   repository publishes; an ID outside them needs a new glob.
2. Update `nuget-policy.json` in the same pull request that adds the package, so it
   stays an exact copy of the policy.
3. Run **Publish preview** with **dry-run**; it reports the new ID with a notice.

The mirror is the only source the check has: nuget.org does not publish a policy,
and no request tells whether a key may push a version that does not exist yet (the
`verifykey` endpoint answers 404 for it). Pushing a throwaway package to probe the
key would itself publish, and a step after `NuGet/login` could only try the real
push. Keep the mirror honest when the policy changes.

Publications run one at a time. Every workflow that publishes waits in the one
`publish-preview` concurrency group, whatever the version, and never cancels a
publication in progress; `publish.test.mjs` checks it.

## Verifying a release

Every package, VSIX file and the SBOM of a release have a signed build-provenance
attestation from `publish-preview.yml` on `main`; the packages and VSIX files also
have an SBOM attestation. Verify with the GitHub CLI (`gh auth login` first):

```sh
version=0.8.0
gh release download "v$version" -R Runic-Artifex/runic-translations-sdk
gh attestation verify "Runic.Translations.$version.nupkg" -R Runic-Artifex/runic-translations-sdk \
  --signer-workflow Runic-Artifex/runic-translations-sdk/.github/workflows/publish-preview.yml
gh attestation verify "runic-translations-sdk-$version.cdx.json" -R Runic-Artifex/runic-translations-sdk \
  --signer-workflow Runic-Artifex/runic-translations-sdk/.github/workflows/publish-preview.yml
gh attestation verify runic-translations.vsix -R Runic-Artifex/runic-translations-sdk \
  --signer-workflow Runic-Artifex/runic-translations-sdk/.github/workflows/publish-preview.yml
gh attestation verify runic-translations-visualstudio.vsix -R Runic-Artifex/runic-translations-sdk \
  --signer-workflow Runic-Artifex/runic-translations-sdk/.github/workflows/publish-preview.yml
# The SBOM attestation (CycloneDX); the release asset runic-translations-sdk-<version>.cdx.json is the same document.
gh attestation verify "Runic.Translations.$version.nupkg" -R Runic-Artifex/runic-translations-sdk \
  --predicate-type https://cyclonedx.org/bom \
  --signer-workflow Runic-Artifex/runic-translations-sdk/.github/workflows/publish-preview.yml
# npm serves the published bytes unchanged, so a registry tarball verifies directly.
npm pack "@runic-artifex/translations-svelte@$version"
gh attestation verify "runic-artifex-translations-svelte-$version.tgz" -R Runic-Artifex/runic-translations-sdk \
  --signer-workflow Runic-Artifex/runic-translations-sdk/.github/workflows/publish-preview.yml
```

Pass `--signer-workflow` for every file. The provenance names `publish-preview.yml`,
the workflow that attested and published the bytes, not the job that built them.
The packages are built by a successful `ci.yml` run for the same commit, where the
locked build and test dependencies also run. The publish workflow accepts only that
run's artifact, by id, and checks the files against the candidate's hashes and
inventory before it attests them.

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
through its package page or the documentation portal. Nothing else in a README
changes; `eng/release/readme-pack.test.mjs` packs READMEs and compares them with
their source, so shell line continuations survive on Linux.
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

### Release candidates and 1.0.0

Translations goes through `1.0.0-rc.N` to `1.0.0` in the release train, after
Command Line and before Runic SDK. An rc is a routine release, except:

- **Validate version** accepts `1.0.0-rc.N` and `1.0.0`; the IDE extensions map them
  as [IDE extension versions](#ide-extension-versions) describes.
- After an rc, move the PublicAPI entries as above, but leave
  `RunicTranslationsPackageValidationBaselineVersion` at the last `0.y.z` release:
  from 1.0 the baseline is never a prerelease (`ci-artifact.test.mjs` fails
  otherwise). An rc takes no API or behaviour changes, so it needs no
  `CompatibilitySuppressions.xml`.
- After 1.0.0: set the baseline to `1.0.0`, every `PublicAPI.Unshipped.txt` is
  empty, delete every `CompatibilitySuppressions.xml` (from then on one is allowed
  only as the shared policy describes), and replace the pre-1.0 wording
  ([Versions before 1.0](#versions-before-10) and "until 1.0" / "before 1.0" in the
  guides and package READMEs; release notes and `AnalyzerReleases` stay as they are).

Before the first standalone release, configure NuGet and npm trusted publishers
for `Runic-Artifex/runic-translations-sdk`, `publish-preview.yml`, and the
`preview` environment. Each npm trusted publisher also needs **Allow npm dist-tag**
enabled. New repository ownership does not inherit trusted publisher
configuration from the former SDK repository.

## Versions before 1.0

Releases before 1.0 use plain `0.y.z` versions, as Runic SDK does from 0.9.0
(no `-preview.N` suffix; the last such release was `0.7.0-preview.4`). Until 1.0:

- A new minor release (`0.8.0`, `0.9.0`) may contain breaking changes, each listed
  with its upgrade steps in the release notes. A patch release (`0.8.1`) only fixes
  or adds. Only the latest minor gets fixes.
- Releases are still previews: the GitHub release is marked as a prerelease, and
  npm `latest` moves to every release (the publish workflow keeps its name
  `publish-preview.yml`, to which trusted publishing is bound). A plain `0.y.z` is
  packaged as a final IDE release, without the VS Code pre-release flag, since the
  VSIX files are attached to the GitHub release and not published to a marketplace.
- Translations has no preview packages: every package is stable-tier and follows
  these rules. The IDE extensions and the `lsp` command are the exception listed
  below.

## Versioning and support from 1.0

This policy takes effect with 1.0.0. The
[Runic SDK release guide](https://github.com/Runic-Artifex/runic-sdk/blob/main/eng/release/README.md#versioning-and-support-from-10)
has the shared policy for the three SDKs: SemVer rules and what counts as
breaking, enforcement, package tiers, .NET versions, deprecation and fixes. The
[documentation portal](https://docs.runic-artifex.eu/support/policy/) states the
same policy. For Translations:

- Every NuGet and npm package shares the version in `eng/workspace.json` and is
  stable from 1.0.0. From 1.0 npm previews go to the `preview` dist-tag and
  `latest` moves only to stable versions. A 1.0 release candidate is published as
  `1.0.0-rc.N` ([IDE extension versions](#ide-extension-versions)). The Translations
  Editor is a separate product on its own timeline and is not released here.
- `Runic.Translations.Tooling` is a stable package. Its public API is the
  compiler (`TranslationCompiler` and its source, options, diagnostic and
  compilation types), XLIFF interchange with the review sidecar, artifact
  inspection and project scaffolding. The command plumbing of the tool, the
  workspace transactions, the MF2 syntax tree and the language service are
  internal: they serve the tool and the editor, built from the same commit, and may
  be published later as additive API. There is no experimental API family.
- Outside the 1.0 compatibility rules, until the production language server (2.0):
  the `lsp` command of `dotnet-runic-translations` (hidden from help) with its
  protocol subset and custom methods, and the VS Code and Visual Studio extensions
  with their commands and settings. The extensions keep the "Preview" label on every
  release and may change in a minor release. Both extensions share the release
  version and are attached to every GitHub release.
- `Runic.Translations.CommandLine` depends on Runic Command Line. Translations
  ships 1.0.0 after Command Line 1.0.0 and before Runic SDK 1.0.0 in one release
  train. Until 1.0 it references the Command Line minor it was tested with
  (`[0.y.0, 0.(y+1))`), and from 1.0 the tested major (`[1.y.0, 2.0)`); a plain
  Translations release never depends on a Command Line prerelease. Its release notes
  name the Command Line version it was tested with.
- Contracts covered by the breaking-change rules, besides the C# API of each
  package's `PublicAPI.Shipped.txt`, the `Runic.Translations.Build` MSBuild
  properties and the `dotnet-runic-translations` commands and options: the JSON
  contract `runic.translations.interchange-review/1`; the
  `runic.translations.editor-diagnostics/1` bundle only as input of
  `runic-translations support`, which keeps reading every `/1` bundle; the packed
  schemas `project-v1`, `message-ast-v5`, `locale-artifact-v5`, `external-pack-v5`
  and `web-module-manifest-v3`, with the Vite plugin's virtual module IDs; the
  tool payloads `runic.translations.tool/1` and `runic.translations.support/1`
  (schemas `translations-tool-v1` and `translations-support-v1`; `output` and
  `error` are display text, and new members may be added); and the
  translations transport: a 1.x reader must read every message any 1.x writer
  produced, including persisted messages, and writers emit a new transport
  version only on request until every supported reader accepts it. The editor's
  own state file (`runic.translations.editor-state/1`) belongs to the editor
  product and is not an SDK contract. The `serve` protocol
  (`runic-translations-serve/1`, schema `translations-serve-v1`) is a contract
  between the Vite plugin and the tool of the same release only.
- Open and closed sets: the RMF2 document block set (`runic:p`, `runic:h`,
  `runic:ul`, `runic:ol`, `runic:li` today) is open, so a later minor may add
  blocks; renderers and custom document or WPF markup factories must handle an
  unknown block name, by failing or falling back (the built-in renderers fail
  closed). The inline binding kinds (`runic:link`, `runic:action`, `runic:icon`) are
  closed. Open enums, which a minor release may extend (keep a default case):
  `TranslationPackFailureReason`, `TranslationReferenceDiagnosticReason`,
  `TextArgumentType`, `TextArgumentFormat`, `LocalizedTextContentNodeKind`, and the
  SvelteKit `LocaleResolutionStrategy`/`LocaleResolutionSource` unions. Closed:
  `TranslationReferenceOutcome`, `UnsupportedLocalePolicy`,
  `MissingTranslationPolicy`, `TranslationSnapshotPublishReason` and the SvelteKit
  `BaseLocalePath` union. Each type's documentation says which it is.
- Interfaces that applications implement (`ITranslationSnapshot`,
  `ITranslationProvider`, `ITranslationSnapshotFactory`,
  `IExternalTranslationSource`) gain members in a minor release only with a default
  implementation; a default may throw `NotSupportedException` or report that the
  feature is not supported (as `FormatContent`, `TryGetKey` and
  `TryGetPlaceholders` do).
- Generated code is public API in the application's assembly. The C# contract is
  the `<Class>` type with its `Messages` members (named by the flattened key) and
  encoded `r_<hex>` members, the typed slot types, and the `<Class>Catalog` factory
  methods (`CreateProvider`, `CreateManager`, `CreateManagerAsync` and the external
  pack methods) with `CatalogId`, `DefaultLocale` and `ContractFingerprint`. The
  other `<Class>Catalog` constants (`SourceHash`, `GeneratorVersion`, the ABI,
  grammar, profile and name versions, `Rmf2MarkupContract`) describe the build and
  are diagnostics, not contract; they are `const`, so code that reads them sees the
  value it was compiled against. For ESM the contract is the exported names of the
  generated modules and their declarations (for example `localeDirection`) and the
  Vite plugin's virtual module IDs. File names, private and
  `Runic.Translations.Generated` members and formatting are not part of the
  contract. Code generated by a 1.x release runs on every later 1.x runtime; the
  runtime ABI check (`TranslationsCompatibility`) reports a mismatch explicitly.
- Package validation runs against `RunicTranslationsPackageValidationBaselineVersion`
  in `eng/Versions.props`; from 1.0 that is the latest stable release of the
  major line, and a `CompatibilitySuppressions.xml` or a `*REMOVED*` entry is
  allowed only as the shared policy describes.
- The runtime packages are managed and run wherever .NET 10 does;
  `Runic.Translations.Wpf` targets `net10.0-windows`.
- Bug and security fixes ship in a patch of the latest minor only. `[Obsolete]`
  APIs stay for at least one minor release and are removed only in a major.
