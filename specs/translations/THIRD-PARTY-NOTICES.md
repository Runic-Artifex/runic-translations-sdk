# Third-party notices

## Unicode CLDR

Runic Translations contains a normalized subset derived from Unicode Common
Locale Data Repository (CLDR) 48.2 data.

- Copyright © 2019–2025 Unicode, Inc.
- License: Unicode License v3 (`Unicode-3.0`)
- Pinned source and digest: `eng/cldr/runic-subset-48.2.json`
- Complete license text: `eng/cldr/LICENSE` in the source tree and
  `licenses/LICENSE` in packages that embed the generated data or compiler

The generated subset is used for portable plural rules, locale capability
declarations, and selected relative-time patterns.

## Tomlyn

Runic Translations compiler and bundled authoring tools use Tomlyn 2.10.1 for
TOML 1.1 syntax parsing and validation. Generated application translation
runtimes do not depend on Tomlyn.

- Source: https://github.com/xoofx/Tomlyn/releases/tag/2.10.1
- License: BSD 2-Clause (`BSD-2-Clause`)
- Complete upstream license: `specs/translations/licenses/Tomlyn-LICENSE.txt`
  in the source tree and `licenses/Tomlyn-LICENSE.txt` in compiler/tool packages.
