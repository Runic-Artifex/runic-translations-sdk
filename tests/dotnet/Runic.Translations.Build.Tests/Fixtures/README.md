# Build test fixtures

`sarif-schema-2.1.0.json` is the OASIS SARIF 2.1.0 JSON schema (draft-07), as published at
<https://json.schemastore.org/sarif-2.1.0.json> from
<https://github.com/oasis-tcs/sarif-spec/blob/main/Schemata/sarif-schema-2.1.0.json>. `validate --sarif` reports
are checked against it.

`vs-18.12-lsp-initialize-request.json` is the `initialize` request Visual Studio 2026 18.12.0 sent to the language
server, captured unchanged by a stdio proxy in the Visual Studio spike (SPIKE-VS-0, 2026-11). The protocol 2 tests use
its `capabilities` as Visual Studio's client: `create` and `rename` without `delete`, no `window` block, no dynamic
watched-file registration and no `edit` in `codeAction.resolveSupport`.

`lsp-initialize-result.json` is the golden snapshot of the language server's `initialize` result for a client without
capabilities or initialization options, with `serverInfo.version` replaced by `(tool version)`. It pins the standard
LSP surface and the `experimental.runic` capability that the compatibility promise covers
(`docs/guides/translations/language-server.md#compatibility`). `Rmf2ProtocolLspTests.InitializeGolden` compares
against it; after an intended, compatible change, run that test with `RUNIC_UPDATE_LSP_SNAPSHOT=1` and review the diff.
