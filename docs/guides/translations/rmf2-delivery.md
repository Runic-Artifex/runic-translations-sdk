# RMF2 continuation deliverables

This checklist tracks work on `feature/rmf2` following the initial implementation
in `61603fd8`. The accepted design and milestone boundaries remain in W200/D014.
Items are ordered by dependency; the last two deliverables are the requested IDE
integrations. Completing a slice does not imply full Unicode MF2 execution.

1. [x] Shared lossless MF2 syntax/data model: declarations, operands, options,
   attributes, variants and open/close/standalone markup with exact source spans;
   separate syntax, caller-contract, inline-profile and backend diagnostics.
2. [x] Broader bounded execution: literal/local evaluation, formatter options and
   portable .NET/ESM fixtures, with explicit unsupported-target diagnostics.
3. [x] Shared catalog language service: contract-aware completion/hover, examples,
   references and safe input/local/slot/resource refactors, mounted configuration
   edits, bounded syntax reuse, cancellation and stale-result suppression.
4. [x] Rendering integrations:
   pre-resolved registry dispatch, web SSR/hydration,
   native toolkit adapters and accessibility/plain-text behavior, exercised with
   the payment consumer and representative allocation measurements.
5. [x] Authoring workflow: structural Editor commands, rich previews, lossless
   RMF2 source edits, explicit XLIFF text-profile loss reporting and maintained
   consumer journeys. Arbitrary lossless rich XLIFF remains outside W200-008.
6. [x] Safe language boundaries: catalog-source navigation and refactors; native
   C#/TypeScript/Svelte services retain application usage ownership. F2 refuses
   application/legacy workspaces rather than returning partial edits. Explicitly
   named resource-only commands provide the narrower transaction.
7. [x] VS Code integration: packaged RMF2 language support, project-local language
   server lifecycle, syntax highlighting, diagnostics/navigation/refactoring and
   preview commands, configuration and extension-host integration tests.
8. [x] Visual Studio integration: packaged RMF2 content type and language client,
   project-local server lifecycle, diagnostics/navigation/refactoring and preview
   commands, configuration and Windows integration verification.

C++ RMF2 execution, terms and atomic compound fallback remain separately scoped
follow-on work under the existing backend limits and W200-010; the core delivery
list does not silently promote them into prerequisites.

Implemented continuation work includes deterministic grammar/data-model parsing,
multiline lowering and literal aliases; mounted resource, input/local/slot and
locale transactions; editor rich previews and mounted workflow smoke tests;
compiler-owned completion/diagnostics; syntax reuse, cancellation, stale-request
rejection and semantic highlighting; Svelte SSR/hydration and a WPF adapter.

Current focused results and reproducible measurements are in
[RMF2 validation](rmf2-validation.md). Core, authoring, compiler, CLI/LSP, editor,
Svelte browser and real VS Code host checks pass. Both IDE VSIX artifacts build.
Native Windows 11 verification now passes for the WPF payment consumer and
Visual Studio 2026 18.8.2 in an isolated experimental profile. Native IDE checks
cover registered preview/restart commands, inert rich content, invalid samples,
locale switching and unsaved buffers across restart. The RMF2 CLI/LSP suite
passes on both Windows and Linux; the runtime suite passes 175 checks.
Windows execution exposed and fixed portable-path overlays, per-locale preview
contracts, plain preview rendering and zero-input compiled variant formatting.
Visual Studio 2022 remains the compile-time API target; its own native host was
not separately exercised.

The IDE clients and their support matrices are documented in
[VS Code](../../../tools/vscode-runic-translations/README.md) and
[Visual Studio](../../../tools/visualstudio-runic-translations/README.md).
The latter keeps Visual Studio's JSON service and refuses configuration-changing
refactors; VS Code synchronizes unsaved resource and configuration buffers.
