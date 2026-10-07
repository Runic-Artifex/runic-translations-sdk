#!/usr/bin/env bash
# Runs a command with a time limit per attempt and retries it, so a stalled
# download or package install fails over instead of hanging the job.
# Usage: retry.sh <attempts> <seconds-per-attempt> <command> [args...]
# RETRY_CLEANUP, if set, is run with bash between attempts (for example to
# release locks a killed attempt left behind).
set -u -m
attempts=$1 limit=$2
shift 2
status=1
for ((attempt = 1; attempt <= attempts; attempt++)); do
  "$@" &
  pid=$!
  (
    sleep "$limit"
    echo "::warning::Attempt $attempt timed out after ${limit}s: $*"
    kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null
    sleep 15
    kill -KILL -- "-$pid" 2>/dev/null || kill -KILL "$pid" 2>/dev/null
  ) &
  watchdog=$!
  wait "$pid"
  status=$?
  kill -- "-$watchdog" 2>/dev/null || kill "$watchdog" 2>/dev/null
  wait "$watchdog" 2>/dev/null
  if [ "$status" -eq 0 ]; then exit 0; fi
  echo "::warning::Attempt $attempt of $attempts failed with status $status: $*"
  if [ "$attempt" -lt "$attempts" ] && [ -n "${RETRY_CLEANUP:-}" ]; then bash -c "$RETRY_CLEANUP" || true; fi
done
exit "$status"
