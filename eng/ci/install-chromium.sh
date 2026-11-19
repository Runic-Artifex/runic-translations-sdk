#!/usr/bin/env bash
# Downloads Playwright's Chromium from the current directory's playwright-core.
# Its Ubuntu dependencies are not installed here: .github/actions/install-chromium
# installs them through .github/actions/apt-install first (CONTRIBUTING.md,
# "Ubuntu packages in CI"). A stalled browser download has hung jobs until their
# timeout, while a normal download takes under a minute, so each attempt is
# time-limited.
set -euo pipefail
"$(dirname "$0")/retry.sh" 3 300 bun node_modules/playwright-core/cli.js install chromium
