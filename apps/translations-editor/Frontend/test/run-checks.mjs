// The frontend checks of `verify:checks`: `check` first, then every test/verify-*.mjs and test/verify-*.ts except the
// exclusions below, each in its own Bun process, one after another in file-name order. It stops at the first failure.
// A test that needs Bun runtime flags declares them in a leading comment line: `// run-checks-bun-flags: --expose-gc`.
import { spawnSync } from "node:child_process";
import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

export const frontendDirectory = fileURLToPath(new URL("..", import.meta.url));
const testDirectory = new URL("./", import.meta.url);

// Tests that are not part of `verify:checks`, with where they run instead.
export const excluded = new Map([
  ["verify-production.mjs", "verify:built and verify run it against the production build"],
]);

const flagsDirective = /^\/\/ run-checks-bun-flags:(.*)$/;

/** The Bun runtime flags a test declares in its leading comment lines. */
export function bunFlags(file) {
  const flags = [];
  for (const line of readFileSync(new URL(file, testDirectory), "utf8").split("\n")) {
    if (!line.startsWith("//")) break;
    const match = flagsDirective.exec(line.trim());
    if (match) flags.push(...match[1].trim().split(/\s+/).filter(Boolean));
  }
  return flags;
}

/** Every test file the runner runs, in order, with its Bun runtime flags. */
export function discoverChecks() {
  return readdirSync(testDirectory)
    .filter(file => /^verify-.+\.(?:mjs|ts)$/.test(file) && !excluded.has(file))
    .sort()
    .map(file => ({ file, flags: bunFlags(file) }));
}

function run(label, args) {
  console.log(`\n==> ${label}`);
  const result = spawnSync(process.execPath, args, { cwd: frontendDirectory, stdio: "inherit" });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    console.error(`FAIL: ${label} (${result.signal ? `signal ${result.signal}` : `exit ${result.status}`})`);
    process.exit(result.status || 1);
  }
}

if (import.meta.main) {
  const checks = discoverChecks();
  run("check", ["run", "--bun", "check"]);
  for (const { file, flags } of checks) run(`test/${file}`, [...flags, `test/${file}`]);
  console.log(`\nPASS: check and ${checks.length} frontend verification tests.`);
}
