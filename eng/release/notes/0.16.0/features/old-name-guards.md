## Old-name guards for the Runic Artifex rename

`Runic.Translations.Build` ships the old-name guards of the rename (D079.11) in
`build/Runic.Translations.RenamedNames.targets`, generated from `eng/rename/old-names.json`. They fail a build that has
a package of a former ID in its package graph (`RTR0110`), sets a former MSBuild name (`RTR0086`, as for the names from
before the `Runic` prefix) or has a former environment variable set (`RTR0111`), each once, with an error that names the
new name; the package error also names `rax migrate`. This release lists no name, so nothing reports them yet; the
rename release fills the lists, and the guards stay through 1.0.x. See
[RTR0110](../../../docs/guides/translations/diagnostics.md#rtr0110).
