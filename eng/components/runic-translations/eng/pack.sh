#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <package-version> <output-directory>" >&2
  exit 2
fi

package_version="$1"
output_directory="$2"
configuration="Release"
command_line_version="${RunicCommandLineVersion:-1.0.0-preview.1}"
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repository_commit="$(git -C "$repository_root" rev-parse HEAD)"

if [[ ! "$package_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([+-][0-9A-Za-z.-]+)?$ ]]; then
  echo "Package version must be a SemVer-compatible version such as 0.1.0-preview.1." >&2
  exit 2
fi

mkdir -p "$output_directory"
pack_build_root="$(mktemp -d)"

package_projects=(
  "$repository_root/dotnet/src/Runic.Translations/Runic.Translations.csproj"
  "$repository_root/dotnet/src/Runic.Translations.Tooling/Runic.Translations.Tooling.csproj"
  "$repository_root/dotnet/src/Runic.Translations.Build/Runic.Translations.Build.csproj"
  "$repository_root/dotnet/tools/dotnet-runic-translations/dotnet-runic-translations.csproj"
  "$repository_root/dotnet/templates/Runic.Translations.Templates/Runic.Translations.Templates.csproj"
)

for project in "${package_projects[@]}"; do
  dotnet pack "$project" -c "$configuration" --no-restore \
    -p:RunicCommandLineVersion="$command_line_version" \
    -p:WarningsNotAsErrors=NU5104 \
    -p:PackageVersion="$package_version" \
    -p:RepositoryCommit="$repository_commit" \
    -p:ContinuousIntegrationBuild=true \
    -p:RunicTranslationsBuildMode=Verification \
    -p:BaseOutputPath="$pack_build_root/$(basename "${project%.*}")/" \
    -o "$output_directory"
done
