# Runic Translations Editor availability and limits

The editor has releases of its own, with its own version, tagged `editor-v<version>`
in the Runic Translations SDK repository: deb, rpm, AppImage and `.tar.gz` packages
for Linux, a disk image for macOS and a zip for Windows, each with a `.sha256` file.
They are unsigned and not notarized, and the editor has no automatic update. You can
also build and run the editor from the SDK source as described in [README.md](README.md).
The SDK's packages do not include the editor; their publication policy is documented
in [eng/release](../../eng/release/README.md).

The Editor is the public demonstration for English and German
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
scope and compatibility. The [distribution guide](docs/editor-distribution.md) describes
each package, its checksum and package manifest, and the steps for each system.

Direct MF2 sources remain supported as the alternate source representation.

The patched Linux environment has an accepted small residual memory trend whose
cause remains unassigned. See the [native limitations](../../eng/native/README.md);
this preview does not claim a complete native leak fix.
