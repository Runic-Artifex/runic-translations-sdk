#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
  echo "Usage: $0 <artifact-directory> <version> [--preflight-only]" >&2
  exit 2
fi

artifact_directory="$1"
expected_version="$2"
mode="${3:-}"
registry="https://registry.npmjs.org"

if [[ -n "$mode" && "$mode" != "--preflight-only" ]]; then
  echo "Unknown option '$mode'." >&2
  exit 2
fi
if [[ ! "$expected_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([+-][0-9A-Za-z.-]+)?$ ]]; then
  echo "'$expected_version' is not a SemVer-compatible version." >&2
  exit 2
fi

shopt -s nullglob
packages=("$artifact_directory"/*.tgz)
if (( ${#packages[@]} == 0 )); then
  echo "No npm artifacts were found in '$artifact_directory'." >&2
  exit 1
fi

missing_packages=()
for package in "${packages[@]}"; do
  manifest="$(tar -xOf "$package" package/package.json)"
  package_name="$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).name)' "$manifest")"
  package_version="$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).version)' "$manifest")"
  if [[ "$package_version" != "$expected_version" ]]; then
    echo "$package_name has version '$package_version'; expected '$expected_version'." >&2
    exit 1
  fi

  local_integrity="$(node -e '
    const { createHash } = require("node:crypto");
    const { readFileSync } = require("node:fs");
    process.stdout.write(`sha512-${createHash("sha512").update(readFileSync(process.argv[1])).digest("base64")}`);
  ' "$package")"

  view_output_file="$(mktemp)"
  view_error_file="$(mktemp)"
  if npm view "$package_name@$expected_version" dist.integrity \
      --registry "$registry" --json >"$view_output_file" 2>"$view_error_file"; then
    published_integrity="$(node -e '
      const { readFileSync } = require("node:fs");
      const value = JSON.parse(readFileSync(process.argv[1], "utf8"));
      process.stdout.write(Array.isArray(value) ? value[0] : value);
    ' "$view_output_file")"
    rm -f "$view_output_file" "$view_error_file"
    if [[ "$published_integrity" != "$local_integrity" ]]; then
      echo "$package_name@$expected_version already exists with different artifact integrity." >&2
      exit 1
    fi
    echo "$package_name@$expected_version already matches the verified artifact; it will be skipped."
  else
    if ! grep -q 'E404' "$view_output_file" "$view_error_file"; then
      cat "$view_output_file" "$view_error_file" >&2
      rm -f "$view_output_file" "$view_error_file"
      echo "Could not determine publication state for $package_name@$expected_version." >&2
      exit 1
    fi
    rm -f "$view_output_file" "$view_error_file"
    missing_packages+=("$package")
  fi
done

if [[ "$mode" == "--preflight-only" ]]; then
  echo "Preflighted ${#packages[@]} npm artifact(s); ${#missing_packages[@]} require publication."
  exit 0
fi

dist_tag="latest"
if [[ "$expected_version" == *-* ]]; then
  dist_tag="preview"
fi

for package in "${missing_packages[@]}"; do
  npm publish "$package" \
    --registry "$registry" \
    --access public \
    --tag "$dist_tag" \
    --provenance
done
