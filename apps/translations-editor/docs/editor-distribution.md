# Downloads, packages and releases

Runic Translations Editor has releases of its own, built from this repository's source
by [`release-editor.yml`](../../../.github/workflows/release-editor.yml) and attached to
a GitHub release tagged `editor-v<version>` (D078). Each release has packages for
`linux-x64`, `osx-arm64` and `win-x64`, each with a sibling `.sha256` file. You can
also [build the editor from source](../README.md#build-from-source).

## Versions

The editor has its own version line, plain `0.y.z` before 1.0, starting at 0.1.0. It
is independent of the version of the Runic Translations packages
(`RunicTranslationsVersion` in `eng/Versions.props`): an editor release is built from
the Translations source of the same commit, and the editor's release notes say which
Translations release that matches. The version is `RunicTranslationsEditorVersion` in
[`Directory.Build.props`](../Directory.Build.props). `--version`, **About &
diagnostics** and diagnostic bundles show it with the update channel (`preview`
before 1.0), the source revision and the runtime identifier:

```text
$ runic-translations-editor --version
Runic Translations Editor 0.1.0+<commit>
Channel: preview
Commit: <commit>
Runtime: linux-x64
```

The editor's releases are never marked "Latest" on GitHub, so that slot stays with
the SDK's releases, and before 1.0 they are prereleases, as the SDK's are. Its notes
are in `eng/release/notes/editor/` ([Release notes](../../../eng/release/README.md#release-notes)).

## What a release contains

| File | Built with | Contents |
| --- | --- | --- |
| `runic-translations-editor_<version>_amd64.deb` | `dotnet runic package deb` | The editor in `/usr/lib/eu.runicartifex.TranslationsEditor`, `/usr/bin/Runic.Translations.Editor`, the desktop entry, icon and AppStream metadata. Depends on `libgtk-4-1` and `libwebkitgtk-6.0-4` (Ubuntu 24.04, Debian 13). |
| `runic-translations-editor-<version>-1.x86_64.rpm` | `dotnet runic package rpm` | The same files. Requires `webkitgtk6.0` (Fedora). |
| `runic-translations-editor-<version>-x86_64.AppImage` | `dotnet runic package appimage` | The same files in an AppImage, which uses the system's GTK 4 and WebKitGTK 6. |
| `runic-translations-editor-<version>-linux-x64.tar.gz` | the deb's files | The application folder with the `runic-translations-editor` launcher and `package-manifest.json`. |
| `runic-translations-editor-<version>-osx-arm64.dmg` | `dotnet runic package macos` | `Runic Translations Editor.app`, ad-hoc signed, for Apple silicon. |
| `runic-translations-editor-<version>-win-x64.zip` | `dotnet publish --self-contained` | The application folder with the `runic-translations-editor.cmd` launcher and `package-manifest.json`. |

The Linux and macOS editors are NativeAOT executables, made by runic-sdk's
`dotnet runic package` at the runic-sdk version the editor is built on
(`RunicSdkDependencyVersion`). The Windows editor is a self-contained .NET
application. None needs an SDK, Node.js or a separate .NET runtime. Every package
carries the static editor frontend, the example workspace, the licence, the
third-party notices and the preview notice. The window's WebView comes from the
system: GTK 4.12 or newer with WebKitGTK 6 on Linux, the Microsoft Edge WebView2
Runtime on Windows, and WKWebView on macOS. Without it, `edit` opens the editor in
an installed browser instead and says why on standard error.

There is no Flatpak: building one installs the GNOME runtime and SDK from Flathub on
every release build, and its sandbox would need filesystem permissions of its own for
an editor that writes workspaces anywhere on disk. There is no MSIX either, because
Windows installs only signed MSIX packages.

### Checksums and the package manifest

`<file>.sha256` holds one line, `<sha256>  <file>`, as `sha256sum` writes it. The
`.tar.gz` and `.zip` archives also hold `package-manifest.json`
(`runic.translations.editor-package/1`): the version, channel, runtime identifier,
repository, source commit and tree, and the size and SHA-256 of every other file in
the archive. The release workflow writes it from the built editor's own `--version`
and refuses a build that reports another version or commit.

## Download and install

Check the download first. On Linux, `sha256sum -c <file>.sha256`; on macOS,
`shasum -a 256 -c <file>.sha256`; in Windows PowerShell:

```powershell
$file = 'runic-translations-editor-0.1.0-win-x64.zip'
$actual = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
$expected = (Get-Content "$file.sha256").Split(' ')[0]
if ($actual -ne $expected) { throw 'Checksum mismatch' }
```

**Debian and Ubuntu:** `sudo apt install ./runic-translations-editor_0.1.0_amd64.deb`.
**Fedora:** `sudo dnf install ./runic-translations-editor-0.1.0-1.x86_64.rpm`. Both
add the editor to the applications menu and `Runic.Translations.Editor` to the
`PATH`; remove them with `sudo apt remove runic-translations-editor` or
`sudo dnf remove runic-translations-editor`.

**AppImage:** install WebKitGTK 6 (`libwebkitgtk-6.0-4` on Debian and Ubuntu,
`webkitgtk6.0` on Fedora, `libwebkitgtk-6_0-4` on openSUSE, `webkitgtk-6.0` on Arch
Linux), then `chmod +x` the file and run it. A tool such as Gear Lever or
AppImageLauncher adds it to the applications menu. Without FUSE 2, run it with
`--appimage-extract-and-run`.

**Linux archive:**

```bash
tar -xzf runic-translations-editor-0.1.0-linux-x64.tar.gz
./runic-translations-editor-0.1.0-linux-x64/runic-translations-editor edit /path/to/workspace
./runic-translations-editor-0.1.0-linux-x64/runic-translations-editor desktop-entry install
```

**macOS:** open the disk image and drag **Runic Translations Editor** to
Applications. The bundle is ad-hoc signed, not with a Developer ID, and not
notarized, so Gatekeeper refuses the first start of a downloaded copy ("cannot be
opened because Apple cannot check it for malicious software"). After checking the
checksum, either open **System Settings → Privacy & Security**, find the message
about Runic Translations Editor and choose **Open Anyway** (macOS asks once more),
or remove the quarantine attribute:

```bash
xattr -dr com.apple.quarantine "/Applications/Runic Translations Editor.app"
```

**Windows:** extract the zip (in PowerShell,
`Expand-Archive runic-translations-editor-0.1.0-win-x64.zip -DestinationPath .`) and
start `runic-translations-editor-0.1.0-win-x64\runic-translations-editor.cmd`. The
files are not code-signed, so Microsoft Defender SmartScreen may show "Windows
protected your PC" for a downloaded file: choose **More info**, then **Run anyway**.
Unblocking the zip before extracting it (**Properties → Unblock**, or
`Unblock-File` in PowerShell) avoids the question for every file.

The launcher accepts `edit [workspace]` and `validate [workspace]`; without
arguments it edits the current directory. Started from the applications menu, the
editor opens its example workspace. A workspace contains one `runic.json`
translation project, which can use direct MF2 or grouped RMF2 resources.

## Trust and updates

The packages are unsigned (D078.4): there is no paid code-signing certificate or
Apple Developer ID behind them, and macOS packages are not notarized. What you can
check is the `.sha256` file of each download, the `package-manifest.json` of an
archive, and the source commit that `--version` reports, which the release's tag
names. Where a machine's policy forbids unsigned
applications, build the editor from source.

The editor performs no update request and never changes itself. To update, install
the new package over the old one, or replace an extracted archive. The deb and rpm
are not served from a package repository, so `apt upgrade` and `dnf upgrade` do not
update them. Preferences, recent projects and drafts stay in the per-user state
described below.

## Releasing the editor

Every step that publishes needs the maintainer's go-ahead, each time.

1. **Prepare.** In a pull request, set `RunicTranslationsEditorVersion` in
   `apps/translations-editor/Directory.Build.props`, write the introduction in
   `eng/release/notes/editor/<version>/_intro.md`, run
   `bun eng/release/notes.mjs --editor assemble <version>`, review the assembled
   `eng/release/notes/editor/<version>.md` and commit it with the removed fragments.
   The pull request's **Release editor** run builds and checks every package as a dry run.
2. **Merge** it once every check has passed.
3. **Tag**, with the maintainer's approval: `git tag editor-v<version> <commit>` on that
   `main` commit and `git push origin editor-v<version>`. The tag starts **Release
   editor** and nothing else; the SDK's CI and Publish preview do not run for
   `editor-v*` tags. The workflow refuses a tag that does not name the editor's
   version, and notes that are still fragments.
4. **Review the draft.** After the packages of all three systems have been built,
   installed or extracted, and opened on their runners, the workflow creates a draft
   release named after the tag, with the editor's notes, as a prerelease before 1.0,
   and not marked Latest. Download and try the packages.
5. **Publish** the draft from the releases page, with the maintainer's approval.

**Release editor** can also be dispatched: with **dry-run** (the default) it builds and
checks every package of a branch or tag without releasing; dispatched on an
`editor-v<version>` tag with **dry-run** off, it drafts that tag's release, for
example after a failed run. A rerun replaces the assets of a draft and never changes a
published release.

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
