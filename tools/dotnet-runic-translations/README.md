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

The project path may name the conventional directory or its `runic.json`.
New projects declare `sourceLayout: "locale-toml"` and keep MF2 messages as
string values in sibling files such as `en.toml` and `de.toml`. Projects without
that field retain the legacy locale-directory `.mf2` layout.

## Migrate an existing catalog

```bash
dotnet tool run runic-translations -- migrate --project translations --dry-run
dotnet tool run runic-translations -- migrate --project translations
```

Migration validates the complete proposed catalog before committing its file
transaction. It preserves decoded MF2 content and refuses collisions or stale
files. The dry run leaves the original project unchanged.

## Generate C# and ESM

```bash
dotnet tool run runic-translations -- generate \
  --project translations \
  --output obj/translations \
  --emit-csharp \
  --emit-esm
```

When no emit option is present, projects that omit `executionProfile` retain the
existing output set. An `rmf2-v1` project with
`executionProfile: "rmf2-execution-v2"` defaults to typed C#, locale-v5 JSON plus
its asset manifest, and the cohesive ESM-v5 package. When any option is present,
only that group is rendered. V5 supports `--emit-csharp`, `--emit-json`, and
`--emit-esm`; `--emit-typescript`, `--emit-template-manifest`, and `--emit-cpp`
fail with `RTR0065` because no version-correct standalone v5 renderer exists.
Validation permits an empty v5 project as a scaffold, but `generate` and `verify`
fail with `RTR0009` until the effective default locale defines a canonical key.

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

- [Vite quick start](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/quickstart-vite.md)
- [MF2 project guide](https://github.com/Runic-Artifex/runic-sdk/blob/main/docs/guides/translations/mf2-projects.md)
- [CLI source and examples](https://github.com/Runic-Artifex/runic-sdk/tree/main/tools/dotnet-runic-translations)
- [Issues and support](https://github.com/Runic-Artifex/runic-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-sdk/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-sdk/blob/main/specs/translations/THIRD-PARTY-NOTICES.md) for bundled data attribution.

## RMF2

`sourceLayout: "rmf2-v1"` enables recursive resources and markup contracts. For
typed grammar 5/runtime ABI 2 generation, also set
`executionProfile: "rmf2-execution-v2"`; `validate`, `generate`, and `verify`
all dispatch from that selector. Omission preserves grammar/artifact v4 and ESM
ABI 3. Generated v5 JSON is `{catalog}.{locale}.locale-v5.json`; ESM output is
under `{catalog}.esm-v5/` with `web-module-manifest-v3.json`.
For
legacy projects, run `migrate` and validate first, then use
`migrate-rmf2 --project translations --dry-run` to preview the TOML-to-RMF2
step. Applying that second step retains byte-preserving sibling `.toml.bak`
files; keep a durable VCS or external backup for the first legacy-to-TOML step.
`lsp` starts the bounded stdio language service. See the [RMF2 guide](../../docs/guides/translations/rmf2.md) for capabilities and limits.

The LSP's standard resource rename is deliberately resource-only: it refuses
workspaces containing application or legacy source files rather than emitting a
partial application refactor. Use the explicit source transaction in an IDE or
the editor, then update application call sites with the native language service.
