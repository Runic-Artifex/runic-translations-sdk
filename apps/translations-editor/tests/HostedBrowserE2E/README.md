# Real hosted editor acceptance

This Linux acceptance runs the packaged editor and the production Views CS-WebUI host with Chromium selected by this repository's optional locked `editor` Nix shell; it does not mock the frontend,
filesystem, parser, authoring engine, validation, or save path. It prepares a
disposable RMF2 v2 workspace, proves that two logical edits in one `.rmf2`
resource serialize through the UI transform queue before save, detects an
external revision conflict without overwriting disk, and repairs a malformed
RMF2 source through the production repair dialog.

From the repository root, build with the default locked environment, install the
harness's locked dependencies, then run the prebuilt assembly with the optional
browser runtime:

```sh
direnv exec . bun run bootstrap
direnv exec . bun run build
direnv exec . bun install --frozen-lockfile --cwd apps/translations-editor/tests/HostedBrowserE2E
RUNIC_EDITOR_HOSTED_E2E_REPORT_DIR="$PWD/artifacts/editor-hosted-rmf2-ui" \
  nix develop .#editor -c bun apps/translations-editor/tests/HostedBrowserE2E/run-hosted-rmf2-ui.mjs \
  apps/translations-editor/bin/Release/net10.0/Runic.Translations.Editor.dll
```

The `editor` shell supplies `WEBUI_BROWSER_PATH` and native runtime libraries
from this repository's `flake.lock`. The default CLI shell stays lightweight.
Neither the harness nor shell entry installs Playwright browser downloads.
Outside Nix, explicitly set `WEBUI_BROWSER_PATH` to a compatible installed
Chromium executable and make the CS-WebUI Linux native dependencies available.

This proof uses the production `serve` host and a headless browser. It does not
prove an interactive desktop launch, embedded WebView, or native file chooser;
those need separate checks in the intended X11/Wayland and D-Bus/portal session.

The runner isolates `XDG_STATE_HOME`, binds the report to the tested assembly
SHA-256, records host/browser logs and browser metadata, and cleans successful
workspaces. On failure it preserves only its task-owned workspace and captures
the page and a screenshot for diagnosis.
