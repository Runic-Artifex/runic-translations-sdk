![Runic Translations Editor banner](.github/assets/brand/banner.png)

# Runic Translations Editor

A focused desktop editor for [Runic Translations](https://github.com/Runic-Artifex/runic-translations) workspaces. The interface is designed around translators: locale coverage, message search, review state, structured variants, variables, previews, and quality feedback are available without editing JSON directly.

This repository owns the editor application, its cross-platform preview archives, and editor releases. The compiler, schema, runtime, command-line tooling, and language integrations remain in the Runic Translations repository. Existing `RunicTextResources.*` package and code identifiers are intentionally retained for compatibility.

## Run locally

The editor currently consumes preview dependencies from GitHub Packages. Export a GitHub token with package-read access, then restore and run:

```bash
export GITHUB_ACTOR="your-github-user"
export GITHUB_TOKEN="your-token"
export NODE_AUTH_TOKEN="$GITHUB_TOKEN"

dotnet tool restore
npm --prefix Frontend ci
dotnet run --project RunicTranslations.Editor.csproj -- \
  --workspace ExampleWorkspace
```

Add `--webview` to request an embedded WebView instead of the recommended installed browser. Pass any directory containing one catalog manifest and its resource documents to `--workspace`.

For frontend-only development, build the .NET project once so the localized ESM module exists, then run:

```bash
RUNIC_TEXT_MANIFEST=../obj/Debug/net10.0/text-resources/editor.esm/web-module-manifest-v1.json \
  npm --prefix Frontend run dev:mock
```

Mock mode keeps writes in memory.

## Verify

```bash
./verify.sh
```

Verification builds against released packages only, checks the Svelte application and production bundle, runs the editor's compiler/save/recovery smoke path, and rejects unintended generated changes.

## Package a preview

The matching-OS packaging script creates and starts a self-contained archive that requires neither Node.js nor a separately installed .NET runtime:

```bash
pwsh -NoProfile -File ./eng/package-editor.ps1 \
  -RuntimeIdentifier linux-x64 \
  -OutputDirectory ./artifacts/editor \
  -Version 0.1.0-preview.local \
  -RepositoryCommit "$(git rev-parse HEAD)"
```

See [distribution and release policy](docs/editor-distribution.md) for supported targets, manifests, checksums, update channels, and the signing boundary.

## Current scope

The editor creates schema-v2 projects; discovers, repairs, and edits catalogs and locale documents; manages locale graphs, keys, structured messages, and review metadata; and produces privacy-bounded diagnostics. Machine-translation providers and signed stable distribution remain explicit follow-ups.
