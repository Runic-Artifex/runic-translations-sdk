# Real hosted editor acceptance

Use the locked project shell and its `WEBUI_BROWSER_PATH`. The root workspace already pins `playwright-core`; do not download another Chromium. Build serially, including the frontend and embedded assets:

```sh
dotnet build apps/translations-editor/Runic.Translations.Editor.csproj -c Release
```

Then run independently without another build:

```sh
RUNIC_EDITOR_HOSTED_E2E_REPORT_DIR="$PWD/artifacts/editor-hosted-toml-ui" \
  bun apps/translations-editor/tests/HostedBrowserE2E/run-hosted-toml-ui.mjs \
  apps/translations-editor/bin/Release/net10.0/Runic.Translations.Editor.dll
```

The Linux runner creates a unique disposable public Runic fixture and isolated `XDG_STATE_HOME`, starts the real `serve` host on its automatically selected loopback port, and opens the packaged frontend using pinned Chromium. No mock bridge or filesystem HTTP endpoints are used. The browser enters text in the real inline editor while the first real transform response is held, selects a second key in the same TOML file, and requests save before releasing that response. It checks sequential physical-file inputs, both persisted values after reload, unchanged comments/CRLF, an actual external revision conflict with EN/DE notice switching, and malformed TOML repair through the real compiler and UI.

The runner kills only its own host. Successful workspaces/state are removed; failures retain their task-owned temporary workspace for diagnosis. The report directory contains host/browser logs, result JSON bound to the assembly SHA-256 and optional GITHUB_SHA, the actual browser version, and public screenshots. Failure captures also retain DOM and real bridge frames. SIGTERM closes the browser before the driver exits. Remove retained temporary workspaces after diagnosis and reports after artifact upload.

Only the separate legacy wire fixture requires building with `-p:RunicEditorHostedE2E=true`. The existing `hosted-web-browser.mjs <url>/__hosted-e2e/hosted-web-browser.html` fixture remains a separate wire-protocol workflow. It does not establish frontend queue coverage.
