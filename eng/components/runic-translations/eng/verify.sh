#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifacts_root="$repository_root/artifacts/verification"
package_feed="$artifacts_root/packages"
package_version="${RUNIC_PACKAGE_VERSION:-1.0.0-preview.1}"
command_line_version="${RunicCommandLineVersion:-1.0.0-preview.1}"
configuration="Release"
runtime_identifier="$(dotnet --info | awk '/ RID:/{print $2; exit}')"
restore_options=()
if [[ -n "${NUGET_CONFIG_FILE:-}" ]]; then
  restore_options+=(--configfile "$NUGET_CONFIG_FILE")
fi

rm -rf "$artifacts_root"
mkdir -p "$package_feed"

(cd "$repository_root/web" && bun install --frozen-lockfile)
node "$repository_root/eng/generate-cldr.mjs" --check
node "$repository_root/eng/render-capabilities.mjs" --check

dotnet restore "$repository_root/Runic.Translations.slnx" --force-evaluate \
  -p:RunicCommandLineVersion="$command_line_version" \
  "${restore_options[@]}"
dotnet build "$repository_root/Runic.Translations.slnx" -c "$configuration" --no-restore -t:Rebuild \
  -p:RunicTranslationsBuildMode=Verification \
  -p:RunicCommandLineVersion="$command_line_version"

test_projects=(
  Runic.Translations.ApiTests
  Runic.Translations.Authoring.Tests
  Runic.Translations.Compiler.Tests
  Runic.Translations.Generator.Tests
  Runic.Translations.Runtime.Tests
  Runic.Translations.Tooling.Tests
  Runic.Translations.Build.Tests
)

for project in "${test_projects[@]}"; do
  dotnet run --project "$repository_root/dotnet/tests/$project/$project.csproj" \
    -c "$configuration" --no-restore -p:RunicCommandLineVersion="$command_line_version"
done

export RunicCommandLineVersion="$command_line_version"
"$repository_root/eng/pack.sh" "$package_version" "$package_feed"

tooling_package="$package_feed/Runic.Translations.Tooling.$package_version.nupkg"
tooling_schema_root="$artifacts_root/tooling-schemas"
mkdir -p "$tooling_schema_root"
7z x -y "$tooling_package" 'schemas/*.schema.json' -o"$tooling_schema_root" >/dev/null
node - "$tooling_schema_root/schemas" <<'NODE'
const { existsSync, readFileSync } = require("node:fs");
const { join, normalize } = require("node:path");
const root = process.argv[2];
const visit = (file, seen = new Set()) => {
  if (seen.has(file)) return;
  seen.add(file);
  const walk = value => {
    if (Array.isArray(value)) return value.forEach(walk);
    if (!value || typeof value !== "object") return;
    if (typeof value.$ref === "string" && !value.$ref.startsWith("#")) {
      const target = normalize(join(root, value.$ref));
      if (!target.startsWith(root + "/") || !existsSync(target)) throw new Error(`Missing packaged schema reference: ${value.$ref}`);
      visit(target, seen);
    }
    Object.values(value).forEach(walk);
  };
  walk(JSON.parse(readFileSync(file, "utf8")));
};
visit(join(root, "locale-pack-v2.schema.json"));
NODE

# The fixed verification version is intentionally reused. Isolate package
# consumption from developer/global caches so it always exercises this run's
# freshly packed binaries rather than a stale package with the same version.
export NUGET_PACKAGES="$artifacts_root/nuget"
export DOTNET_CLI_HOME="$artifacts_root/dotnet-home"
mkdir -p "$DOTNET_CLI_HOME"

package_consumer="$repository_root/dotnet/tests/Runic.Translations.PackageTests/Runic.Translations.PackageTests.csproj"
dotnet restore "$package_consumer" \
  "${restore_options[@]}" \
  -p:TranslationsPackageFeed="$package_feed" \
  -p:TranslationsPackageVersion="$package_version"
tool_root="$artifacts_root/tool"
dotnet tool install dotnet-runic-translations --version "$package_version" \
  --tool-path "$tool_root" \
  "${restore_options[@]}"
"$tool_root/runic-translations" --help >/dev/null
"$tool_root/runic-translations" help >/dev/null

expect_tool_usage() {
  local expected="$1"
  shift
  local text
  if text=$("$tool_root/runic-translations" "$@" 2>&1); then
    echo "Expected packaged tool invocation to fail: $*" >&2
    exit 1
  fi
  [[ "$text" == *"$expected"* ]] || { echo "Missing packaged diagnostic '$expected': $text" >&2; exit 1; }
}
expect_tool_usage "validate requires --catalog <file>" validate
expect_tool_usage "validate requires --documents <path-or-glob...>" validate --catalog missing.json
expect_tool_usage "unknown command" definitely-not-a-command
expect_tool_usage "--catalog requires exactly one value" validate --catalog
tool_parse_json=$("$tool_root/runic-translations" validate --runic-output=JSON 2>&1 || true)
node -e '
  const value = JSON.parse(process.argv[1]);
  if (value.success !== false || value.exitCode !== 2 || value.fault?.code !== "RCLI1012" || value.diagnostics?.[0]?.arguments?.[0] !== "--catalog") process.exit(1);
' "$tool_parse_json"
tool_json_root="$artifacts_root/tool-json"
tool_json=$("$tool_root/runic-translations" schema --output "$tool_json_root" --runic-output=json)
node -e 'const value = JSON.parse(process.argv[1]); if (value.success !== true || value.payloadType !== "runic.translations.tool/1") process.exit(1);' "$tool_json"

verify_conflict_presentation() {
  local executable="$1"
  local project="$2"
  local label="$3"
  local human_output="$artifacts_root/$label-conflict.out"
  local human_error="$artifacts_root/$label-conflict.err"
  local json_output="$artifacts_root/$label-conflict.json"
  local json_error="$artifacts_root/$label-conflict.json.err"

  "$executable" init --directory "$project" --catalog "$label" --default-locale en \
    --namespace Verification.Tool --class VerificationText
  if "$executable" init --directory "$project" --catalog "$label" --default-locale en \
    --namespace Verification.Tool --class VerificationText >"$human_output" 2>"$human_error"; then
    echo "Expected $label conflict invocation to fail." >&2
    exit 1
  fi
  test ! -s "$human_output"
  test "$(wc -l < "$human_error")" -eq 1
  grep -F "runic-translations: Target path '" "$human_error" >/dev/null
  grep -F "already exists; no files were written." "$human_error" >/dev/null

  if "$executable" init --directory "$project" --catalog "$label" --default-locale en \
    --namespace Verification.Tool --class VerificationText --runic-output=JSON >"$json_output" 2>"$json_error"; then
    echo "Expected $label JSON conflict invocation to fail." >&2
    exit 1
  fi
  test ! -s "$json_error"
  node -e '
    const text = require("fs").readFileSync(process.argv[1], "utf8");
    const value = JSON.parse(text);
    if (value.success !== false || value.exitCode !== 2 || value.diagnostics?.[0]?.code !== "RCLI9004" || text.includes("Target path")) process.exit(1);
  ' "$json_output"
}

verify_parser_compatibility() {
  local executable="$1"
  local label="$2"
  local parser_root="$artifacts_root/$label-parser"
  mkdir -p "$parser_root"

  expect_usage() {
    local expected="$1"
    shift
    local output="$parser_root/$RANDOM.out"
    local error="$parser_root/$RANDOM.err"
    if "$executable" "$@" >"$output" 2>"$error"; then
      echo "Expected $label parser invocation to fail: $*" >&2
      exit 1
    fi
    test ! -s "$output"
    grep -F "runic-translations: $expected" "$error" >/dev/null
  }

  "$executable" help >/dev/null
  expect_usage "help does not accept additional arguments." help validate
  expect_usage "a command is required."
  expect_usage "unknown command 'definitely-not-a-command'." definitely-not-a-command
  local command
  for command in init validate generate verify schema import analyze inspect migrate xliff-export xliff-import review-export review-import review-report locale-pack; do
    expect_usage "unknown option or positional argument '--bogus'." "$command" --bogus
  done
  expect_usage "schema accepts only --output <directory>." schema --catalog catalog.json
  expect_usage "unknown option or positional argument '--bogus'." schema --bogus
  expect_usage "validate does not accept --output." validate --catalog catalog.json --documents document.json --output output
  expect_usage "validate does not accept emit switches." validate --catalog catalog.json --documents document.json --emit-json
  expect_usage "analyze does not accept emit switches." analyze --catalog catalog.json --documents document.json --emit-json
  expect_usage "--documents requires at least one explicit path or glob." validate --catalog catalog.json --documents
  expect_usage "--documents may be specified only once." validate --catalog catalog.json --documents first.json --documents second.json
  expect_usage "--sources requires at least one explicit path or glob." analyze --catalog catalog.json --documents document.json --sources
  expect_usage "--sources may be specified only once." analyze --catalog catalog.json --documents document.json --sources first.cs --sources second.cs
  expect_usage "import requires at least one --source <locale>=<file>." import --catalog catalog --default-locale en --namespace Verification.Tool --class VerificationText --output output
}

verify_conflict_presentation "$tool_root/runic-translations" "$artifacts_root/managed-tool-project" managed
verify_parser_compatibility "$tool_root/runic-translations" managed

# Exercise the published tool itself and retain its strict NativeAOT surface.
tool_aot_root="$artifacts_root/tool-aot"
tool_project="$repository_root/dotnet/tools/dotnet-runic-translations/dotnet-runic-translations.csproj"
dotnet restore "$tool_project" -r "$runtime_identifier" \
  -p:RunicCommandLineVersion="$command_line_version" \
  -p:PublishAot=true -p:PublishTrimmed=true -p:TrimMode=full \
  "${restore_options[@]}"
dotnet publish "$tool_project" -c "$configuration" -r "$runtime_identifier" --self-contained true --no-restore \
  -p:RunicCommandLineVersion="$command_line_version" \
  -p:PublishAot=true -p:PublishTrimmed=true -p:TrimMode=full \
  -p:IlcTreatWarningsAsErrors=true \
  -p:PublishDir="$tool_aot_root"
"$tool_aot_root/dotnet-runic-translations" --help >/dev/null
"$tool_aot_root/dotnet-runic-translations" help >/dev/null
native_project="$artifacts_root/native-tool-project"
"$tool_aot_root/dotnet-runic-translations" schema --output "$artifacts_root/native-schemas"
"$tool_aot_root/dotnet-runic-translations" init \
  --directory "$native_project" --catalog native --default-locale en \
  --namespace Native.Tool --class NativeText --locale de
"$tool_aot_root/dotnet-runic-translations" generate \
  --catalog "$native_project/native.catalog.json" --documents "$native_project/native.en.json" "$native_project/native.de.json" \
  --output "$artifacts_root/native-generated"
"$tool_aot_root/dotnet-runic-translations" verify \
  --catalog "$native_project/native.catalog.json" --documents "$native_project/native.en.json" "$native_project/native.de.json" \
  --output "$artifacts_root/native-generated"
test -f "$artifacts_root/native-generated/native.en.locale-v2.json"
native_parse_json=$("$tool_aot_root/dotnet-runic-translations" validate --runic-output=JSON 2>&1 || true)
node -e '
  const value = JSON.parse(process.argv[1]);
  if (value.success !== false || value.exitCode !== 2 || value.fault?.code !== "RCLI1012") process.exit(1);
' "$native_parse_json"
verify_conflict_presentation "$tool_aot_root/dotnet-runic-translations" "$native_project-conflict" native-conflict
verify_parser_compatibility "$tool_aot_root/dotnet-runic-translations" native
"$tool_aot_root/dotnet-runic-translations" validate \
  --catalog "$native_project/native.catalog.json" --documents "$native_project/native.en.json" "$native_project/native.de.json"
"$tool_aot_root/dotnet-runic-translations" locale-pack \
  --catalog "$native_project/native.catalog.json" --documents "$native_project/native.en.json" "$native_project/native.de.json" \
  --output "$artifacts_root/native-locale-pack"
"$tool_aot_root/dotnet-runic-translations" xliff-export \
  --catalog "$native_project/native.catalog.json" --documents "$native_project/native.en.json" "$native_project/native.de.json" \
  --output "$artifacts_root/native-xliff"
native_xliff="$(find "$artifacts_root/native-xliff" -name '*.xliff' -print -quit)"
test -n "$native_xliff"
"$tool_aot_root/dotnet-runic-translations" xliff-import --source "$native_xliff" --output "$artifacts_root/native-xliff-import"
"$tool_aot_root/dotnet-runic-translations" review-export --catalog native --output "$artifacts_root/native-review.json"
"$tool_aot_root/dotnet-runic-translations" review-import --source "$artifacts_root/native-review.json" >/dev/null
"$tool_aot_root/dotnet-runic-translations" review-report --source "$artifacts_root/native-review.json" >/dev/null
"$tool_aot_root/dotnet-runic-translations" migrate --source "$native_project/native.en.json" --output "$artifacts_root/native.en.v3.json"

template_package="$package_feed/Runic.Translations.Templates.$package_version.nupkg"
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
dotnet restore "$template_project" \
  -p:RestoreAdditionalProjectSources="$package_feed" \
  "${restore_options[@]}"
dotnet build "$template_project" -c "$configuration" --no-restore \
  -p:TranslationsToolCommand="$tool_root/runic-translations" \
  -p:RunicTranslationsBuildMode=Verification
test -f "$template_root/project/obj/$configuration/net10.0/translations/product.esm/messages.js"

dotnet run --project "$package_consumer" -c "$configuration" --no-restore \
  -p:TranslationsPackageVersion="$package_version" \
  -p:TranslationsGenerateOnBuild=true \
  -p:TranslationsToolCommand="$tool_root/runic-translations" \
  -- --feed "$package_feed"

aot_consumer="$repository_root/dotnet/tests/Runic.Translations.AotTests/Runic.Translations.AotTests.csproj"
dotnet restore "$aot_consumer" -r "$runtime_identifier" \
  "${restore_options[@]}" \
  -p:TranslationsPackageFeed="$package_feed" \
  -p:TranslationsPackageVersion="$package_version" \
  -p:PublishAot=true \
  -p:PublishTrimmed=true \
  -p:TrimMode=full
dotnet publish "$aot_consumer" -c "$configuration" -r "$runtime_identifier" --self-contained true --no-restore \
  -p:TranslationsPackageVersion="$package_version" \
  -p:PublishAot=true \
  -p:PublishTrimmed=true \
  -p:TrimMode=full \
  -p:IlcTreatWarningsAsErrors=true \
  -p:PublishDir="$artifacts_root/aot/"
"$artifacts_root/aot/Runic.Translations.AotTests"

(cd "$repository_root/web" && bun test)
echo "Runic Translations verification passed."
