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
node "$repository_root/eng/generate-cldr.mjs" --check
node "$repository_root/eng/render-capabilities.mjs" --check

dotnet restore "$repository_root/RunicTranslations.slnx"
dotnet build "$repository_root/RunicTranslations.slnx" -c "$configuration" --no-restore \
  -p:RunicTranslationsBuildMode=Verification

test_projects=(
  RunicTranslations.ApiTests
  RunicTranslations.Authoring.Tests
  RunicTranslations.Compiler.Tests
  RunicTranslations.Generator.Tests
  RunicTranslations.Runtime.Tests
  RunicTranslations.Build.Tests
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

package_consumer="$repository_root/dotnet/tests/RunicTranslations.PackageTests/RunicTranslations.PackageTests.csproj"
dotnet restore "$package_consumer" \
  -p:TranslationsPackageFeed="$package_feed"
tool_root="$artifacts_root/tool"
dotnet tool install RunicTranslations.Tool --version "$package_version" \
  --tool-path "$tool_root" \
  --add-source "$package_feed"
"$tool_root/runic-translations" --help >/dev/null

template_package="$package_feed/RunicTranslations.Templates.$package_version.nupkg"
template_root="$artifacts_root/templates"
dotnet new install "$template_package" >/dev/null
dotnet new runic-translations \
  --output "$template_root/item" \
  --catalog product \
  --defaultLocale de \
  --namespace Customer.Product \
  --className ProductText
"$tool_root/runic-translations" init \
  --directory "$template_root/cli" \
  --catalog product \
  --default-locale de \
  --namespace Customer.Product \
  --class ProductText
cmp "$template_root/item/product.catalog.json" "$template_root/cli/product.catalog.json"
cmp "$template_root/item/product.de.json" "$template_root/cli/product.de.json"
dotnet new runic-translations-project \
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
  -p:TranslationsToolCommand="$tool_root/runic-translations" \
  -p:RunicTranslationsBuildMode=Verification
test -f "$template_root/project/obj/$configuration/net10.0/translations/product.esm/messages.js"

dotnet run --project "$package_consumer" -c "$configuration" --no-restore \
  -p:TranslationsGenerateOnBuild=true \
  -p:TranslationsToolCommand="$tool_root/runic-translations" \
  -- --feed "$package_feed"

aot_consumer="$repository_root/dotnet/tests/RunicTranslations.AotTests/RunicTranslations.AotTests.csproj"
runtime_identifier="$(dotnet --info | awk '/ RID:/{print $2; exit}')"
dotnet restore "$aot_consumer" -r "$runtime_identifier" \
  -p:TranslationsPackageFeed="$package_feed" \
  -p:PublishAot=true \
  -p:PublishTrimmed=true \
  -p:TrimMode=full
dotnet publish "$aot_consumer" -c "$configuration" -r "$runtime_identifier" --self-contained true --no-restore \
  -p:PublishAot=true \
  -p:PublishTrimmed=true \
  -p:TrimMode=full \
  -p:IlcTreatWarningsAsErrors=true \
  -p:PublishDir="$artifacts_root/aot/"
"$artifacts_root/aot/RunicTranslations.AotTests"

npm --prefix "$repository_root/web" test
echo "Runic Translations verification passed."
