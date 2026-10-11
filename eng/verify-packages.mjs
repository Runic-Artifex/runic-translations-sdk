import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { existsSync, mkdtempSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve } from "node:path";
import { root, run, workspace } from "./run.mjs";
import { verifySvelteKitQuickStart } from "./verify-sveltekit-quickstart.mjs";

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

// Vite warns on every dev start when a shipped sourcemap names sources the package omits.
function assertSelfContainedSourceMaps(name, directory) {
  const files = readdirSync(directory, { recursive: true, withFileTypes: true }).filter(entry => entry.isFile());
  for (const map of files.filter(entry => entry.name.endsWith(".map"))) {
    const path = join(map.parentPath, map.name);
    const { sources = [], sourcesContent = [], sourceRoot = "" } = JSON.parse(readFileSync(path, "utf8"));
    sources.forEach((source, index) => assert.ok(typeof sourcesContent[index] === "string" || existsSync(resolve(dirname(path), sourceRoot, source)),
      `${name}/${relative(directory, path)} points to the missing source ${source}`));
  }
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
    // Runic.CommandLine itself restores from nuget.org; the built-in German text proves the bundled catalog loads.
    dotnetConsumer(directory, "Runic.Translations.CommandLine", version, {},
      "using System.Globalization; using Runic.Translations.CommandLine; string? text = new TranslationCommandTextResolver().Resolve(\"help.usage\", CultureInfo.GetCultureInfo(\"de\"), []); if (text != \"Aufruf\") throw new InvalidOperationException(text); Console.WriteLine(text);", environment);
    dotnetConsumer(directory, "Runic.Translations.Build", version, { build: true },
      "Console.WriteLine(\"Build package restored.\");", environment);
    dotnetConsumer(directory, "Runic.Translations.Wpf", version, { framework: "net10.0-windows", wpf: true },
      "using Runic.Translations.Wpf; _ = typeof(WpfInlineRenderer); _ = typeof(WpfDocumentRenderer);", environment);
    for (const consumer of readdirSync(directory, { withFileTypes: true }).filter(entry => entry.isDirectory())) {
      const path = join(directory, consumer.name);
      if (existsSync(join(path, "obj", "project.assets.json"))) assertNoSourceReferences(path);
    }

    const templateHome = join(directory, "template-home");
    run("dotnet", ["new", "install", `Runic.Translations.Templates@${version}`, "--nuget-source", nuget, "--force"], directory,
      { ...environment, DOTNET_CLI_HOME: templateHome });
    // The namespace follows --name; --emit-esm exercises the packaged tool during the build.
    run("dotnet", ["new", "runic-translations-project", "--name", "GeneratedTranslations",
      "--catalog", "app", "--class-name", "AppText", "--emit-esm"], directory,
      { ...environment, DOTNET_CLI_HOME: templateHome });
    const generatedProject = join(directory, "GeneratedTranslations", "GeneratedTranslations.csproj");
    assert.ok(existsSync(generatedProject), "Template did not create its project");
    const generatedProjectText = readFileSync(generatedProject, "utf8");
    assert.ok(!generatedProjectText.includes("__PACKAGE_VERSION__"), "Template package version was not stamped");
    assert.ok(generatedProjectText.includes(`Version="${version}"`), "Template does not reference the candidate package version");
    // Restore only Translations identities from the local candidate feed, then
    // build the generated project so its packaged MSBuild analyzer is exercised.
    run("dotnet", ["restore", generatedProject, "--configfile", join(directory, "NuGet.config")], directory, environment);
    run("dotnet", ["tool", "restore", "--configfile", join(directory, "NuGet.config")], join(directory, "GeneratedTranslations"), environment);
    run("dotnet", ["build", generatedProject, "--configuration", "Release", "--no-restore"], directory, environment);
    assertNoSourceReferences(join(directory, "GeneratedTranslations"));
    const generatedRunic = JSON.parse(readFileSync(join(directory, "GeneratedTranslations", "translations", "runic.json"), "utf8"));
    assert.equal(generatedRunic.code.namespace, "GeneratedTranslations", "Template namespace does not follow --name");
    assert.ok(existsSync(join(directory, "GeneratedTranslations", "obj", "Release", "net10.0", "translations", "app.esm-v5", "web-module-manifest-v3.json")),
      "Template --emit-esm did not emit ESM output");
    // The camelCase options of earlier previews remain hidden aliases.
    const legacyItem = join(directory, "legacy-item");
    mkdirSync(legacyItem);
    run("dotnet", ["new", "runic-translations", "--output", ".", "--defaultLocale", "de", "--className", "LegacyText", "--locales", "en"], legacyItem,
      { ...environment, DOTNET_CLI_HOME: templateHome });
    const legacyRunic = JSON.parse(readFileSync(join(legacyItem, "translations", "runic.json"), "utf8"));
    assert.deepEqual([legacyRunic.baseLocale, legacyRunic.code.className, legacyRunic.locales], ["de", "LegacyText", ["de", "en"]]);

    // Exercise the documented typed API from an application's own library
    // reference. Runic dependencies must still come entirely from the feed.
    const consoleApp = join(directory, "console-app");
    mkdirSync(consoleApp);
    writeFileSync(join(consoleApp, "Consumer.csproj"), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><ProjectReference Include="../GeneratedTranslations/GeneratedTranslations.csproj"/></ItemGroup></Project>`);
    writeFileSync(join(consoleApp, "Program.cs"), `using GeneratedTranslations;
using Runic.Translations;
ITranslationManager manager = await AppTextCatalog.CreateManagerAsync();
var text = new AppText(manager);
if (text.r_6170706c69636174696f6e_r_7469746c65 != "AppText") throw new Exception("Template message did not render.");
Console.WriteLine(text.r_6170706c69636174696f6e_r_7469746c65);
`);
    run("dotnet", ["run", "--project", "Consumer.csproj", "--configuration", "Release"], consoleApp, environment);

    const tools = join(directory, "tools");
    run("dotnet", ["tool", "install", "dotnet-runic-translations", "--version", version, "--configfile", join(directory, "NuGet.config"), "--tool-path", tools], directory, environment);
    const toolAssembly = join(tools, ".store", "dotnet-runic-translations", version,
      "dotnet-runic-translations", version, "tools", "net10.0", "any", "dotnet-runic-translations.dll");
    run("dotnet", [toolAssembly, "--help"], directory, environment);

    const frontend = join(directory, "frontend");
    mkdirSync(frontend);
    writeFileSync(join(frontend, "package.json"), JSON.stringify({
      name: "runic-translations-package-consumer", private: true, type: "module",
      scripts: { build: "vite build" },
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
      assertSelfContainedSourceMaps(name, join(frontend, "node_modules", name));
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

    // Fresh-checkout Vite owns ignored .runic output. Validate authoring first,
    // then build: byte verification cannot precede generation of ignored files.
    run("dotnet", ["new", "tool-manifest", "--output", ".config"], frontend, environment);
    run("dotnet", ["tool", "install", "dotnet-runic-translations", "--version", version,
      "--configfile", join(directory, "NuGet.config")], frontend, environment);
    mkdirSync(join(frontend, "translations"));
    writeFileSync(join(frontend, "translations", "runic.json"), JSON.stringify({
      schemaVersion: 1, catalog: "app", code: { namespace: "Example", className: "AppText" },
      baseLocale: "en", locales: ["en", "de"],
    }, null, 2));
    writeFileSync(join(frontend, "translations", "en.rmf2"), "application {\n  title = Runic application\n}\n");
    writeFileSync(join(frontend, "translations", "de.rmf2"), "application {\n  title = Runic-Anwendung\n}\n");
    writeFileSync(join(frontend, "index.html"), '<!doctype html><html lang="en"><meta charset="utf-8"><div id="app"></div><script type="module" src="/main.js"></script></html>\n');
    writeFileSync(join(frontend, "main.js"), `import { m } from 'virtual:runic-translations/app';\ndocument.querySelector('#app').textContent = m.application_title();\n`);
    writeFileSync(join(frontend, "vite.config.js"), `import { defineConfig } from 'vite';
import { runicTranslations } from '@runic-artifex/vite-plugin-runic-translations';
export default defineConfig({ plugins: [runicTranslations()] });
`);
    assert.ok(!existsSync(join(frontend, ".runic")), "Fresh Vite consumer already has generated output");
    run("dotnet", ["tool", "run", "runic-translations", "--", "validate", "--project", "translations"], frontend, environment);
    run("bun", ["run", "build"], frontend, environment);
    assert.ok(existsSync(join(frontend, ".runic", "translations", "app.esm-v5", "web-module-manifest-v3.json")));
    assert.ok(existsSync(join(frontend, ".runic", "translations", "virtual.d.ts")));
    assert.ok(existsSync(join(frontend, "dist", "index.html")));
    const bundles = readdirSync(join(frontend, "dist", "assets")).filter(name => name.endsWith(".js"));
    assert.ok(bundles.some(name => readFileSync(join(frontend, "dist", "assets", name), "utf8").includes("Runic application")),
      "Vite production bundle did not contain the template message");

    // Retained artifacts are a separate, compiler-owned tree. Vite's ambient
    // declarations stay outside it so extra-file verification remains useful.
    const compilerArguments = ["--project", "translations", "--output", "generated/translations", "--emit-esm"];
    run("dotnet", ["tool", "run", "runic-translations", "--", "generate", ...compilerArguments], frontend, environment);
    writeFileSync(join(frontend, "vite.config.js"), `import { defineConfig } from 'vite';
import { runicTranslations } from '@runic-artifex/vite-plugin-runic-translations';
export default defineConfig({ plugins: [runicTranslations({
  manifest: 'generated/translations/app.esm-v5/web-module-manifest-v3.json',
  typeDeclarations: '.runic/translations/virtual.d.ts',
  sourceFiles: ['translations/runic.json', 'translations/en.rmf2', 'translations/de.rmf2'],
})] });
`);
    run("dotnet", ["tool", "run", "runic-translations", "--", "verify", ...compilerArguments], frontend, environment);
    run("bun", ["run", "build"], frontend, environment);
    run("dotnet", ["tool", "run", "runic-translations", "--", "verify", ...compilerArguments], frontend, environment);

    await verifySvelteKitQuickStart({ directory, npmArchives, nugetConfig: join(directory, "NuGet.config"), version, environment });
    console.log("All seven NuGet and three npm package consumers passed.");
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
