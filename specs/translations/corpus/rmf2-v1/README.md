# RMF2 v1 conformance corpus

This directory is the version-explicit release oracle for `rmf2-v1` resources
executed by `rmf2-execution-v2`: normalized message AST and locale artifact v5,
runtime ABI 2, generated-name mapping v1, and markup contract v1. `index.json`
is language-neutral; the compiler, generated C#, .NET pack loader, generated
ESM, and ESM dynamic-pack runners consume the same cases and expectations.

The primary project covers direct and effective resources across `en`, `de`,
`fr`, and a requested regional `fr-FR`, including an explicit fallback chain.
`locale_extra` is deliberately absent from the base locale: it is dynamically
addressable in German, but must not enter canonical generated APIs or the caller
fingerprint. The layout fixtures prove equivalent flat, split, and mounted
logical catalogs without copying the primary project. The flat layout declares
CRLF framing in the index; runners materialize those bytes so Git newline
normalization cannot erase this part of the contract.

The corpus reuses the message-level [`semantic-v5`](../semantic-v5/README.md)
golden for typed literals, declarations, dynamic options, ordered annotations,
numeric selection, and recovery-adjacent semantic validation. It also retains
the smaller [`v5-project`](../v5-project/README.md) fixture as a focused linker
golden. Neither referenced fixture is silently treated as an activatable pack:
only artifacts emitted from the project in this directory are pack inputs.

Cross-backend expectations store decimal and integer arguments as strings so a
JavaScript runner cannot round through binary64. Structured cases compare both
the effective content locale and the explicit plain-text projection with typed
functional-slot bindings. Pack rejection cases use the public `RTR0023/*`
taxonomy shared by .NET and ESM, including closed-member rejection for every
normalized wrapper, AST, input, declaration, selector, variant, key, node,
expression, option, annotation, and value object shape. Value discriminator
cases also prove that a missing or non-string `kind` remains malformed even
when the object contains an additive member.

The corpus intentionally excludes terms, references, group fallback, C++, rich
XLIFF, application call-site rewriting, direct legacy-to-RMF2 migration, and
marketplace concerns. Those are outside the frozen RMF2 v1 release boundary.
