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
`instant`, and `uuid`.

Structured variant values are arrays. They admit literal strings, `{ "input":
"name" }`, format expressions, local format references, and semantic markup. A
format may also be named once in the message's optional `declarations` array and
used as `{ "local": "name" }`:

```json
{
  "inputs": { "count": { "type": "int64" }, "delta": { "type": "decimal" } },
  "declarations": [
    { "name": "groupedCount", "input": "count", "function": "integer", "format": "grouped" },
    { "name": "relativeDelta", "input": "delta", "function": "relativeTime", "unit": "day", "numeric": "auto" }
  ],
  "selectors": [],
  "variants": [{
    "match": {},
    "value": [
      { "markup": { "name": "strong", "attributes": { "tone": "critical" }, "children": [{ "local": "groupedCount" }] } },
      " ", { "local": "relativeDelta" }
    ]
  }]
}
```

Markup names and attributes are semantic host tokens. .NET returns
`LocalizedTextContent`; ESM returns `LocalizedContent`. Neither converts markup
to HTML. Local declarations are statically inlined during lowering, so generated
backends and dynamic artifacts execute the same closed normalized nodes without
name lookup.

The built-in cardinal registry covers `en`, `de`, `es`, `fr`, `it`, `nl`, `sv`,
`no`, and `da`; ordinal selection currently covers `en`. Relative time covers
`en`, `de`, and `fr`. A catalog using a function outside its locale registry fails
with `RTR0031` during compilation.
