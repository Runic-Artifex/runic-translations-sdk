// The generic-client smoke of the language server (#262 B3): packs dotnet-runic-translations under a local-only
// version, installs it as a local tool in a new project, as the language server guide tells users to, and runs the
// minimal stdio LSP client (tests/lsp-generic-client/client.mjs) against `dotnet tool run runic-translations -- lsp`,
// the command it gives Neovim, Helix and other LSP clients. Run it with `bun eng/run.mjs lsp-smoke`.
import assert from "node:assert/strict";
import { existsSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { resolve } from "node:path";
import { configuration, root, run, workspace } from "./run.mjs";

// The suffixed version never exists on nuget.org, so the install can only take the package just packed.
export function lspSmokeVersion(release) {
  return `${release}${release.includes("-") ? "." : "-"}lsp-smoke`;
}

const project = `{"schemaVersion":1,"catalog":"app","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","locales":["en","de"]}\n`;
// The client breaks en.rmf2 in its buffer (an unescaped `}`) to get a diagnostic with a quick fix.
const english = "title = Hello {$name}\nprice = Costs 5\n";
const german = "title = Hallo {$name}\nprice = Kostet 5\n";

export function lspSmoke(version = lspSmokeVersion(workspace.version)) {
  const tool = workspace.nuget.find(entry => entry.name === "dotnet-runic-translations");
  assert.ok(tool, "eng/workspace.json lists no dotnet-runic-translations package");
  const feed = resolve(root, "artifacts/lsp-smoke");
  rmSync(feed, { recursive: true, force: true });
  run("dotnet", ["pack", tool.project, "--configuration", configuration, "--output", feed, `-p:PackageVersion=${version}`, "--nologo"]);
  assert.ok(existsSync(resolve(feed, `dotnet-runic-translations.${version}.nupkg`)), "Missing the packed tool");

  // A user's project outside this repository: its own NuGet.config with only the local feed, so neither this
  // repository's configuration nor nuget.org takes part.
  const directory = mkdtempSync(resolve(tmpdir(), "runic-lsp-smoke-"));
  const cache = resolve(process.env.NUGET_PACKAGES ?? resolve(root, ".cache/nuget"));
  const removeLocalPackage = () => rmSync(resolve(cache, "dotnet-runic-translations", version), { recursive: true, force: true });
  removeLocalPackage();
  let passed = false;
  try {
    writeFileSync(resolve(directory, "NuGet.config"),
      `<?xml version="1.0" encoding="utf-8"?>\n<configuration><packageSources><clear /><add key="lsp-smoke" value="${feed}" /></packageSources></configuration>\n`);
    writeFileSync(resolve(directory, "runic.json"), project);
    writeFileSync(resolve(directory, "en.rmf2"), english);
    writeFileSync(resolve(directory, "de.rmf2"), german);
    run("dotnet", ["new", "tool-manifest", "--output", ".config"], directory);
    run("dotnet", ["tool", "install", "dotnet-runic-translations", "--version", version], directory, { NUGET_PACKAGES: cache });
    run("bun", [resolve(root, "tests/lsp-generic-client/client.mjs"), directory, "--", "dotnet", "tool", "run", "runic-translations", "--", "lsp"],
      directory, { NUGET_PACKAGES: cache });
    passed = true;
  } finally {
    // The local-only version must not linger in a shared package cache. A failed run keeps its project for inspection.
    removeLocalPackage();
    rmSync(feed, { recursive: true, force: true });
    if (passed) rmSync(directory, { recursive: true, force: true });
    else console.error(`The smoke project is kept in ${directory}`);
  }
}
