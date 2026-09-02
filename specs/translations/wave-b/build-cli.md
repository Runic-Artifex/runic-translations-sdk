# Build and CLI contract

The build integration and `runic-translations` tool are adapters over the same
MF2 project compiler and renderers. They MUST report the same `RTR` diagnostic
ID, severity, invariant message arguments, normalized source path, and source
span for equivalent input.

## Project convention

Every operation starts from one project directory containing `runic.json`.
Messages use `{locale}/{message-id}.mf2` paths relative to that file. Catalog
manifests, resource JSON documents, document globs, response files, and implicit
package-management operations are not part of the command contract.

## CLI grammar

The exact command forms are:

```text
init     --directory <directory> --catalog <id> --default-locale <tag> --namespace <namespace> --class <name> [--locale <tag[:fallback]>...] [--no-starter]
validate --project <directory>
generate --project <directory> --output <directory> [emit-options]
verify   --project <directory> --output <directory> [emit-options]
schema   --output <directory>
help | --help | -h
```

Each scalar option occurs once. Exit categories are stable:

- `0`: requested operation succeeded; warnings may have been printed;
- `1`: compiler errors or a `verify` difference;
- `2`: invalid invocation, I/O failure, or unexpected internal failure.

`validate` performs no writes. `schema` writes only the currently supported
project and generated-artifact schemas. Diagnostics go to the diagnostic stream
in deterministic order; generated artifact bytes never share that stream.

## Generate and verify

`generate` renders the requested output set into a sibling temporary directory,
flushes and closes files, then replaces the declared destination files. It never
edits source inputs. A failure before replacement leaves the last complete output
set intact. Stale files owned by the same output manifest are removed only as
part of the successful replacement transaction.

`verify` renders to an isolated temporary directory and compares bytes. It
reports ordinal-sorted `missing`, `changed`, and `extra` relative paths and does
not modify the requested output directory. Unrelated consumer files outside the
owned output manifest are not claimed or deleted.

Output roots are canonicalized before writes. Absolute child paths, rooted
paths, empty names, `.`/`..` segments, device names, alternate data streams,
separator tricks, and links/reparse points that escape the permitted root are
rejected. Containment is rechecked immediately before each replacement.

## Build integration

The build package discovers exactly one configured or conventional `runic.json`
project and its locale MF2 files. Outputs live beneath the intermediate root,
declare deterministic inputs and outputs, and are exposed as items for downstream
packaging. A normal build never writes tracked source artifacts. Clean removes
only files listed in the owned output manifest and never traverses an
unconstrained consumer directory.
