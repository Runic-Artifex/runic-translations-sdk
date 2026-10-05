# Developer and translator experience implementation plan

Recorded 2026-10-05 before implementation. Baseline: `7859afeb49b556726ea7498c7fca7b2f1a516596`.
The user selected developer and translator experience, then authorized implementing all findings using GPT-6.1 Sol subagents. This document preserves findings, scope and progress across context compaction.

## Findings and acceptance criteria

| ID | Finding and implementation | Completion criteria | Status |
| --- | --- | --- | --- |
| T1 | Structural composer controls emit objects discarded by `+page.svelte:690`; ordinary text edits work. Existing MF2 sources are wrapped as one plain variant. Legacy input rename mutates temporary nodes, and chip parsing expects `{name}` rather than MF2 `{$name}`. Implement compiler-backed editable projection and revision-safe source operations for variables, declarations/selectors and plural branches. | Open existing plural, edit branch, preview 1/2, save/reload and verify source/runtime results. Structural operations persist; unknown syntax/comments preserved or explicit capability fallback shown. Never serialize legacy JSON into MF2. | Validated compiler/smoke/hosted corrected candidate; release gate below |
| T2 | QA compares full source with `.trim()`, falsely warning on normal terminal file newlines. Glossary/fuzzy matching inspect raw syntax and ignore placeholder/slot compatibility. Use semantic message text/contracts for quality and suggestions. | Real mf2/rmf2 fixtures distinguish storage newline from intentional message whitespace; declarations excluded from glossary; incompatible suggestions rejected or clearly labeled. | Validated compiler/pure fixtures |
| T3 | Compiler/LSP preserve source comments and `@example` values, but Editor entries drop context and use generic preview samples. Carry descriptions/tags/examples through additive DTOs and display context/sample choices. | Context and examples survive mounted sources/unsaved drafts; chosen samples produce canonical previews equivalent to LSP. | Validated compiler/mounted smoke/hosted corrected candidate |
| T4 | Diagnostic selection opens raw document without selecting its span; LSP has no code actions. Add exact navigation and at least one safe compiler-owned revision-aware quick fix shared by Editor/LSP. | UTF-8/UTF-16, multiline/mounted-path spans correct; stale fixes refuse newer edits; shared fix has meaningful regression coverage. | Validated compiler/LSP/smoke/hosted corrected candidate |
| T5 | VS Code previews use first example, repeatedly prompt and remain static. Expose all examples, remember message sample sets during session, and refresh unsaved source previews. | Refresh uses latest source; cancellation/selection races cannot replace a newer preview; session sample reuse verified. | Validated installed VS Code host |
| D2 | Root README offers contributor commands only. Editor README names absent `dev:editor` and falsely claims all Runic dependencies use source. Vite guide ignores `.runic` but verifies missing generated output before building. Add consumer template/.NET, Vite and Editor routes and correct launch/CI commands. | Package-only quick-start verified; Editor launch command exists with correct published dependencies; clean-checkout Vite flow works; byte-verification reserved for intentionally retained artifacts. | Validated package consumers and own locked editor shell |

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

### Focused implementation evidence

- T2 semantic QA and suggestions now use compiler literal text, boundary whitespace, typed placeholder signatures and functional slots. Pure helper and strict TypeScript checks pass; meaningful semantic fixtures cover storage newline, declarations, and incompatible suggestions. Independent function review found no blocker, conditional on compiler metadata propagation.
- T4 compiler/LSP implementation provides physical UTF-8/absolute UTF-16 spans and an explicit-empty-assignment quick fix authorized by compiler diagnostics, tied to complete source SHA-256. LSP actions use document versions and stale resolve refusal. Focused compiler suite passed 52 tests and CLI/LSP/build suite passed 13 tests before final integration.
- D2 package-only verifier passed all six NuGet and three npm candidates, including template-generated typed console, fresh Vite generation/build, and retained compiler output verification before/after manifest-mode Vite build. Fresh ignored `.runic` output is generated by build; type declarations live outside retained compiler trees for byte verification. C# examples now use actual collision-safe generated members and tool manifests explicitly target `.config` under .NET 10.
- Editor page wiring serializes structural operations with the latest compiler projection revision, coalesces pending branch typing, retains recovery/save queues, and exposes context example sample choices. Exact raw selection uses compiler absolute UTF-16 spans after mount; stale diagnostics are revalidated against current content. Quick fixes edit drafts and use the existing revision-aware save flow. Hosted acceptance fixture now includes plural edit/preview 1 and 2/save/reload; execution remains pending the final integrated build.

### Integrated verification and upstream transport defect

Release Editor build (Verification mode), prebuilt Release smoke, and complete Frontend `verify:built` passed. The standalone CI runner now consumes the built frontend verification suite and prebuilt smoke, replacing a stale absent `eng/toolchain.mjs`/old monorepo job assertion. Native VS Code final host also passed (12 unit tests/44 assertions), including multiple samples, session reuse, latest unsaved buffers and restart.

The real hosted Editor journey passed queued logical edits/save/reload and applied a plural branch edit, but canonical preview acceptance exposed two runtime issues. The new example-label input had an inferred string carrier while the page supplied a number; this was fixed and covered through actual generated English/German UI messages. After that correction, raw native transport responses intermittently append bytes after complete valid JSON (`error:null}{`, or Snapshot suffixes `022se`, `t`). Captured report: `artifacts/editor-hosted-authoring-transport-failure/failure.json`; no frontend sanitization was introduced.

Backend inspection identified the pinned published `CsWebUi 2.5.0-beta.4.5` UTF-8 boundary: `Utf8.Encode` allocates an uninitialized `byteCount+1` buffer and fails to write its final zero terminator before C-string native response calls. Root is coordinating independent confirmation and the smallest repair through the published dependency boundary. Hosted plural/preview1/2/save/reload and Unicode fix acceptance remain pending that upstream repair; source/compiler/UI implementation is not being represented as a passed browser proof.

An optional own locked `.#editor` Nix shell now supplies browser/native Linux prerequisites; default CLI shell and lockfile stay unchanged. The docs distinguish headless verification from an interactive native desktop session. The next hosted attempt will use `.direnv/editor-profile` rather than a sibling development environment.

Final focused managed suites passed: Authoring 38, Build 27, Compiler 61, Generator 8, Runtime 112 and Tooling 10; additive compiler diagnostic-action API approval passed without changing existing API/runtime baselines. All three web package suites passed (Vite 13, Svelte 7, SvelteKit 9). Logs: `artifacts/authoring-managed.log`, `artifacts/authoring-web.log`.

Root's upstream repair produced isolated `CsWebUi`/`CsWebUi.Native 2.5.0-beta.4.6` candidates, with native assets identical to the published 4.5 package. An isolated source-only Editor copy and NuGet package directory consume that candidate for proof; ordinary repository dependencies remain published 4.5. Its Release build and prebuilt smoke passed. Candidate provenance is recorded in `artifacts/editor-candidate-verification.json`; the hosted acceptance report will be `artifacts/editor-hosted-authoring-candidate`. Shipping dependency selection remains root-owned and requires the upstream published repair.

The complete hosted journey passed against that corrected candidate (`artifacts/editor-hosted-authoring-candidate/result.json`, with stable assembly SHA-256, browser metadata and retained logs). It proves queued edits/save/reload/conflict recovery, plural branch editing, canonical previews for 1 and 2, both context examples, source preservation/no legacy JSON, saved/reloaded preview, exact UTF-16 diagnostic selection after a supplementary Unicode character, and the shared explicit-empty quick fix through save. Screenshots: `plural-saved-en.png`, `diagnostic-fixed-en.png`. The diagnostic's compiler span covers the full `cancel = ` assignment; a name-only harness assumption was corrected after inspecting the actual span and selection. No production selection change or transport sanitization was required.

**Release gate:** the repository's ordinary published 4.5 dependency still contains the upstream native transport defect. Feature acceptance on isolated 4.6 is complete, but ordinary native launch/hosted verification cannot be called repaired until root publishes/chooses the upstream fix and pins the released package. No registry publication or dependency change was performed by this implementation team.

Independent review accepted T1–T5 and D2 against the corrected hosted candidate after inspecting assertions, logs, browser metadata, screenshots, assembly hashes and current-versus-candidate source hashes. Failed hosted fixture/state directories were removed after diagnosis; their reports remain under `artifacts/`. VS Code diagnostic logs were consolidated under `artifacts/vscode-preview-diagnostics/`. The isolated candidate source/restore/output remains available for root's upstream release decision.

Final integration confirmation: the compiler public API baseline contains only 45 additive lines for diagnostic-action types/members. The affected approval suite passed again against retained Release outputs: Runtime 74 types/440 members, Compiler 32/267, Tooling 20/113, Generator 1/2; total 127 types/822 members (`artifacts/authoring-api-approval.log`). All 535 implementation/component/module/test/package source files compared against the accepted candidate match, and its recorded three Editor/CsWebUi DLL hashes remain unchanged (`artifacts/editor-candidate-final-source-comparison.json`). Ordinary Editor restore assets still resolve published `CsWebUi` and `CsWebUi.Native 2.5.0-beta.4.5`; the stock dependency release gate above remains active.
