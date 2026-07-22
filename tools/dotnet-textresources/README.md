# dotnet-textresources

Deterministic command-line validation and generation for WebUIToolkit text-resource catalogs.

```text
dotnet-textresources validate --catalog catalog.json --documents "locales/**/*.json"
dotnet-textresources generate --catalog catalog.json --documents "locales/**/*.json" --output obj/text-resources
dotnet-textresources verify --catalog catalog.json --documents "locales/**/*.json" --output obj/text-resources
dotnet-textresources schema --output schemas
```

`schema` copies all six versioned Wave A/B schemas bundled in the tool.

`generate` and `verify` accept `--emit-csharp`, `--emit-json`,
`--emit-typescript`, and `--emit-template-manifest`. When none is specified,
all output groups are selected. When any is specified, only the selected groups
are rendered.

Arguments may be placed in a UTF-8 response file and supplied as `@arguments.rsp`.
Exit code `0` means success, `1` means catalog or verification diagnostics, and `2`
means invalid invocation or an operational failure.
