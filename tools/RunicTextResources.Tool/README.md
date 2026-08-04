# runic-textresources

Deterministic command-line validation and generation for RunicTextResources text-resource catalogs.

```text
runic-textresources validate --catalog catalog.json --documents "locales/**/*.json"
runic-textresources generate --catalog catalog.json --documents "locales/**/*.json" --output obj/text-resources
runic-textresources verify --catalog catalog.json --documents "locales/**/*.json" --output obj/text-resources
runic-textresources schema --output schemas
```

`schema` copies all six versioned Wave A/B schemas bundled in the tool.

`generate` and `verify` accept `--emit-csharp`, `--emit-json`,
`--emit-typescript`, and `--emit-template-manifest`. When none is specified,
all output groups are selected. When any is specified, only the selected groups
are rendered. Any selected non-C# group also emits `{catalog}.asset-manifest-v1.json`.
That frozen v1 host contract inventories every selected locale, template, and
TypeScript artifact with its path, exact UTF-8 byte length, lowercase SHA-256,
media type, and canonical locale where applicable; it never lists itself.

Arguments may be placed in a UTF-8 response file and supplied as `@arguments.rsp`.
Exit code `0` means success, `1` means catalog or verification diagnostics, and `2`
means invalid invocation or an operational failure.
