# dotnet-runic-translations

Create, import, validate, generate, and verify Runic Translations MF2 projects from a project-local .NET tool. Use it in developer workflows and CI to catch message errors before generated C# or ESM reaches an application.

## Install locally

```bash
dotnet new tool-manifest --output .config
dotnet tool install dotnet-runic-translations --version <VERSION>
```

Replace `<VERSION>` with the current version shown on NuGet. The tool targets .NET 10. Commit `.config/dotnet-tools.json`, restore it with `dotnet tool restore`, and keep the tool on the same exact release as the runtime, build package, and Vite adapter.

## Validate an MF2 project

```bash
dotnet tool run runic-translations -- validate \
  --project translations
```

The project path may name the conventional directory or its `runic.json`.
Without `--project`, `validate`, `generate` and `verify` use `./runic.json`, then
`./translations/runic.json`, so `runic-translations validate` works from the
project directory. `runic-translations help <command>` describes every option.
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
`--emit-esm`; the retired `--emit-typescript`, `--emit-template-manifest` and
`--emit-cpp` options were removed and are rejected as unknown options.
Validation permits an empty v5 project as a scaffold, but `generate` and `verify`
fail with `RTR0009` until the effective default locale defines a canonical key.

Use `Runic.Translations.Build` for generated C# or when MSBuild should invoke this local tool. Use this CLI directly when Vite, CI, or a custom script owns artifact generation.

## Import a text-keyed catalog

```bash
dotnet tool run runic-translations -- import-text \
  --project translations \
  --group stored \
  --base locales/untranslated.json \
  --table de=locales/de.json
```

`import-text` turns a catalog keyed by base-language text, such as
`{ "Remove user {name}": "Benutzer {name} entfernen" }`, into the RMF2 group
`<project>/<group>/`. Each `--table <locale>=<path>` (repeatable) is a JSON object
of string values for a locale declared in `runic.json` other than the base locale.
`--base` is an optional JSON array of base texts without a translation yet. The
base texts are the `--base` entries and then each table's keys, in order of first
appearance.

It writes `<baseLocale>.rmf2`, one `<locale>.rmf2` per table (an untranslated
text is omitted) and `base-text.json`, then validates the project as `validate`
does. `{name}` placeholders become `{$name}`; other `{`, `}` and `\` are escaped,
and text that an unquoted pattern would change (outer whitespace, a leading `.`,
a newline) is written as a quoted pattern. A translation that uses a placeholder
its base text lacks, or a table value that is not a string, fails with `RCLI9014`
and exit code 2, and nothing is written. `--dry-run` reports and validates the
files without writing them.

Keys are the first seven ASCII words of the text without placeholders
(`Remove user {name}` becomes `remove_user`), with `_2`, `_3` and so on for
collisions. A re-import reads the existing `<baseLocale>.rmf2` back: a text that is
still present keeps its key, new texts get new keys, and removed texts are dropped
and listed in the output. `base-text.json` maps each base text to its generated
message key, `{ "Remove user {name}": "stored_remove_user" }`, for an application
that must look up text it stored in the base language. See
[Import a text-keyed catalog](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/rmf2.md#import-a-text-keyed-catalog).

## Other commands

```text
runic-translations verify  --project <directory|runic.json> --output <directory>
runic-translations schema  --output <directory>
runic-translations serve
```

- `verify` renders in isolation and byte-compares the selected expected output, including extra-file detection.
- `schema` copies the bundled source, artifact, manifest, normalized-AST, and capability schemas.
- `serve` is a persistent compiler for development servers, used by `@runic-artifex/vite-plugin-runic-translations`. It keeps unchanged sources parsed and compiled between requests, so a request after an edit recompiles only the edited files. See [serve protocol](#serve-protocol).
- `lsp` runs the RMF2 language server over standard input/output for the VS Code and Visual Studio extensions. It is hidden from help, and its protocol subset and custom methods are outside the 1.0 compatibility rules until the production language server (2.0).

`generate` leaves output files whose bytes are unchanged untouched, so file watchers only see artifacts that changed.

Arguments can be placed in a UTF-8 response file and passed as `@arguments.rsp`.

## Output and exit codes

Every command writes human text by default. `--format json` (or
`RUNIC_COMMANDLINE_OUTPUT=json`) writes one `runic.commandline/1` envelope to
standard output instead. `--runic-output` is the former spelling of
`--format`; it is still accepted until 2.0 but no longer listed in help or
completions.

The exit codes are the same in every Runic tool:

| Exit code | Meaning |
| --- | --- |
| `0` | Success. |
| `1` | The command failed or a resource was unavailable, for example a `verify` difference or a file that cannot be read or written. |
| `2` | Invalid invocation, or input that failed validation, such as translation (`RTR`) diagnostics. |
| `3` | Internal or host failure. |
| `130` | The command was cancelled, for example with Ctrl+C. |

`serve` reports the same exit code in each response's `exitCode`.

## Support envelope

```text
runic-translations support preview --editor-diagnostics <zip>
runic-translations support collect --editor-diagnostics <zip> --destination <path>
runic-translations support remove  --destination <path>
```

`support` reads the diagnostic ZIP that the Translations Editor's `diagnostics`
command exports (`runic.translations.editor-diagnostics/1`) and builds a local,
unsigned `runic.support-envelope/1` from it. `preview` reports what the envelope
would contain, `collect` writes it to a new file, and `remove` deletes a file
only after checking that `collect` wrote it. It accepts only bounded,
path-free summary fields and never collects source, translation or review
text, workspace paths, sessions, cookies or tokens. Nothing is uploaded. It
moved here from `dotnet runic support --mode <mode>`, which forwards to it
until 2.0.

With `--format json` the payload is `runic.translations.support/1`:

```json
{"mode":"collect","destination":"/home/me/support-envelope.json","digest":"sha256:...","collectors":["runic.translations.editor-diagnostics"],"omissions":["automatic-capture","network","workspace-roots","relative-paths","source-text","translation-text","review-text","sessions-cookies-tokens"],"outboundTransportAttempts":0}
```

`destination` is the full path of the envelope, or `null` for `preview`;
`digest` is the SHA-256 of the envelope's bytes; `collectors` and `omissions`
are empty for `remove`. A failure reports an `RTRSUP` code from the
[diagnostics reference](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/diagnostics.md#runic-translations-support-codes)
with exit code `2`, or `RTRSUP025` with exit code `1` when a file cannot be
read or written.

## Diagnostics

Translation diagnostics are printed to standard error, one per line, in the
form editors and MSBuild recognize:

```text
translations/de.rmf2(2,3,2,8): error RTR0011: Locale 'de' defines 'commands.remov', which base locale 'en' does not define. Did you mean 'commands.remove'?
```

With `--format json` they are structured entries rather than text. A
success carries them in `payload.diagnostics`; a failure carries the same
`runic.translations.tool/1` payload in `fault.data.payload`:

```json
{"code":"RTR0011","severity":"error","message":"Locale 'de' defines ...","path":"translations/de.rmf2","line":2,"column":3,"endLine":2,"endColumn":8,"helpUri":"https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/diagnostics.md#rtr0011"}
```

The envelope's own `diagnostics` array holds only Runic.CommandLine `RCLI`
diagnostics, such as usage errors. See the
[diagnostics reference](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/diagnostics.md).

The [Translations Editor](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/apps/translations-editor/README.md)
is built from source; the editor is a separate product with its own releases.
Its `validate <path>` takes the workspace as a positional argument; the
equivalent tool command is `runic-translations validate --project <path>`.

## Serve protocol

`serve` speaks line-delimited JSON (`runic-translations-serve/1`) over standard input and output; diagnostics never go to standard output. It first writes `{"protocol":"runic-translations-serve/1","event":"ready","version":"..."}`. Each request is one line and gets exactly one response line with the same `id`, in order:

```text
{"id":1,"method":"generate","project":"translations","output":".runic/translations","emit":["esm"]}
{"id":1,"ok":true,"exitCode":0,"output":"generated 12 artifact(s).","message":"","diagnostics":[],"elapsedMs":9}
```

Methods are `generate` (`project`, `output`, optional `emit` with `csharp`, `json` or `esm`; another name fails the request), `validate` (`project`) and `shutdown`. `exitCode` and `diagnostics` match the one-shot command, and a translation diagnostic (an `RTR` code) also carries `path`, `line`, `column`, `endLine`, `endColumn` and `helpUri`; `message` is the text it would print on failure. Paths resolve against the server's working directory. The server exits after `shutdown` or when standard input closes. A request line longer than 1 MiB is discarded without being buffered; its reply has `"id":null` and answers the oldest outstanding request, because requests are answered in order.

Trust model: `serve` has the same authority as the one-shot commands run by the same user. It reads requests only from its standard input, so only the parent process that started it can send them; it opens no socket or port. A request may name any `project` and `output` the process can access, and `generate` writes there, exactly like `runic-translations generate --project ... --output ...`. Start it only from trusted tooling, and do not forward untrusted input into its standard input.

## Compatibility and status

This tool targets .NET 10. Before 1.0 a minor release may contain breaking changes, each listed with upgrade steps in the release notes (see [Versions before 1.0](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/eng/release/README.md#versions-before-10)). Pin one exact version in the local manifest and coordinate upgrades with all consumers of its generated artifacts.

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
