![Runic Translations Editor banner](.github/assets/brand/banner.png)

# Runic Translations Editor

Translate and review a Runic Translations workspace in a purpose-built desktop editor instead of hand-editing JSON. Browse locale coverage, search messages, edit structured variants and variables, preview changes, manage review state, and resolve compiler feedback in one place.

Runic Translations Editor is a companion to [Runic Translations](https://github.com/Runic-Artifex/runic-translations). The editor manages workspaces; the compiler, schema, runtime, CLI, and language integrations are published by the main project.

## Preview availability

The first public preview has not been published yet. [GitHub Releases](https://github.com/Runic-Artifex/runic-translations-editor/releases) will be the canonical download location when it is ready; until a release appears there, do not trust archives distributed elsewhere. Source development uses the local candidate feeds documented below; it does not use GitHub Packages.

The planned preview artifacts are:

| Platform | Architecture | Archive |
| --- | --- | --- |
| Linux | x64 | `Runic.Translations.Editor-*-linux-x64.tar.gz` |
| Windows | x64 | `Runic.Translations.Editor-*-win-x64.zip` |
| macOS | Apple silicon | `Runic.Translations.Editor-*-osx-arm64.tar.gz` |

Published archives will be self-contained and include the .NET runtime, Runic Desktop presentation host, the SvelteKit application, launchers, and an example workspace. Archive users will not need Node.js, a .NET SDK, package-registry credentials, or a separately installed runtime. The editor opens in an installed browser by default; pass `--webview` to request its embedded WebView.

### Verify a future preview archive

Preview archives will initially be unsigned. After a release is published, verify its checksum before extraction, keep a backup or commit of translation files before editing, and do not bypass an organizational security policy to run an unknown-publisher application.

```bash
# Linux
sha256sum -c Runic.Translations.Editor-*-linux-x64.tar.gz.sha256
tar -xzf Runic.Translations.Editor-*-linux-x64.tar.gz
./Runic.Translations.Editor/runic-translations-editor edit /path/to/workspace

# macOS
shasum -a 256 Runic.Translations.Editor-*-osx-arm64.tar.gz
# Compare the printed digest with the first value in the sibling .sha256 file.
tar -xzf Runic.Translations.Editor-*-osx-arm64.tar.gz
./Runic.Translations.Editor/runic-translations-editor edit /path/to/workspace
```

```powershell
# Windows PowerShell
$archive = Get-ChildItem .\Runic.Translations.Editor-*-win-x64.zip | Select-Object -First 1
$actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$expected = (Get-Content "$($archive.FullName).sha256").Split(' ')[0]
if ($actual -ne $expected) { throw 'Checksum mismatch' }
Expand-Archive $archive
.\Runic.Translations.Editor\runic-translations-editor.cmd edit C:\path\to\workspace
```

On first launch, open the directory containing the catalog manifest and locale documents, choose the catalog when prompted, work through the editor diagnostics, and save only after they are resolved. Try the included [example workspace](https://github.com/Runic-Artifex/runic-translations-editor/tree/main/ExampleWorkspace) with `edit ExampleWorkspace` from the extracted application directory. Run `edit` without a workspace to open the current directory.

Future preview builds are evaluation builds: they will be neither code-signed nor notarized, update only when you manually download, verify, and replace the extracted application, and make no update requests or changes to themselves. The preview workflow produces review-only candidates and cannot create a public release. Windows may show an unknown-publisher warning and macOS Gatekeeper may block launch under local policy. See the [preview notice](https://github.com/Runic-Artifex/runic-translations-editor/blob/main/PREVIEW-NOTICE.md) and [distribution policy](https://github.com/Runic-Artifex/runic-translations-editor/blob/main/docs/editor-distribution.md) for the trust boundary and supported delivery details.

## Validate a workspace in CI

Once a preview is published, use its packaged launcher to run the same compiler-backed workspace load and diagnostics used by the editor:

```bash
./runic-translations-editor validate /path/to/workspace
./runic-translations-editor validate /path/to/workspace --catalog catalog-id
```

On Windows, replace the launcher with `runic-translations-editor.cmd`. The command returns `0` for a valid selected catalog, `1` for compiler diagnostics, no catalog, or an ambiguous multi-catalog workspace, and `2` when validation cannot start. Pass `--catalog <id>` whenever the directory contains more than one catalog.

## Headless interchange

Use `export` to write XLIFF or portable review JSON. Use `report` to inspect an import's reviewable diff and refusals without changing the workspace. An import is applied only when `--apply` is explicit; it previews and consumes the confirmation within one process, so no confirmation token is persisted or reusable.

```bash
./runic-translations-editor export /path/to/workspace --format xliff --output .runic-translations/export
./runic-translations-editor report /path/to/workspace --format xliff --source .runic-translations/export/catalog.en.xliff
./runic-translations-editor import /path/to/workspace --format xliff --source .runic-translations/export/catalog.en.xliff --apply

./runic-translations-editor export /path/to/workspace --format review --output .runic-translations/export/catalog.review.json
./runic-translations-editor report /path/to/workspace --format review --source .runic-translations/export/catalog.review.json
```

`--output` belongs to `export`; select the command response envelope with `--runic-output json` (or `RUNIC_COMMANDLINE_OUTPUT=json`). A report or import that is refused returns exit code `1` and lists its refusal codes in both human output and the JSON fault details.

## Local support diagnostics

Run `diagnostics <workspace>` to create the existing privacy-bounded diagnostic ZIP for an explicit local support collection. The command never uploads it. Use `dotnet runic support --mode preview|collect|remove --editor-diagnostics <zip> --destination <path>` to inspect, collect, or remove a local unsigned support envelope. The editor stores preferences, recents, and recovery drafts in one native per-user record, not a browser profile; it is atomically replaced and an unreadable record is quarantined on next launch. In the editor’s **About & diagnostics** panel, **Local editor state** reports only entry counts and bytes; use **Clear local state** to remove those records without changing workspace files or current in-memory work.

## What it supports

The editor creates schema-v2 projects; discovers, repairs, and edits catalogs and locale documents; manages locale graphs, keys, structured messages, and review metadata; and keeps diagnostics privacy-bounded. It validates drafts before atomic replacement, warns on external changes, and keeps equivalent editor operations deterministic from the same starting files. The editor intentionally preserves a translator's direct-document formatting rather than reformatting every JSON file.

Machine-translation providers and signed stable distribution are not available yet. For diagnostic-bundle contents, recovery behavior, and detailed determinism guarantees, see the [editor distribution documentation](https://github.com/Runic-Artifex/runic-translations-editor/blob/main/docs/editor-distribution.md).

## Build from source

Source development requires the .NET 10 SDK, Node.js, and npm. Sibling source projects are used automatically when they are available. Package-consumer fixtures provide the maintained isolated-candidate path: provide `RUNIC_EDITOR_NUGET_FEED`, `RUNIC_EDITOR_NPM_ARCHIVE`, and `RUNIC_EDITOR_COMPATIBILITY_SET` explicitly for the exact temporary local candidates. The verifier creates its own temporary NuGet configuration and caches; it never relies on a shared workspace feed or persistent user configuration. The canonical compatibility train is `1.0.0-preview.1`. Do not add a project `.npmrc`, configure GitHub credentials, or use GitHub Packages for local work.

For the repeatable package proof, stage the exact candidate `.nupkg` files in a fresh temporary directory and point the verifier at the canonical compatibility manifest:

```bash
RUNIC_EDITOR_NUGET_FEED=/tmp/runic-editor-feed \
RUNIC_EDITOR_NPM_ARCHIVE=/tmp/runic-artifex-vite-plugin-runic-translations-1.0.0-preview.1.tgz \
RUNIC_EDITOR_COMPATIBILITY_SET=/path/to/runic.compatibility-set.json \
  node eng/verify-localized-desktop-product.mjs run-twice > localized-desktop-receipt.json
```

```bash
dotnet tool restore
npm --prefix Frontend ci
dotnet run --project Runic.Translations.Editor.csproj -- edit ExampleWorkspace
```

For frontend-only development, build the .NET project once to produce the localized ESM module, then run:

```bash
RUNIC_TRANSLATIONS_MANIFEST=../obj/Debug/net10.0/translations/editor.esm/web-module-manifest-v1.json \
  npm --prefix Frontend run dev:mock
```

Mock mode keeps writes in memory. Run `./verify.sh` to build against released packages, check the Svelte application and production bundle, exercise the compiler/save/recovery path, and detect unintended generated changes.

## Support and license

Report reproducible editor problems through [GitHub Issues](https://github.com/Runic-Artifex/runic-translations-editor/issues). Please include your platform, archive version or `--version` output, and safe-to-share validation output; do not include translation text or workspace paths in public reports.

Runic Translations Editor is released under the [MIT License](https://github.com/Runic-Artifex/runic-translations-editor/blob/main/LICENSE). See [third-party notices](https://github.com/Runic-Artifex/runic-translations-editor/blob/main/THIRD-PARTY-NOTICES.md) for bundled dependency notices.
