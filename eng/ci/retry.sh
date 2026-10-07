#!/usr/bin/env bash
# Runs a command with a time limit per attempt and retries it, so a stalled
# download or package install fails over instead of hanging the job.
# Usage: retry.sh <attempts> <seconds-per-attempt> <command> [args...]
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
done
exit "$status"
