# Runic.Translations.Tooling (preview)

`Runic.Translations.Tooling` is the preview authoring facade. It owns compiler
invocation, deterministic source migration, and interchange, and bundles the
transactional authoring assembly `Runic.Translations.Authoring` for workspace
discovery, project creation, mutation, and editor state. The compiler and
authoring assemblies ship inside this package; it intentionally does not
reference the runtime.

`MigrateV2ToV3` accepts a v2 resource-document byte sequence and returns
canonical UTF-8 v3 bytes plus a machine-readable, deterministic loss report.
The v3 envelope uses the closed `runic-mf2-subset/1` profile. It is not a full
MessageFormat 2 parser.

`TranslationInterchange.ExportXliff21` and `ImportXliff21` implement a closed,
deterministic XLIFF 2.1 text profile for one successfully compiled catalog.
They export one document per non-default locale, preserve plain text patterns,
placeholder contracts, resource metadata, and review state/notes. The compact
`runic.translations.interchange-review/1` JSON sidecar is the Git-friendly
authoritative form for review data. Rich selector, formatter, or markup messages and source-layer
provenance are never silently flattened: export records deterministic loss
events and import rejects structured units. This is not general XLIFF or MF2
conformance.

The package carries the complete schema closure for this preview: resources-v3,
message-ast-v3, locale-pack-v2, and locale-artifact-v2 (which locale-pack-v2
references).

The compiler and authoring assemblies are implementation parts of this package,
not separately versioned products.
