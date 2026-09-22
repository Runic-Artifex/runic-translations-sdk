# Runic Translations Editor availability and limits

Standalone Editor distributions are outside the SDK preview. No public Editor
archive, signing, notarization or automatic update capability is claimed.
Build and run the editor from the SDK source as described in [README.md](README.md).

SDK package publication does not imply an Editor archive. The SDK's package
publication policy is documented in [eng/release](../../eng/release/README.md).

The source-built Editor is the public demonstration for English and German
localization and parameterized MF2 count messages. Its resources use grouped
RMF2 documents declared by `runic.json`.

The editor supports grouped `.rmf2` and direct `.mf2` projects. Its edits remain
resource-only and never rewrite application call sites. Plain direct resources
use the closed, deterministic XLIFF 2.1 text profile. Structured exports carry
an explicit loss report and their units are refused on import through that text
profile instead of being flattened. V5 preview is rendered through the verified
.NET artifact/pack path; the browser receives only inert semantic runs and does
not load application callbacks or renderers.
RMF2 is a bounded profile rather than full Unicode MF2 conformance; see the
[RMF2 implementation guide](../../docs/guides/translations/rmf2.md) for its
supported resource and markup contracts.

Back up or commit translation files before evaluating editing or interchange flows.
Preview APIs and file-format behavior may change with documented migrations. The
editor never updates itself; no application download is implied by SDK package
publication.

See the [RMF2 guide](../../docs/guides/translations/rmf2.md) for package
scope and compatibility. Any future Editor archive should include its own artifact
inventory, checksum, and platform instructions; see the [distribution guidance](docs/editor-distribution.md).

Direct MF2 sources remain supported as the alternate source representation.

The patched Linux environment has an accepted small residual memory trend whose
cause remains unassigned. See the [native limitations](../../eng/native/README.md);
this preview does not claim a complete native leak fix.
