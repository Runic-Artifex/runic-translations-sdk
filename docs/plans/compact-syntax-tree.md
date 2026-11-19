# Compact syntax tree: API impact (Option A chosen)

Recorded 2026-11-19 for tr#271, step 3 (memory). **Decision (maintainer, 2026-11-19): Option A, as step 5 of #271,
within D077.10. Option B is on hold:** it touches the language server's and the Translations Editor's (D074) areas, and is
decided only after A, once the server's own caches are measured, if the 20k memory budget still misses.

## Where the memory is

Measured at 20,000 messages × 10 locales (200,000 messages), as the live heap after a validation session's first
compile, with the trims of the memory PR applied:

| Part | per message | at 20k |
| --- | ---: | ---: |
| The compiled result: the lowered model and the MF2 syntax without its token stream | 1,808 B | 344 MiB |
| The session's own: the RMF2 parse nodes, byte maps, the extracted messages and the memos | 1,277 B | 243 MiB |
| Total | 3,086 B | 588 MiB |

Before the memory PR the session held 4,686 B per message (893 MiB). The token stream (7.2 tokens per message) is
already out of the steady state: a session releases it once a file is compiled, and `Mf2SyntaxDocument.Tokens`
reads it again on demand, with the same values. That keeps the API as it is.

What remains is mostly objects, not data. A message carries about 5–7 `TextSourceLocation` objects (48 B each), one
for each node, expression, operand, option, declaration, variant and key. It also carries its own `TranslationSource`
(path, and a copy of the message's UTF-8 bytes), and the records and lists that hold them.

## Option A: compact inside, same API (no breaking change); chosen

The syntax records keep their types and members. They store compact spans (start and length in the message, as
integers) and a shared line index, and create the `TextSourceLocation` a member returns when it's read.
`Mf2SyntaxDocument.Source` is created on demand from the file's bytes and the message's byte map.

- **Saves** an estimated 250–400 B per message (50–80 MiB at 20k).
- **Consumers:** no source change. Two behaviors change:
  - Two reads of a location return equal but different objects. Nothing compares them by reference today (checked:
    the language server, the editor, Authoring and Tooling read their fields).
  - A read allocates. The server and editor read locations for one message or one file at a time (hover,
    completion, references, rename, semantic tokens, diagnostics).
- **Risk:** low, but positional records stop being plain records. Their constructors become internal factories,
  constructed only inside the compiler.

## Option B: a compact syntax model (breaking); on hold

The syntax records (`Mf2SyntaxToken`, `Mf2OperandSyntax`, `Mf2PropertySyntax`, `Mf2ExpressionSyntax`,
`Mf2DeclarationSyntax`, `Mf2VariantSyntax`, `Mf2MatchSyntax`, `Rmf2ResourceNode`) expose a value-type span, such as
`Mf2Span { int StartByte; int LengthBytes; }`, instead of `TextSourceLocation`. Lines and columns come from the
document (`document.LineColumn(span)`). The token stream becomes an array of structs. `Raw`/`Value` would be
slices of the message text, read through the document.

- **Saves** an estimated 600–900 B per message (120–170 MiB at 20k), and the allocation of the first compile
  (shorter GC, a faster open).
- **Breaks:** every consumer that reads a syntax location. As of this commit:
  - **Language server** (`tools/dotnet-runic-translations`): `Rmf2LanguageServer.cs`,
    `Rmf2LanguageServer.Refactoring.cs`, `Rmf2LanguageServer.Libraries.cs`. These cover hover, completion, references,
    rename, semantic tokens and code actions: `.Location`, `.NameLocation`, `.Tokens`, `.Expressions`,
    `.Declarations`, `VariableReferences`, `MessageByteMap`.
  - **Translations Editor** (`apps/translations-editor`): `PreviewService.cs`, `ReviewService.cs`,
    `EntryProjector.cs`, `EditorMessageDrafts.cs`, `MutationPlanner.cs`, `DiagnosticProjector.cs`,
    `EditorDocumentViewModel.cs`.
  - **Authoring and Tooling** (mine): `Rmf2Workspace.cs`, `Rmf2ResourceWriter.cs`, `TranslationInterchangePattern.cs`,
    `TranslationInterchangeProjection.cs`, `TranslationCatalogImport.cs`, `TranslationLibraryCatalog.cs`,
    `TranslationReviewFile.cs`, `TranslationXliffReader.cs`; and `MessageEditing.cs` in the tool.
  - **Unchanged:** `TextSourceLocation` itself, a public type in the compiler's PublicAPI that diagnostics use. So are
    the diagnostics, the lowered model and every public package API. The change is to compiler-internal types that
    these hosts see through InternalsVisibleTo.
- **Migration:** one PR per consumer area, coordinated with the language-server agent and the D074 workstream, or one
  PR that adapts all of them with their owners' review. Each call site of `x.Location` becomes
  `document.Location(x.Span)`.

## Recommendation

Option A first: it saves most of what is left per object without touching a consumer. Option B only if the
memory budget at 20k still needs it after A and the server's own caches are measured.
