// eng/ci/apt-install.sh against a stubbed sudo, apt-get and curl (CONTRIBUTING.md, "Ubuntu packages in CI"): the probe
// orders the mirrors by measured rate, an attempt below the rate floor or without progress moves to the next mirror, a
// slow last mirror that keeps delivering finishes, and the download budget bounds the whole phase. Nothing here runs
// the real apt-get, sudo or curl.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { root } from '../run.mjs';

const stubs = {
  // The probe: answers a ranged GET at the host's rate in FAKE_PROBES ("host=bytes-per-second" or "host=fail"; an
  // unlisted host fails), stopping at --max-time as curl does.
  curl: `#!/usr/bin/env bash
range= limit= url=
while [ $# -gt 0 ]; do
  case $1 in
    --range) range=$2; shift 2 ;;
    --max-time) limit=$2; shift 2 ;;
    --output|--write-out) shift 2 ;;
    -*) shift ;;
    *) url=$1; shift ;;
  esac
done
host=\${url#*://}; host=\${host%%/*}
echo "$host" >>"$FAKE_STATE/probes"
rate=fail
for entry in $FAKE_PROBES; do if [ "\${entry%%=*}" = "$host" ]; then rate=\${entry#*=}; fi; done
if [ "$rate" = fail ]; then printf '000 0 0.000000'; exit 7; fi
awk -v size=$(( \${range#*-} - \${range%-*} + 1 )) -v rate="$rate" -v limit="$limit" 'BEGIN {
  t = size / rate; if (t > limit) { printf "206 %d %f", rate * limit, limit; exit 28 } printf "206 %d %f", size, t }'
`,
  sudo: '#!/usr/bin/env bash\nexec env "$@"\n',
  dpkg: '#!/usr/bin/env bash\nexit 0\n',
  // Signals only the stubbed apt-get of this test.
  pkill: `#!/usr/bin/env bash
signal=\${1#-}
if [ "$3" = apt-get ] && [ -f "$FAKE_STATE/apt.pid" ]; then kill "-$signal" "$(cat "$FAKE_STATE/apt.pid")" 2>/dev/null; fi
exit 0
`,
  // Serves one archive (pkg_<FAKE_VERSION>_amd64.deb, stored as pkg.deb) of FAKE_SIZE bytes from the mirror apt would try first, at that mirror's rate in FAKE_RATES
  // ("host=bytes-per-second" or "host=stall"), resuming a partial download.
  'apt-get': `#!/usr/bin/env bash
archives=
args=()
while [ $# -gt 0 ]; do
  case $1 in
    -o) if [[ $2 == Dir::Cache::Archives=* ]]; then archives=\${2#*=}; fi; shift 2 ;;
    *) args+=("$1"); shift ;;
  esac
done
archives=\${archives%/}
host() {
  if grep -qsF mirror+file: "$APT_ETC"/sources.list.d/*; then
    awk '{ p = 1e9; for (i = 2; i <= NF; i++) if ($i ~ /^priority:/) p = substr($i, 10) + 0; printf "%012d %06d %s\\n", p, NR, $1 }' "$APT_ETC/apt-mirrors.txt" | sort | head -n 1 | awk '{print $3}'
  else
    grep -hoE 'https?://[^/[:space:]]*ubuntu\\.com' "$APT_ETC"/sources.list.d/* | head -n 1
  fi | sed -E 's#^[a-z+]+://##; s#/.*##'
}
mirror=$(host)
echo "$mirror" >>"$FAKE_STATE/mirrors"
echo "\${args[*]}" >>"$FAKE_STATE/commands"
case " \${args[*]} " in
  *" update "*) if [ -n "\${FAKE_UPDATE_FAILS:-}" ]; then echo "E: Failed to fetch"; exit 100; fi; echo "Hit:1 http://$mirror/ubuntu noble InRelease" ;;
  *" --print-uris "*) if [ ! -f "$archives/pkg.deb" ]; then echo "'http://$mirror/ubuntu/pool/main/p/pkg.deb' pkg_\${FAKE_VERSION:-1.0}_amd64.deb $FAKE_SIZE SHA256:00"; fi ;;
  *" --download-only "*)
    if [ -f "$archives/pkg.deb" ]; then exit 0; fi
    echo $$ >"$FAKE_STATE/apt.pid"
    mkdir -p "$archives/partial"
    echo "Get:1 http://$mirror/ubuntu noble/main amd64 pkg amd64 1.0 [$FAKE_SIZE B]"
    rate=
    for entry in $FAKE_RATES; do if [ "\${entry%%=*}" = "$mirror" ]; then rate=\${entry#*=}; fi; done
    if [ "$rate" = stall ]; then head -c 1000 /dev/zero >>"$archives/partial/pkg.deb"; exec sleep 1000; fi
    while [ "$(stat -c %s "$archives/partial/pkg.deb" 2>/dev/null || echo 0)" -lt "$FAKE_SIZE" ]; do
      have=$(stat -c %s "$archives/partial/pkg.deb" 2>/dev/null || echo 0)
      head -c $((rate / 4 < FAKE_SIZE - have ? rate / 4 : FAKE_SIZE - have)) /dev/zero >>"$archives/partial/pkg.deb"
      sleep 0.25 & wait $!
    done
    mv "$archives/partial/pkg.deb" "$archives/pkg.deb"
    ;;
esac
exit 0
`,
};

const run = (setup, env, args = ['pkg']) => {
  const directory = mkdtempSync(resolve(tmpdir(), 'runic-apt-test-'));
  try {
    const bin = resolve(directory, 'bin');
    const etc = resolve(directory, 'etc/apt');
    const state = resolve(directory, 'state');
    for (const path of [bin, resolve(etc, 'apt.conf.d'), resolve(etc, 'sources.list.d'), state]) mkdirSync(path, { recursive: true });
    for (const [name, content] of Object.entries(stubs)) {
      writeFileSync(resolve(bin, name), content);
      chmodSync(resolve(bin, name), 0o755);
    }
    setup(etc);
    const result = spawnSync('bash', [resolve(root, 'eng/ci/apt-install.sh'), ...args], {
      encoding: 'utf8',
      timeout: 120000,
      env: {
        ...process.env, PATH: `${bin}:${process.env.PATH}`, APT_ETC: etc, APT_ARCHIVES: resolve(directory, 'archives'),
        FAKE_STATE: state, APT_STALL_SECONDS: '3', APT_POLL_SECONDS: '1', ...env,
      },
    });
    const read = path => { try { return readFileSync(path, 'utf8'); } catch { return ''; } };
    const archive = resolve(directory, 'archives/pkg.deb');
    return {
      status: result.status, output: result.stdout + result.stderr,
      list: read(resolve(etc, 'apt-mirrors.txt')), sources: read(resolve(etc, 'sources.list.d/ubuntu.sources')),
      stdout: result.stdout,
      mirrors: read(resolve(state, 'mirrors')).trim().split('\n'),
      commands: read(resolve(state, 'commands')).trim().split('\n').filter(Boolean),
      probes: read(resolve(state, 'probes')).trim().split('\n').filter(Boolean),
      size: (() => { try { return statSync(archive).size; } catch { return 0; } })(),
    };
  } finally { rmSync(directory, { recursive: true, force: true }); }
};

// The x64 runners' layout: sources naming the mirror list, which lists an EC2 mirror, then the official hosts.
const mirrorList = etc => {
  writeFileSync(resolve(etc, 'apt-mirrors.txt'),
    'http://us-east-2.ec2.archive.ubuntu.com/ubuntu/\tpriority:1\nhttp://archive.ubuntu.com/ubuntu/\tpriority:2\nhttp://security.ubuntu.com/ubuntu/\tpriority:3\n');
  writeFileSync(resolve(etc, 'sources.list.d/ubuntu.sources'), `Types: deb\nURIs: mirror+file:${etc}/apt-mirrors.txt\nSuites: noble\n`);
};
const plainSources = etc => writeFileSync(resolve(etc, 'sources.list.d/ubuntu.sources'),
  'Types: deb\nURIs: http://us-east-2.ec2.archive.ubuntu.com/ubuntu/\nSuites: noble noble-updates\n\nTypes: deb\nURIs: http://security.ubuntu.com/ubuntu/\nSuites: noble-security\n');

const linux = { skip: process.platform === 'win32' };
const us = 'us-east-2.ec2.archive.ubuntu.com';
const azure = 'azure.archive.ubuntu.com';

// runic-sdk#1129 (here #258): the EC2 mirror delivered about 100 kB/s while the Azure one delivered 3-8 MB/s; the probe picks the fast one.
test('apt-install probes the mirrors and downloads from the fastest', linux, () => {
  const result = run(mirrorList, {
    FAKE_SIZE: '300000', FAKE_PROBES: `${us}=100000 ${azure}=5000000 archive.ubuntu.com=3000000`,
    FAKE_RATES: `${us}=20000 ${azure}=400000 archive.ubuntu.com=400000`, APT_DOWNLOAD_SECONDS: '60',
  });
  assert.equal(result.status, 0, result.output);
  assert.equal(result.size, 300000);
  // One probe per candidate: the runner's three mirrors and the added Azure mirror.
  assert.deepEqual(result.probes.sort(), ['archive.ubuntu.com', azure, 'security.ubuntu.com', us]);
  assert.match(result.output, /Mirror probe \(0\.3 MB of pkg\.deb\): us-east-2\.ec2\.archive\.ubuntu\.com 100 kB\/s, archive\.ubuntu\.com 3000 kB\/s, security\.ubuntu\.com failed, azure\.archive\.ubuntu\.com 5000 kB\/s/);
  assert.equal(result.list, `http://${azure}/ubuntu/\tpriority:1\nhttp://archive.ubuntu.com/ubuntu/\tpriority:2\nhttp://${us}/ubuntu/\tpriority:3\nhttp://security.ubuntu.com/ubuntu/\tpriority:4\n`);
  assert.match(result.output, /Attempt 1 of up to 5 via azure\.archive\.ubuntu\.com,archive\.ubuntu\.com,us-east-2\.ec2\.archive\.ubuntu\.com,security\.ubuntu\.com: 0\.3 MB to fetch/);
  assert.match(result.output, /Downloaded 0\.3 MB in \d+s \(\d+ kB\/s\) via azure/);
  assert.doesNotMatch(result.output, /::warning::/);
});

test('apt-install moves on from a mirror below the rate floor and resumes on the next', linux, () => {
  // The EC2 mirror answers the probe fast but serves the download at 20 kB/s, below the 100 kB/s floor.
  const result = run(mirrorList, {
    FAKE_SIZE: '300000', FAKE_PROBES: `${us}=5000000 ${azure}=1000000`,
    FAKE_RATES: `${us}=20000 ${azure}=400000`, APT_DOWNLOAD_SECONDS: '60', APT_RATE_FLOOR: '100000', APT_RATE_WINDOW: '2',
  });
  assert.equal(result.status, 0, result.output);
  assert.equal(result.size, 300000);
  assert.match(result.output, /Attempt 1 of up to 5 via us-east-2/);
  assert.match(result.output, /::warning::\d+ kB\/s after \d+s is below the floor of 100 kB\/s; stopping apt-get to try the next mirror/);
  assert.match(result.output, /failed on attempt 1 via us-east-2[^:]*: fetched 0\.\d MB in \d+s \(\d+ kB\/s\), 0\.\d MB still to fetch/);
  // The probe-failed official hosts stay behind the slow but working EC2 mirror.
  assert.match(result.output, /Next attempt prefers azure\.archive\.ubuntu\.com: azure\.archive\.ubuntu\.com,us-east-2\.ec2\.archive\.ubuntu\.com,archive\.ubuntu\.com,security\.ubuntu\.com/);
  assert.match(result.output, /Downloaded 0\.\d MB in \d+s \(\d+ kB\/s\) via azure/);
  assert.deepEqual([...new Set(result.mirrors)], [us, azure]);
});

test('apt-install keeps a slow last mirror that makes progress beyond the stall window', linux, () => {
  // No probe answers, so no mirror is known to be faster and the rate floor stays off. 150 kB at 25 kB/s takes 6
  // seconds, twice the stall window.
  const result = run(mirrorList, { FAKE_SIZE: '150000', FAKE_RATES: `${us}=25000`, APT_DOWNLOAD_SECONDS: '60', APT_RATE_WINDOW: '2' });
  assert.equal(result.status, 0, result.output);
  assert.equal(result.size, 150000);
  assert.match(result.output, /Mirror probe \(0\.1 MB of pkg\.deb\): (\S+ failed(, )?){4}/);
  assert.match(result.output, /Attempt 1 of up to 5 via us-east-2\.ec2\.archive\.ubuntu\.com,archive\.ubuntu\.com,security\.ubuntu\.com,azure\.archive\.ubuntu\.com: 0\.1 MB to fetch/);
  assert.match(result.output, /Downloaded 0\.1 MB in \d+s \(\d+ kB\/s\) via us-east-2/);
  assert.doesNotMatch(result.output, /::warning::/);
});

test('apt-install stops a stalled mirror and the next attempt prefers another official mirror', linux, () => {
  const result = run(mirrorList, {
    FAKE_SIZE: '100000', FAKE_RATES: `${us}=stall archive.ubuntu.com=200000`, APT_DOWNLOAD_SECONDS: '60',
  });
  assert.equal(result.status, 0, result.output);
  assert.equal(result.size, 100000);
  assert.match(result.output, /::warning::No download progress for 3s; stopping apt-get/);
  assert.match(result.output, /failed on attempt 1 via us-east-2[^:]*: fetched 0\.0 MB in \d+s \(\d+ kB\/s\), 0\.1 MB still to fetch/);
  assert.match(result.output, /Next attempt prefers archive\.ubuntu\.com: archive\.ubuntu\.com,security\.ubuntu\.com,azure\.archive\.ubuntu\.com,us-east-2\.ec2\.archive\.ubuntu\.com/);
  assert.equal(result.list, `http://archive.ubuntu.com/ubuntu/\tpriority:1\nhttp://security.ubuntu.com/ubuntu/\tpriority:2\nhttp://${azure}/ubuntu/\tpriority:3\nhttp://${us}/ubuntu/\tpriority:4\n`);
  // The second attempt resumed the 1000 bytes the stalled one left in partial/.
  assert.match(result.output, /Attempt 2 of up to 5 via archive\.ubuntu\.com[^:]*: 0\.1 MB to fetch/);
  assert.match(result.output, /Downloaded 0\.1 MB in \d+s \(\d+ kB\/s\) via archive\.ubuntu\.com/);
});

test('apt-install moves the archive host of plain sources and keeps the security pocket', linux, () => {
  const result = run(plainSources, {
    FAKE_SIZE: '50000', FAKE_PROBES: `${us}=3000000 ${azure}=2000000 archive.ubuntu.com=1000000`,
    FAKE_RATES: `${us}=stall ${azure}=stall archive.ubuntu.com=200000`, APT_DOWNLOAD_SECONDS: '60',
  });
  assert.equal(result.status, 0, result.output);
  // Only the archive host is a candidate in plain sources, plus the two added ones.
  assert.deepEqual(result.probes.sort(), ['archive.ubuntu.com', azure, us]);
  assert.deepEqual([...new Set(result.mirrors)], [us, azure, 'archive.ubuntu.com']);
  assert.equal(result.sources,
    'Types: deb\nURIs: http://archive.ubuntu.com/ubuntu/\nSuites: noble noble-updates\n\nTypes: deb\nURIs: http://security.ubuntu.com/ubuntu/\nSuites: noble-security\n');
});

test('apt-install skips the probe and the rate floor for ports.ubuntu.com', linux, () => {
  const ports = etc => writeFileSync(resolve(etc, 'sources.list.d/ubuntu.sources'), 'Types: deb\nURIs: http://ports.ubuntu.com/ubuntu-ports/\nSuites: noble\n');
  const result = run(ports, { FAKE_SIZE: '60000', FAKE_RATES: 'ports.ubuntu.com=20000', APT_DOWNLOAD_SECONDS: '60', APT_RATE_WINDOW: '1' });
  assert.equal(result.status, 0, result.output);
  assert.deepEqual(result.probes, []);
  assert.match(result.output, /no alternative mirror/);
  assert.match(result.output, /Downloaded 0\.1 MB in \d+s \(\d+ kB\/s\) via ports\.ubuntu\.com/);
});

test('apt-install fails within the download budget and says how far it got', linux, () => {
  // 10 kB/s never stalls, but 1 MB needs 100 seconds; the budget is 6.
  const result = run(mirrorList, { FAKE_SIZE: '1000000', FAKE_RATES: `${us}=10000`, APT_DOWNLOAD_SECONDS: '6' });
  assert.equal(result.status, 1, result.output);
  assert.equal(result.size, 0);
  assert.match(result.output, /::warning::The download budget of 6s is used up; stopping apt-get/);
  assert.match(result.output, /::error title=Ubuntu packages could not be downloaded::pkg could not be downloaded within 6s \(0\.\d MB fetched\)\. attempt 1 via [^:]*: fetched 0\.\d MB in \d+s \(\d+ kB\/s\), 0\.\d MB still to fetch; download budget used up while fetching/);
  // The budget left less than a stall window, so no second attempt started.
  assert.doesNotMatch(result.output, /Attempt 2/);
});

// runic-sdk#1131: the action keys its archive cache by this hash, so a new package version gets its own entry.
test('apt-install --resolve prints a hash of the archive versions from a cold cache', linux, () => {
  const resolved = env => run(mirrorList, { FAKE_SIZE: '50000', ...env }, ['--resolve', '--no-install-recommends', 'pkg']);
  const first = resolved({});
  assert.equal(first.status, 0, first.output);
  const hash = first.stdout.trim();
  assert.match(hash, /^[0-9a-f]{16}$/);
  assert.equal(hash, execFileSync('sha256sum', { input: 'pkg_1.0_amd64.deb\n', encoding: 'utf8' }).slice(0, 16));
  assert.match(first.output, new RegExp(`Package versions ${hash}: 1 archives, 0\\.1 MB not on the image`));
  // apt-get update, then the listing with the options of the install; nothing is downloaded or probed.
  assert.deepEqual(first.commands.map(command => command.split(' ').filter(word => !word.startsWith('-')).join(' ')),
    ['update', 'install pkg']);
  assert.match(first.commands[1], /--download-only --print-uris -qq --no-install-recommends pkg/);
  assert.deepEqual(first.probes, []);
  assert.equal(first.size, 0);
  assert.equal(resolved({}).stdout.trim(), hash);
  assert.notEqual(resolved({ FAKE_VERSION: '1.1' }).stdout.trim(), hash);
  // Without indexes the versions stay unresolved: no hash, and the job goes on to the install.
  const failed = resolved({ FAKE_UPDATE_FAILS: '1' });
  assert.equal(failed.status, 0, failed.output);
  assert.equal(failed.stdout, '');
  assert.match(failed.output, /::warning::apt-get update failed; the package versions are unresolved/);
});

test('apt-install reuses the resolved indexes for its first attempt', linux, () => {
  const updates = env => run(mirrorList, { FAKE_SIZE: '50000', FAKE_RATES: `${us}=400000`, APT_DOWNLOAD_SECONDS: '60', ...env })
    .commands.filter(command => command === 'update').length;
  assert.equal(updates({}), 1);
  assert.equal(updates({ APT_UPDATED: 'true' }), 0);
});
