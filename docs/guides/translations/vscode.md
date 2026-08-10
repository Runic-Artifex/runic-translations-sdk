# VS Code JSON schemas

Runic publishes immutable, versioned JSON Schema identifiers beneath
`https://runic-artifex.eu/schemas/translations/`. Schema feedback covers JSON
shape and completion; compiler validation through `runic-translations generate`
or `runic-translations verify` remains authoritative for locale fallback graphs,
cross-file contracts, message semantics, and generated identifier rules.

For a new project, the CLI can write scoped associations into the directory it
creates:

```bash
dotnet tool run runic-translations -- init \
  --directory translations \
  --catalog app \
  --default-locale en \
  --namespace Example \
  --class AppText \
  --vscode
```

To configure an existing workspace yourself, add this to `.vscode/settings.json`
and replace `app` with the catalog ID:

```json
{
  "json.schemas": [
    {
      "url": "https://runic-artifex.eu/schemas/translations/catalog-v2.schema.json",
      "fileMatch": ["**/*.catalog.json"]
    },
    {
      "url": "https://runic-artifex.eu/schemas/translations/resources-v2.schema.json",
      "fileMatch": ["**/app.*.json", "!**/app.catalog.json"]
    }
  ]
}
```

Schema-v2 completion covers typed inputs, declarations, literal/cardinal/ordinal
selectors, ordered variants, relative-time options, and semantic markup. A
minimal plural message is:

```json
{
  "$value": {
    "inputs": { "count": { "type": "int64" } },
    "selectors": [
      { "name": "quantity", "input": "count", "function": "plural" }
    ],
    "variants": [
      { "match": { "quantity": "one" }, "value": "One item" },
      { "match": { "quantity": "*" }, "value": [{ "input": "count" }, " items"] }
    ]
  }
}
```

For offline or version-pinned use, export the exact schemas shipped with the
tool and use relative `url` values in the same associations:

```bash
dotnet tool run runic-translations -- schema --output .runic/schemas
```

For example, the catalog URL becomes
`.runic/schemas/catalog-v2.schema.json`. Re-export the directory when upgrading
the pinned tool version.
