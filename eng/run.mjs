#!/usr/bin/env bun
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

export const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
export const workspace = JSON.parse(readFileSync(resolve(root, "eng/workspace.json"), "utf8"));
export const configuration = process.env.CONFIGURATION ?? "Release";

function executable(command) {
  return process.platform === "win32" && ["npm", "npx"].includes(command) ? `${command}.cmd` : command;
}

export function run(command, args, cwd = root, environment = {}) {
  console.log(`\n> ${command} ${args.join(" ")} (${cwd === root ? "/" : cwd.slice(root.length)})`);
  const result = spawnSync(executable(command), args, {
    cwd,
    stdio: "inherit",
    env: { ...process.env, ...environment },
  });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`${command} exited with ${result.status ?? result.signal}`);
}

function commandOutput(command, args, cwd = root) {
  return execFileSync(executable(command), args, { cwd, encoding: "utf8" }).trim();
}

function packageManifest(path) {
  return JSON.parse(readFileSync(resolve(root, path, "package.json"), "utf8"));
}

function orderedNpmPackages() {
  const pending = new Map(workspace.npm.map(entry => [entry.name, entry]));
  const ordered = [];
  while (pending.size) {
    const ready = [...pending.values()].filter(entry => {
      const manifest = packageManifest(entry.path);
      return Object.keys({ ...manifest.dependencies, ...manifest.peerDependencies, ...manifest.devDependencies })
        .every(name => !pending.has(name));
    });
    assert.ok(ready.length, `npm dependency cycle: ${[...pending.keys()].join(", ")}`);
    for (const entry of ready) {
      ordered.push(entry);
      pending.delete(entry.name);
    }
  }
  return ordered;
}

function ensureVersion(version) {
  assert.match(version, /^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$/, "Version must be SemVer-compatible.");
}

function buildWeb() {
  for (const entry of orderedNpmPackages()) run("bun", ["run", "--bun", "build"], resolve(root, entry.path));
  // Rebuild workspace links after emitted package entry points exist; the editor's
  // frontend consumes those package exports while building.
  run("bun", ["install", "--frozen-lockfile"]);
}

function buildManaged() {
  run("dotnet", ["restore", "Runic.Translations.slnx"]);
  run("dotnet", ["build", "Runic.Translations.slnx", "--configuration", configuration, "--no-restore", "-p:RunicTranslationsBuildMode=Verification", "--nologo"]);
}

function buildEditor() {
  run("dotnet", ["build", workspace.editor.project, "--configuration", configuration, "-p:RunicTranslationsBuildMode=Verification", "--nologo"]);
}

function testManaged() {
  const projects = [
    "tests/dotnet/Runic.Translations.ApiTests/Runic.Translations.ApiTests.csproj",
    "tests/dotnet/Runic.Translations.Authoring.Tests/Runic.Translations.Authoring.Tests.csproj",
    "tests/dotnet/Runic.Translations.Build.Tests/Runic.Translations.Build.Tests.csproj",
    "tests/dotnet/Runic.Translations.Compiler.Tests/Runic.Translations.Compiler.Tests.csproj",
    "tests/dotnet/Runic.Translations.Generator.Tests/Runic.Translations.Generator.Tests.csproj",
    "tests/dotnet/Runic.Translations.Runtime.Tests/Runic.Translations.Runtime.Tests.csproj",
    "tests/dotnet/Runic.Translations.Tooling.Tests/Runic.Translations.Tooling.Tests.csproj",
    "tests/dotnet/Runic.Translations.Rmf2AotTests/Runic.Translations.Rmf2AotTests.csproj",
  ];
  for (const project of projects)
    run("dotnet", ["run", "--project", project, "--configuration", configuration,
      ...(project.includes("Rmf2AotTests") ? [] : ["--no-build"])]);
}

function testPackagedManaged(version = workspace.version) {
  const feed = resolve(root, "artifacts/packages/nuget");
  const cache = resolve(root, ".cache/packaged-test-packages");
  rmSync(cache, { recursive: true, force: true });
  const environment = { NUGET_PACKAGES: cache };
  const packageArguments = [`-p:TranslationsPackageVersion=${version}`, `-p:TranslationsPackageFeed=${feed}`];
  run("dotnet", ["run", "--project", "tests/dotnet/Runic.Translations.AotTests/Runic.Translations.AotTests.csproj", "--configuration", configuration, ...packageArguments], root, environment);
  run("dotnet", ["run", "--project", "tests/dotnet/Runic.Translations.PackageTests/Runic.Translations.PackageTests.csproj", "--configuration", configuration, ...packageArguments, "--", "--feed", feed], root, environment);
}

function testNativeAot() {
  const output = resolve(root, "artifacts/rmf2-native-aot");
  rmSync(output, { recursive: true, force: true });
  run("dotnet", ["publish", "tests/dotnet/Runic.Translations.Rmf2AotTests/Runic.Translations.Rmf2AotTests.csproj", "--configuration", configuration,
    "--runtime", "linux-x64", "--self-contained", "true", "-p:PublishAot=true", "-p:IlcTreatWarningsAsErrors=true", "--output", output]);
  run(resolve(output, "Runic.Translations.Rmf2AotTests"), []);
}

function testWeb() {
  for (const entry of orderedNpmPackages()) run("bun", ["run", "--bun", "test"], resolve(root, entry.path));
}

function verifyEditorIntegrations() {
  run("bun", ["install", "--frozen-lockfile"], resolve(root, workspace.editor.vscode));
  run("bun", ["run", "--bun", "check"], resolve(root, workspace.editor.vscode));
  run("bun", ["run", "--bun", "test"], resolve(root, workspace.editor.vscode));
  run("python3", [workspace.editor.visualStudioVerification, "--check-source"]);
}

export function build() {
  buildManaged();
  buildWeb();
  buildEditor();
}

export function test() {
  build();
  testManaged();
  testWeb();
  verifyEditorIntegrations();
  pack(workspace.version, { built: true });
  testPackagedManaged();
}

function packNpm(directory, destination, version, source) {
  const manifest = resolve(directory, "package.json");
  const original = readFileSync(manifest);
  try {
    writeFileSync(manifest, `${JSON.stringify({ ...JSON.parse(original), version, gitHead: source }, null, 2)}\n`);
    run("bun", ["pm", "pack", "--destination", destination], directory);
  } finally {
    writeFileSync(manifest, original);
  }
}

export function pack(version = workspace.version, { built = false } = {}) {
  ensureVersion(version);
  if (!built) build();
  const nuget = resolve(root, "artifacts/packages/nuget");
  const npm = resolve(root, "artifacts/packages/npm");
  rmSync(resolve(root, "artifacts/packages"), { recursive: true, force: true });
  mkdirSync(nuget, { recursive: true });
  mkdirSync(npm, { recursive: true });
  const source = commandOutput("git", ["rev-parse", "HEAD"]);
  for (const entry of workspace.nuget) {
    run("dotnet", ["pack", entry.project, "--configuration", configuration, "--no-build", "--output", nuget,
      `-p:PackageVersion=${version}`, `-p:RepositoryCommit=${source}`]);
    assert.ok(existsSync(resolve(nuget, `${entry.name}.${version}.nupkg`)), `Missing ${entry.name} package`);
  }
  for (const entry of orderedNpmPackages()) {
    packNpm(resolve(root, entry.path), npm, version, source);
    const archive = `${entry.name.replace("@", "").replace("/", "-")}-${version}.tgz`;
    assert.ok(existsSync(resolve(npm, archive)), `Missing ${entry.name} package`);
  }
}

async function main() {
  process.env.NUGET_PACKAGES ??= resolve(root, ".cache/nuget");
  const [command, version] = process.argv.slice(2);
  switch (command) {
    case "bootstrap":
      run("bun", ["install", "--frozen-lockfile"]);
      run("dotnet", ["restore", "Runic.Translations.slnx"]);
      break;
    case "build": build(); break;
    case "test": test(); break;
    case "pack": pack(version); break;
    case "pack-built": pack(version, { built: true }); break;
    case "verify-packages":
      pack(version);
      await (await import("./verify-packages.mjs")).verifyPackages(version ?? workspace.version);
      break;
    case "verify-editor": verifyEditorIntegrations(); break;
    case "native-aot": testNativeAot(); break;
    default:
      throw new Error("Use bootstrap, build, test, pack [version], pack-built [version], verify-packages [version], verify-editor, or native-aot.");
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => {
    console.error(error.stack ?? error.message);
    process.exitCode = 1;
  });
}
