#!/usr/bin/env bash
# Opens a published editor's example workspace in its native Runic Desktop window and checks the evidence of
# `edit --native-shell-canary`: the window opened embedded (no browser fallback), its page connected to the backend, and
# the window closed and drained. CI's Windows and macOS editor jobs run it on `bun eng/run.mjs publish-editor <rid>`, and
# the release workflow (.github/workflows/release-editor.yml) on each installed or extracted package.
# Usage: editor-native-canary.sh <publish-directory> <expected-backend> <log-directory> [<expected-version>]
# The publish directory holds the editor executable and ExampleWorkspace: a publish folder, an extracted archive, the
# installed /usr/lib/<application id> of a deb or rpm, or the Contents/MacOS of a macOS bundle. EDITOR_EXECUTABLE names
# another executable to start, such as an AppImage, with the publish directory's example workspace. The expected backend
# is webview2, wkwebview or gtk4-webkitgtk6. With an expected version, the first line of --version must be
# "Runic Translations Editor <version>", optionally with the source revision as build metadata (+<commit>).
# Standard output and error are kept in the log directory.
set -euo pipefail
publish=$1 backend=$2 logs=$3 version=${4:-}

mkdir -p "$logs"
editor="${EDITOR_EXECUTABLE:-$publish/Runic.Translations.Editor}"
[[ -f "$editor.exe" ]] && editor="$editor.exe"
# The physical path: a macOS bundle's Contents/MacOS/ExampleWorkspace is a symbolic link to Contents/Resources, and the
# editor refuses a workspace path that crosses a link.
workspace="$(cd -P "$publish/ExampleWorkspace" && pwd)"

"$editor" --version | tee "$logs/version.txt"
if [[ -n "$version" ]]; then
  first="$(head -n 1 "$logs/version.txt" | tr -d '\r')"
  if [[ ! "$first" =~ ^Runic\ Translations\ Editor\ ${version//./\\.}(\+[0-9a-f]{40})?$ ]]; then
    echo "::error::The editor reports \"$first\", not its own version $version."
    exit 1
  fi
fi
"$editor" validate "$workspace" 2>&1 | tee "$logs/validate.txt"

status=0
start=$SECONDS
"$editor" edit "$workspace" --native-shell-canary >"$logs/canary.stdout.txt" 2>"$logs/canary.stderr.txt" || status=$?
echo "The canary exited with $status after $((SECONDS - start)) s."
cat "$logs/canary.stdout.txt"
cat "$logs/canary.stderr.txt" >&2
if [[ $status -ne 0 ]]; then
  echo "::error::The native editor window did not open embedded, connect and close (exit $status); see the canary logs."
  exit 1
fi

# The JSON line is the last line of standard output. The script is JavaScript, with no shell expansion.
# shellcheck disable=SC2016
EVIDENCE="$(tail -n 1 "$logs/canary.stdout.txt")" BACKEND="$backend" bun -e '
  const evidence = JSON.parse(process.env.EVIDENCE);
  const expected = {
    schema: "runic.translations.editor-native-shell/4", host: "desktop", presentation: "Embedded",
    backend: process.env.BACKEND, packagedUiPresent: true, bridgeConnected: true, cleanup: "closed-disposed",
  };
  const wrong = Object.entries(expected).filter(([key, value]) => evidence[key] !== value);
  if (wrong.length) {
    console.log(`::error::Unexpected native-shell evidence: ${wrong.map(([key, value]) => `${key} is ${JSON.stringify(evidence[key])}, not ${JSON.stringify(value)}`).join("; ")}`);
    process.exit(1);
  }
  console.log(`The ${evidence.backend} window opened embedded, its page connected, and it closed and drained.`);
'
