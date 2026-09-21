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

## RMF2

The public `CompileProject` surface also accepts v4 RMF2 projects, and
`BuildRmf2LocalePacks` emits artifact 4; `BuildLocalePackV2` retains its version 2
contract. Use `Runic.Translations.Build` or `dotnet-runic-translations` for
execution-v2 project emission. `Rmf2ResourceReader`, `Rmf2ResourceWriter` and
`Rmf2Workspace` provide recoverable resources, explicit segment edits and
revisioned transaction plans. The package includes the artifact 4 schema. See
the [RMF2 guide](../../../docs/guides/translations/rmf2.md) for the supported
subset and remaining interchange work.
