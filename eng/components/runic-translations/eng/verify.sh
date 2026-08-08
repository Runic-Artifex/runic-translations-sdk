#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifacts_root="$repository_root/artifacts/verification"
package_feed="$artifacts_root/packages"
package_version="1.0.0"
configuration="Release"

rm -rf "$artifacts_root"
mkdir -p "$package_feed"

npm --prefix "$repository_root/web" ci

dotnet restore "$repository_root/RunicTextResources.slnx"
dotnet build "$repository_root/RunicTextResources.slnx" -c "$configuration" --no-restore \
  -p:RunicTextResourcesBuildMode=Verification

test_projects=(
  RunicTextResources.ApiTests
  RunicTextResources.Authoring.Tests
  RunicTextResources.Compiler.Tests
  RunicTextResources.Generator.Tests
  RunicTextResources.Runtime.Tests
  RunicTextResources.Build.Tests
)

for project in "${test_projects[@]}"; do
  dotnet run --project "$repository_root/dotnet/tests/$project/$project.csproj" \
    -c "$configuration" --no-restore
done

"$repository_root/eng/pack.sh" "$package_version" "$package_feed"

# The fixed verification version is intentionally reused. Isolate package
# consumption from developer/global caches so it always exercises this run's
# freshly packed binaries rather than a stale package with the same version.
export NUGET_PACKAGES="$artifacts_root/nuget"
export DOTNET_CLI_HOME="$artifacts_root/dotnet-home"
mkdir -p "$DOTNET_CLI_HOME"

package_consumer="$repository_root/dotnet/tests/RunicTextResources.PackageTests/RunicTextResources.PackageTests.csproj"
dotnet restore "$package_consumer" \
  -p:TextResourcesPackageFeed="$package_feed"
tool_root="$artifacts_root/tool"
dotnet tool install RunicTextResources.Tool --version "$package_version" \
  --tool-path "$tool_root" \
  --add-source "$package_feed"
"$tool_root/runic-textresources" --help >/dev/null

template_package="$package_feed/RunicTextResources.Templates.$package_version.nupkg"
template_root="$artifacts_root/templates"
dotnet new install "$template_package" >/dev/null
dotnet new runic-textresources \
  --output "$template_root/item" \
  --catalog product \
  --defaultLocale de \
  --namespace Customer.Product \
  --className ProductText
"$tool_root/runic-textresources" init \
  --directory "$template_root/cli" \
  --catalog product \
  --default-locale de \
  --namespace Customer.Product \
  --class ProductText
cmp "$template_root/item/product.catalog.json" "$template_root/cli/product.catalog.json"
cmp "$template_root/item/product.de.json" "$template_root/cli/product.de.json"
dotnet new runic-textresources-project \
  --output "$template_root/project" \
  --name Customer.Product.Text \
  --catalog product \
  --defaultLocale de \
  --namespace Customer.Product \
  --className ProductText
cmp "$template_root/item/product.catalog.json" "$template_root/project/Resources/product.catalog.json"
cmp "$template_root/item/product.de.json" "$template_root/project/Resources/product.de.json"
template_project="$template_root/project/Customer.Product.Text.csproj"
dotnet restore "$template_project" -p:RestoreAdditionalProjectSources="$package_feed"
dotnet build "$template_project" -c "$configuration" --no-restore \
  -p:TextResourcesToolCommand="$tool_root/runic-textresources" \
  -p:RunicTextResourcesBuildMode=Verification
test -f "$template_root/project/obj/$configuration/net10.0/text-resources/product.esm/messages.js"

dotnet run --project "$package_consumer" -c "$configuration" --no-restore \
  -p:TextResourcesGenerateOnBuild=true \
  -p:TextResourcesToolCommand="$tool_root/runic-textresources" \
  -- --feed "$package_feed"

aot_consumer="$repository_root/dotnet/tests/RunicTextResources.AotTests/RunicTextResources.AotTests.csproj"
runtime_identifier="$(dotnet --info | awk '/ RID:/{print $2; exit}')"
dotnet restore "$aot_consumer" -r "$runtime_identifier" \
  -p:TextResourcesPackageFeed="$package_feed" \
  -p:PublishAot=true \
  -p:PublishTrimmed=true \
  -p:TrimMode=full
dotnet publish "$aot_consumer" -c "$configuration" -r "$runtime_identifier" --self-contained true --no-restore \
  -p:PublishAot=true \
  -p:PublishTrimmed=true \
  -p:TrimMode=full \
  -p:IlcTreatWarningsAsErrors=true \
  -p:PublishDir="$artifacts_root/aot/"
"$artifacts_root/aot/RunicTextResources.AotTests"

npm --prefix "$repository_root/web" test

editor_root="$repository_root/samples/RunicTextResources.Editor"
editor_frontend="$editor_root/Frontend"
editor_manifest="$editor_root/obj/$configuration/net10.0/text-resources/editor.esm/web-module-manifest-v1.json"
RUNIC_TEXT_MANIFEST="$editor_manifest" npm --prefix "$editor_frontend" run check
node "$editor_frontend/test/verify-message-preview.mjs"
node "$editor_frontend/test/verify-review-model.mjs"
node "$editor_frontend/test/verify-production.mjs"
dotnet run --project "$editor_root/RunicTextResources.Editor.csproj" \
  -c "$configuration" --no-build -- \
  --smoke-test \
  --workspace "$editor_root/ExampleWorkspace"

editor_commit="${GITHUB_SHA:-local-verification}"
pwsh -NoProfile -File "$repository_root/eng/package-editor.ps1" \
  -RuntimeIdentifier linux-x64 \
  -OutputDirectory "$artifacts_root/editor" \
  -Version 0.1.0-preview.verify \
  -RepositoryCommit "$editor_commit"

echo "Runic Text Resources verification passed."
