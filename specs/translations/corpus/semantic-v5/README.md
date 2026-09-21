# Semantic v5 golden corpus

`message.mf2` is a lossless syntax input for the opt-in semantic compiler.
`locale-artifact.json` is a v5 schema instance containing its expected normalized
AST. The compiler test compares the emitted AST structurally with this fixture.
Draft 2020-12 validation also checks emitted ASTs and the complete envelope using
the checked-in schema references, and rejects representative malformed mutations.
Each expression records its underlying `valueType` separately from its formatter.

The enclosing artifact is hand-authored: its all-zero fingerprint and empty
markup contract are placeholders for schema shape validation. It is not a
runtime-activatable pack or generated project artifact. Profile-aware hosts test
project activation separately; this fixture remains only semantic/schema evidence.

The fixture exercises declaration order, typed caller inputs, formatted local
chains, dynamic options, ordered valueless/empty/numeric annotations, numeric
exact keys, category keys, wildcard fallback, literal formatting and closing
markup annotations. The compiler test suite adds malformed input, Unicode
normalization, numeric domain boundaries and multi-selector precedence cases.
