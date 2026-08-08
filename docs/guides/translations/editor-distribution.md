# Runic Translations Editor distribution

This document defines the first customer-preview distribution contract for the
Runic Translations Editor. It covers build identity, supported artifacts,
updates, diagnostics, and the boundary between unsigned previews and a future
stable release.

## Supported preview artifacts

CI publishes self-contained archives for:

| Runtime identifier | Runner | Archive |
|---|---|---|
| `linux-x64` | Ubuntu 24.04 x64 | `.tar.gz` |
| `win-x64` | Windows Server 2025 x64 | `.zip` |
| `osx-arm64` | macOS 15 arm64 | `.tar.gz` |

Each archive contains the .NET runtime, native CsWebUi/WebUI assets, the static
SvelteKit application, an example workspace, `LICENSE.txt`, and
`THIRD-PARTY-NOTICES.md`. A customer machine does not need Node.js, npm, the
.NET SDK, or a separately installed .NET runtime.

`eng/package-editor.ps1` performs a matching-OS restore and self-contained
publish, starts the resulting executable directly, and runs the complete editor
smoke workflow against the packaged example. It then emits:

- `package-manifest.json` with schema
  `runic.textresources.editor-package/1`, the exact version, channel, source
  commit, runtime identifier, and SHA-256 for every payload file;
- an archive that preserves executable permissions on Linux and macOS;
- a sibling `.sha256` file for the archive.

The application exposes the same identity through `--version` and its About
dialog. Packaging fails if version, channel, or commit do not match the request.

To create the local Linux preview used by CI:

```bash
nix develop -c pwsh -NoProfile -File ./eng/package-editor.ps1 \
  -RuntimeIdentifier linux-x64 \
  -OutputDirectory ./artifacts/editor \
  -Version 0.1.0-preview.local \
  -RepositoryCommit "$(git rev-parse HEAD)"
```

Windows and macOS packages are built and tested only on their matching hosted
CI runners. No local Windows or macOS sign-off is required.

## Update channels

The assembly and package manifest carry one immutable update-channel value.
The current pipeline produces `preview` artifacts only.

- **Preview** builds are retained CI artifacts for evaluation. They are
  unsigned, may change editor-state details between previews, and are updated by
  replacing the complete extracted application. The editor performs no network
  request and never updates itself.
- **Stable** is reserved for signed/notarized releases with a documented support
  window. Stable is not emitted until Windows code-signing and Apple Developer
  ID/notarization credentials are configured as protected CI secrets.

A later opt-in updater may consume a signed HTTPS manifest containing channel,
version, minimum editor-state schema, artifact URL, size, SHA-256, signing
identity, and source commit. It must download to a temporary sibling, verify the
signature and digest before replacement, preserve customer workspaces, and
offer an explicit restart. Automatic background download, silent replacement,
and cross-channel upgrades are outside this contract.

## Signing and publication gate

Preview archives are deliberately unsigned. They are not presented as stable
customer releases. A stable release job must fail closed unless all of these are
available:

1. a hardware- or service-backed Windows code-signing identity and timestamp
   service;
2. an Apple Developer ID Application identity, hardened-runtime signing,
   notarization credentials, and stapling;
3. protected release approval and the exact `PUBLISH STABLE` confirmation;
4. post-signing startup tests and regenerated archive digests;
5. build-provenance attestation binding every archive to one source commit.

Linux archives retain the manifest and checksum provenance; an additional
repository release signature can be added with the stable credential rollout.

## About and diagnostics

The About dialog reports product version, update channel, source revision,
runtime identifier, operating system, and process architecture. It can create a
versioned diagnostic zip in the operating system's temporary diagnostics
directory.

The bundle intentionally includes only:

- application/runtime identity;
- catalog ID and schema version;
- locale, document, message, and transaction counts;
- compiler success and editor-state availability;
- diagnostic ID/severity counts;
- project and third-party notices.

It excludes the workspace root, relative file paths, diagnostic messages, JSON
source, translation text, review notes, sample arguments, and recent-project
history. Nothing is uploaded. The customer chooses whether to share the zip.

## CI and provenance

The exhaustive Linux verification remains `eng/verify.sh`. Windows and macOS
run the shared authoring/editor smoke test. A separate three-OS matrix invokes
the same packaging script and uploads one runtime-specific artifact per job.
Every artifact uses `${{ github.sha }}` as its source revision, and packaging
verifies that the executable and manifest contain that value before upload.

The archives are previews until the signing gate above is implemented with real
credentials. Failing or unavailable hosted-runner UI automation is diagnosed in
CI; it is not replaced by undocumented manual testing.
