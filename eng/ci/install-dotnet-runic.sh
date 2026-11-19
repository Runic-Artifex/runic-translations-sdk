#!/usr/bin/env bash
# Installs runic-sdk's dotnet-runic tool at the runic-sdk release the Translations Editor is built on
# (RunicSdkDependencyVersion in eng/Versions.props) into a tool path, for dotnet runic package in
# .github/workflows/release-editor.yml. Usage: install-dotnet-runic.sh <tool-path>
set -euo pipefail
tools=$1
version=$(sed -n 's:.*<RunicSdkDependencyVersion>\([^<]*\)</RunicSdkDependencyVersion>.*:\1:p' eng/Versions.props)
test -n "$version"
"$(dirname "$0")/retry.sh" 3 60 dotnet tool install dotnet-runic --version "$version" --tool-path "$tools"
"$tools/dotnet-runic" --version
