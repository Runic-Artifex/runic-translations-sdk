# RunicTranslations.Tool

Create, validate, generate, verify, import, and analyze Runic Translations catalogs from a project-local .NET tool. Use it in developer workflows and CI to catch catalog errors before generated C# or ESM reaches an application.

## Install locally

```bash
dotnet new tool-manifest
dotnet tool install RunicTranslations.Tool --version <VERSION>
```

Replace `<VERSION>` with the current preview shown on NuGet. The tool targets .NET 10. Commit `.config/dotnet-tools.json`, restore it with `dotnet tool restore`, and keep the tool on the same exact release as the runtime, generator, build package, and Vite adapter.

## Create and validate a catalog

```bash
dotnet tool run runic-translations -- init \
  --directory Resources \
  --catalog app \
  --default-locale en \
  --locale de \
  --namespace Example.Translations \
  --class AppText

dotnet tool run runic-translations -- validate \
  --catalog Resources/app.catalog.json \
  --documents Resources/app.en.json Resources/app.de.json
```

`init` creates a compiler-valid schema-v2 catalog and locale documents as one all-or-nothing directory commit. It never overwrites an existing target. Additional locales fall back to the default; use `--locale <tag>:<fallback>` for an explicit edge. `--vscode` adds project-scoped schema associations, while `--no-esm` and `--no-starter` omit their respective defaults.

## Generate C# and ESM

```bash
dotnet tool run runic-translations -- generate \
  --catalog Resources/app.catalog.json \
  --documents Resources/app.en.json Resources/app.de.json \
  --output obj/translations \
  --emit-csharp \
  --emit-esm
```

When no emit option is present, all non-experimental output groups are selected. When any is present, only those groups are rendered. Available selections are `--emit-csharp`, `--emit-json`, `--emit-typescript`, `--emit-template-manifest`, `--emit-esm`, and experimental `--emit-cpp`.

Use `RunicTranslations.Generator` for C# that should participate directly in a Roslyn compilation. Use `RunicTranslations.Build` when MSBuild should invoke this local tool. Use this CLI directly when Vite, CI, or a custom script owns artifact generation.

## Other commands

```text
runic-translations verify  --catalog <file> --documents <files...> --output <directory>
runic-translations schema  --output <directory>
runic-translations import  --source en=<file> --source de=<file> --output <directory> ...
runic-translations analyze --catalog <file> --documents <files...> --sources <files...>
```

- `verify` renders in isolation and byte-compares the selected expected output, including extra-file detection.
- `schema` copies the bundled source, artifact, manifest, normalized-AST, editor-state, and capability schemas.
- `import` performs a diagnostic one-way conversion from conventional JSON or the supported lossless inlang subset. It writes native Runic sources and `runic-import-report.json`; `--dry-run` writes only the report to stdout.
- `analyze` combines catalog completeness and contract checks with conservative C# and TypeScript usage evidence. Dynamic access does not make a key safe to delete by default.

Arguments can be placed in a UTF-8 response file and passed as `@arguments.rsp`. Exit code `0` means success, `1` means catalog or verification diagnostics, and `2` means invalid invocation or an operational failure.

## Compatibility and status

This tool is a public preview for .NET 10. Preview commands and generated output can change with documented migrations. Pin one exact version in the local manifest and coordinate upgrades with all consumers of its generated artifacts.

- [Vite quick start](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/quickstart-vite.md)
- [Import guide](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/importing.md)
- [Analysis guide](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/analysis.md)
- [VS Code schema setup](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/vscode.md)
- [CLI source and examples](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tools/RunicTranslations.Tool)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations/blob/main/THIRD-PARTY-NOTICES.md) for bundled data attribution.
