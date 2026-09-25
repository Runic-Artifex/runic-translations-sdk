# Source builds and archive guidance

The supported way to use Runic Translations Editor is to build it from the SDK
source, as described in the [editor README](../README.md). SDK package publication
does not create an Editor archive.

If an archive is produced for `linux-x64`, `win-x64`, or `osx-arm64`, it should be
self-contained: the .NET runtime, Runic Views CS-WebUI host, required native
browser assets, static SvelteKit application, launchers, example workspace, license,
third-party notices, per-file manifest, and sibling SHA-256 checksum travel together.
Users should not need an SDK, Node.js installation, package-registry authentication,
or a separate runtime to start it.

Archive verification should exercise the artifact a user receives, not only its
staging directory: compare reproducible archive digests, verify its checksum,
extract it into a clean directory, verify every file against `package-manifest.json`,
and run executable, launcher, and editor-smoke validation. The executable and
manifest should identify the version, channel, source revision, and runtime
identifier used to build them.

## Download, trust, and updates

No public Editor archive is currently claimed. An archive should provide one
same-named `.sha256` file and clear platform instructions.

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

The launcher accepts `edit [workspace]` and `validate [workspace]`; without
arguments it edits the current directory. A workspace contains one `runic.json`
translation project, which can select legacy MF2 or `rmf2-v1`.

## Availability and trust

This repository is the authority for Editor source and any Editor archive. The
[Runic Translations](https://github.com/Runic-Artifex/runic-translations)
repository publishes compiler/runtime/tool packages consumed here, but does not
package or release this application.

The editor performs no update request and never changes itself. Replacing an
extracted archive is a manual action. No public signing, notarization, or automatic
update capability is claimed.

Windows may show an unknown-publisher warning. macOS Gatekeeper may prevent an
unsigned archive from starting under the machine's policy. Do not bypass those
controls; use a source build when policy forbids an unsigned archive.

## Validation and reviewable diffs

The validation invocation for an extracted archive or source-built launcher is:

```bash
./runic-translations-editor validate /path/to/workspace
```

On Windows, use `runic-translations-editor.cmd` with the same arguments. This command constructs the same `EditorWorkspace` and calls the same compiler-backed load path as the editor UI. A save is separately validated against the same compiler with its in-memory draft substituted before atomic replacement. There is no weaker editor-only schema or validator.

Editor smoke tests apply identical key and review operations to two clean workspace copies and require byte-identical outputs. They also require structural changes to touch only the expected locale documents and review metadata to appear in one separate editor-state sidecar. Local `./verify.sh` snapshots the Git diff and status before generation and fails if verification introduces an unreviewed tracked or untracked change.

The editor intentionally preserves a translator's MF2 formatting for direct document saves. Determinism means the same starting bytes and editor operation produce the same ending bytes; it does not mean every manually edited message is reformatted.

The bounded [translator usability study](translator-usability-test.md) can reveal
workflow and accessibility problems that automated smoke tests cannot. Its findings
inform focused product work; it is not a release gate.

## Diagnostics and privacy

The About dialog reports product version, update channel, source revision, runtime identifier, operating system, and architecture. Diagnostic bundles include application/runtime identity, catalog/schema metadata, counts, compiler success, editor-state availability, diagnostic severity counts, and notices. They contain at most three entries, 256 diagnostic groups, 1 MiB per copied legal notice, and 2 MiB after compression. Creation errors are generic so filesystem details are not exposed.

They exclude workspace roots, relative file paths, diagnostic messages, JSON source, translation text, review notes, sample arguments, and recent-project history. Nothing is uploaded automatically.

## Per-user local state

The packaged browser or WebView profile stores no durable editor state. Preferences, recent-project metadata, and crash-recovery drafts are held in one native per-user application-data record, independently of the browser origin or profile. It contains no account, telemetry, machine-translation provider, or background synchronization state: providers are unavailable in this preview and therefore cannot receive customer text or request consent. **About & diagnostics** offers a privacy-bounded inventory (entry counts and byte totals only) and a **Clear local state** action. Clearing removes only this editor’s application-owned records; it never changes workspace files and deliberately leaves work already open in the current window in memory.

Diagnostic ZIPs use the same per-user application-data root rather than a temporary directory. The About dialog exposes their path and explicit **Reveal location**, **Copy path**, and **Delete bundle** controls. The application never uploads a bundle; sharing it remains a separate user action.
