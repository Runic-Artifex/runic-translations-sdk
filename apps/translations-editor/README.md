> This package is now developed in the [Runic SDK workspace](../../README.md). Use its root build and verification commands.

![Runic Translations Editor banner](.github/assets/brand/banner.png)

# Runic Translations Editor

Translate and review the same MessageFormat 2 project that application builds consume. Browse locale coverage, search messages, edit patterns and variables, preview changes, manage review state, and resolve compiler feedback in one place while keeping ordinary `.mf2` files readable in Git and any text editor.

Runic Translations Editor is a companion to [Runic Translations](https://github.com/Runic-Artifex/runic-translations). The editor manages workspaces; the compiler, schema, runtime, CLI, and language integrations are published by the main project.

## Preview availability

The first public preview has not been published yet. [GitHub Releases](https://github.com/Runic-Artifex/runic-translations-editor/releases) will be the canonical download location when it is ready; until a release appears there, do not trust archives distributed elsewhere. Source development uses the shared SDK workspace.

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

On first launch, open the directory containing `translations/runic.json` (or the
`translations` directory itself). The editor discovers locale directories and
their `.mf2` messages through the same compiler path as MSBuild and Vite. Work
through diagnostics and save only after they are resolved. Try the included
[example workspace](https://github.com/Runic-Artifex/runic-translations-editor/tree/main/ExampleWorkspace)
with `edit ExampleWorkspace` from the extracted application directory. Run
`edit` without a workspace to open the current directory.

Future preview builds are evaluation builds: they will be neither code-signed nor notarized, update only when you manually download, verify, and replace the extracted application, and make no update requests or changes to themselves. The preview workflow produces review-only candidates and cannot create a public release. Windows may show an unknown-publisher warning and macOS Gatekeeper may block launch under local policy. See the [preview notice](https://github.com/Runic-Artifex/runic-translations-editor/blob/main/PREVIEW-NOTICE.md) and [distribution policy](https://github.com/Runic-Artifex/runic-translations-editor/blob/main/docs/editor-distribution.md) for the trust boundary and supported delivery details.

## Validate a workspace in CI

Once a preview is published, use its packaged launcher to run the same compiler-backed workspace load and diagnostics used by the editor:

```bash
./runic-translations-editor validate /path/to/workspace
```

On Windows, replace the launcher with `runic-translations-editor.cmd`. The command returns `0` for a valid Runic project, `1` for compiler diagnostics or a missing project, and `2` when validation cannot start. A workspace has one conventional `runic.json` project source.

## Headless interchange

Use `export` to write XLIFF or portable review JSON. Use `report` to inspect an import's reviewable diff and refusals without changing the workspace. An XLIFF import updates the target locale's conventional MF2 message files. An import is applied only when `--apply` is explicit; it previews and consumes the confirmation within one process, so no confirmation token is persisted or reusable.

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

The editor opens conventional `runic.json` projects, validates and saves their
MF2 messages, watches external changes, and previews through the canonical
compiler model. Diagnostics remain privacy-bounded and writes use revision
checks plus atomic replacement.

Machine-translation providers and signed stable distribution are not available yet. For diagnostic-bundle contents, recovery behavior, and detailed determinism guarantees, see the [editor distribution documentation](https://github.com/Runic-Artifex/runic-translations-editor/blob/main/docs/editor-distribution.md).

## Build from source

Use the shared SDK toolchain and run these commands from its root:

```sh
bun run bootstrap
bun run build
bun run dev:editor
```

All Runic dependencies resolve from source in the workspace. `bun run test` checks
the frontend, generated contract, editor save/recovery smoke and example workspace.
`bun run verify-packages` checks the SDK's isolated package consumers. See the
[contributor guide](../../CONTRIBUTING.md) for the complete verification sequence.

For frontend-only development, build once to generate the localized ESM module,
then run this from the SDK root:

```sh
RUNIC_TRANSLATIONS_MANIFEST="$PWD/apps/translations-editor/obj/Debug/net10.0/translations/editor.esm/web-module-manifest-v1.json" \
  bun run --cwd apps/translations-editor/Frontend dev:mock
```

Mock mode keeps writes in memory. Former standalone release and candidate-feed
scripts are retained as [engineering history](../../eng/archive/README.md).

## Support and license

Report reproducible editor problems through [GitHub Issues](https://github.com/Runic-Artifex/runic-translations-editor/issues). Please include your platform, archive version or `--version` output, and safe-to-share validation output; do not include translation text or workspace paths in public reports.

Runic Translations Editor is released under the [MIT License](https://github.com/Runic-Artifex/runic-translations-editor/blob/main/LICENSE). See [third-party notices](https://github.com/Runic-Artifex/runic-translations-editor/blob/main/THIRD-PARTY-NOTICES.md) for bundled dependency notices.
