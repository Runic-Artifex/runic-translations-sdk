import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

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
for (const test of ["verify-ui-catalog.mjs", "verify-keyboard-a11y.mjs", "verify-command-palette.mjs", "verify-w03-simulation.mjs", "verify-local-state.mjs"]) {
  assert.match(expandedCommand, new RegExp(test.replace(".", "\\.")), `Frontend verification omits ${test}.`);
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
