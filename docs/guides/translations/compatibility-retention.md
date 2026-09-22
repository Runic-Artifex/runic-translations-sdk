# Translation compatibility-retention ledger

This ledger records compatibility that remains readable in the current preview
without being part of the current authoring or generation contract. It prevents
retained names from being mistaken for supported new output.

## Current markers and retained surface

| Retained item                                                                                  | Reason it remains                                                                                                                           | Current-writer boundary                                                                                                                                                                                                                                          | Exit criteria                                                                                                                                                                                                               |
| ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Generic generated-code ABI marker `RuntimeAbiVersion = 1`                                      | Keep the generated C# registration contract stable across the current package family. This is a current marker, not a legacy snapshot path. | The current C# v5 writer emits `RuntimeAbiVersion = 1` and checks it against `TranslationsCompatibility.RuntimeAbiVersion`. It is independent from the RMF2 execution ABI.                                                                                       | Change only in a coordinated generated-code/runtime ABI revision with a migration note, updated API/generator baselines, and the full packed-consumer and NativeAOT matrix.                                                 |
| RMF2 execution ABI marker `Rmf2RuntimeAbiVersion = 2`                                          | Identify the current v5 typed evaluator, pack loader, and generated C#/ESM execution contract.                                              | The current C# and ESM writers emit RMF2 ABI 2 and require `EnsureRmf2RuntimeAbi(2)` or the equivalent generated check. No current writer emits an older RMF2 execution ABI.                                                                                     | Change only with a separately versioned execution contract, cross-runtime corpus update, explicit migration, and full package/NativeAOT/ESM consumer evidence.                                                              |
| Public `CompiledTextMessage` and `CompiledTranslation*` constructors that predate the v5 model | Preserve source/binary compatibility for callers that construct the original snapshot graph directly.                                       | These constructors retain their original ASCII placeholder validation and are not a v5 project-linking or pack-loading entry point. New generated code uses `CompiledRmf2*`, `CompiledTextMessage.FromRmf2`, and `CompiledTranslationDefinition.FromRmf2Inputs`. | Remove after supported external consumers and compatibility fixtures no longer require the constructors, replacement APIs have a documented migration, and the API baseline is intentionally deleted in a breaking release. |

The pre-v5 constructors are read-side/source compatibility only. The current
ABI markers are active contract identity. Neither category permits an older
carrier, selector, artifact, or generated web module to be selected by
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
