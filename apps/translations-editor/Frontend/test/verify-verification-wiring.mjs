import assert from "node:assert/strict";
import { readToolchain } from "../../../../eng/toolchain.mjs";
import { readFile } from "node:fs/promises";

const [packageJson, fullVerification, viteConfig, svelteConfig] = await Promise.all([
  readFile(new URL("../package.json", import.meta.url), "utf8"),
  readFile(new URL("../../../../.github/workflows/ci.yml", import.meta.url), "utf8"),
  readFile(new URL("../vite.config.ts", import.meta.url), "utf8"),
  readFile(new URL("../svelte.config.js", import.meta.url), "utf8"),
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
const toolchain = readToolchain();
assert.equal(frontend.packageManager, `bun@${toolchain.bun}`, "Frontend must use the authority-pinned Bun release.");
for (const test of ["verify-ui-catalog.mjs", "verify-keyboard-a11y.mjs", "verify-command-palette.mjs", "verify-w03-simulation.mjs", "verify-local-state.mjs"]) {
  assert.match(expandedCommand, new RegExp(test.replace(".", "\\.")), `Frontend verification omits ${test}.`);
}
const editorJob = Bun.YAML.parse(fullVerification).jobs.editor;
assert.ok(editorJob.steps.some(step => step["working-directory"] === "apps/translations-editor/Frontend"
  && step.run === "bun run --bun verify:built"),
  "The SDK verifier bypasses the frontend verification source of truth.");
assert.ok(editorJob.steps.some(step => step.env?.RUNIC_TRANSLATIONS_MANIFEST?.includes("web-module-manifest-v1.json")),
  "The SDK verifier must supply the generated translation manifest.");
assert.doesNotMatch(viteConfig, /desktop:\s*true/,
  "The Vite plugin must not duplicate SvelteKit Desktop output ownership.");
assert.match(svelteConfig, /runicToolkitAdapter\(\{[^}]*mode:\s*["']spa["'][^}]*desktop:\s*true[^}]*\}\)/s,
  "The Runic SvelteKit adapter must own relocatable Desktop output.");
assert.match(svelteConfig, /router:\s*\{\s*type:\s*["']hash["']\s*\}/,
  "The Desktop SPA must retain hash routing under generated surface paths.");

console.log("PASS: full editor verification delegates to the complete frontend verification suite.");
