# Releasing Runic Translations SDK

Runic Translations releases six NuGet packages together: `Runic.Translations`,
`Runic.Translations.Build`, `Runic.Translations.Tooling`,
`Runic.Translations.Wpf`, `Runic.Translations.Templates`, and
`dotnet-runic-translations`. It also releases the Vite, Svelte, and SvelteKit
packages listed in `eng/workspace.json`. The editor and IDE extensions are
verified with the release source, but are not registry packages in this workflow.

Set the intended preview version in both `eng/Versions.props` and
`eng/workspace.json`, merge to `main` and wait for that commit's CI push run to
succeed. Then dispatch **Publish preview** from `main` with that exact version.
Check **dry-run** first to run every check without publishing.

The workflow does not test or pack again; CI already verified the isolated consumers
of these exact files. Its `candidate` job (`contents: read`, `actions: read`, no
environment) runs `eng/release/ci-artifact.mjs`:

- `find-ci` finds the newest successful `ci.yml` push run on `main` for the
  dispatched commit and its `runic-translations-packages` artifact. It fails clearly
  when there is no such run, the run is still in progress or failed, or the artifact
  expired (CI keeps it 30 days; rerun all jobs of that CI run to upload it again).
- `actions/download-artifact` downloads it by id (`artifact-ids`, `run-id`,
  `github-token`); a re-upload under the same name gets a new id, so it cannot swap the bytes.
- `prepare` requires exactly the `eng/workspace.json` packages at the requested
  version, packed from the dispatched commit for this repository, and records their
  hashes in the `release-candidate-<run id>` artifact.
- `publish.mjs publish --dry-run` reports which versions are missing and fails if a
  published version has different contents; `release-check` fails if the tag or an
  existing GitHub release points at another commit, or the release is a draft (a
  release of this exact tag and commit is kept); `publish.mjs tag-latest
  --dry-run` reports where npm `latest` would move.

A dry run ends there, without OIDC, a registry write, a tag or a release. It
cannot detect missing trusted-publisher configuration, such as the first
publication of a new npm package name (W120-023). Otherwise the `publish` job, the
only one in the `preview` environment and the only one with `id-token: write` and
`contents: write`, downloads the same artifact, checks it against the candidate
inventory, requested version and CI run (`ci-artifact.mjs verify`), publishes missing
package versions, creates the GitHub prerelease at the dispatched commit from those
same files (`ci-artifact.mjs release`; a rerun after a partial publication keeps a
release of this tag and commit and uploads only missing assets), and then,
because every release is a preview until 1.0, moves npm `latest` to it, never to
an older version, waiting while npm still answers 401 or 404 for a just-published
name. `eng/release/ci-artifact.test.mjs` pins this contract.

After publication, update the documentation catalog in `runic-site` (see
"Release catalogs" in its `docs/README.md`). The workflow does not push to other
repositories; the publish summary repeats this reminder.

Run `bun run verify:candidate [version]` to pack a fresh candidate and verify its
isolated NuGet and npm consumers once. Packing builds the set in a staging directory
beside `artifacts/packages` and replaces it only when every package succeeded, so a
failed or interrupted pack keeps the previous set. The version and `gitHead` are
written into the packed npm archives; tracked `package.json` files are not modified.
It does not run `bun run verify-editor-packed`, which CI runs separately.

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
