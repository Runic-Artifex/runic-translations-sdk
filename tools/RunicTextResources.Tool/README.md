# runic-textresources

Deterministic command-line validation and generation for RunicTextResources text-resource catalogs.

```text
runic-textresources init --directory Resources --catalog product --default-locale de --locale en --locale fr --namespace Customer.Product --class ProductText
runic-textresources validate --catalog catalog.json --documents "locales/**/*.json"
runic-textresources generate --catalog catalog.json --documents "locales/**/*.json" --output obj/text-resources
runic-textresources verify --catalog catalog.json --documents "locales/**/*.json" --output obj/text-resources
runic-textresources schema --output schemas
```

`init` creates a compiler-valid schema-v2 project as one all-or-nothing directory
commit. Additional `--locale` values fall back to the default locale. Use
`--locale <tag>:<fallback>` for an explicit fallback edge. Existing targets are
never overwritten. ESM output metadata and an `Application.Name` starter message
are included by default; use `--no-esm` or `--no-starter` to omit them.

`schema` copies every bundled source, artifact, manifest, and normalized-AST schema.

`generate` and `verify` accept `--emit-csharp`, `--emit-json`,
`--emit-typescript`, `--emit-template-manifest`, `--emit-esm`, and the experimental
`--emit-cpp`. When none is specified,
all output groups are selected. When any is specified, only the selected groups
are rendered; experimental C++ remains opt-in. ESM output has its own nested,
hashed `web-module-manifest-v1.json` and includes per-message modules, declarations,
a locale/formatting runtime, and a bounded text-reference decoder. Any selected
JSON/template/TypeScript-contract group also emits `{catalog}.asset-manifest-v1.json`.
That frozen v1 host contract inventories every selected locale, template, and
TypeScript artifact with its path, exact UTF-8 byte length, lowercase SHA-256,
media type, and canonical locale where applicable; it never lists itself.

Arguments may be placed in a UTF-8 response file and supplied as `@arguments.rsp`.
Exit code `0` means success, `1` means catalog or verification diagnostics, and `2`
means invalid invocation or an operational failure.
