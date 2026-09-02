import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const [packageJson, fullVerification, viteConfig, svelteConfig] = await Promise.all([
  readFile(new URL("../package.json", import.meta.url), "utf8"),
  readFile(new URL("../../verify.sh", import.meta.url), "utf8"),
  readFile(new URL("../vite.config.ts", import.meta.url), "utf8"),
  readFile(new URL("../svelte.config.js", import.meta.url), "utf8"),
]);
const frontend = JSON.parse(packageJson);
const command = frontend.scripts?.verify;

assert.equal(typeof command, "string", "Frontend has no verify script.");
assert.equal(frontend.packageManager, "bun@1.4.0", "Frontend must use the authority-pinned Bun release.");
for (const test of ["verify-ui-catalog.mjs", "verify-keyboard-a11y.mjs", "verify-command-palette.mjs", "verify-w03-simulation.mjs", "verify-local-state.mjs"]) {
  assert.match(command, new RegExp(test.replace(".", "\\.")), `Frontend verification omits ${test}.`);
}
assert.match(fullVerification, /RUNIC_TRANSLATIONS_MANIFEST="\$manifest" bun run --cwd "\$frontend" verify/,
  "The repository verifier bypasses the frontend verification source of truth.");
assert.doesNotMatch(viteConfig, /desktop:\s*true/,
  "The Vite plugin must not duplicate SvelteKit Desktop output ownership.");
assert.match(svelteConfig, /runicToolkitAdapter\(\{[^}]*mode:\s*["']spa["'][^}]*desktop:\s*true[^}]*\}\)/s,
  "The Runic SvelteKit adapter must own relocatable Desktop output.");
assert.match(svelteConfig, /router:\s*\{\s*type:\s*["']hash["']\s*\}/,
  "The Desktop SPA must retain hash routing under generated surface paths.");

console.log("PASS: full editor verification delegates to the complete frontend verification suite.");
