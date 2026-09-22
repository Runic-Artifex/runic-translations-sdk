# Translation compatibility-retention ledger

This ledger records compatibility that remains readable in the current preview
without being part of the current authoring or generation contract. It prevents
retained names from being mistaken for supported new output.

## Retained surface

| Retained item | Reason it remains | Current-writer boundary | Exit criteria |
| --- | --- | --- | --- |
| `TranslationsCompatibility.RuntimeAbiVersion = 1` and the associated legacy snapshot/runtime path | Allow applications built against the published 0.3 package family to continue loading their existing generated catalogs while the v5 cut is adopted. | No current compiler, generator, CLI, MSBuild integration, or Vite path emits ABI 1. Current RMF2 output requires literal RMF2 ABI 2 and is checked with `EnsureRmf2RuntimeAbi(2)` or `SupportsRmf2RuntimeAbi(2)`. | Remove after repository and packed-consumer audits find no supported generated caller or fixture targeting ABI 1, the migration is documented in a release note, and the next intentional breaking release has passed the full package/NativeAOT matrix. |
| Public `CompiledTextMessage` and `CompiledTranslation*` constructors that predate the v5 model | Preserve source/binary compatibility for existing callers that construct the original snapshot graph directly. | These constructors retain their original ASCII placeholder validation and are not a v5 project-linking or pack-loading entry point. New code uses `CompiledRmf2*` types and `CompiledTextMessage.FromRmf2`, or the generated catalog APIs. | Remove once the shipped-0.3 compatibility set and all maintained examples/package consumers no longer reference the constructors, replacement APIs have a documented migration, and the API baseline is intentionally deleted in a breaking release. |

The retained surface is read-side/source compatibility only. It does not permit
an older carrier, selector, artifact, or generated web module to be selected by
`runic.json`, a project option, or a release tool. Unknown or mismatched versions
fail explicitly.

## Current contract

All current writers use the single `rmf2-execution-v2` contract: resource syntax
`rmf2-v1`, grammar and normalized AST 5, locale artifact 5, RMF2 runtime ABI 2,
and ESM ABI 4 under `web-module-manifest-v3`. Both direct `.mf2` and grouped
`.rmf2` authoring feed this same contract; direct `.mf2` is an active supported
representation, not a compatibility format.

The ledger is reviewed when a release changes the generated contract or package
baseline. It is not a telemetry promise and does not add a separate release gate;
the ordinary full CI and packed-consumer checks provide the evidence for each
removal decision.
