# Runic Translations Editor availability

Standalone Editor distributions are outside the SDK preview. No public Editor
archive, signing, notarization or automatic update capability is claimed.
Build and run the editor from the SDK source as described in [README.md](README.md).

The source-built Editor is the public demonstration for English and German
localization and parameterized MF2 count messages. Its resources use one TOML file
per locale with `sourceLayout: "locale-toml"` in `runic.json`. Automated UI
acceptance is still being completed; final coverage and interaction acceptance
are not claimed here.

Back up or commit translation files before evaluating editing or interchange flows.
Preview APIs and file-format behavior may change with documented migrations. The
editor never updates itself; no application download is implied by SDK package
publication.

See the [SDK preview guide](../../docs/guides/releases/0.2.0-preview.1.md) for package
scope, compatibility and required acceptance evidence. Any future Editor distribution
needs its own verified artifact inventory, checksums and platform instructions.

Existing projects without `sourceLayout` retain legacy per-message MF2 loading.
See the [translation migration workflow](../../docs/guides/releases/preview-migration.md#translation-catalogs)
for a dry run and transactional conversion. The pinned Tomlyn 2.10.1 parser is
used by compiler and authoring tooling, not generated application runtimes.

The patched Linux environment has an accepted small residual memory trend whose
cause remains unassigned. See the [native limitations](../../eng/native/README.md);
this preview does not claim a complete native leak fix.
