import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import { bunFlags, discoverChecks, excluded } from "./run-checks.mjs";

const [packageJson, fullVerification, viteConfig, rootPackageJson, runner] = await Promise.all([
  readFile(new URL("../package.json", import.meta.url), "utf8"),
  readFile(new URL("../../../../.github/workflows/ci.yml", import.meta.url), "utf8"),
  readFile(new URL("../vite.config.ts", import.meta.url), "utf8"),
  readFile(new URL("../../../../package.json", import.meta.url), "utf8"),
  readFile(new URL("../../../../eng/run.mjs", import.meta.url), "utf8"),
]);
const frontend = JSON.parse(packageJson);
const expandScript = (name, visited = new Set()) => {
  if (visited.has(name)) return "";
  visited.add(name);
  const command = frontend.scripts?.[name];
  if (typeof command !== "string") return "";
  const nested = [...command.matchAll(/\bbun run (?:--bun )?([\w:-]+)/g)]
    .map(([, dependency]) => expandScript(dependency, visited));
  return [command, ...nested].join("\n");
};
const command = frontend.scripts?.verify;
const expandedCommand = expandScript("verify");

assert.equal(typeof command, "string", "Frontend has no verify script.");
assert.equal(frontend.packageManager, JSON.parse(rootPackageJson).packageManager, "Frontend must use the repository-pinned Bun release.");
// `verify:checks` is the runner, which runs every test/verify-* file except its declared exclusions; each exclusion
// runs in the full verification instead.
assert.equal(frontend.scripts?.["verify:checks"], "bun test/run-checks.mjs", "verify:checks must run the discovered checks.");
assert.match(expandedCommand, /\bbun run --bun verify:checks\b/, "Frontend verification bypasses verify:checks.");
const checks = discoverChecks().map(check => check.file);
const testFiles = (await readdir(new URL("./", import.meta.url))).filter(file => file.startsWith("verify-"));
for (const file of testFiles) {
  if (excluded.has(file)) {
    assert.ok(!checks.includes(file), `The check runner runs the excluded test/${file}.`);
    assert.match(expandedCommand, new RegExp(`\\bbun test/${file.replaceAll(".", "\\.")}\\b`), `test/${file} is excluded from the runner and runs nowhere else.`);
  } else {
    assert.ok(checks.includes(file), `The check runner omits test/${file}; name it verify-<name>.mjs or verify-<name>.ts.`);
  }
}
for (const file of excluded.keys()) assert.ok(testFiles.includes(file), `The runner excludes test/${file}, which does not exist.`);
assert.ok(checks.includes(new URL(import.meta.url).pathname.split("/").pop()), "The check runner skips this wiring check.");
// A test that needs the garbage collector declares the flag that exposes it.
for (const file of checks) {
  if (/\bglobalThis\.gc\(\)/.test(await readFile(new URL(file, import.meta.url), "utf8")))
    assert.ok(bunFlags(file).includes("--expose-gc"), `test/${file} calls the garbage collector without a run-checks-bun-flags: --expose-gc line.`);
}
const verifyJob = Bun.YAML.parse(fullVerification).jobs["build-and-test"];
assert.ok(verifyJob.steps.some(step => step.run === "bun eng/run.mjs test"),
  "CI must invoke the standalone repository verification runner.");
assert.match(runner, /run\("bun", \["run", "--bun", "verify:built"\]/,
  "The repository runner bypasses complete frontend verification.");
assert.match(runner, /RUNIC_TRANSLATIONS_MANIFEST:.*resolve\(editorDirectory,[^\n]*web-module-manifest-v3\.json/,
  "The repository runner must supply the generated translation manifest.");
assert.match(runner, /build\(\);\s*verifyEditorFrontend\(\);/,
  "Frontend verification must consume the current build outputs.");
assert.match(runner, /"--smoke-test"/,
  "Repository verification omits the compiler-backed Editor smoke journey.");
assert.doesNotMatch(viteConfig, /desktop:\s*true/,
  "The Vite plugin must not duplicate SvelteKit Desktop output ownership.");
assert.match(viteConfig, /runicAdapter\(\{[^}]*mode:\s*["']spa["'][^}]*desktop:\s*false[^}]*\}\)/s,
  "The Runic SvelteKit adapter must emit the Views host SPA output.");
assert.match(viteConfig, /router:\s*\{\s*type:\s*["']hash["']\s*\}/,
  "The Desktop SPA must retain hash routing under generated surface paths.");

console.log("PASS: full editor verification delegates to the complete frontend verification suite.");
