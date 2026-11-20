#!/usr/bin/env bash
# Whether a change touches a path that CI's macOS jobs exercise, as listed in eng/ci/macos-paths.txt. CI's `changes`
# job runs this for a pull request against its test merge commit; pushes to main, nightly and manual runs always run
# the macOS jobs (CONTRIBUTING.md, "CI triggers and macOS jobs").
#
#   eng/ci/macos-changes.sh <base> [<head>]   the paths `git diff --name-only <base> <head>` lists (head: HEAD)
#   eng/ci/macos-changes.sh --stdin           changed paths, one per line, on standard input
#
# Prints the matching paths and then `macos=true` or `macos=false`; with GITHUB_OUTPUT set, also writes that line there.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
patterns_file="$root/eng/ci/macos-paths.txt"

case "${1-}" in
  --stdin) changed="$(cat)" ;;
  "" | -*) echo "Usage: eng/ci/macos-changes.sh <base> [<head>] | --stdin" >&2; exit 64 ;;
  *) changed="$(git -C "$root" diff --name-only --no-renames "$1" "${2:-HEAD}")" ;;
esac

includes=()
excludes=()
while IFS= read -r line || [[ -n "$line" ]]; do
  line="${line%%#*}"
  line="${line%"${line##*[![:space:]]}"}"
  [[ -z "$line" ]] && continue
  if [[ "$line" == '!'* ]]; then excludes+=("${line:1}"); else includes+=("$line"); fi
done < "$patterns_file"

# In [[ == ]] an unquoted pattern is a glob whose `*` also matches `/`, so `dir/*` covers everything below dir.
matches() {
  local path="$1" pattern
  for pattern in "${excludes[@]}"; do
    # shellcheck disable=SC2053
    [[ "$path" == $pattern ]] && return 1
  done
  for pattern in "${includes[@]}"; do
    # shellcheck disable=SC2053
    [[ "$path" == $pattern ]] && return 0
  done
  return 1
}

macos=false
while IFS= read -r path; do
  [[ -z "$path" ]] && continue
  if matches "$path"; then
    echo "macOS path: $path"
    macos=true
  fi
done <<< "$changed"

echo "macos=$macos"
if [[ -n "${GITHUB_OUTPUT-}" ]]; then echo "macos=$macos" >> "$GITHUB_OUTPUT"; fi
