# Source schema and message AST version 2

Simple values remain strings. A message needing selection uses a structured
`$value` with `inputs`, `selectors`, and ordered `variants`:

```json
{
  "Files": {
    "Deleted": {
      "$value": {
        "inputs": { "count": { "type": "int64" } },
        "selectors": [
          { "name": "quantity", "input": "count", "function": "plural" }
        ],
        "variants": [
          { "match": { "quantity": "one" }, "value": "One file" },
          { "match": { "quantity": "*" }, "value": "{count} files" }
        ]
      }
    }
  }
}
```

Selectors are `literal`, `plural`, or `ordinal`. Every variant matches every
selector; exactly repeated matches are rejected and an all-`*` catch-all is
required. Locale translations must keep the same input and selector contract.
Portable input names are `string`, `bool`, `int64`, `decimal`, `date`, `time`,
`instant`, and `uuid`. See `spec/schemas/message-ast-v2.schema.json` for the
normalized target contract, including reserved safe markup and formatter nodes.
