# Runic locale TOML profile v1

A project opts into this profile with `"sourceLayout": "locale-toml"` in its
schema-version-1 `runic.json`. Absence selects the historical
`{locale}/{message-id}.mf2` layout. Other discriminator values are errors.
`CompileProject` dispatches explicitly; `CompileMf2Project` remains a compatible
entry point and forwards to the dispatcher.

A locale file is named `{locale}.toml` directly beside the project file. Locale
tags use the compiler's existing canonicalization. Multiple filenames mapping
to the same canonical tag are an error. A project cannot mix MF2 and TOML source
files. An empty TOML file represents a locale with no direct messages; normal
completeness and fallback policies still apply.

The container language is TOML 1.1.0, parsed and semantically validated by the
pinned Tomlyn 2.10.1 syntax layer. Only top-level key/string pairs are admitted.
Decoded keys must match `[A-Za-z_][A-Za-z0-9_]*`. Bare, basic-quoted and
literal-quoted keys are equivalent. Dotted structural keys and literal dots in
quoted keys are rejected. Tables, arrays, inline tables and nonstring values
are rejected. Duplicate keys are errors, including differently quoted spellings.
Comments, whitespace, Unicode and TOML's basic, literal, multiline basic and
multiline literal string forms are supported according to TOML 1.1.

Every decoded string is an MF2 message in the existing Runic MF2 profile. The
compiler continues its existing newline normalization and body trimming; storing
decoded bytes does not change formatted-message semantics. No runtime grammar,
ABI, descriptor schema or application runtime dependency changes.

The existing limits apply independently: physical document bytes (8 MiB by
default), nesting depth (64), decoded value bytes (64 KiB), keys per catalog
(50,000), locales per catalog (256), and MF2 placeholder/profile limits. Invalid
UTF-8 is rejected. Parsing is bounded by document bytes and parser depth;
cancellation is checked before/after the synchronous syntax parse and between
entries. Source enumeration and diagnostics are sorted deterministically.

## Physical and logical identity

`TranslationLocaleReader.Read` returns an immutable physical `TranslationSource`,
locale, entries and diagnostics, without leaking parser types. Each entry carries
its logical key and decoded UTF-8 `Message`, whose path remains the actual
physical path. `KeyLocation` and `ValueLocation` address raw TOML tokens in UTF-8
bytes; `StatementLocation` covers the key through the value and excludes leading
indentation and trailing comments/newline. All ranges are half-open. Tomlyn's
inclusive UTF-16 syntax spans are converted using a precomputed UTF-8 boundary
index, including astral Unicode.

MF2 currently reports whole-message errors. Such diagnostics point to the
containing TOML value token, including delimiters. They do not claim token-level
precision or a decoded-byte boundary map through TOML escapes. TOML syntax
errors retain the parser's physical span. Consumers must use the physical value
span for replacement and must not derive offsets by adding quote widths to
decoded MF2 positions. A future precise MF2 mapper requires retaining offsets
through both TOML decoding and existing MF2 newline/body normalization.

Writers verify the whole-file revision and splice value/key token byte ranges,
then reparse and compile the complete proposed document. Deleting a statement
must explicitly choose comment ownership; the reader never silently includes a
neighboring comment. Multiple logical edits sharing a file form one physical
replacement and transaction. Unchanged bytes retain their original spelling.
