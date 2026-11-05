#!/usr/bin/env bash
# The localized formatting tests, the cross-runtime date/percent and currency/unit conformance corpora and the all-locale comparisons on one
# platform: .NET with the platform's ICU, and the ESM half in Bun and Node. CI's Windows and macOS formatting jobs run this; Linux runs the same
# tests in the full suite.
set -euo pipefail

compiler=tests/dotnet/Runic.Translations.Compiler.Tests/Runic.Translations.Compiler.Tests.csproj
runtime=tests/dotnet/Runic.Translations.Runtime.Tests/Runic.Translations.Runtime.Tests.csproj

dotnet --info | sed -n '1,12p'
node --version
bun --version

dotnet run --project "$compiler" --configuration Release -- \
  --treenode-filter "/*/*/Rmf2DatePercentConformanceTests/*" --test-parameter esm-host=all --minimum-expected-tests 2
dotnet run --project "$compiler" --configuration Release --no-build -- \
  --treenode-filter "/*/*/Rmf2EsmLocalizedFormattingTests/*" --test-parameter esm-host=all --minimum-expected-tests 1
# Every CLDR locale: pinned numbers exactly; dates only where .NET and the engine load the same ICU (else reported).
dotnet run --project "$compiler" --configuration Release --no-build -- \
  --treenode-filter "/*/*/LocalizedFormatComparisonTests/*" --test-parameter esm-host=all --minimum-expected-tests 1
# :currency, :unit and compact notation (pinned CLDR): the corpus exactly in four routes, and every CLDR locale against
# Intl where the engine carries the pinned CLDR version (else reported).
dotnet run --project "$compiler" --configuration Release --no-build -- \
  --treenode-filter "/*/*/Rmf2CurrencyUnitConformanceTests/*" --test-parameter esm-host=all --minimum-expected-tests 2
dotnet run --project "$compiler" --configuration Release --no-build -- \
  --treenode-filter "/*/*/CurrencyUnitComparisonTests/*" --test-parameter esm-host=all --minimum-expected-tests 1
dotnet run --project "$runtime" --configuration Release -- \
  --treenode-filter "/*/*/LocalizedFormattingTests/*" --minimum-expected-tests 8
