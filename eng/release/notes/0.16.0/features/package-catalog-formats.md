## Library catalogs in the renamed format

The Runic Artifex rename (0.17) renames a packed library catalog. The new spelling is `rax/<catalog>/rax.json` with
`*.ramf2` sources, which write Runic's MF2 namespace `rax:` (`:rax:uuid`, `{#rax:link}`), declared as a
`RaxTranslationsPackedCatalog` item. This release already reads a package's catalog in either format: in the build, the
`overrides` CLI, the language server and the editor. A package that ships both is read in the new format. The
`Runic.Translations.CommandLine` adapter takes the `runic-command-line` catalog from `translations/rax.json` when the
Command Line package ships it there. So Command Line can move to the new format before Translations does, and no library
has to republish when an application updates. Packing, an application's own catalog and its override sources are
unchanged, and `rax:` is read only in `.ramf2` sources. From the rename on, a package catalog in the former format still
builds and warns with `RTR0109`; nothing reports it in this release. See
[Library catalog formats](../../../docs/guides/translations/libraries.md#library-catalog-formats).
