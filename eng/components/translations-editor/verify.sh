#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="$repository_root/RunicTranslations.Editor.csproj"
frontend="$repository_root/Frontend"
manifest="$repository_root/obj/Release/net10.0/translations/editor.esm/web-module-manifest-v1.json"

dotnet tool restore
npm --prefix "$frontend" ci --ignore-scripts --no-audit --no-fund
dotnet build "$project" -c Release --nologo \
  -p:RunicTranslationsBuildMode=Verification

RUNIC_TEXT_MANIFEST="$manifest" npm --prefix "$frontend" run check
RUNIC_TEXT_MANIFEST="$manifest" npm --prefix "$frontend" run build
node "$frontend/test/verify-appearance.mjs"
node "$frontend/test/verify-message-preview.mjs"
node "$frontend/test/verify-review-model.mjs"
node "$frontend/test/verify-production.mjs"

dotnet run --project "$project" -c Release --no-build -- \
  --smoke-test \
  --workspace "$repository_root/ExampleWorkspace"

git -C "$repository_root" diff --check
