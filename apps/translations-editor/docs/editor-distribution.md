# Editor distribution

Runic Translations Editor preview builds are self-contained archives for `linux-x64`, `win-x64`, and `osx-arm64`. Each archive contains the .NET runtime, native CsWebUi/WebUI assets, the static SvelteKit application, an example workspace, the license, third-party notices, a per-file manifest, and a sibling SHA-256 checksum. Customer machines need no SDK, Node.js installation, or separate runtime.

`eng/package-editor.ps1` performs a matching-OS publish, starts the packaged executable, and runs the complete editor smoke workflow. The executable and `package-manifest.json` both carry the exact version, `preview` update channel, source commit, and runtime identifier supplied by CI.

## Release boundary

This repository is the only publisher of editor archives and editor GitHub releases. The [Runic Translations](https://github.com/Runic-Artifex/runic-translations) repository publishes the compiler/runtime/tool packages consumed here, but does not package or release this application.

Preview archives are unsigned evaluation builds and update by replacing the complete extracted application. The editor performs no update request and never changes itself. Stable releases remain disabled until Windows code-signing and Apple Developer ID/notarization credentials, protected release approval, post-signing startup tests, and build-provenance attestations are available.

## Diagnostics and privacy

The About dialog reports product version, update channel, source revision, runtime identifier, operating system, and architecture. Diagnostic bundles include application/runtime identity, catalog/schema metadata, counts, compiler success, editor-state availability, diagnostic severity counts, and notices.

They exclude workspace roots, relative file paths, diagnostic messages, JSON source, translation text, review notes, sample arguments, and recent-project history. Nothing is uploaded automatically.

