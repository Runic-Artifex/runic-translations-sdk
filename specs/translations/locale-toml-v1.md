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
pinned Tomlyn 2.10.1 syntax layer. String messages may use root keys, dotted
keys, ordinary tables, nested tables and inline-table containers. Each decoded key or table path segment
must match `[A-Za-z_][A-Za-z0-9_]*`. Bare, basic-quoted and literal-quoted
segments are equivalent. Literal dots inside a quoted segment are rejected;
structural dots separate segments. Inline tables recursively contain strings or
other inline tables or arrays of identifiable inline tables. Scalar/mixed arrays and
nonstring message leaves are rejected. Empty arrays represent empty groups. TOML's duplicate key, table redefinition
and value/table namespace rules apply before Runic identifier flattening.
Comments, whitespace, Unicode and TOML's basic, literal, multiline basic and
multiline literal string forms are supported according to TOML 1.1.

The compiler joins the enclosing table path and local key path with `_` to
produce the generated message identifier. For example, `document.saved = 'Saved'`
and `[document]` followed by `saved = 'Saved'` both produce `document_saved`.
`[document.dialog]` followed by `title = 'Title'` produces
`document_dialog_title`. Flat identifiers retain their existing meaning and
are never heuristically split at underscores. Distinct TOML paths that flatten
to the same identifier are rejected: `document_saved` conflicts with
`document.saved`, and `a_b.c` conflicts with `a.b_c`. These collision checks span
the entire locale document, including inline containers. For example,
`document = { saved = 'Saved', dialog = { title = 'Title' } }` produces
`document_saved` and `document_dialog_title`. Inline tables obey TOML
namespace sealing: subsequent dotted keys or table declarations cannot extend
them. Grouping is organizational syntax for MF2 string
messages, not a separate variant or matching language.

## Arrays of tables

Arrays of tables are a compatibility authoring option; generated/new documents
prefer readable ordinary named groups and nested tables. Each `[[notifications]]`
element requires a direct `_id` string matching `[A-Za-z_][A-Za-z0-9_]*`.
The identity becomes one effective path segment: `_id = 'saved'` and
`title = 'Saved'` produce `notifications_saved_title`. The same identity must
identify the same logical row across locale files; row ordering does not affect
message identifiers. Duplicate identities within one actual parent array are
errors, even for rows without message leaves. `_id` is structural only directly
inside an array element; ordinary tables/root/ordinary inline tables may retain
an `_id` string message.

Nested array declarations bind to the most recently declared actual parent
according to TOML semantics. For example `[[menus]]` with `_id = 'file'`, then
`[[menus.items]]` with `_id = 'open'`, then `title = 'Open'`, produces
`menus_file_items_open_title`. A later `menus` element starts a new parent scope;
its nested items may reuse `open` without colliding with the earlier parent's ID.
Ordinary child table headers also inherit the current parent element identity.

Inline arrays use the same mapping: `notifications = [{ _id = 'saved',
title = 'Saved' }]` produces `notifications_saved_title`. Every array element
must be an inline table with a valid direct identity. Nested inline arrays are
supported. Scalar and mixed arrays are rejected. Empty arrays contain no messages and
create no synthetic row identity or row insertion target; no numeric index-based
message identities are inferred. All forms share flattened-ID
collision detection, value limits, total declaration limits and effective path
depth limits, including inserted identity segments.

Every decoded string is an MF2 message in the existing Runic MF2 profile. The
compiler continues its existing newline normalization and body trimming; storing
decoded bytes does not change formatted-message semantics. No runtime grammar,
ABI, descriptor schema or application runtime dependency changes.

The existing limits apply independently: physical document bytes (8 MiB by
default), nesting depth (64), decoded value bytes (64 KiB), keys per catalog
(50,000 key declarations across root, tables and inline containers), locales per catalog (256), and MF2 placeholder/profile limits. Invalid
UTF-8 is rejected. Parsing is bounded by document bytes and parser depth;
cancellation is checked before/after the synchronous syntax parse and between
entries. The combined enclosing-table and local-key segment count, including
empty table paths, is also bounded by the depth limit. Source enumeration and diagnostics are sorted deterministically.

## Physical and logical identity

`TranslationLocaleReader.Read` returns an immutable physical `TranslationSource`,
locale, entries and diagnostics, without leaking parser types. Each entry carries
its flattened logical key and decoded UTF-8 `Message`, whose path remains the
actual physical path. Immutable `TablePath`, `InlinePath` and `KeyPath` lists retain decoded
enclosing-table, relative inline-container and local-key segments before
flattening. Non-inline entries have an empty `InlinePath`. `KeyLocation` covers
the entire local dotted key token sequence, excluding its enclosing table
header. `RootInsertionByte` identifies the first table opening bracket or EOF
when no table exists, including documents containing only empty tables. This
lets writers insert root entries without accidentally inheriting table scope.
`Tables` lists declared tables and array elements in physical order, including empty tables.
Each table has its effective `Path` (including array identity segments), original
header `DeclaredPath`, `IsArrayElement` flag, bracket-inclusive `HeaderLocation` and
`InsertionByte` at the next table opening bracket or EOF. Writers may use these
boundaries to add messages within the deepest matching existing group. Leading
or trailing comments are not owned implicitly by a header or message.

`InlineTables` is separate from ordinary `Tables`. Each inline table provides
its effective absolute segment `Path`, `IsArrayElement` flag, brace-inclusive
`ValueLocation`, and ordered
`Members`, including nested container members. Each member provides its local
`KeyPath`, physical key/value/statement spans and nullable `SeparatorLocation`
for its trailing comma. Array element members include structural `_id` for
separator preservation, while logical message `Entries` omit it. Writers must
not expose direct array `_id` as a message-add/rename target. This permits comma-aware authoring without treating
inline containers as ordinary table headers. Inline additions must occur before
the closing brace; inline deletions must preserve a valid separator sequence.
TOML 1.1 multiline strings, newlines and trailing commas remain supported inside
inline containers. Writers must validate the complete result before committing. `KeyLocation` and `ValueLocation` address raw TOML tokens in UTF-8
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
