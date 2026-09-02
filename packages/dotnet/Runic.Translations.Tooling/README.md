# Runic.Translations.Tooling (preview)

`Runic.Translations.Tooling` is the preview MF2 authoring facade. It owns compiler
invocation and interchange, and bundles the
transactional authoring assembly `Runic.Translations.Authoring` for MF2 project
creation, mutation, transactions, and editor state. The compiler and
authoring assemblies ship inside this package; it intentionally does not
reference the runtime.

`TranslationInterchange.ExportXliff21` and `ImportXliff21` implement a closed,
deterministic XLIFF 2.1 text profile for one successfully compiled catalog.
They export one document per non-default locale, preserve plain text patterns,
placeholder contracts, resource metadata, and review state/notes. The compact
`runic.translations.interchange-review/1` JSON sidecar is the Git-friendly
authoritative form for review data. Rich selector, formatter, or markup messages and source-layer
provenance are never silently flattened: export records deterministic loss
events and import rejects structured units. This is not general XLIFF or MF2
conformance.

The package carries the schemas needed for the generated runtime artifacts,
including locale-pack-v2 and locale-artifact-v2.

The compiler and authoring assemblies are implementation parts of this package,
not separately versioned products.
