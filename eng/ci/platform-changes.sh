#!/usr/bin/env bash
# Whether CI runs its macOS and its Windows jobs, by the event and the changed paths (CONTRIBUTING.md, "CI triggers and
# platform jobs"). CI's `changes` job runs this for every run:
#   - pull_request: each platform's jobs run when the test merge commit changes a path in that platform's list
#     (eng/ci/macos-paths.txt, eng/ci/windows-paths.txt) against the base branch;
#   - push (to main): the same against the previous main commit, except that a version or release commit, one that
#     changes a path in eng/ci/full-run-paths.txt, runs both;
#   - every other event (schedule, workflow_dispatch): both.
# The macOS and Windows jobs are the only jobs a run can skip, so a run that runs both runs every job: a full run, which
# CI marks with the ci-full-run artifact for Publish preview and the runic-sdk train.
#
#   eng/ci/platform-changes.sh [--event <event>] <base> [<head>]   the paths `git diff --name-only <base> <head>` lists (head: HEAD)
#   eng/ci/platform-changes.sh [--event <event>] --stdin           changed paths, one per line, on standard input
#
# <event> is GitHub's event name; without --event the pull request rule applies (a local check of a branch).
# Prints the matching paths, then `macos=…`, `windows=…` and `full=…`; with GITHUB_OUTPUT set, also writes them there.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
usage() { echo "Usage: eng/ci/platform-changes.sh [--event <event>] <base> [<head>] | [--event <event>] --stdin" >&2; exit 64; }

event=pull_request
if [[ "${1-}" == --event ]]; then
  [[ $# -ge 2 && -n "$2" ]] || usage
  event="$2"
  shift 2
fi
case "${1-}" in
  --stdin) changed="$(cat)" ;;
  "" | -*) usage ;;
  # Only the pull request and push rules read the changed paths.
  *) case "$event" in
       pull_request | push) changed="$(git -C "$root" diff --name-only --no-renames "$1" "${2:-HEAD}")" ;;
       *) changed="" ;;
     esac ;;
esac

# Reads a path list: `*` also matches `/`; a line starting with `!` excludes, and exclusions win.
load() {
  local line
  includes=()
  excludes=()
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%%#*}"
    line="${line%"${line##*[![:space:]]}"}"
    [[ -z "$line" ]] && continue
    if [[ "$line" == '!'* ]]; then excludes+=("${line:1}"); else includes+=("$line"); fi
  done < "$root/eng/ci/$1"
}

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

# Prints each changed path the loaded list matches, with a label; succeeds when there is one.
any_change() {
  local label="$1" path found=1
  while IFS= read -r path; do
    [[ -z "$path" ]] && continue
    if matches "$path"; then
      echo "$label: $path"
      found=0
    fi
  done <<< "$changed"
  return "$found"
}

# Each platform by its own list.
by_paths() {
  load macos-paths.txt
  if any_change "macOS path"; then macos=true; fi
  load windows-paths.txt
  if any_change "Windows path"; then windows=true; fi
}

macos=false
windows=false
case "$event" in
  pull_request) by_paths ;;
  push)
    load full-run-paths.txt
    if any_change "Version or release path"; then
      echo "A version or release commit runs every job."
      macos=true
      windows=true
    else
      by_paths
    fi
    ;;
  *)
    echo "A $event run runs every job."
    macos=true
    windows=true
    ;;
esac

full=false
if [[ "$macos" == true && "$windows" == true ]]; then full=true; fi
printf 'macos=%s\nwindows=%s\nfull=%s\n' "$macos" "$windows" "$full"
if [[ -n "${GITHUB_OUTPUT-}" ]]; then printf 'macos=%s\nwindows=%s\nfull=%s\n' "$macos" "$windows" "$full" >> "$GITHUB_OUTPUT"; fi
