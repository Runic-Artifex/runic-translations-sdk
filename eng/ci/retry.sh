#!/usr/bin/env bash
# Runs a command with a time limit per attempt and retries it, so a stalled
# download or package install fails over instead of hanging the job.
# Usage: retry.sh <attempts> <seconds-per-attempt> <command> [args...]
# RETRY_CLEANUP, if set, is run with bash between attempts (for example to
# release locks a killed attempt left behind). It gets its own two-minute limit.
set -u -m
attempts=$1 limit=$2
shift 2

# Runs "$@" in its own process group and stops the group after $1 seconds:
# TERM first, KILL 15 seconds later. Returns the command's status.
run_limited() {
  local seconds=$1 pid watchdog status
  shift
  "$@" &
  pid=$!
  # The watchdog is its own process group (set -m), so stopping that group
  # normally stops its sleep too. Its sleeps don't hold the step's output, so
  # one that survives a race only runs out on its own.
  (
    sleep "$seconds" </dev/null >/dev/null 2>&1
    echo "::warning::Timed out after ${seconds}s: $*"
    kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null
    sleep 15 </dev/null >/dev/null 2>&1
    kill -KILL -- "-$pid" 2>/dev/null || kill -KILL "$pid" 2>/dev/null
  ) &
  watchdog=$!
  wait "$pid"
  status=$?
  kill -TERM -- "-$watchdog" 2>/dev/null || kill -TERM "$watchdog" 2>/dev/null
  wait "$watchdog" 2>/dev/null
  return "$status"
}

status=1
for ((attempt = 1; attempt <= attempts; attempt++)); do
  run_limited "$limit" "$@"
  status=$?
  if [ "$status" -eq 0 ]; then exit 0; fi
  echo "::warning::Attempt $attempt of $attempts failed with status $status: $*"
  if [ "$attempt" -lt "$attempts" ] && [ -n "${RETRY_CLEANUP:-}" ]; then
    run_limited 120 bash -c "$RETRY_CLEANUP" || true
  fi
done
exit "$status"
