#!/usr/bin/env bash
set -euo pipefail

# Hosted-browser e2e ceremony, ported from the toolkit's eng/run-hosted-browser-e2e.sh:
# require a pinned Chromium, boot the product under test headlessly, and drive one real
# browser page over the Application Bridge WebSocket transport. verify.sh invokes this
# unconditionally so both suites (native smoke + hosted web) run locally by default.
#
# Standalone use after a Release build:
#   nix develop -c bash eng/run-hosted-e2e.sh

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

e2e_dir="$repository_root/tests/HostedBrowserE2E"

# Chromium provisioning: prefer the explicit WEBUI_BROWSER_PATH contract, then fall
# back to the devshell/system Chromium or Chrome.
browser="${WEBUI_BROWSER_PATH:-}"
if [[ -z "$browser" ]]; then
  for candidate in chromium chromium-browser google-chrome google-chrome-stable; do
    if command -v "$candidate" >/dev/null 2>&1; then
      browser="$(command -v "$candidate")"
      break
    fi
  done
fi
if [[ -z "$browser" || ! -x "$browser" ]]; then
  echo "WEBUI_BROWSER_PATH must name the pinned Chromium executable." >&2
  exit 1
fi
export WEBUI_BROWSER_PATH="$browser"

# playwright-core resolution: vendored node_modules first, then the sibling toolkit
# checkout, otherwise restore the pinned dev dependency.
if [[ ! -f "$e2e_dir/node_modules/playwright-core/package.json" ]]; then
  toolkit_node_modules="$repository_root/../runic-toolkit/node_modules"
  if [[ -f "$toolkit_node_modules/playwright-core/package.json" ]]; then
    export NODE_PATH="$toolkit_node_modules"
  else
    bun install --cwd "$e2e_dir" --frozen-lockfile --ignore-scripts
  fi
fi

fixture_workspace="$(mktemp -d)"
server_log="$(mktemp)"
trap 'kill "${server_pid:-}" 2>/dev/null || true; rm -rf "$fixture_workspace" "$server_log"' EXIT
cp "$repository_root"/ExampleWorkspace/* "$fixture_workspace"/

# Compile the disposable E2E host with its fixture-only route. The production
# assembly excludes that source file entirely.
dotnet build "$repository_root/Runic.Translations.Editor.csproj" \
  --configuration Release --no-restore -p:RunicEditorHostedE2E=true

# Boot the hosted-web mode against a disposable copy of the packaged example.
RUNIC_EDITOR_HOSTED_E2E_ASSETS="$e2e_dir" \
dotnet run --project "$repository_root/Runic.Translations.Editor.csproj" \
  --configuration Release --no-build --no-restore -p:RunicEditorHostedE2E=true -- \
  serve --workspace "$fixture_workspace" >"$server_log" 2>&1 &
server_pid=$!

hosted_url=""
for _ in $(seq 1 120); do
  hosted_url="$(grep -o 'at http://127\.0\.0\.1:[0-9]*/' "$server_log" | tail -1 | sed 's/^at //')" || true
  if [[ -n "$hosted_url" ]]; then break; fi
  if ! kill -0 "$server_pid" 2>/dev/null; then
    echo "The hosted editor exited before serving:" >&2
    cat "$server_log" >&2
    exit 1
  fi
  sleep 0.5
done
if [[ -z "$hosted_url" ]]; then
  echo "The hosted editor did not report its URL in time:" >&2
  cat "$server_log" >&2
  exit 1
fi

node "$e2e_dir/hosted-web-browser.mjs" "${hosted_url}__hosted-e2e/hosted-web-browser.html"
