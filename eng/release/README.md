# Releasing Runic Translations SDK

Runic Translations releases six NuGet packages together: `Runic.Translations`,
`Runic.Translations.Build`, `Runic.Translations.Tooling`,
`Runic.Translations.Wpf`, `Runic.Translations.Templates`, and
`dotnet-runic-translations`. It also releases the Vite, Svelte, and SvelteKit
packages listed in `eng/workspace.json`. The editor and IDE extensions are
verified with the release source, but are not registry packages in this workflow.

Set the intended preview version in both `eng/Versions.props` and
`eng/workspace.json`, then dispatch **Publish preview** from `main` with that
exact version. The workflow creates candidates, verifies isolated consumers,
publishes missing package versions, and creates a GitHub prerelease from those
same package files. Until 1.0, every release is a preview, so the workflow then
moves npm `latest` to it, never to an older version.

After publishing, move each library's `PublicAPI.Unshipped.txt` entries into
`PublicAPI.Shipped.txt`, set `RunicTranslationsPackageValidationBaselineVersion`
in `eng/Versions.props` to the published version, and delete any
`CompatibilitySuppressions.xml` files, which describe breaks from the old
baseline. See [Public API](../../CONTRIBUTING.md#public-api).

Before the first standalone release, configure NuGet and npm trusted publishers
for `Runic-Artifex/runic-translations-sdk`, `publish-preview.yml`, and the
`preview` environment. Each npm trusted publisher also needs **Allow npm dist-tag**
enabled. New repository ownership does not inherit trusted publisher
configuration from the former SDK repository.
