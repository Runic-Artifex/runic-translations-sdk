#!/usr/bin/env bash
# Ubuntu restricts capabilities inside unprivileged user namespaces. Permit the distribution's bwrap helper, which
# WebKitGTK uses for its web process sandbox, without disabling AppArmor or WebKit's sandbox, then prove bwrap can
# start. The same script as runic-sdk's eng/ci/permit-webkit-sandbox.sh (v0.22.0).
set -euo pipefail
test "$(command -v bwrap)" = /usr/bin/bwrap
sudo tee /etc/apparmor.d/runic-ci-bwrap > /dev/null <<'PROFILE'
abi <abi/4.0>,
include <tunables/global>
profile runic-ci-bwrap /usr/bin/bwrap flags=(unconfined) {
  userns,
}
PROFILE
sudo apparmor_parser -r /etc/apparmor.d/runic-ci-bwrap
bwrap --unshare-user --unshare-net --ro-bind / / --proc /proc --dev /dev true
