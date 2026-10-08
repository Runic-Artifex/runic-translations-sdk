#!/usr/bin/env bash
# Installs Ubuntu packages on a CI runner without letting a stalled mirror hold
# the job: apt gets network timeouts, and update and install each get a time
# limit per attempt and are retried. A killed attempt's apt-get or dpkg is
# stopped and its locks released before the next attempt.
# Usage: apt-install.sh <package>...
set -euo pipefail
dir=$(dirname "$0")
printf '%s\n' 'Acquire::http::Timeout "30";' 'Acquire::https::Timeout "30";' 'Acquire::Retries "3";' \
  | sudo tee /etc/apt/apt.conf.d/99runic-timeouts >/dev/null
export RETRY_CLEANUP="sudo pkill -KILL -x apt-get; sudo pkill -KILL -x dpkg; sleep 2; sudo DEBIAN_FRONTEND=noninteractive dpkg --configure -a"
"$dir/retry.sh" 3 300 sudo apt-get update
"$dir/retry.sh" 3 600 sudo DEBIAN_FRONTEND=noninteractive apt-get install --yes "$@"
