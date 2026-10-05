# Developer and translator experience implementation plan

Recorded 2026-10-05 before implementation. Baseline: `7859afeb49b556726ea7498c7fca7b2f1a516596`.
The user selected developer and translator experience, then authorized implementing all findings using GPT-6.1 Sol subagents. This document preserves findings, scope and progress across context compaction.

## Findings and acceptance criteria

| ID | Finding and implementation | Completion criteria | Status |
| --- | --- | --- | --- |
| T1 | Structural composer controls emit objects discarded by `+page.svelte:690`; ordinary text edits work. Existing MF2 sources are wrapped as one plain variant. Legacy input rename mutates temporary nodes, and chip parsing expects `{name}` rather than MF2 `{$name}`. Implement compiler-backed editable projection and revision-safe source operations for variables, declarations/selectors and plural branches. | Open existing plural, edit branch, preview 1/2, save/reload and verify source/runtime results. Structural operations persist; unknown syntax/comments preserved or explicit capability fallback shown. Never serialize legacy JSON into MF2. | Planned |
| T2 | QA compares full source with `.trim()`, falsely warning on normal terminal file newlines. Glossary/fuzzy matching inspect raw syntax and ignore placeholder/slot compatibility. Use semantic message text/contracts for quality and suggestions. | Real mf2/rmf2 fixtures distinguish storage newline from intentional message whitespace; declarations excluded from glossary; incompatible suggestions rejected or clearly labeled. | Planned |
| T3 | Compiler/LSP preserve source comments and `@example` values, but Editor entries drop context and use generic preview samples. Carry descriptions/tags/examples through additive DTOs and display context/sample choices. | Context and examples survive mounted sources/unsaved drafts; chosen samples produce canonical previews equivalent to LSP. | Planned |
| T4 | Diagnostic selection opens raw document without selecting its span; LSP has no code actions. Add exact navigation and at least one safe compiler-owned revision-aware quick fix shared by Editor/LSP. | UTF-8/UTF-16, multiline/mounted-path spans correct; stale fixes refuse newer edits; shared fix has meaningful regression coverage. | Planned |
| T5 | VS Code previews use first example, repeatedly prompt and remain static. Expose all examples, remember message sample sets during session, and refresh unsaved source previews. | Refresh uses latest source; cancellation/selection races cannot replace a newer preview; session sample reuse verified. | Planned |
| D2 | Root README offers contributor commands only. Editor README names absent `dev:editor` and falsely claims all Runic dependencies use source. Vite guide ignores `.runic` but verifies missing generated output before building. Add consumer template/.NET, Vite and Editor routes and correct launch/CI commands. | Package-only quick-start verified; Editor launch command exists with correct published dependencies; clean-checkout Vite flow works; byte-verification reserved for intentionally retained artifacts. | Planned |

## Evidence and authority

T1 was independently confirmed by two read-only reviews and bounded pure-function probes. Relevant source: `MessageComposer.svelte:85,171,261`, `message-composer.ts:6,54`, `+page.svelte:680,1139`, `resource-model.ts:161`. Hosted UI proof previously covers plain messages/conflict/repair, not structural editing.

T2 probe: `Hello\n` → `Hallo\n` produces a whitespace warning (`review-model.ts:77`), although source storage normalizes terminal newlines. Preserve intentional quoted whitespace; do not blindly trim user message content.

The composer obtains plural categories from host `Intl.PluralRules`, while compiler/runtime use pinned CLDR 48.2. This is an authority/drift risk, not a demonstrated category mismatch. T1 must obtain categories/capabilities from compiler authority rather than introduce a second evaluator.

Preserve canonical compiler-backed previews/inert semantic runs, resource-only revision-aware atomic writes, recovery/review state, mounted discovery, bounded caches and virtualized lists. RMF2 source/schema/artifact/runtime ABI stays stable. Editor-internal DTOs may evolve additively. Keep dependencies on CLI/Application/Platform published and independently pinned; no sibling source fallback.

Related CLI work lives in `Runic-Artifex/runic-cli-sdk`, `docs/plans/developer-experience.md`: root help, generated diagnostics, context-aware completion, optional localization integration, bounded stdin/environment controls and independent-release/onboarding corrections.

## Working protocol

- Root integrates Git; agents do not stage/commit/push. Translations lead owns cross-cutting Editor wiring and allocates disjoint helper ownership before edits. No two agents edit `+page.svelte` or shared DTOs concurrently.
- Apply both Svelte skills for component/module analysis and editing. Read locked flake/env/development instructions; reuse caches/toolchains/browser configuration. No duplicate full matrices or broad cleanup.
- Focused meaningful tests while iterating; coordinated final local verification and one final GitHub CI run. Independent review before commits/PRs.
- Record owners, contract decisions, validation, review results and PR/commit evidence below. Publishing registry packages/release tags and merging PRs are outside this implementation.

## Progress and evidence

Implementation has not started at recording time. Root/Translations lead update statuses and decisions as work proceeds. Browser proof is still required for the structural-edit persistence journey.

### Implementation ownership (2026-10-05)

Translations lead owns `+page.svelte`, `TranslationEditor.svelte`, `ValidationPanel.svelte`, hosted Editor browser acceptance and this plan. Helpers have disjoint ownership: compiler/editor contract owner handles additive DTOs, bridge/generated bindings and backend authoring source operations; diagnostics owner handles shared compiler span/fix helpers and LSP; composer owner handles projection UI and resource metadata copying; semantic review owner handles semantic QA/suggestions; VS Code owner handles extension previews; onboarding owner handles consumer READMEs, canonical `docs/guides/translations` quickstarts and fresh package verifier. Root alone performs Git integration.

The compiler projection carries a source revision, capabilities/fallback reason, inputs, selectors, source-addressed variants and categories from the pinned compiler. Structural operations patch source through the backend; they never emit legacy JSON into MF2. Compiler entries add context (comments/tags/examples) and conservative semantic text/placeholder/slot contracts. Diagnostics add compiler-computed absolute UTF-16 spans and revision-bound quick fixes shared with LSP.
