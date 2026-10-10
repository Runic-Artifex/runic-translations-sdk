# dotnet-runic-translations

Create, validate, generate, and verify Runic Translations MF2 projects from a project-local .NET tool. Use it in developer workflows and CI to catch message errors before generated C# or ESM reaches an application.

## Install locally

```bash
dotnet new tool-manifest --output .config
dotnet tool install dotnet-runic-translations --version <VERSION>
```

Replace `<VERSION>` with the current preview shown on NuGet. The tool targets .NET 10. Commit `.config/dotnet-tools.json`, restore it with `dotnet tool restore`, and keep the tool on the same exact release as the runtime, build package, and Vite adapter.

## Validate an MF2 project

```bash
dotnet tool run runic-translations -- validate \
  --project translations
```

The project path may name the conventional directory or its `runic.json`.
New projects use grouped RMF2 resources in sibling files such as `en.rmf2` and
`de.rmf2`. Direct locale-directory `.mf2` messages are also supported; the
representation is inferred from source files and cannot be mixed.

## Generate C# and ESM

```bash
dotnet tool run runic-translations -- generate \
  --project translations \
  --output obj/translations \
  --emit-csharp \
  --emit-esm
```

When no emit option is present, projects default to typed C#, locale-v5 JSON plus
its asset manifest, and the cohesive ESM-v5 package. When any option is present,
only that group is rendered. V5 supports `--emit-csharp`, `--emit-json`, and
`--emit-esm`. `--emit-typescript`, `--emit-template-manifest`, and `--emit-cpp`
are not listed in help: they fail with `RTR0065` because no version-correct
standalone v5 renderer exists.
Validation permits an empty v5 project as a scaffold, but `generate` and `verify`
fail with `RTR0009` until the effective default locale defines a canonical key.

Use `Runic.Translations.Build` for generated C# or when MSBuild should invoke this local tool. Use this CLI directly when Vite, CI, or a custom script owns artifact generation.

## Other commands

```text
runic-translations verify  --project <directory|runic.json> --output <directory>
runic-translations schema  --output <directory>
runic-translations serve
```

- `verify` renders in isolation and byte-compares the selected expected output, including extra-file detection.
- `schema` copies the bundled source, artifact, manifest, normalized-AST, editor-state, and capability schemas.
- `serve` is a persistent compiler for development servers, used by `@runic-artifex/vite-plugin-runic-translations`. It keeps unchanged sources parsed and compiled between requests, so a request after an edit recompiles only the edited files. See [serve protocol](#serve-protocol).

`generate` leaves output files whose bytes are unchanged untouched, so file watchers only see artifacts that changed.

Arguments can be placed in a UTF-8 response file and passed as `@arguments.rsp`. Exit code `0` means success, `1` means catalog or verification diagnostics, and `2` means invalid invocation or an operational failure.

## Serve protocol

`serve` speaks line-delimited JSON (`runic-translations-serve/1`) over standard input and output; diagnostics never go to standard output. It first writes `{"protocol":"runic-translations-serve/1","event":"ready","version":"..."}`. Each request is one line and gets exactly one response line with the same `id`, in order:

```text
{"id":1,"method":"generate","project":"translations","output":".runic/translations","emit":["esm"]}
{"id":1,"ok":true,"exitCode":0,"output":"generated 12 artifact(s).","message":"","diagnostics":[],"elapsedMs":9}
```

Methods are `generate` (`project`, `output`, optional `emit` with `csharp`, `json`, `typescript`, `template-manifest`, `esm` or `cpp`), `validate` (`project`) and `shutdown`. `exitCode` and `diagnostics` match the one-shot command; `message` is the text it would print on failure. Paths resolve against the server's working directory. The server exits after `shutdown` or when standard input closes. A request line longer than 1 MiB is discarded without being buffered; its reply has `"id":null` and answers the oldest outstanding request, because requests are answered in order.

Trust model: `serve` has the same authority as the one-shot commands run by the same user. It reads requests only from its standard input, so only the parent process that started it can send them; it opens no socket or port. A request may name any `project` and `output` the process can access, and `generate` writes there, exactly like `runic-translations generate --project ... --output ...`. Start it only from trusted tooling, and do not forward untrusted input into its standard input.

## Compatibility and status

This tool is a public preview for .NET 10. Preview commands and generated output can change with documented migrations. Pin one exact version in the local manifest and coordinate upgrades with all consumers of its generated artifacts.

- [Vite quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-vite.md)
- [RMF2 project guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md)
- [CLI source and examples](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/tools/dotnet-runic-translations)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations-sdk/issues)

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/LICENSE). See [Third-Party Notices](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/specs/translations/THIRD-PARTY-NOTICES.md) for bundled data attribution.

## RMF2

Grouped `.rmf2` sources enable recursive resources and markup contracts; direct
`.mf2` sources are also supported. `validate`, `generate`, and `verify` use
grammar 5/runtime ABI 2 consistently. Generated JSON is
`{catalog}.{locale}.locale-v5.json`; ESM output is under `{catalog}.esm-v5/`
with `web-module-manifest-v3.json`.
`lsp` starts the bounded stdio language service. See the [RMF2 guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md) for capabilities and limits.

The LSP's standard resource rename is deliberately resource-only: it refuses
workspaces containing application or other non-translation source files rather than emitting a
partial application refactor. Use the explicit source transaction in an IDE or
the editor, then update application call sites with the native language service.
