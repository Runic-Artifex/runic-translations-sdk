#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="$repository_root/Runic.Translations.Editor.csproj"
frontend="$repository_root/Frontend"
manifest="$repository_root/obj/Release/net10.0/translations/editor.esm/web-module-manifest-v1.json"
verification_root="$(mktemp -d)"
cli_workspace="$(mktemp -d)"
registry_pid=""
cleanup() {
  if [[ -n "$registry_pid" ]]; then
    kill "$registry_pid" 2>/dev/null || true
    wait "$registry_pid" 2>/dev/null || true
  fi
  rm -rf "$verification_root" "$cli_workspace"
}
trap cleanup EXIT

git -C "$repository_root" diff --binary --no-ext-diff > "$verification_root/before.diff"
git -C "$repository_root" status --porcelain=v1 -z > "$verification_root/before.status"

if [[ ! -f "$repository_root/../runic-translations/dotnet/tools/dotnet-runic-translations/dotnet-runic-translations.csproj" ]]; then
  tool_restore_args=()
  if [[ -n "${RUNIC_EDITOR_NUGET_CONFIG:-}" ]]; then
    tool_restore_args+=(--configfile "$RUNIC_EDITOR_NUGET_CONFIG")
  fi
  dotnet tool restore "${tool_restore_args[@]}"
fi

local_npm_manifests=(
  "$repository_root/../runic-toolkit/web/packages/application-bridge/package.json"
  "$repository_root/../runic-desktop/web/packages/desktop/package.json"
  "$repository_root/../runic-svelte/packages/svelte/package.json"
  "$repository_root/../runic-vite/package.json"
  "$repository_root/../runic-translations/web/package.json"
)
if [[ -f "${local_npm_manifests[0]}" && -f "${local_npm_manifests[1]}" && -f "${local_npm_manifests[2]}" && -f "${local_npm_manifests[3]}" && -f "${local_npm_manifests[4]}" ]]; then
  npm_feed="$verification_root/npm-feed"
  registry_ready="$verification_root/npm-registry.url"
  npm_userconfig="$verification_root/npmrc"
  mkdir -p "$npm_feed"
  npm --prefix "$repository_root/../runic-toolkit" run build --workspace @runic-artifex/application-bridge
  # The authority contract pins the reproducible release archive, not npm's
  # source-worktree pack (which may inject a gitHead field). Use the Toolkit
  # packer so this consumer verifies the same immutable bytes it locks.
  node "$repository_root/../runic-toolkit/eng/pack-npm.mjs" 1.0.0-preview.1 "$npm_feed" github
  npm --prefix "$repository_root/../runic-desktop" run build --workspace @runic-artifex/desktop
  npm --prefix "$repository_root/../runic-desktop" pack --workspace @runic-artifex/desktop --ignore-scripts --pack-destination "$npm_feed"
  npm --prefix "$repository_root/../runic-svelte" run build --workspace @runic-artifex/svelte
  npm --prefix "$repository_root/../runic-svelte" pack --workspace @runic-artifex/svelte --ignore-scripts --pack-destination "$npm_feed"
  (
    cd "$repository_root/../runic-vite"
    npm run build
    npm pack --ignore-scripts --pack-destination "$npm_feed"
  )
  (
    cd "$repository_root/../runic-translations/web"
    npm pack --ignore-scripts --pack-destination "$npm_feed"
  )
  node "$repository_root/eng/local-npm-registry.mjs" "$registry_ready" "$npm_feed"/*.tgz &
  registry_pid=$!
  for _ in $(seq 1 100); do
    [[ -s "$registry_ready" ]] && break
    sleep 0.05
  done
  [[ -s "$registry_ready" ]]
  printf '@runic-artifex:registry=%s\n' "$(<"$registry_ready")" > "$npm_userconfig"
  export NPM_CONFIG_USERCONFIG="$npm_userconfig"
fi
if [[ "${RUNIC_EDITOR_FRONTEND_CANDIDATES:-}" == "1" ]]; then
  [[ -d "$frontend/node_modules" ]] || { echo "The coordinated frontend candidates were not installed." >&2; exit 1; }
else
  npm --prefix "$frontend" ci --ignore-scripts --no-audit --no-fund
fi
build_args=(build "$project" -c Release --nologo -p:RunicTranslationsBuildMode=Verification)
if [[ -n "${RUNIC_EDITOR_NUGET_CONFIG:-}" ]]; then
  build_args+=("-p:RestoreConfigFile=$RUNIC_EDITOR_NUGET_CONFIG")
fi
dotnet "${build_args[@]}"
if rg -a -q 'RUNIC_EDITOR_HOSTED_E2E_ASSETS|__hosted-e2e' "$repository_root/bin/Release/net10.0/Runic.Translations.Editor.dll"; then
  echo "The production editor assembly contains hosted-browser fixture routing." >&2
  exit 1
fi

RUNIC_TRANSLATIONS_MANIFEST="$manifest" npm --prefix "$frontend" run verify

dotnet run --project "$project" -c Release --no-build -- \
  --smoke-test \
  --workspace "$repository_root/ExampleWorkspace"
dotnet run --project "$project" -c Release --no-build -- \
  validate "$repository_root/ExampleWorkspace"
cp -a "$repository_root/ExampleWorkspace/." "$cli_workspace"
dotnet run --project "$project" -c Release --no-build -- \
  export "$cli_workspace" --format review --output .runic-translations/export/cli.review.json
dotnet run --project "$project" -c Release --no-build -- \
  report "$cli_workspace" --format review --source .runic-translations/export/cli.review.json --runic-output json
dotnet run --project "$project" -c Release --no-build -- \
  import "$cli_workspace" --format review --source .runic-translations/export/cli.review.json --apply
dotnet run --project "$project" -c Release --no-build -- \
  export "$cli_workspace" --format xliff --output .runic-translations/export/cli-xliff
if refusal_report="$(dotnet run --project "$project" -c Release --no-build -- \
  report "$cli_workspace" --format xliff --source .runic-translations/export/cli-xliff/customer-product.en.xliff --runic-output json)"; then
  echo "Expected the structured XLIFF report to be refused." >&2
  exit 1
fi
[[ "$refusal_report" == *'"refusalCodes":"XLIFF21-STRUCTURED-IMPORT"'* ]] || {
  echo "The refused XLIFF report omitted its machine-readable refusal code." >&2
  exit 1
}
pwsh -NoProfile -File "$repository_root/eng/verify-editor-release-contract.ps1"
bash "$repository_root/eng/run-hosted-e2e.sh"

git -C "$repository_root" diff --check
git -C "$repository_root" diff --binary --no-ext-diff > "$verification_root/after.diff"
git -C "$repository_root" status --porcelain=v1 -z > "$verification_root/after.status"
cmp "$verification_root/before.diff" "$verification_root/after.diff"
cmp "$verification_root/before.status" "$verification_root/after.status"
