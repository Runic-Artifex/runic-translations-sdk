# Build and CLI contract

The build task and `runic-translations` tool are adapters over the same pure
compiler and renderers. They MUST report the same `RTR` diagnostic ID,
severity, invariant message arguments, normalized source path, and source span
for equivalent input.

## CLI grammar

The exact command forms are:

```text
validate --catalog <file> --documents <path-or-glob...>
generate --catalog <file> --documents <path-or-glob...> --output <directory>
verify   --catalog <file> --documents <path-or-glob...> --output <directory>
schema   --output <directory>
help | --help | -h
```

Each scalar option occurs once. `--documents` consumes one or more explicit
paths or globs up to the next option. There is no implicit discovery. Globs are
expanded deterministically and the resulting normalized paths are ordinal-sorted.

An argument beginning `@` expands a strict UTF-8 response file. Response files
support quoted tokens and full-line `#` comments, may nest to depth 16, and
resolve nested relative paths from the including file. `@@` escapes a literal
leading `@`. Cycles, invalid UTF-8, unterminated quotes, missing files, or deeper
nesting are invocation failures.

Exit categories are stable:

- `0`: requested operation succeeded; warnings may have been printed;
- `1`: compiler errors, `RTR0020`, or a `verify` difference;
- `2`: invalid invocation, I/O failure, or unexpected internal failure.

`validate` performs no writes. `schema` writes the bundled versioned schemas.
Diagnostics go to the diagnostic stream in deterministic order; generated
artifact bytes never share that stream.

## Generate and verify

`generate` renders the complete declared output set into a sibling temporary
directory, flushes and closes files, then replaces the declared destination
files. It never edits source inputs. A failure before replacement leaves the
last complete output set intact. Stale files owned by the same output manifest
are removed only as part of the successful replacement transaction.

`verify` renders to an isolated temporary directory and compares bytes. It
reports ordinal-sorted `missing`, `changed`, and `extra` relative paths and does
not modify the requested output directory. "Extra" means a file claimed by the
Translations asset manifest but absent from the expected render set; unrelated
consumer files outside that manifest are not claimed or deleted.

Output roots are canonicalized before writes. Absolute child paths, rooted
paths, empty names, `.`/`..` segments, device names, alternate data streams,
separator tricks, and links/reparse points that escape the permitted root are
rejected with `RTR0020`. Containment is rechecked immediately before each
replacement.

## Build integration

Build inputs are explicit catalog/document items. Outputs live beneath an
explicit intermediate/output root, declare deterministic `Inputs` and `Outputs`,
and are exposed as items for downstream packaging. A normal build never writes
tracked source artifacts. Clean removes only files listed in the owned asset
manifest and never traverses an unconstrained consumer directory.

The build package, CLI, and compiler can release together, but their package
versions do not select schema, grammar, artifact, manifest, or runtime ABI
behavior.
