import assert from "node:assert/strict";
import { cpSync, existsSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { root, run } from "./run.mjs";

// Builds the "Svelte without SvelteKit" section of the Vite quick start (docs/guides/translations/quickstart-vite.md)
// from packed candidates and the packed compiler, as a clean checkout: no .runic/ yet, so its build script must run
// Vite, which writes the virtual-module declarations, before svelte-check (#84). The app imports LocalizedInline (#88)
// and useMessageLocale and passes a JavaScript number to a :number input (#86). Its project tag, declared only in
// runic.json, renders through a LocalizedInline snippet without a restated contract (#96).
export function verifySvelteViteQuickStart({ directory, npmArchives, nugetConfig, version, environment }) {
  const app = join(directory, "svelte-vite-quickstart");
  cpSync(resolve(root, "tests/fixtures/translations/svelte-vite-quickstart"), app, { recursive: true });
  assert.ok(!existsSync(join(app, ".runic")), "The fixture must not carry generated declarations.");
  writeFileSync(join(app, "package.json"), JSON.stringify({
    name: "runic-translations-svelte-vite-quickstart", private: true, type: "module",
    scripts: { build: "vite build && svelte-check --tsconfig ./tsconfig.json --fail-on-warnings" },
    devDependencies: {
      // A plain Vite app: the plugin and the Svelte adapter, not the SvelteKit one.
      ...Object.fromEntries(npmArchives.filter(([name]) => name !== "@runic-artifex/translations-sveltekit").map(([name, archive]) => [name, `file:${archive}`])),
      "@sveltejs/vite-plugin-svelte": "7.3.1", svelte: "5.57.1", "svelte-check": "4.7.6", typescript: "6.0.3", vite: "8.3.5",
    },
  }, null, 2));
  run("bun", ["install", "--ignore-scripts"], app);
  run("dotnet", ["new", "tool-manifest", "--output", ".config"], app, environment);
  run("dotnet", ["tool", "install", "dotnet-runic-translations", "--version", version, "--configfile", nugetConfig], app, environment);
  run("bun", ["run", "build"], app, environment);

  const assets = join(app, "dist", "assets");
  const bundle = readdirSync(assets).filter(name => name.endsWith(".js")).map(name => readFileSync(join(assets, name), "utf8")).join("\n");
  for (const text of ["Updates", "Aktualisierungen", "Bedingungen", "/terms", "Aktualisierungen warten", "app:highlight"])
    assert.ok(bundle.includes(text), `The Svelte and Vite quick start bundle lacks ${text}.`);
  console.log("Svelte and Vite quick start passed: clean-checkout build order, LocalizedInline with a project tag, useMessageLocale and :number types.");
}
