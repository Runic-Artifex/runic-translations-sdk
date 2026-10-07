import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { cpSync, existsSync, readFileSync, writeFileSync } from "node:fs";
import { createServer } from "node:net";
import { join, resolve } from "node:path";
import { root, run } from "./run.mjs";

// Builds the SvelteKit quick start (docs/guides/translations/quickstart-sveltekit.md) from
// packed candidates and the packed compiler, then checks prerendering, SSR in both locales,
// canonical redirects and request isolation against the production adapter-node server.
export async function verifySvelteKitQuickStart({ directory, npmArchives, nugetConfig, version, environment }) {
  const app = join(directory, "sveltekit-quickstart");
  cpSync(resolve(root, "tests/fixtures/translations/sveltekit-quickstart"), app, { recursive: true });
  writeFileSync(join(app, "package.json"), JSON.stringify({
    name: "runic-translations-sveltekit-quickstart", private: true, type: "module",
    scripts: { build: "vite build", check: "svelte-kit sync && svelte-check --tsconfig ./tsconfig.json --fail-on-warnings" },
    imports: { "#lib": "./src/lib/index.js", "#lib/*": "./src/lib/*" },
    devDependencies: {
      ...Object.fromEntries(npmArchives.map(([name, archive]) => [name, `file:${archive}`])),
      "@sveltejs/adapter-node": "6.0.0", "@sveltejs/kit": "3.0.0", "@sveltejs/vite-plugin-svelte": "7.3.1",
      "@types/node": "24.19.1", svelte: "5.57.1", "svelte-check": "4.7.6", typescript: "6.0.3", vite: "8.3.2",
    },
  }, null, 2));
  run("bun", ["install", "--ignore-scripts"], app);
  run("dotnet", ["new", "tool-manifest", "--output", ".config"], app, environment);
  run("dotnet", ["tool", "install", "dotnet-runic-translations", "--version", version, "--configfile", nugetConfig], app, environment);
  run("dotnet", ["tool", "run", "runic-translations", "--", "validate", "--project", "translations"], app, environment);
  run("bun", ["run", "build"], app, environment);
  // The layout passes the generated locale source to typed helpers without a cast.
  run("bun", ["run", "check"], app, environment);

  const prerendered = name => readFileSync(join(app, "build", "prerendered", name), "utf8");
  assertPage(prerendered("index.html"), "en", "Welcome to Runic", "prerendered /");
  assert.match(prerendered("index.html"), /Hello, Ada!/);
  assertPage(prerendered("de.html"), "de", "Willkommen bei Runic", "prerendered /de");
  assert.match(prerendered("de.html"), /Hallo, Ada!/);
  assert.ok(!existsSync(join(app, "build", "prerendered", "about.html")), "/about must stay server-rendered");

  const port = await freePort();
  const server = spawn("node", ["build"], { cwd: app, env: { ...process.env, PORT: String(port), HOST: "127.0.0.1" }, stdio: ["ignore", "pipe", "pipe"] });
  let output = "";
  server.stdout.on("data", chunk => { output += chunk; });
  server.stderr.on("data", chunk => { output += chunk; });
  try {
    const origin = `http://127.0.0.1:${port}`;
    await waitFor(async () => (await fetch(`${origin}/about`)).ok, () => output);
    const page = async path => {
      const response = await fetch(`${origin}${path}`, { redirect: "manual" });
      return { status: response.status, location: response.headers.get("location"), body: await response.text() };
    };
    const english = await page("/about"), german = await page("/de/about");
    assert.equal(english.status, 200); assertPage(english.body, "en", "Welcome to Runic", "SSR /about");
    assert.equal(german.status, 200); assertPage(german.body, "de", "Willkommen bei Runic", "SSR /de/about");
    const redirect = await page("/en/about");
    assert.ok(redirect.status >= 300 && redirect.status < 400 && redirect.location === "/about",
      `/en/about must redirect to /about, got ${redirect.status} ${redirect.location}`);
    const data = await page("/de/about/__data.json");
    assert.equal(data.status, 200); assert.match(data.body, /Willkommen bei Runic/);

    // Interleaved requests: each server load reads its own request locale.
    const paths = Array.from({ length: 40 }, (_, index) => index % 2 ? "/de/about" : "/about");
    const results = await Promise.all(paths.map(page));
    results.forEach((result, index) => index % 2
      ? assertPage(result.body, "de", "Willkommen bei Runic", `concurrent ${paths[index]} #${index}`)
      : assertPage(result.body, "en", "Welcome to Runic", `concurrent ${paths[index]} #${index}`));
    assert.doesNotMatch(output, /error/i, `The production server logged an error:\n${output}`);
  } finally {
    server.kill();
  }
  console.log("SvelteKit quick start passed: prerendering, SSR in both locales, redirects and request isolation.");
}

function assertPage(html, locale, title, label) {
  assert.match(html, new RegExp(`<html lang="${locale}"`), `${label}: expected lang="${locale}"`);
  assert.match(html, new RegExp(`<h1>${title}</h1>`), `${label}: expected the ${locale} title`);
}

function freePort() {
  return new Promise((resolvePort, reject) => {
    const probe = createServer();
    probe.once("error", reject);
    probe.listen(0, "127.0.0.1", () => {
      const { port } = probe.address();
      probe.close(() => resolvePort(port));
    });
  });
}

async function waitFor(ready, log) {
  for (let attempt = 0; attempt < 150; attempt++) {
    try { if (await ready()) return; } catch { /* not listening yet */ }
    await new Promise(resolveDelay => setTimeout(resolveDelay, 100));
  }
  throw new Error(`The production server did not start:\n${log()}`);
}
