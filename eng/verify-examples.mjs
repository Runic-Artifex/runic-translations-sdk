import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import { basename, join, resolve } from "node:path";
import { root, run, workspace } from "./run.mjs";
import { packageEntries, packageNuspec } from "./verify-packages.mjs";

// Builds the examples that need packed packages: examples/shared-catalog/Frontend, a minimal Vite page, and
// examples/component-library.

// The shared catalog's page (examples/shared-catalog/Frontend): the catalog that the Tray process formats in C#,
// compiled by the candidate Vite plugin and compiler.
export function verifySharedCatalogFrontend({ directory, npmArchives, nugetConfig, version, environment }) {
  const frontend = join(directory, "shared-catalog-frontend");
  cpSync(resolve(root, "examples/shared-catalog/Frontend"), frontend, {
    recursive: true, filter: source => !["node_modules", "dist", ".runic", ".config"].includes(basename(source)),
  });
  const manifest = JSON.parse(readFileSync(join(frontend, "package.json"), "utf8"));
  const plugin = "@runic-artifex/vite-plugin-runic-translations";
  assert.ok(plugin in manifest.devDependencies, `the shared-catalog page lists ${plugin}`);
  manifest.devDependencies[plugin] = `file:${npmArchives.find(([name]) => name === plugin)[1]}`;
  writeFileSync(join(frontend, "package.json"), JSON.stringify(manifest, null, 2));
  run("bun", ["install", "--ignore-scripts"], frontend);
  run("dotnet", ["new", "tool-manifest", "--output", ".config"], frontend, environment);
  run("dotnet", ["tool", "install", "dotnet-runic-translations", "--version", version, "--configfile", nugetConfig], frontend, environment);
  run("dotnet", ["tool", "run", "runic-translations", "--", "validate", "--project", "translations"], frontend, environment);
  run("bun", ["run", "build"], frontend, environment);
  assert.ok(existsSync(join(frontend, "dist", "index.html")), "the page's index.html");
  const bundle = readdirSync(join(frontend, "dist", "assets")).filter(name => name.endsWith(".js"))
    .map(name => readFileSync(join(frontend, "dist", "assets", name), "utf8")).join("\n");
  for (const text of ["Updates", "Aktualisierungen"]) assert.ok(bundle.includes(text), `the page's bundle has ${text}`);
  assert.ok(!bundle.includes("Check for updates"), "the page bundles only the messages it calls, not the tray's");
  console.log("Shared-catalog page passed: built with the candidate Vite plugin.");
}

// Builds examples/component-library (see its README) from the packed candidates: the library as a NuGet package
// (packed catalog) and an npm package (exports["./runic-translations"]), the .NET console application with build-time
// overrides, and the SvelteKit application with Vite dependencies: "auto", served by adapter-node.
export async function verifyComponentLibraryExample({ directory, nuget, npmArchives, version, environment }) {
  const example = join(directory, "component-library");
  cpSync(resolve(root, "examples/component-library"), example, {
    recursive: true,
    filter: source => !["bin", "obj", "node_modules", "dist", ".runic", ".svelte-kit", "build", "packages"].includes(basename(source)),
  });
  const feed = join(example, "packages");
  writeFileSync(join(example, "NuGet.config"), `<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="candidate" value="${quoted(nuget)}"/><add key="library" value="${quoted(feed)}"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><clear/><packageSource key="candidate"><package pattern="Runic.Translations*"/><package pattern="dotnet-runic-translations"/></packageSource><packageSource key="library"><package pattern="Acme.*"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>`);
  const msbuild = [`-p:RunicTranslationsVersion=${version}`];

  // The library's NuGet package: the packed catalog, no dependency on the development-only Build package, and the
  // runtime range of release N.
  const library = join(example, "library");
  run("dotnet", ["pack", "Acme.Widgets.csproj", "--configuration", "Release", "--output", feed, ...msbuild], library, environment);
  const nupkg = join(feed, "Acme.Widgets.1.0.0.nupkg");
  const entries = packageEntries(nupkg);
  for (const entry of ["lib/net10.0/Acme.Widgets.dll", "buildTransitive/Acme.Widgets.props", "runic/acme.widgets/runic.json",
    "runic/acme.widgets/en.rmf2", "runic/acme.widgets/de.rmf2", "runic/acme.widgets/contract.json"])
    assert.ok(entries.includes(entry), `Acme.Widgets packs ${entry}: ${entries.join(", ")}`);
  const nuspec = packageNuspec(nupkg);
  assert.ok(!nuspec.includes("Runic.Translations.Build"), "the library must not depend on Runic.Translations.Build");
  const [major, minor] = version.split(".");
  assert.ok(nuspec.includes(`id="Runic.Translations" version="[${version}, ${major}.${Number(minor) + 1}.0-0)"`) || Number(major) >= 1,
    `the library's Runic.Translations dependency has the release range:\n${nuspec}`);

  // The local compiler for the library's prebuilt ESM catalog and the web application's Vite builds.
  run("dotnet", ["new", "tool-manifest", "--output", ".config"], example, environment);
  run("dotnet", ["tool", "install", "dotnet-runic-translations", "--version", version, "--configfile", join(example, "NuGet.config")], example, environment);

  // The .NET application: overrides one library key, adds French, switches every catalog at once.
  const app = join(example, "app");
  run("dotnet", ["build", "Shop.csproj", "--configuration", "Release", ...msbuild], app, environment);
  const output = runOutput("dotnet", ["run", "--project", "Shop.csproj", "--configuration", "Release", "--no-build"], app, environment);
  assert.equal(plainSpaces(output), expectedConsoleOutput, "the .NET application's output");

  // The library's npm package: a prebuilt catalog plus its sources.
  run("bun", ["run", "build"], library, environment);
  run("bun", ["pm", "pack", "--quiet", "--destination", library], library);
  assert.ok(existsSync(join(library, "acme-widgets-1.0.0.tgz")), "bun pm pack wrote acme-widgets-1.0.0.tgz");

  // The SvelteKit application, on the candidate npm packages.
  const web = join(example, "web");
  const manifest = JSON.parse(readFileSync(join(web, "package.json"), "utf8"));
  for (const [name, archive] of npmArchives) {
    assert.ok(name in manifest.devDependencies, `web/package.json lists ${name}`);
    manifest.devDependencies[name] = `file:${archive}`;
  }
  writeFileSync(join(web, "package.json"), JSON.stringify(manifest, null, 2));
  run("bun", ["install", "--ignore-scripts"], web);
  run("dotnet", ["tool", "run", "runic-translations", "--", "validate", "--project", "translations"], web, environment);
  run("bun", ["run", "build"], web, environment);
  run("bun", ["run", "check"], web, environment);
  assert.ok(existsSync(join(web, ".runic", "translations", "dependencies", "acme.widgets.d.ts")), "declarations of the regenerated library catalog");
  const client = readdirSync(join(web, "build", "client", "_app", "immutable"), { recursive: true })
    .filter(name => name.endsWith(".js")).map(name => readFileSync(join(web, "build", "client", "_app", "immutable", name), "utf8")).join("\n");
  assert.ok(client.includes("Your drive is empty"), "the client bundle has the application's override of the library");
  assert.ok(client.includes("Aucun fichier"), "the client bundle has the French that the overrides add");

  await servePages(web, async page => {
    for (const [path, expected] of Object.entries(expectedPages)) {
      const response = await page(path);
      assert.equal(response.status, 200, `${path}: ${response.status}`);
      assertPage(response.body, expected, path);
    }
    // Interleaved requests: the library's text follows each request's locale on the server.
    const paths = Array.from({ length: 30 }, (_, index) => ["/", "/de", "/fr"][index % 3]);
    const results = await Promise.all(paths.map(page));
    results.forEach((result, index) => assertPage(result.body, expectedPages[paths[index]], `concurrent ${paths[index]} #${index}`));
  });
  console.log("Component library example passed: NuGet and npm library, .NET overrides and French, SvelteKit SSR in three locales.");
}

// Formatted numbers contain CLDR's no-break spaces (U+00A0, U+202F); the comparisons read them as plain spaces.
const expectedConsoleOutput = [
  "Languages: de, en, fr",
  "[en] Cloud storage | Storage | Your drive is empty | 3 files · 1.5 GB of 15 GB used | €4.99 per month",
  "[de] Cloud-Speicher | Speicher | Noch keine Dateien | 3 Dateien · 1,5 GB von 15 GB belegt | 4,99 € pro Monat",
  "[de-CH] Cloud-Speicher | Speicher | Noch keine Dateien | 3 Dateien · 1.5 GB von 15 GB belegt | EUR 4.99 pro Monat",
  "  shop shows de for de-CH (Parent)",
  "  acme.widgets shows de for de-CH (Parent)",
  "[fr] Stockage en ligne | Stockage | Aucun fichier pour l’instant | 3 fichiers · 1,5 Go sur 15 Go utilisés | 4,99 € par mois",
  "",
].join("\n");

const expectedPages = {
  "/": { lang: "en", texts: ["Cloud storage", "Storage", "Your drive is empty", "3 files · 1.5 GB of 15 GB used", "€4.99 per month"] },
  "/de": { lang: "de", texts: ["Cloud-Speicher", "Speicher", "Noch keine Dateien", "3 Dateien · 1,5 GB von 15 GB belegt", "4,99 € pro Monat"] },
  "/fr": { lang: "fr", texts: ["Stockage en ligne", "Stockage", "Aucun fichier pour l’instant", "3 fichiers · 1,5 Go sur 15 Go utilisés", "4,99 € par mois"] },
};

function assertPage(html, { lang, texts }, label) {
  assert.match(html, new RegExp(`<html lang="${lang}" dir="ltr">`), `${label}: <html lang="${lang}">`);
  // Svelte escapes text; the expected texts contain no markup-significant characters. Formatted numbers may contain
  // no-break spaces, so compare with plain spaces.
  const plain = html.replace(/[  ]/g, " ");
  for (const text of texts) assert.ok(plain.includes(text), `${label}: missing ${JSON.stringify(text)}`);
}

function plainSpaces(text) {
  return text.replace(/[\u00a0\u202f]/g, " ");
}

async function servePages(web, check) {
  const port = await freePort();
  const server = spawn("node", ["build"], { cwd: web, env: { ...process.env, PORT: String(port), HOST: "127.0.0.1" }, stdio: ["ignore", "pipe", "pipe"] });
  let output = "", errors = "";
  server.stdout.on("data", chunk => { output += chunk; });
  server.stderr.on("data", chunk => { output += chunk; errors += chunk; });
  const exited = new Promise(resolveExit => server.once("exit", resolveExit));
  try {
    const origin = `http://127.0.0.1:${port}`;
    const deadline = Date.now() + 30_000;
    for (;;) {
      if (server.exitCode !== null) throw new Error(`The production server exited:\n${output}`);
      try { if ((await fetch(origin)).ok) break; } catch { /* not listening yet */ }
      if (Date.now() > deadline) throw new Error(`The production server did not start:\n${output}`);
      await new Promise(resolveWait => setTimeout(resolveWait, 100));
    }
    await check(async path => {
      const response = await fetch(`${origin}${path}`, { redirect: "manual" });
      return { status: response.status, body: await response.text() };
    });
    assert.doesNotMatch(errors, /^\s+at /m, `The production server logged an error:\n${errors}`);
  } finally {
    if (server.exitCode === null && server.signalCode === null) server.kill();
    await exited;
  }
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

function runOutput(command, args, cwd, environment) {
  console.log(`\n> ${command} ${args.join(" ")} (${cwd})`);
  const result = spawnSync(command, args, { cwd, env: { ...process.env, ...environment }, encoding: "utf8" });
  if (result.error) throw result.error;
  assert.equal(result.status, 0, `${command} exited with ${result.status}:\n${result.stdout}${result.stderr}`);
  process.stdout.write(result.stdout);
  return result.stdout.replaceAll("\r\n", "\n");
}

function quoted(value) {
  return value.replaceAll("&", "&amp;").replaceAll("\"", "&quot;").replaceAll("<", "&lt;");
}

// Runs the examples against the packages that `bun eng/run.mjs pack` wrote to artifacts/packages.
if (import.meta.main) {
  const version = process.argv[2] ?? workspace.version;
  const nuget = resolve(root, "artifacts/packages/nuget");
  const npm = resolve(root, "artifacts/packages/npm");
  const npmArchives = workspace.npm.map(entry => {
    const archive = join(npm, `${entry.name.replace("@", "").replace("/", "-")}-${version}.tgz`);
    assert.ok(existsSync(archive), `Pack ${entry.name} ${version} first: bun eng/run.mjs pack`);
    return [entry.name, archive];
  });
  const directory = mkdtempSync(join(tmpdir(), "runic-translations-examples-"));
  mkdirSync(join(directory, "nuget-cache"));
  const environment = { NUGET_PACKAGES: join(directory, "nuget-cache"), DOTNET_CLI_HOME: join(directory, "dotnet-home") };
  const nugetConfig = join(directory, "NuGet.config");
  writeFileSync(nugetConfig, `<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="candidate" value="${quoted(nuget)}"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><clear/><packageSource key="candidate"><package pattern="Runic.Translations*"/><package pattern="dotnet-runic-translations"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>`);
  Promise.resolve()
    .then(() => verifySharedCatalogFrontend({ directory, npmArchives, nugetConfig, version, environment }))
    .then(() => verifyComponentLibraryExample({ directory, nuget, npmArchives, version, environment }))
    .catch(error => {
      console.error(error.stack ?? error.message);
      process.exitCode = 1;
    })
    .finally(() => { if (!process.env.KEEP_EXAMPLE) rmSync(directory, { recursive: true, force: true }); });
}
