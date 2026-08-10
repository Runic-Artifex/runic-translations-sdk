#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="$repository_root/RunicTranslations.Editor.csproj"
frontend="$repository_root/Frontend"
manifest="$repository_root/obj/Release/net10.0/translations/editor.esm/web-module-manifest-v1.json"
verification_root="$(mktemp -d)"
trap 'rm -rf "$verification_root"' EXIT

git -C "$repository_root" diff --binary --no-ext-diff > "$verification_root/before.diff"
git -C "$repository_root" status --porcelain=v1 -z > "$verification_root/before.status"

dotnet tool restore
npm --prefix "$frontend" ci --ignore-scripts --no-audit --no-fund
dotnet build "$project" -c Release --nologo \
  -p:RunicTranslationsBuildMode=Verification

RUNIC_TRANSLATIONS_MANIFEST="$manifest" npm --prefix "$frontend" run check
RUNIC_TRANSLATIONS_MANIFEST="$manifest" npm --prefix "$frontend" run build
node "$frontend/test/verify-appearance.mjs"
node "$frontend/test/verify-message-preview.mjs"
node "$frontend/test/verify-review-model.mjs"
node "$frontend/test/verify-production.mjs"

dotnet run --project "$project" -c Release --no-build -- \
  --smoke-test \
  --workspace "$repository_root/ExampleWorkspace"
dotnet run --project "$project" -c Release --no-build -- \
  validate "$repository_root/ExampleWorkspace"

git -C "$repository_root" diff --check
git -C "$repository_root" diff --binary --no-ext-diff > "$verification_root/after.diff"
git -C "$repository_root" status --porcelain=v1 -z > "$verification_root/after.status"
cmp "$verification_root/before.diff" "$verification_root/after.diff"
cmp "$verification_root/before.status" "$verification_root/after.status"
