#!/usr/bin/env bash
set -euo pipefail

sample_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "$sample_root/../.." && pwd)"
project="$sample_root/RunicTextResources.Editor.csproj"
frontend="$sample_root/Frontend"
manifest="$sample_root/obj/Release/net10.0/text-resources/editor.esm/web-module-manifest-v1.json"

dotnet build "$project" -c Release --nologo \
  -p:RunicTextResourcesBuildMode=Verification

RUNIC_TEXT_MANIFEST="$manifest" npm --prefix "$frontend" run check
RUNIC_TEXT_MANIFEST="$manifest" npm --prefix "$frontend" run build
node "$frontend/test/verify-production.mjs"

dotnet run --project "$project" -c Release --no-build -- \
  --smoke-test \
  --workspace "$sample_root/ExampleWorkspace"

git -C "$repository_root" diff --check
