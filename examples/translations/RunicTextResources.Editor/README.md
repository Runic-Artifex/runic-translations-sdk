# Runic Translations Editor

This is the first complete consuming application for Runic Text Resources. It
is also the foundation for the customer-facing editor: the normal workflow is
optimized for translators, while schema-v2 structures and raw JSON remain
available to developers and localization specialists.

The implementation roadmap is in
[`docs/translations-editor-plan.md`](../../docs/translations-editor-plan.md).
The self-contained preview, update-channel, diagnostics, signing, and provenance
contract is in
[`docs/editor-distribution.md`](../../docs/editor-distribution.md).

## What the vertical proves

- the C# host discovers and bounds a customer workspace;
- the canonical compiler validates every draft before a save;
- saves use revision checks and same-directory atomic replacement;
- one-locale catalogs have the same focused workflow as multi-locale catalogs;
- locale fallback, coverage, missing translations, warnings, and errors are
  visible without reading JSON;
- SvelteKit consumes the generated, tree-shakeable editor ESM through the Vite
  adapter;
- CsWebUi serves the static SvelteKit application and exposes the narrow file
  operation bridge.
- a guided New Project flow previews and creates one- or multi-locale projects
  through the same compiler-validated authoring layer as the CLI.
- locale/key lifecycle changes are previewed and committed through recoverable
  workspace transactions;
- the optional versioned editor-state sidecar carries review states, notes,
  preview samples, and terminology without becoming a compiler input;
- stale-source and local quality checks, translation-memory suggestions,
  deterministic CSV reports, bulk workflow changes, and bounded message-list
  rendering support large catalogs without requiring a network service.

The included `ExampleWorkspace` deliberately uses German as its source locale,
adds English and French, includes a structured plural message, and leaves some
French translations missing. A customer with only German can declare only the
`de` locale and use the same editor without comparison-only UI.

## Run

From the repository root:

```bash
nix develop
dotnet run --project samples/RunicTextResources.Editor -- \
  --workspace samples/RunicTextResources.Editor/ExampleWorkspace
```

Add `--webview` to request an embedded WebView instead of the recommended
installed browser. Pass any directory containing one catalog manifest and its
resource documents to `--workspace`.

The frontend can be developed without a native host:

```bash
cd samples/RunicTextResources.Editor/Frontend
RUNIC_TEXT_MANIFEST=../obj/Debug/net10.0/text-resources/editor.esm/web-module-manifest-v1.json \
  npm run dev:mock
```

Build the .NET project once first so the editor's own localized ESM modules
exist. Mock mode keeps writes in memory.

## Verify

```bash
nix develop -c ./samples/RunicTextResources.Editor/verify.sh
```

The smoke path opens a copy of the example as both a multi-locale and
single-locale workspace, creates and switches to a new three-locale project,
asks the real compiler to validate valid and invalid drafts, performs an atomic
save, round-trips the optional review sidecar, and verifies stale-write
rejection. The frontend verification also enforces a 50,000-message quality and
search pass across 100 review locales, a 10-second processing budget, a 256 MiB
heap-growth budget, and a 300-row rendering batch. It does not open a browser
window.

## Self-contained preview

The matching-OS packaging script publishes and launches a complete application
that needs neither Node.js nor a separately installed .NET runtime:

```bash
nix develop -c pwsh -NoProfile -File ./eng/package-editor.ps1 \
  -RuntimeIdentifier linux-x64 \
  -OutputDirectory ./artifacts/editor
```

The About dialog shows the embedded version/channel/commit and creates a
privacy-bounded diagnostic zip. Preview archives also include a per-file hash
manifest, archive checksum, license, and third-party notices.

## Deliberate current scope

This vertical creates a new schema-v2 catalog, discovers and selects catalogs,
repairs malformed documents, and edits locale documents, locale graphs, keys,
structured messages, and review metadata. Machine-translation providers and
signed customer distribution remain explicit product follow-ups.
