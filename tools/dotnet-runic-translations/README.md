# dotnet-runic-translations

Create, validate, generate, and verify Runic Translations MF2 projects from a project-local .NET tool. Use it in developer workflows and CI to catch message errors before generated C# or ESM reaches an application.

## Install locally

```bash
dotnet new tool-manifest
dotnet tool install dotnet-runic-translations --version <VERSION>
```

Replace `<VERSION>` with the current preview shown on NuGet. The tool targets .NET 10. Commit `.config/dotnet-tools.json`, restore it with `dotnet tool restore`, and keep the tool on the same exact release as the runtime, build package, and Vite adapter.

## Validate an MF2 project

```bash
dotnet tool run runic-translations -- validate \
  --project translations
```

The project path may name the conventional directory or its `runic.json`. The
tool discovers locale directories and `.mf2` messages beneath it.

## Generate C# and ESM

```bash
dotnet tool run runic-translations -- generate \
  --project translations \
  --output obj/translations \
  --emit-csharp \
  --emit-esm
```

When no emit option is present, all non-experimental output groups are selected. When any is present, only those groups are rendered. Available selections are `--emit-csharp`, `--emit-json`, `--emit-typescript`, `--emit-template-manifest`, `--emit-esm`, and experimental `--emit-cpp`.

Use `Runic.Translations.Build` for generated C# or when MSBuild should invoke this local tool. Use this CLI directly when Vite, CI, or a custom script owns artifact generation.

## Other commands

```text
runic-translations verify  --project <directory|runic.json> --output <directory>
runic-translations schema  --output <directory>
```

- `verify` renders in isolation and byte-compares the selected expected output, including extra-file detection.
- `schema` copies the bundled source, artifact, manifest, normalized-AST, editor-state, and capability schemas.

Arguments can be placed in a UTF-8 response file and passed as `@arguments.rsp`. Exit code `0` means success, `1` means catalog or verification diagnostics, and `2` means invalid invocation or an operational failure.

## Compatibility and status

This tool is a public preview for .NET 10. Preview commands and generated output can change with documented migrations. Pin one exact version in the local manifest and coordinate upgrades with all consumers of its generated artifacts.

- [Vite quick start](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/quickstart-vite.md)
- [MF2 project guide](https://github.com/Runic-Artifex/runic-translations/blob/main/docs/mf2-projects.md)
- [CLI source and examples](https://github.com/Runic-Artifex/runic-translations/tree/main/dotnet/tools/dotnet-runic-translations)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations/blob/main/THIRD-PARTY-NOTICES.md) for bundled data attribution.
