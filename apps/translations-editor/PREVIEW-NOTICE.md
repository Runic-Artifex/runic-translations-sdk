# Runic Translations Editor availability and limits

Standalone Editor distributions are outside the SDK preview. No public Editor
archive, signing, notarization or automatic update capability is claimed.
Build and run the editor from the SDK source as described in [README.md](README.md).

SDK package publication does not imply an Editor archive. The SDK's package
publication policy is documented in [eng/release](../../eng/release/README.md).

The source-built Editor is the public demonstration for English and German
localization and parameterized MF2 count messages. Its resources use one TOML file
per locale with `sourceLayout: "locale-toml"` in `runic.json`.

The editor also supports projects that opt into `sourceLayout: "rmf2-v1"`,
including explicit `executionProfile: "rmf2-execution-v2"`. Its edits remain
resource-only. Plain direct resources use the closed XLIFF text profile;
structured exports carry an explicit loss report and cannot be imported through
that text profile.
RMF2 is a bounded profile rather than full Unicode MF2 conformance; see the
[RMF2 implementation guide](../../docs/guides/translations/rmf2.md) for its
supported resource and markup contracts.

Back up or commit translation files before evaluating editing or interchange flows.
Preview APIs and file-format behavior may change with documented migrations. The
editor never updates itself; no application download is implied by SDK package
publication.

See the [SDK preview guide](../../docs/guides/releases/0.2.0-preview.1.md) for package
scope and compatibility. Any future Editor archive should include its own artifact
inventory, checksum, and platform instructions; see the [distribution guidance](docs/editor-distribution.md).

Existing projects without `sourceLayout` retain legacy per-message MF2 loading.
See the [translation migration workflow](../../docs/guides/releases/preview-migration.md#translation-catalogs)
for a dry run and transactional conversion. The pinned Tomlyn 2.10.1 parser is
used by compiler and authoring tooling, not generated application runtimes.

The patched Linux environment has an accepted small residual memory trend whose
cause remains unassigned. See the [native limitations](../../eng/native/README.md);
this preview does not claim a complete native leak fix.
