## Packages for Linux, macOS and Windows

The editor now comes as packages of its own, built from this repository's source by
`.github/workflows/release-editor.yml` and attached to the editor's GitHub release. None needs a .NET runtime, an SDK
or Node.js:

- **Linux (x64):** a deb (Debian, Ubuntu) and an rpm (Fedora), which install the editor in
  `/usr/lib/eu.runicartifex.TranslationsEditor` with its desktop entry, icon and AppStream metadata and depend on the
  system's GTK 4 and WebKitGTK 6; an AppImage, which uses the same system libraries; and a `.tar.gz` archive. The
  editor is a NativeAOT executable, built with runic-sdk's `dotnet runic package`.
- **macOS (Apple silicon):** a disk image with the ad-hoc signed application bundle.
- **Windows (x64):** a self-contained `.zip` with the `runic-translations-editor.cmd` launcher. It is not an MSIX,
  because Windows installs only signed MSIX packages.

Every file has a sibling `.sha256` file, and the archives carry a `package-manifest.json` with the version, channel,
source revision and the SHA-256 of every file. The packages are unsigned (D078.4): there is no paid certificate behind
them. The release workflow installs or extracts each package on a runner of its system and opens the editor's window
before the release is drafted.
