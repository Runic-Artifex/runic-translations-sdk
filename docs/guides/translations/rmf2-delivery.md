# RMF2 continuation deliverables

This checklist tracks work on `feature/rmf2` following the initial implementation
in `61603fd8`. The accepted design and milestone boundaries remain in W200/D014.
Items are ordered by dependency; the last two deliverables are the requested IDE
integrations. Completing a slice does not imply full Unicode MF2 execution.

1. [ ] Shared lossless MF2 syntax/data model: declarations, operands, options,
   attributes, variants and open/close/standalone markup with exact source spans;
   separate syntax, caller-contract, inline-profile and backend diagnostics.
2. [ ] Broader bounded execution: literal/local evaluation, formatter options and
   portable .NET/ESM fixtures, with explicit unsupported-target diagnostics.
3. [ ] Shared catalog language service: contract-aware completion/hover, examples,
   references and safe input/local/slot/resource refactors, mounted configuration
   edits, incremental indexes, cancellation and stale-result suppression.
4. [ ] Rendering integrations: pre-resolved registry dispatch, web SSR/hydration,
   native toolkit adapters and accessibility/plain-text behavior, exercised with
   the payment consumer and representative allocation measurements.
5. [ ] Authoring workflow: structural Editor commands, rich previews, lossless
   resource/markup interchange and maintained consumer journeys.
6. [ ] Cross-language navigation/refactors across the supported C#, TypeScript,
   Svelte, TOML and legacy MF2 boundaries, with explicit refusal of unsupported edits.
7. [ ] VS Code integration: packaged RMF2 language support, project-local language
   server lifecycle, syntax highlighting, diagnostics/navigation/refactoring and
   preview commands, configuration and extension-host integration tests.
8. [ ] Visual Studio integration: packaged RMF2 content type and language client,
   project-local server lifecycle, diagnostics/navigation/refactoring and preview
   commands, configuration and Windows integration verification.

C++ RMF2 execution, terms and atomic compound fallback remain separately scoped
follow-on work under the existing backend limits and W200-010; the core delivery
list does not silently promote them into prerequisites.

Implemented continuation slices: lossless tokens and expression/declaration
projections, separate inline-profile validation, unformatted literal-local and
alias folding, semantic local rename through the workspace and LSP, shared
project-markup completion/hover, and catalog-wide input definitions/references
with message-local scoping. Items
1–3 remain open for their remaining grammar, execution and catalog-wide work.
