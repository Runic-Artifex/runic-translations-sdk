import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { existsSync, mkdtempSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { root, run, workspace } from "./run.mjs";

function quoted(value) {
  return value.replaceAll("&", "&amp;").replaceAll("\"", "&quot;").replaceAll("<", "&lt;");
}

function requireArchive(directory, name, version, extension) {
  const file = `${name.replace("@", "").replace("/", "-")}-${version}.${extension}`;
  const path = join(directory, file);
  assert.ok(existsSync(path), `Expected package candidate ${file}`);
  return path;
}

function consumerProject(name, version, { framework = "net10.0", wpf = false, build = false } = {}) {
  return `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>${framework}</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors>${wpf ? "<EnableWindowsTargeting>true</EnableWindowsTargeting><UseWPF>true</UseWPF>" : ""}</PropertyGroup><ItemGroup><PackageReference Include="${name}" Version="[${version}]"/>${build ? `<PackageReference Include="Runic.Translations" Version="[${version}]"/>` : ""}</ItemGroup></Project>`;
}

function dotnetConsumer(directory, name, version, options, program, environment) {
  const consumer = join(directory, name.replaceAll(".", "-").toLowerCase());
  mkdirSync(consumer, { recursive: true });
  writeFileSync(join(consumer, "Consumer.csproj"), consumerProject(name, version, options));
  writeFileSync(join(consumer, "Program.cs"), program);
  const args = options.wpf
    ? ["build", "Consumer.csproj", "--configuration", "Release"]
    : ["run", "--project", "Consumer.csproj", "--configuration", "Release"];
  run("dotnet", args, consumer, environment);
}

function assertNoSourceReferences(directory) {
  const assets = JSON.parse(readFileSync(join(directory, "obj", "project.assets.json"), "utf8"));
  assert.ok(Object.values(assets.libraries).every(library => library.type !== "project"), "Consumer leaked a project reference");
}

export async function verifyPackages(version = workspace.version) {
  const nuget = resolve(root, "artifacts/packages/nuget");
  const npm = resolve(root, "artifacts/packages/npm");
  for (const entry of workspace.nuget) {
    assert.ok(readdirSync(nuget).includes(`${entry.name}.${version}.nupkg`), `Pack ${entry.name} first`);
  }
  const npmArchives = workspace.npm.map(entry => [entry.name, requireArchive(npm, entry.name, version, "tgz")]);
  const directory = mkdtempSync(join(tmpdir(), "runic-translations-consumer-"));
  console.log(`Package-only consumers: ${directory}`);
  const environment = {
    NUGET_PACKAGES: join(directory, "nuget-cache"),
    DOTNET_CLI_HOME: join(directory, "dotnet-home"),
  };
  try {
    writeFileSync(join(directory, "NuGet.config"), `<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="candidate" value="${quoted(nuget)}"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><clear/><packageSource key="candidate"><package pattern="Runic.Translations*"/><package pattern="dotnet-runic-translations"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>`);
    environment.NUGET_CONFIG_FILE = join(directory, "NuGet.config");

    dotnetConsumer(directory, "Runic.Translations", version, {},
      "using Runic.Translations; Console.WriteLine(new TranslationReference(\"catalog\", \"sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\", \"greeting\").Catalog);", environment);
    dotnetConsumer(directory, "Runic.Translations.Tooling", version, {},
      "using Runic.Translations.Tooling; Console.WriteLine(typeof(ArtifactInspector).Assembly.GetName().Name);", environment);
    dotnetConsumer(directory, "Runic.Translations.Build", version, { build: true },
      "Console.WriteLine(\"Build package restored.\");", environment);
    dotnetConsumer(directory, "Runic.Translations.Wpf", version, { framework: "net10.0-windows", wpf: true },
      "using Runic.Translations.Wpf; _ = typeof(WpfInlineRenderer);", environment);
    for (const consumer of readdirSync(directory, { withFileTypes: true }).filter(entry => entry.isDirectory())) {
      const path = join(directory, consumer.name);
      if (existsSync(join(path, "obj", "project.assets.json"))) assertNoSourceReferences(path);
    }

    const templateHome = join(directory, "template-home");
    run("dotnet", ["new", "install", "Runic.Translations.Templates", "--nuget-source", nuget, "--force"], directory,
      { ...environment, DOTNET_CLI_HOME: templateHome });
    run("dotnet", ["new", "runic-translations-project", "--name", "GeneratedTranslations"], directory,
      { ...environment, DOTNET_CLI_HOME: templateHome });
    assert.ok(existsSync(join(directory, "GeneratedTranslations", "GeneratedTranslations.csproj")), "Template did not create its project");

    const tools = join(directory, "tools");
    run("dotnet", ["tool", "install", "dotnet-runic-translations", "--version", version, "--configfile", join(directory, "NuGet.config"), "--tool-path", tools], directory, environment);
    const toolAssembly = join(tools, ".store", "dotnet-runic-translations", version,
      "dotnet-runic-translations", version, "tools", "net10.0", "any", "dotnet-runic-translations.dll");
    run("dotnet", [toolAssembly, "--help"], directory, environment);

    const frontend = join(directory, "frontend");
    mkdirSync(frontend);
    writeFileSync(join(frontend, "package.json"), JSON.stringify({
      name: "runic-translations-package-consumer", private: true, type: "module",
      dependencies: {
        ...Object.fromEntries(npmArchives.map(([name, archive]) => [name, `file:${archive}`])),
        svelte: "5.57.1", "@sveltejs/kit": "3.0.0", vite: "8.3.2"
      }
    }, null, 2));
    run("bun", ["install", "--ignore-scripts"], frontend);
    for (const [name] of npmArchives) {
      const manifest = JSON.parse(readFileSync(join(frontend, "node_modules", name, "package.json"), "utf8"));
      for (const dependency of Object.values({ ...manifest.dependencies, ...manifest.peerDependencies }))
        assert.ok(!/^(workspace:|file:|link:)/.test(dependency), `${name} has an unpublished dependency ${dependency}`);
    }
    writeFileSync(join(frontend, "consumer.mjs"), `
import assert from "node:assert/strict";
import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";
import * as svelte from "@runic-artifex/translations-svelte";
import * as sveltekit from "@runic-artifex/translations-sveltekit/translations";
assert.equal(typeof runicTranslations, "function");
assert.ok(Object.keys(svelte).length > 0);
assert.ok(Object.keys(sveltekit).length > 0);
console.log("Packaged npm imports passed.");`);
    run("bun", ["consumer.mjs"], frontend);
    console.log("All six NuGet and three npm package consumers passed.");
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

if (import.meta.main) {
  verifyPackages(process.argv[2]).catch(error => {
    console.error(error.stack ?? error.message);
    process.exitCode = 1;
  });
}
