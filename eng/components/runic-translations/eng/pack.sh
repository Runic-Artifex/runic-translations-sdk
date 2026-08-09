#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <package-version> <output-directory>" >&2
  exit 2
fi

package_version="$1"
output_directory="$2"
configuration="Release"
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repository_commit="$(git -C "$repository_root" rev-parse HEAD)"

if [[ ! "$package_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([+-][0-9A-Za-z.-]+)?$ ]]; then
  echo "Package version must be a SemVer-compatible version such as 0.1.0-preview.1." >&2
  exit 2
fi

mkdir -p "$output_directory"

package_projects=(
  "$repository_root/dotnet/src/RunicTextResources/RunicTextResources.csproj"
  "$repository_root/dotnet/src/RunicTextResources.Compiler/RunicTextResources.Compiler.csproj"
  "$repository_root/dotnet/src/RunicTextResources.Authoring/RunicTextResources.Authoring.csproj"
  "$repository_root/dotnet/src/RunicTextResources.Generator/RunicTextResources.Generator.csproj"
  "$repository_root/dotnet/src/RunicTextResources.Build/RunicTextResources.Build.csproj"
  "$repository_root/dotnet/tools/RunicTextResources.Tool/RunicTextResources.Tool.csproj"
  "$repository_root/dotnet/templates/RunicTextResources.Templates/RunicTextResources.Templates.csproj"
)

for project in "${package_projects[@]}"; do
  dotnet pack "$project" -c "$configuration" --no-restore \
    -p:PackageVersion="$package_version" \
    -p:RepositoryCommit="$repository_commit" \
    -p:ContinuousIntegrationBuild=true \
    -p:RunicTextResourcesBuildMode=Verification \
    -o "$output_directory"
done
