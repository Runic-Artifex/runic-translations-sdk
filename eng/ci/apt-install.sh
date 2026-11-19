#!/usr/bin/env bash
# Installs Ubuntu packages on a CI runner without letting a slow or stalled mirror
# hold the job (CONTRIBUTING.md, "Ubuntu packages in CI").
#
# Downloading and installing are separate phases. The x64 runners' mirror list
# (/etc/apt/apt-mirrors.txt) starts with an Azure or an EC2 mirror depending on the
# runner image, and either can crawl: in November 2026 the EC2 mirror delivered
# about 100 kB/s while the Azure one delivered 3-8 MB/s (runic-sdk#1129), and 40 kB/s
# here (#258). Neither is assumed slow. Instead, before the first download the
# script measures each candidate mirror with a ranged GET (APT_PROBE_BYTES, 2 MB)
# of the largest archive to fetch, in parallel and at most APT_PROBE_SECONDS (8)
# each, and orders the mirrors by that rate. The candidates are the runner's own
# Ubuntu mirrors plus azure.archive.ubuntu.com and archive.ubuntu.com; only
# *.ubuntu.com hosts are used.
#
# The download is bounded by progress, not by a fixed time per attempt. An attempt
# is stopped when the package archives (the finished .deb files and apt's partial/
# downloads) have not grown for APT_STALL_SECONDS (60), or, while an untried mirror
# is left, when its average rate after APT_RATE_WINDOW seconds (45) is below
# APT_RATE_FLOOR bytes per second (1000000). All attempts share a budget of
# APT_DOWNLOAD_SECONDS (1200), so a slow last mirror that keeps delivering still
# finishes. The next attempt puts the fastest untried mirror first (the mirror
# list rewritten with explicit priorities, or the archive host of plain sources
# replaced), and resumes the downloads. The arm64 runners' ports.ubuntu.com has no
# alternative, so they skip the probe and the rate floor and only retry.
#
# All mirrors are signed by the same Ubuntu archive key; apt still verifies the
# signed indexes and every package's hash, including cached .deb files before it
# uses them. Each attempt logs the bytes it fetched, its average rate and the bytes
# still to fetch. The install then runs without network access. A stopped attempt's
# apt-get or dpkg is killed and its locks released before the next one.
#
# Usage: apt-install.sh [--resolve] [--no-install-recommends] <package>...
# APT_ARCHIVES, if set, is the directory for downloaded .deb files, so a cache can
# restore and save them (.github/actions/apt-install). APT_ETC (default /etc/apt)
# lets eng/ci/apt-install.test.mjs run the script against a stubbed apt-get and curl.
#
# --resolve only runs apt-get update and prints a 16-character hash of the archive
# file names (name_version_arch.deb) the packages need from a cold archive cache, or
# nothing when apt cannot tell; the action puts it in its cache key (runic-sdk#1131), so
# a new package version gets a new entry. A later install with APT_UPDATED=true reuses
# those indexes and skips its first apt-get update.
set -euo pipefail
dir=$(cd "$(dirname "$0")" && pwd)
resolve=false
if [ "${1:-}" = --resolve ]; then resolve=true; shift; fi
options=()
if [ "${1:-}" = --no-install-recommends ]; then options=(--no-install-recommends); shift; fi
packages=("$@")
archives=${APT_ARCHIVES:-/var/cache/apt/archives}
etc=${APT_ETC:-/etc/apt}
mirror_list=$etc/apt-mirrors.txt
stall=${APT_STALL_SECONDS:-60}
budget=${APT_DOWNLOAD_SECONDS:-1200}
poll=${APT_POLL_SECONDS:-2}
rate_floor=${APT_RATE_FLOOR:-1000000}
rate_window=${APT_RATE_WINDOW:-45}
probe_bytes=${APT_PROBE_BYTES:-2000000}
probe_seconds=${APT_PROBE_SECONDS:-8}
tab=$'\t'
max_attempts=5
log=$(mktemp)
originals=$(mktemp -d)
trap 'rm -rf "$log" "$originals"' EXIT

printf '%s\n' 'Acquire::http::Timeout "30";' 'Acquire::https::Timeout "30";' 'Acquire::Retries "3";' \
  | sudo tee "$etc/apt.conf.d/99runic-timeouts" >/dev/null
sudo mkdir -p "$archives"
apt=(sudo DEBIAN_FRONTEND=noninteractive apt-get -o "Dir::Cache::Archives=$archives/")
cleanup="sudo pkill -KILL -x apt-get; sudo pkill -KILL -x dpkg; sleep 2; sudo DEBIAN_FRONTEND=noninteractive dpkg --configure -a"

source_files() {
  local file
  for file in "$etc/sources.list" "$etc"/sources.list.d/*.list "$etc"/sources.list.d/*.sources; do
    if [ -f "$file" ]; then echo "$file"; fi
  done
}

# The x64 runners' sources name a mirror list; the arm64 runners have the file too,
# but their sources name ports.ubuntu.com directly.
uses_mirror_list() {
  local files
  mapfile -t files < <(source_files)
  [ -f "$mirror_list" ] && [ "${#files[@]}" -gt 0 ] && grep -qsF "mirror+file:$mirror_list" "${files[@]}"
}

# The Ubuntu mirrors apt uses, in the order it tries them: the runner's mirror list
# (by priority, then position), or the hosts in its sources.
mirrors() {
  local files
  if uses_mirror_list; then
    awk '/^[[:space:]]*[a-z+]+:\/\// { p = 1e9; for (i = 2; i <= NF; i++) if ($i ~ /^priority:/) p = substr($i, 10) + 0; printf "%012d %06d %s\n", p, NR, $1 }' "$mirror_list" \
      | sort | awk '{print $3}'
  else
    mapfile -t files < <(source_files)
    if [ "${#files[@]}" -gt 0 ]; then
      grep -hsE '^[[:space:]]*(deb|URIs:)' "${files[@]}" | grep -oE 'https?://[^/[:space:]]*ubuntu\.com' || true
    fi
  fi | sed -E 's#^[a-z+]+://##; s#/.*##' | awk '!seen[$0]++' | paste -sd, -
}

# The mirrors the script chooses from: mode is "list" (the sources name the mirror
# list), "plain" (the sources name an archive host directly) or empty (no
# alternative, such as ports.ubuntu.com). hosts holds the candidates; speed holds
# each one's last measured rate in bytes per second; tried is 1 for a host whose
# probe failed and 2 for one an attempt has used.
mode=''
original=''
hosts=()
declare -A speed=() tried=()

add_host() {
  if [[ $1 == *.ubuntu.com ]] && [[ " ${hosts[*]} " != *" $1 "* ]]; then hosts+=("$1"); fi
}

init_mirrors() {
  local host file
  if uses_mirror_list; then
    mode=list
    cp "$mirror_list" "$originals/apt-mirrors.txt"
    while IFS= read -r host; do add_host "$host"; done < <(mirrors | tr ',' '\n')
  else
    original=$(mirrors | tr ',' '\n' | grep -E '(^|\.)archive\.ubuntu\.com$' | head -n 1 || true)
    if [ -z "$original" ]; then return 0; fi
    mode=plain
    while IFS= read -r file; do
      if grep -qF "://$original/" "$file"; then mkdir -p "$originals/files$(dirname "$file")"; cp "$file" "$originals/files$file"; fi
    done < <(source_files)
    add_host "$original"
  fi
  add_host azure.archive.ubuntu.com
  add_host archive.ubuntu.com
}

# The candidates in the order the next attempt uses: untried mirrors before tried
# ones, each group fastest first. Among equally fast (rate 0) tried ones a failed
# probe comes before a stalled attempt; remaining ties keep the runner's order.
ordered_hosts() {
  local i host state
  for i in "${!hosts[@]}"; do
    host=${hosts[i]}
    state=${tried[$host]:-0}
    printf '%d %015d %d %03d %s\n' "$((state > 0))" $((999999999999999 - ${speed[$host]:-0})) "$state" "$i" "$host"
  done | sort | awk '{print $5}'
}

untried_left() {
  local host count=0
  for host in "${hosts[@]}"; do if [ -z "${tried[$host]:-}" ]; then count=$((count + 1)); fi; done
  [ "$count" -gt 1 ]
}

# Writes the ordered candidates to the mirror list (with explicit priorities, which
# apt otherwise draws at random), or puts the first one in the plain sources.
apply_order() {
  local order host entry uri tags i=0 file
  mapfile -t order < <(ordered_hosts)
  if [ "$mode" = list ]; then
    for host in "${order[@]}"; do
      entry=$(awk -v h="$host" '/^[[:space:]]*[a-z+]+:\/\// { u = $1; sub(/^[a-z+]+:\/\//, "", u); sub(/\/.*/, "", u); if (u == h) { print; exit } }' "$originals/apt-mirrors.txt")
      entry=${entry:-http://$host/ubuntu/}
      uri=${entry%%[[:space:]]*}
      tags=$(tr -s ' \t' '\n' <<<"${entry#"$uri"}" | grep -v -e '^$' -e '^priority:' | paste -sd'\t' - || true)
      i=$((i + 1))
      printf '%s\tpriority:%d%s\n' "$uri" "$i" "${tags:+$tab$tags}"
    done | sudo tee "$mirror_list.runic" >/dev/null
    sudo mv "$mirror_list.runic" "$mirror_list"
  elif [ "$mode" = plain ]; then
    while IFS= read -r file; do
      sed "s#://${original//./\\.}/#://${order[0]}/#g" "$file" | sudo tee "${file#"$originals/files"}" >/dev/null
    done < <(find "$originals/files" -type f)
  fi
}

# Measures each candidate with a ranged GET of the largest archive to fetch, all at
# once, and records the rates. A host that fails, or delivers nothing in time, gets 0
# and goes last.
probe_mirrors() {
  local path size host code bytes seconds table=() pids=()
  read -r path size < <(awk -F"'" 'NF >= 3 { split($3, f, " "); u = $2; if (sub(/^.*\/pool\//, "pool/", u) && f[2] + 0 > best) { best = f[2] + 0; p = u } } END { if (p != "") print p, best }' "$originals/uris") || true
  if [ -z "${path:-}" ]; then
    echo "Mirror probe skipped: apt listed no archive under pool/ to sample"
    return 0
  fi
  for host in "${hosts[@]}"; do
    curl --silent --output /dev/null --range "0-$((probe_bytes - 1))" --max-time "$probe_seconds" \
      --write-out '%{http_code} %{size_download} %{time_total}' "http://$host/ubuntu/$path" >"$originals/probe-$host" 2>/dev/null &
    pids+=("$!")
  done
  wait "${pids[@]}" 2>/dev/null || true
  for host in "${hosts[@]}"; do
    read -r code bytes seconds <"$originals/probe-$host" || true
    if [[ ${code:-} =~ ^20[06]$ ]] && [ "${bytes:-0}" -gt 0 ]; then
      speed[$host]=$(awk -v b="$bytes" -v s="$seconds" 'BEGIN { if (s < 0.001) s = 0.001; printf "%d\n", b / s }')
      table+=("$host $(rate "${speed[$host]}" 1)")
    else
      # Counted as tried, so it goes last and does not hold the rate floor open.
      speed[$host]=0 tried[$host]=1
      table+=("$host failed")
    fi
    code='' bytes='' seconds=''
  done
  echo "Mirror probe ($(megabytes "$((size < probe_bytes ? size : probe_bytes))") of ${path##*/}): $(printf '%s, ' "${table[@]}" | sed 's/, $//')"
}

# Bytes of package archives on disk: finished .deb files and apt's partial downloads,
# which belong to root.
archive_bytes() {
  sudo find "$archives" -maxdepth 2 -type f \( -name '*.deb' -o -path '*/partial/*' \) -printf '%s\n' 2>/dev/null \
    | awk '{ s += $1 } END { printf "%d\n", s }'
}

# Bytes the packages still need: the archives apt would fetch, less what their
# partial downloads already hold. Empty when apt cannot tell.
needed_bytes() {
  local listed partial
  : >"$originals/uris"
  "$dir/retry.sh" 1 120 "${apt[@]}" install --yes --download-only --print-uris -qq ${options[@]+"${options[@]}"} "${packages[@]}" \
    >"$originals/uris" 2>/dev/null || return 0
  listed=$(awk -F"'" 'NF >= 3 { split($3, f, " "); s += f[2] } END { printf "%d\n", s }' "$originals/uris")
  partial=$(sudo find "$archives/partial" -maxdepth 1 -type f -printf '%s\n' 2>/dev/null | awk '{ s += $1 } END { printf "%d\n", s }')
  echo $((listed > partial ? listed - partial : 0))
}

# Whether process $1 still runs. kill -0 cannot tell for root's sudo, and an unreaped
# child stays a zombie.
running() {
  local state
  state=$(ps -o stat= -p "$1" 2>/dev/null) && [[ $state != Z* ]]
}

megabytes() { awk -v b="$1" 'BEGIN { printf "%.1f MB", b / 1e6 }'; }
rate() { awk -v b="$1" -v s="$2" 'BEGIN { if (s < 1) s = 1; printf "%.0f kB/s", b / s / 1e3 }'; }

# Stops the download in progress: TERM first, KILL after 15 seconds.
stop_download() {
  local pid=$1 i
  sudo pkill -TERM -x apt-get || true
  for ((i = 0; i < 15; i++)); do
    if ! running "$pid"; then return 0; fi
    sleep 1
  done
  sudo pkill -KILL -x apt-get || true
}

# Runs apt-get --download-only while it makes progress, until the download budget
# ends at second $1; with $2 = true, also while its average rate stays above the
# floor. Sets fetched (bytes) and stopped ("", stalled, slow or budget).
download() {
  local deadline=$1 floor=$2 pid status=0 start bytes last changed reported started=$SECONDS
  start=$(archive_bytes)
  last=$start changed=$SECONDS reported=$SECONDS fetched=0 stopped=''
  "${apt[@]}" install --yes --download-only ${options[@]+"${options[@]}"} "${packages[@]}" > >(tee -a "$log") 2>&1 &
  pid=$!
  while running "$pid"; do
    sleep "$poll"
    bytes=$(archive_bytes)
    if [ "$bytes" -gt "$last" ]; then last=$bytes changed=$SECONDS; fi
    if ((SECONDS - reported >= 60)); then
      echo "Downloading: $(megabytes $((last - start))) in $((SECONDS - begun))s ($(rate $((last - start)) $((SECONDS - begun))))"
      reported=$SECONDS
    fi
    if ! running "$pid"; then break; fi
    if ((SECONDS - changed >= stall)); then
      stopped=stalled
      echo "::warning::No download progress for ${stall}s; stopping apt-get"
      stop_download "$pid"
      break
    fi
    if [ "$floor" = true ] && ((SECONDS - started >= rate_window && last - start < rate_floor * (SECONDS - started))); then
      stopped=slow
      echo "::warning::$(rate $((last - start)) $((SECONDS - started))) after $((SECONDS - started))s is below the floor of $(rate "$rate_floor" 1); stopping apt-get to try the next mirror"
      stop_download "$pid"
      break
    fi
    if ((SECONDS >= deadline)); then
      stopped=budget
      echo "::warning::The download budget of ${budget}s is used up; stopping apt-get"
      stop_download "$pid"
      break
    fi
  done
  wait "$pid" || status=$?
  bytes=$(archive_bytes)
  fetched=$((bytes > start ? bytes - start : 0))
  if [ -n "$stopped" ]; then return 1; fi
  return "$status"
}

# What the failed attempt was doing: why it stopped and the download in progress,
# then apt's errors (apt-get update only warns about an index it could not fetch).
failure_summary() {
  local errors
  errors=$(grep -E '^(Err:|E: |W: Failed to fetch)' "$log" | head -n 3 | paste -sd';' - || true)
  case "$stopped" in
    stalled) echo "no progress for ${stall}s while fetching $(grep -E '^Get:' "$log" | tail -n 1 | sed -E 's/^Get:[0-9]+ //')${errors:+; $errors}" ;;
    slow) echo "below the rate floor while fetching $(grep -E '^Get:' "$log" | tail -n 1 | sed -E 's/^Get:[0-9]+ //')${errors:+; $errors}" ;;
    budget) echo "download budget used up while fetching $(grep -E '^Get:' "$log" | tail -n 1 | sed -E 's/^Get:[0-9]+ //')${errors:+; $errors}" ;;
    *) echo "${errors:-apt-get failed without an error line}" ;;
  esac
}

# The archive names come from an empty directory, so archives restored or downloaded
# earlier in the job (another call of the action) do not change the hash.
if [ "$resolve" = true ]; then
  if ! "$dir/retry.sh" 1 120 "${apt[@]}" update >&2; then
    echo "::warning::apt-get update failed; the package versions are unresolved" >&2
    exit 0
  fi
  mkdir -p "$originals/empty/partial"
  if ! "$dir/retry.sh" 1 120 sudo DEBIAN_FRONTEND=noninteractive apt-get -o "Dir::Cache::Archives=$originals/empty/" \
    install --yes --download-only --print-uris -qq ${options[@]+"${options[@]}"} "${packages[@]}" >"$originals/uris"; then
    echo "::warning::apt-get could not list the archives; the package versions are unresolved" >&2
    exit 0
  fi
  names=$(awk -F"'" 'NF >= 3 { split($3, f, " "); print f[1] }' "$originals/uris" | sort -u)
  hash=$(printf '%s\n' "$names" | sha256sum | cut -c1-16)
  echo "Package versions $hash: $(grep -c . <<<"$names" || true) archives, $(megabytes "$(awk -F"'" 'NF >= 3 { split($3, f, " "); s += f[2] } END { printf "%d\n", s }' "$originals/uris")") not on the image" >&2
  echo "$hash"
  exit 0
fi

echo "Ubuntu mirrors: $(mirrors); package archives: $archives ($(find "$archives" -maxdepth 1 -name '*.deb' | wc -l) cached)"
init_mirrors
if [ -n "$mode" ]; then
  echo "Download limits: stopped after ${stall}s without progress or below $(rate "$rate_floor" 1) after ${rate_window}s while another mirror is left, ${budget}s for all attempts; candidates: ${hosts[*]}"
else
  echo "Download limits: stopped after ${stall}s without progress, ${budget}s for all attempts; no alternative mirror"
fi
failures=()
downloaded=false
deadline=$((SECONDS + budget))
total=0
probed=false
for ((attempt = 1; attempt <= max_attempts; attempt++)); do
  begun=$SECONDS
  stopped='' fetched=0 need=''
  : >"$log"
  # The action's resolve step has just updated the indexes for the first attempt.
  if { [ "$attempt" = 1 ] && [ "${APT_UPDATED:-}" = true ]; } || "$dir/retry.sh" 1 120 "${apt[@]}" update 2>&1 | tee -a "$log"; then
    need=$(needed_bytes)
    # Once, before the first download: order the mirrors by a measured sample.
    if [ "$probed" = false ] && [ -n "$mode" ] && [ "${need:-0}" -gt 0 ] && [ "${#hosts[@]}" -gt 1 ]; then
      probed=true
      first=$(mirrors | cut -d, -f1)
      probe_mirrors
      apply_order
      # Plain sources index by host, so a new host needs its indexes.
      if [ "$mode" = plain ] && [ "$(mirrors | cut -d, -f1)" != "$first" ]; then
        "$dir/retry.sh" 1 120 "${apt[@]}" update 2>&1 | tee -a "$log" || true
      fi
    fi
  fi
  used=$(mirrors)
  current=${used%%,*}
  echo "Attempt $attempt of up to $max_attempts via $used: ${need:+$(megabytes "$need") to fetch, }$((deadline - SECONDS))s of the download budget left"
  floor=false
  if [ -n "$mode" ] && untried_left; then floor=true; fi
  if download "$deadline" "$floor"; then
    downloaded=true
    total=$((total + fetched))
    echo "Downloaded $(megabytes "$fetched") in $((SECONDS - begun))s ($(rate "$fetched" $((SECONDS - begun)))) via $used"
    break
  fi
  total=$((total + fetched))
  left=
  if [ -n "$need" ]; then left=$((need > fetched ? need - fetched : 0)); fi
  summary="fetched $(megabytes "$fetched") in $((SECONDS - begun))s ($(rate "$fetched" $((SECONDS - begun))))${left:+, $(megabytes "$left") still to fetch}"
  failures+=("attempt $attempt via $used: $summary; $(failure_summary)")
  echo "::warning::Downloading ${packages[*]} failed on attempt $attempt via $used: $summary"
  "$dir/retry.sh" 1 120 bash -c "$cleanup" || true
  if ((deadline - SECONDS < stall)); then break; fi
  if [ -n "$mode" ] && [ -n "$current" ]; then
    tried[$current]=2
    # A slow mirror keeps its measured rate; a stalled or failed one goes last.
    if [ "$stopped" = slow ]; then speed[$current]=$((fetched / (SECONDS - begun > 0 ? SECONDS - begun : 1))); else speed[$current]=0; fi
    apply_order
    echo "Next attempt prefers $(mirrors | cut -d, -f1): $(mirrors)"
  fi
done
if [ "$downloaded" != true ]; then
  echo "::error title=Ubuntu packages could not be downloaded::${packages[*]} could not be downloaded within ${budget}s ($(megabytes "$total") fetched). $(printf '%s. ' "${failures[@]}")"
  exit 1
fi

export RETRY_CLEANUP="$cleanup"
if ! "$dir/retry.sh" 2 300 "${apt[@]}" install --yes --no-download ${options[@]+"${options[@]}"} "${packages[@]}"; then
  echo "::error title=Ubuntu packages could not be installed::${packages[*]} were downloaded from $(mirrors) but could not be installed; see the apt-get output above."
  exit 1
fi
# A cached archive directory keeps only versions the current indexes still offer,
# so superseded packages do not accumulate in the cache.
if [ -n "${APT_ARCHIVES:-}" ]; then "${apt[@]}" autoclean; fi
echo "Installed ${packages[*]} in ${SECONDS}s"
