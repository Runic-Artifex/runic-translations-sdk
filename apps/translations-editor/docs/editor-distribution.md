# Editor distribution

Runic Translations Editor release candidates are self-contained archives for `linux-x64`, `win-x64`, and `osx-arm64`. Each archive contains the .NET runtime, Runic Desktop presentation host, required native WebView assets, the static SvelteKit application, launchers, an example workspace, the license, third-party notices, a per-file manifest, and a sibling SHA-256 checksum. Customer machines need no SDK, Node.js installation, package-registry authentication, or separate runtime.

`eng/package-editor.ps1` performs a matching-OS publish twice, fixes archive order and timestamps, compares the exact archive digests, verifies the sibling checksum, extracts into a clean temporary directory, verifies every file against `package-manifest.json`, and runs executable plus public-launcher headless validation and the complete editor smoke workflow. Tests therefore exercise the artifact a customer downloads, not only the publish staging directory. The executable and manifest both carry the exact version, channel, source commit, and runtime identifier supplied by CI.

Every candidate also has a closed `release-staging` directory. It contains the
release manifest and checksum set, copied package manifest, SPDX 2.3 artifact
SBOM with its exact .NET/npm dependency and license inventory, provenance, and
an upstream-attestation receipt template. The staging verifier rejects path
traversal, links, unbounded paths, more than 10,000 payload files, payload files
over 512 MiB, aggregate content over 2 GiB, duplicate checksums, and any
unlisted file. Before any future, separately authorized publication decision,
the release-set verifier requires exactly one Linux x64, Windows x64, and macOS
arm64 archive, all from one source revision and tree. It then emits the exact
central evidence artifact `distribution/Runic.Translations.Editor-<version>.zip`,
independently recreates it from the same verified platform snapshots, and rejects
any byte difference. It also emits a receipt template whose artifact path matches
the organization collector.

## Download, trust, and updates

No editor archive has been published yet. Unsigned preview workflow outputs are retained only as CI candidates and are not public downloads. A future public release will display `PREVIEW-NOTICE.md`, provide one platform archive and its same-named `.sha256` file, and bind its exact upstream GitHub attestation receipt into the organization release-evidence bundle.

On Linux:

```bash
sha256sum -c Runic.Translations.Editor-1.0.0-preview.N-linux-x64.tar.gz.sha256
tar -xzf Runic.Translations.Editor-1.0.0-preview.N-linux-x64.tar.gz
./Runic.Translations.Editor/runic-translations-editor edit /path/to/workspace
```

On macOS:

```bash
shasum -a 256 Runic.Translations.Editor-1.0.0-preview.N-osx-arm64.tar.gz
# Compare the printed digest with the first field in the sibling .sha256 file.
tar -xzf Runic.Translations.Editor-1.0.0-preview.N-osx-arm64.tar.gz
./Runic.Translations.Editor/runic-translations-editor edit /path/to/workspace
```

On Windows PowerShell:

```powershell
$actual = (Get-FileHash .\Runic.Translations.Editor-1.0.0-preview.N-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
$expected = (Get-Content .\Runic.Translations.Editor-1.0.0-preview.N-win-x64.zip.sha256).Split(' ')[0]
if ($actual -ne $expected) { throw 'Checksum mismatch' }
Expand-Archive .\Runic.Translations.Editor-1.0.0-preview.N-win-x64.zip
.\Runic.Translations.Editor\runic-translations-editor.cmd edit C:\path\to\workspace
```

The launcher accepts `edit [workspace]` and `validate [workspace]`; without arguments it edits the current directory. The workspace contains one conventional `runic.json` translation project.

## Release boundary

This repository is the only authority for editor archives and editor GitHub releases. The [Runic Translations](https://github.com/Runic-Artifex/runic-translations) repository publishes the compiler/runtime/tool packages consumed here, but does not package or release this application.

Preview archives, if considered after 1.0, are unsigned evaluation builds and
update only by manually replacing the complete extracted application. The editor
performs no update request and never changes itself. Through 1.0 there is no
certificate acquisition, code signing, notarization, signed update metadata, or
signing-oriented staging descriptor. The preview workflow cannot create
releases, and any publication decision remains separately authorized.

Windows may show an unknown-publisher warning. macOS Gatekeeper may prevent an
unsigned application from starting under the machine's policy. The preview does
not ask users to suppress those controls; use a source build when policy
forbids unsigned software.

## Validation and reviewable diffs

The supported CI invocation from an extracted archive is:

```bash
./runic-translations-editor validate /path/to/workspace
```

On Windows, use `runic-translations-editor.cmd` with the same arguments. This command constructs the same `EditorWorkspace` and calls the same compiler-backed load path as the editor UI. A save is separately validated against the same compiler with its in-memory draft substituted before atomic replacement. There is no weaker editor-only schema or validator.

Editor smoke tests apply identical key and review operations to two clean workspace copies and require byte-identical outputs. They also require structural changes to touch only the expected locale documents and review metadata to appear in one separate editor-state sidecar. Local `./verify.sh` snapshots the Git diff and status before generation and fails if verification introduces an unreviewed tracked or untracked change.

The editor intentionally preserves a translator's MF2 formatting for direct document saves. Determinism means the same starting bytes and editor operation produce the same ending bytes; it does not mean every manually edited message is reformatted.

Before declaring the public preview ready, maintainers must run the bounded [translator usability test](translator-usability-test.md) and post its anonymized results to the release issue. Automated smoke tests do not substitute for that human gate.

## Diagnostics and privacy

The About dialog reports product version, update channel, source revision, runtime identifier, operating system, and architecture. Diagnostic bundles include application/runtime identity, catalog/schema metadata, counts, compiler success, editor-state availability, diagnostic severity counts, and notices. They contain at most three entries, 256 diagnostic groups, 1 MiB per copied legal notice, and 2 MiB after compression. Creation errors are generic so filesystem details are not exposed.

They exclude workspace roots, relative file paths, diagnostic messages, JSON source, translation text, review notes, sample arguments, and recent-project history. Nothing is uploaded automatically.

## Per-user local state

The packaged browser or WebView profile stores no durable editor state. Preferences, recent-project metadata, and crash-recovery drafts are held in one native per-user application-data record, independently of the browser origin or profile. It contains no account, telemetry, machine-translation provider, or background synchronization state: providers are unavailable in this preview and therefore cannot receive customer text or request consent. **About & diagnostics** offers a privacy-bounded inventory (entry counts and byte totals only) and a **Clear local state** action. Clearing removes only this editor’s application-owned records; it never changes workspace files and deliberately leaves work already open in the current window in memory.

Diagnostic ZIPs use the same per-user application-data root rather than a temporary directory. The About dialog exposes their path and explicit **Reveal location**, **Copy path**, and **Delete bundle** controls. The application never uploads a bundle; sharing it remains a separate user action.
