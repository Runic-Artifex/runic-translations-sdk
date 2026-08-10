#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 3 || $# -gt 4 ]]; then
  echo "Usage: $0 <artifact-directory> <version> <repository-commit> [--preflight-only]" >&2
  exit 2
fi

artifact_directory="$1"
expected_version="$2"
repository_commit="$3"
mode="${4:-}"
registry="https://api.nuget.org/v3/index.json"

if [[ -n "$mode" && "$mode" != "--preflight-only" ]]; then
  echo "Unknown option '$mode'." >&2
  exit 2
fi
if [[ ! "$expected_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([+-][0-9A-Za-z.-]+)?$ ]]; then
  echo "'$expected_version' is not a SemVer-compatible version." >&2
  exit 2
fi
if [[ ! "$repository_commit" =~ ^[0-9a-fA-F]{40}$ ]]; then
  echo "Repository commit must be a full Git commit." >&2
  exit 2
fi
if [[ "$mode" != "--preflight-only" && -z "${NUGET_API_KEY:-}" ]]; then
  echo "NUGET_API_KEY was not supplied by trusted publishing." >&2
  exit 1
fi

shopt -s nullglob
packages=("$artifact_directory"/*.nupkg)
if (( ${#packages[@]} == 0 )); then
  echo "No NuGet artifacts were found in '$artifact_directory'." >&2
  exit 1
fi

temporary_directory="$(mktemp -d)"
trap 'find "$temporary_directory" -depth -delete' EXIT

# Inspect every target before the first push. An exact version already on
# nuget.org is accepted only when it identifies the same source commit, making
# retries safe while rejecting an unrelated immutable package collision.
for package in "${packages[@]}"; do
  filename="$(basename "$package")"
  suffix=".$expected_version.nupkg"
  if [[ "$filename" != *"$suffix" ]]; then
    echo "Could not derive a package ID from '$filename' for version '$expected_version'." >&2
    exit 1
  fi
  package_id="${filename%"$suffix"}"
  normalized_id="${package_id,,}"
  normalized_version="${expected_version,,}"
  package_url="https://api.nuget.org/v3-flatcontainer/$normalized_id/$normalized_version/$normalized_id.$normalized_version.nupkg"
  existing_package="$temporary_directory/$filename"
  status="$(curl --silent --show-error --location --output "$existing_package" --write-out '%{http_code}' "$package_url")"
  case "$status" in
    200)
      nuspec="$(unzip -p "$existing_package" '*.nuspec')"
      if [[ "$nuspec" != *"commit=\"$repository_commit\""* ]]; then
        echo "$package_id@$expected_version already exists for a different source commit." >&2
        exit 1
      fi
      echo "$package_id@$expected_version already identifies source commit $repository_commit; it will be skipped."
      ;;
    404)
      rm -f "$existing_package"
      ;;
    *)
      echo "nuget.org returned HTTP $status while checking $package_id@$expected_version." >&2
      exit 1
      ;;
  esac
done

if [[ "$mode" == "--preflight-only" ]]; then
  echo "Preflighted ${#packages[@]} NuGet artifact(s)."
  exit 0
fi

for package in "${packages[@]}"; do
  dotnet nuget push "$package" \
    --source "$registry" \
    --api-key "$NUGET_API_KEY" \
    --skip-duplicate
done
