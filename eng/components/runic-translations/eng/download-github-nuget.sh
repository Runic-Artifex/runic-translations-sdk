#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 || -z "${GITHUB_ACTOR:-}" || -z "${GITHUB_TOKEN:-}" ]]; then
  echo "Usage: GITHUB_ACTOR=... GITHUB_TOKEN=... $0 <version> <output-directory>" >&2
  exit 2
fi

version="$1"
output_directory="$2"
lower_version="${version,,}"
base_url="https://nuget.pkg.github.com/Runic-Artifex/download"
package_ids=(
  Runic.Translations
  Runic.Translations.Tooling
  Runic.Translations.Build
  dotnet-runic-translations
  Runic.Translations.Templates
)

mkdir -p "$output_directory"
for package_id in "${package_ids[@]}"; do
  lower_id="${package_id,,}"
  curl --fail --location --silent --show-error --retry 4 \
    --user "$GITHUB_ACTOR:$GITHUB_TOKEN" \
    --output "$output_directory/$package_id.$version.nupkg" \
    "$base_url/$lower_id/$lower_version/$lower_id.$lower_version.nupkg"
  echo "downloaded: $package_id@$version"
done

actual_count="$(find "$output_directory" -maxdepth 1 -type f -name '*.nupkg' | wc -l)"
if [[ "$actual_count" -ne "${#package_ids[@]}" ]]; then
  echo "Expected ${#package_ids[@]} packages, found $actual_count." >&2
  exit 1
fi
