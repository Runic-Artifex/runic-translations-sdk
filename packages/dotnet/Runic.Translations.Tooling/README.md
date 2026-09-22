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

The public export overload preserves the typed compiler boundary. The first-party
editor uses an internal projection for v5 projects; it reads typed resources and
never fabricates a legacy catalog carrier. Approved review stamps use a dedicated
closed-text-profile fingerprint, while workspace source freshness remains a
separate conflict check.

The package carries the schemas needed for the generated v5 runtime artifacts.

The compiler and authoring assemblies are implementation parts of this package,
not separately versioned products.

## RMF2

Use `Runic.Translations.Build` or `dotnet-runic-translations` for project
emission. `Rmf2ResourceReader`, `Rmf2ResourceWriter` and `Rmf2Workspace` provide
recoverable resources, explicit segment edits and revisioned transaction plans.
The package includes the v5 artifact schema. See
the [RMF2 guide](../../../docs/guides/translations/rmf2.md) for the supported
subset, the closed plain-text interchange boundary, and frozen exclusions.
