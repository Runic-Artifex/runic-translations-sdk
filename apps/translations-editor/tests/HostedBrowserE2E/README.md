# Real hosted editor acceptance

This Linux acceptance runs the packaged editor and the production Views CS-WebUI host with the project-pinned Chromium; it does not mock the frontend,
filesystem, parser, authoring engine, validation, or save path. It prepares a
disposable RMF2 v2 workspace, proves that two logical edits in one `.rmf2`
resource serialize through the UI transform queue before save, detects an
external revision conflict without overwriting disk, and repairs a malformed
RMF2 source through the production repair dialog.

Build the editor with the locked environment, then run the prebuilt assembly:

```sh
dotnet build apps/translations-editor/Runic.Translations.Editor.csproj -c Release
RUNIC_EDITOR_HOSTED_E2E_REPORT_DIR="$PWD/artifacts/editor-hosted-rmf2-ui" \
  bun apps/translations-editor/tests/HostedBrowserE2E/run-hosted-rmf2-ui.mjs \
  apps/translations-editor/bin/Release/net10.0/Runic.Translations.Editor.dll
```

The runner isolates `XDG_STATE_HOME`, binds the report to the tested assembly
SHA-256, records host/browser logs and browser metadata, and cleans successful
workspaces. On failure it preserves only its task-owned workspace and captures
the page and a screenshot for diagnosis.
