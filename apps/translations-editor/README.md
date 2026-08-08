# Runic Translations Editor

This is the first complete consuming application for Runic Text Resources. It
is also the foundation for the customer-facing editor: the normal workflow is
optimized for translators, while schema-v2 structures and raw JSON remain
available to developers and localization specialists.

The implementation roadmap is in
[`docs/translations-editor-plan.md`](../../docs/translations-editor-plan.md).

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
single-locale workspace, asks the real compiler to validate valid and invalid
drafts, performs an atomic save, and verifies stale-write rejection. It does not
open a browser window.

## Deliberate current scope

This first vertical edits an existing schema-v2 catalog and its existing locale
documents. Catalog creation, adding or removing locale documents, translation
memory, review/approval workflows, machine translation providers, and project
discovery across multiple catalogs are product follow-ups rather than hidden
filesystem behavior in this initial consumer.
