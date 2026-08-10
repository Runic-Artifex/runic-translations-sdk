# Runic Translations Editor public preview

This archive is a self-contained **unsigned evaluation build**. It needs neither Node.js nor a separately installed .NET runtime. Extract the whole archive before starting it.

- Verify the sibling `.sha256` file before extracting the archive.
- Start the current directory with `runic-translations-editor edit .` (or `runic-translations-editor.cmd edit .` on Windows).
- Validate it in CI with `runic-translations-editor validate .`.
- Updates are manual: download, verify, and replace the complete extracted application. The editor never updates itself.
- Preview archives are not code-signed or notarized. Operating systems may show an unknown-publisher warning. Do not bypass an organizational security policy to run them.
- Preview builds receive best-effort migration guidance, but no stable compatibility promise. Back up or commit translation files before editing.

The archive includes `package-manifest.json`, with the version, source commit, runtime identifier, and a SHA-256 digest for every shipped file. See the [distribution policy and verification instructions](https://github.com/Runic-Artifex/runic-translations-editor/blob/main/docs/editor-distribution.md).
