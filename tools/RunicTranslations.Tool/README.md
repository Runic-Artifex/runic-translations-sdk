# runic-translations

Deterministic command-line validation and generation for RunicTranslations translation catalogs.

```text
runic-translations init --directory Resources --catalog product --default-locale de --locale en --locale fr --namespace Customer.Product --class ProductText
runic-translations validate --catalog catalog.json --documents "locales/**/*.json"
runic-translations generate --catalog catalog.json --documents "locales/**/*.json" --output obj/translations
runic-translations verify --catalog catalog.json --documents "locales/**/*.json" --output obj/translations
runic-translations schema --output schemas
runic-translations import --source en=messages/en.json --source de=messages/de.json --catalog product --default-locale en --namespace Customer.Product --class ProductText --output Resources
runic-translations analyze --catalog catalog.json --documents "locales/**/*.json" --sources "src/**/*.cs" "web/**/*.ts" --format json
```

`init` creates a compiler-valid schema-v2 project as one all-or-nothing directory
commit. Additional `--locale` values fall back to the default locale. Use
`--locale <tag>:<fallback>` for an explicit fallback edge. Existing targets are
never overwritten. ESM output metadata and an `Application.Name` starter message
are included by default; use `--no-esm` or `--no-starter` to omit them.
Use `--vscode` to add schema associations inside the newly created project. It
never edits an existing workspace or user setting.

`schema` copies every bundled source, artifact, manifest, and normalized-AST schema.

`import` performs a diagnostic, one-way conversion of conventional JSON and the
explicitly supported lossless inlang message-format subset. It writes ordinary
schema-v2 inputs plus `runic-import-report.json`, then compiles them before the
atomic output commit. `--dry-run` writes the report to stdout and never writes
files. `--allow-partial` omits unsupported keys from every locale while retaining
diagnostics. `--format auto` distinguishes marked/complex inlang files from
conventional JSON; use `--format json` or `--format inlang` to remove ambiguity.
See the [migration guide](../../../docs/importing.md).

`analyze` combines compiler-owned completeness and contract checks with
conservative C#/TypeScript usage evidence. It reports proven, possible-dynamic,
and unknown references; dynamic access never makes a key safely deletable by
default. Use `--fail-on-findings` for CI and see the
[analysis guide](../../../docs/analysis.md) for artifact fingerprints and exit
codes.

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
