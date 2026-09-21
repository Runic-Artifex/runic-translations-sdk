# Semantic v5 golden corpus

`message.mf2` is a lossless syntax input for the opt-in semantic compiler.
`locale-artifact.json` is a v5 schema instance containing its expected normalized
AST. The compiler test compares the emitted AST structurally with this fixture.

The enclosing artifact is hand-authored: its all-zero fingerprint and empty
markup contract are placeholders for schema shape validation. It is not a
runtime-activatable pack, a generated project artifact, or evidence that v5
project linking is implemented. The normal project compiler still emits v4.

The fixture exercises declaration order, typed caller inputs, formatted local
chains, dynamic options, ordered valueless/empty/numeric annotations, numeric
exact keys, category keys, wildcard fallback, literal formatting and closing
markup annotations. The compiler test suite adds malformed input, Unicode
normalization, numeric domain boundaries and multi-selector precedence cases.
