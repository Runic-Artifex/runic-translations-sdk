# Translation compatibility-retention ledger

This ledger records compatibility that remains readable in the current preview
without being part of the current authoring or generation contract. It prevents
retained names from being mistaken for supported new output.

## Current markers and retained surface

| Retained item                                                                                  | Reason it remains                                                                                                                           | Current-writer boundary                                                                                                                                                                                                                                          | Exit criteria                                                                                                                                                                                                               |
| ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Generic generated-code ABI marker `RuntimeAbiVersion = 1`                                      | Keep the generated C# registration contract stable across the current package family. This is a current marker, not a legacy snapshot path. | The current C# v5 writer emits `RuntimeAbiVersion = 1` and checks it against `TranslationsCompatibility.RuntimeAbiVersion`. It is independent from the RMF2 execution ABI.                                                                                       | Change only in a coordinated generated-code/runtime ABI revision with a migration note, updated API/generator baselines, and the full packed-consumer and NativeAOT matrix.                                                 |
| RMF2 execution ABI marker `Rmf2RuntimeAbiVersion = 3`                                          | Identify the current v5 typed evaluator, pack loader, and generated C#/ESM execution contract.                                              | The current C# and ESM writers emit RMF2 ABI 3 and require `EnsureRmf2RuntimeAbi(3)` or the equivalent generated check. No current writer emits an older RMF2 execution ABI; ABI 2 output and markup contract v1 packs are rejected and must be rebuilt.                                                                                     | Change only with a separately versioned execution contract, cross-runtime corpus update, explicit migration, and full package/NativeAOT/ESM consumer evidence.                                                              |
| Public `CompiledTextMessage` and `CompiledTranslation*` constructors that predate the v5 model | Preserve source/binary compatibility for callers that construct the original snapshot graph directly.                                       | These constructors retain their original ASCII placeholder validation and are not a v5 project-linking or pack-loading entry point. New generated code uses `CompiledRmf2*`, `CompiledTextMessage.FromRmf2`, and `CompiledTranslationDefinition.FromRmf2Inputs`. | Remove after supported external consumers and compatibility fixtures no longer require the constructors, replacement APIs have a documented migration, and the API baseline is intentionally deleted in a breaking release. |

The pre-v5 constructors are read-side/source compatibility only. The current
ABI markers are active contract identity. Neither category permits an older
carrier, selector, artifact, or generated web module to be selected by
`runic.json`, a project option, or a release tool. Unknown or mismatched versions
fail explicitly.

## Current contract

All current writers use the single `rmf2-execution-v2` contract: resource syntax
`rmf2-v1`, grammar and normalized AST 5, locale artifact 5, RMF2 runtime ABI 3
(markup contract v2), and ESM ABI 4 under `web-module-manifest-v3`. Both direct `.mf2` and grouped
`.rmf2` authoring feed this same contract; direct `.mf2` is an active supported
representation, not a compatibility format.

## Migration: markup contract v2 (0.6.0-preview.4)

RMF2 runtime ABI 3 replaces ABI 2 and the exported markup contract moves from
version 1 to version 2. No reader for markup contract v1 is retained. Output and
packs built with 0.6.0-preview.3 or earlier use ABI 2 and must be rebuilt.

- Rebuild generated C# and ESM output with the matching compiler. Generated code
  from an earlier release fails its `EnsureRmf2RuntimeAbi` check, and the Vite
  plugin rejects a manifest with `rmf2RuntimeAbiVersion` 2.
- Every message entry of the contract gains `content` and `skeletons`, so the
  caller fingerprint, the generated C# `CatalogData` and `Registration`, all
  generated ESM files, the web module manifest and every locale artifact change,
  even in projects without documents.
- Rebuild **every** external pack, including packs for inline-only projects.
  Both pack loaders reject a pack whose `markupContract.version` is not 2 with
  `RTR0023/markup-contract-version-mismatch`
  (`TranslationPackFailureReason.MarkupContractVersionMismatch`).
- `runic.json` needs no change. Custom contracts may add
  `"placement": "inline"` and integer options; `markup.structure` is reserved.
- `p`, `h`, `ul`, `ol` and `li` are now built-in aliases for the
  [document profile](../../../specs/translations/rmf2-document-profile-v1.md).
  A project alias with one of these names no longer shadows them and reports
  `RTR0060`; rename it. Messages that use none of these elements stay inline
  and format as before, but every message contract gains `content` and
  `skeletons` members. The caller fingerprint, the generated `CatalogData` and
  `Registration`, `runtime.js`, `transport.js` and every locale artifact
  therefore change, so the rebuild above also covers projects without documents.

The ledger is reviewed when a release changes the generated contract or package
baseline. It is not a telemetry promise and does not add a separate release gate;
the ordinary full CI and packed-consumer checks provide the evidence for each
removal decision.
