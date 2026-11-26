# Old-name guards

The old-name guards (D079.11) fail a build that still uses a name the Runic Artifex rename replaced. They ship in
`build/Runic.Translations.RenamedNames.targets` of `Runic.Translations.Build`, which
`build/Runic.Translations.Build.targets` imports, through 1.0.x, and are removed in 2.0.

| Kind | Reported when | Code |
| --- | --- | --- |
| `packages` | an old package ID is in the package graph: a `PackageReference`, or a package NuGet resolved (`PackageDependencies` after `ResolvePackageAssets`) | `RTR0110` |
| `msbuild` | the project, its imports or the command line sets an old public property or item | `RTR0086` |
| `environment` | an old environment variable is set | `RTR0111` |

Each name is reported once, and the message names the new name; a package error also names `rax migrate`. `RTR0086` is
the code that already reports the names from before the `Runic` prefix, so a renamed MSBuild name has one code. The codes
are in [the diagnostics reference](../../docs/guides/translations/diagnostics.md#rtr0110). 0.16 lists no name; the
rename release fills the lists.

## Changing the list

Edit [`old-names.json`](old-names.json), then regenerate and run the tests:

```sh
bun eng/rename/old-names.mjs           # or node
bun eng/rename/old-names.mjs --check   # what the test checks
dotnet run --project tests/dotnet/Runic.Translations.Build.Tests --configuration Release -- --treenode-filter "/*/*/OldNameGuardsTests/*"
```

The input, version 1:

```jsonc
{
  "version": 1,
  "family": {
    "token": "RunicTranslations",        // prefix of the private items and the guard targets
    "buildName": "Runic Translations",   // "The Runic Translations build property X is renamed to Y."
    // no helpLinkBase: the errors carry the ID as HelpKeyword, as the other RTR errors of the build do
    "diagnostics": { "packages": "RTR0110", "msbuild": "RTR0086", "environment": "RTR0111" },
    "skipCondition": "…",                // WPF's temporary markup project reports nothing
    "root": "../..",                     // the repository root, relative to this file; outputs are relative to it
    "outputs": [ "packages/dotnet/Runic.Translations.Build/build/Runic.Translations.RenamedNames.targets" ]
  },
  // The entries take the shape of the rename plan's rax-map.json names: old, new and (msbuild) kind.
  "packages":    [ { "old": "Runic.Translations", "new": "RunicArtifex.Translations" } ],
  "msbuild":     [ { "old": "RunicTranslationsEmitEsm", "new": "RaxTranslationsEmitEsm", "kind": "property" },
                   { "old": "RunicTranslationProject", "new": "RaxTranslationProject", "kind": "item",
                     "whenNewUnset": false,      // true: stays quiet when the project also sets the new name
                     "note": "…" } ],            // optional: a comment above the item
  "environment": [ { "old": "RUNIC_TRANSLATIONS_X", "new": "RAX_TRANSLATIONS_X" } ]
}
```

Package IDs compare without case. A family may list the old IDs of another family's packages that its own packages
cannot work beside.

`old-names.mjs` is a copy of runic-sdk's `packages/dotnet/Runic.Application.Views/rename/old-names.mjs`; runic-cli-sdk
has the same copy. Each repository's family settings are in its input, so the copies stay identical: change the script in
runic-sdk first, then copy it here.
