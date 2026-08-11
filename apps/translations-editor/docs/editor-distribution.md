# Editor distribution

Runic Translations Editor preview builds are self-contained archives for `linux-x64`, `win-x64`, and `osx-arm64`. Each archive contains the .NET runtime, native CS-WebUI/WebUI assets, the static SvelteKit application, launchers, an example workspace, the license, third-party notices, a per-file manifest, and a sibling SHA-256 checksum. Customer machines need no SDK, Node.js installation, package-registry authentication, or separate runtime.

`eng/package-editor.ps1` performs a matching-OS publish, creates the archive, verifies its sibling checksum, extracts it into a clean temporary directory, verifies every file against `package-manifest.json`, runs headless compiler validation, starts the public launcher, and runs the complete editor smoke workflow. Tests therefore exercise the artifact a customer downloads, not only the publish staging directory. The executable and manifest both carry the exact version, `preview` update channel, source commit, and runtime identifier supplied by CI.

## Download, trust, and updates

Preview artifacts are published on [GitHub Releases](https://github.com/Runic-Artifex/runic-translations-editor/releases). The release page displays `PREVIEW-NOTICE.md`, and the same notice is included in every archive. Download exactly one platform archive and its file with the same name plus `.sha256`.

On Linux:

```bash
sha256sum -c RunicTranslations.Editor-0.1.0-preview.N-linux-x64.tar.gz.sha256
tar -xzf RunicTranslations.Editor-0.1.0-preview.N-linux-x64.tar.gz
./RunicTranslations.Editor/runic-translations-editor edit /path/to/workspace
```

On macOS:

```bash
shasum -a 256 RunicTranslations.Editor-0.1.0-preview.N-osx-arm64.tar.gz
# Compare the printed digest with the first field in the sibling .sha256 file.
tar -xzf RunicTranslations.Editor-0.1.0-preview.N-osx-arm64.tar.gz
./RunicTranslations.Editor/runic-translations-editor edit /path/to/workspace
```

On Windows PowerShell:

```powershell
$actual = (Get-FileHash .\RunicTranslations.Editor-0.1.0-preview.N-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
$expected = (Get-Content .\RunicTranslations.Editor-0.1.0-preview.N-win-x64.zip.sha256).Split(' ')[0]
if ($actual -ne $expected) { throw 'Checksum mismatch' }
Expand-Archive .\RunicTranslations.Editor-0.1.0-preview.N-win-x64.zip
.\RunicTranslations.Editor\runic-translations-editor.cmd edit C:\path\to\workspace
```

The launcher accepts `edit [workspace]` and `validate [workspace]`; without arguments it edits the current directory. For a multi-catalog directory, add `--catalog <id>`.

## Release boundary

This repository is the only publisher of editor archives and editor GitHub releases. The [Runic Translations](https://github.com/Runic-Artifex/runic-translations) repository publishes the compiler/runtime/tool packages consumed here, but does not package or release this application.

Preview archives are unsigned evaluation builds and update by replacing the complete extracted application. The editor performs no update request and never changes itself. Stable releases remain disabled until Windows code-signing and Apple Developer ID/notarization credentials, protected release approval, post-signing startup tests, and build-provenance attestations are available.

Windows may show an unknown-publisher warning. macOS Gatekeeper may prevent an unsigned application from starting under the machine's policy. The preview does not ask users to suppress those controls; use a source build or wait for a signed release when policy forbids unsigned software.

## Validation and reviewable diffs

The supported CI invocation from an extracted archive is:

```bash
./runic-translations-editor validate /path/to/workspace --catalog optional-catalog-id
```

On Windows, use `runic-translations-editor.cmd` with the same arguments. This command constructs the same `EditorWorkspace` and calls the same compiler-backed load path as the editor UI. A save is separately validated against the same compiler with its in-memory draft substituted before atomic replacement. There is no weaker editor-only schema or validator.

Editor smoke tests apply identical key and review operations to two clean workspace copies and require byte-identical outputs. They also require structural changes to touch only the expected locale documents and review metadata to appear in one separate editor-state sidecar. Local `./verify.sh` snapshots the Git diff and status before generation and fails if verification introduces an unreviewed tracked or untracked change.

The editor intentionally preserves a translator's formatting for direct document saves. Determinism means the same starting bytes and editor operation produce the same ending bytes; it does not mean every manually edited JSON document is reformatted.

Before declaring the public preview ready, maintainers must run the bounded [translator usability test](translator-usability-test.md) and post its anonymized results to the release issue. Automated smoke tests do not substitute for that human gate.

## Diagnostics and privacy

The About dialog reports product version, update channel, source revision, runtime identifier, operating system, and architecture. Diagnostic bundles include application/runtime identity, catalog/schema metadata, counts, compiler success, editor-state availability, diagnostic severity counts, and notices.

They exclude workspace roots, relative file paths, diagnostic messages, JSON source, translation text, review notes, sample arguments, and recent-project history. Nothing is uploaded automatically.
