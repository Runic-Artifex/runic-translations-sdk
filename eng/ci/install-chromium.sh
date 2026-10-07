#!/usr/bin/env bash
# Installs Playwright's Chromium and its Ubuntu dependencies from the current
# directory's playwright-core. Stalled apt or browser downloads have hung jobs
# until their timeout, while a normal install takes under three minutes.
set -euo pipefail
# A stalled mirror connection should fail fast rather than hold apt.
printf 'Acquire::http::Timeout "30";\nAcquire::https::Timeout "30";\nAcquire::Retries "3";\n' |
  sudo tee /etc/apt/apt.conf.d/99runic-timeouts >/dev/null
# A killed attempt can leave root-owned apt running and holding its locks.
export RETRY_CLEANUP="sudo pkill -KILL -x apt-get; sudo pkill -KILL -x dpkg; sleep 2; sudo dpkg --configure -a"
"$(dirname "$0")/retry.sh" 3 300 bun node_modules/playwright-core/cli.js install --with-deps chromium
