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

function verifyEditorFrontend() {
  const editorDirectory = dirname(resolve(root, workspace.editor.project));
  run("bun", ["run", "--bun", "verify:built"], resolve(editorDirectory, "Frontend"), {
    RUNIC_TRANSLATIONS_MANIFEST: resolve(editorDirectory, "obj", configuration, "net10.0", "translations", "editor.esm-v5", "web-module-manifest-v3.json"),
  });
  run("dotnet", [resolve(editorDirectory, "bin", configuration, "net10.0", "Runic.Translations.Editor.dll"),
    "--workspace", resolve(editorDirectory, "ExampleWorkspace"), "--smoke-test"]);
}

// Builds the editor against freshly packed Translations packages instead of project
// references, then proves NuGet resolved them from the local feed. The suffixed
// version never exists on nuget.org, so restore cannot pick a published package.
function verifyEditorPacked(version = `${workspace.version}.editor-packed`) {
  ensureVersion(version);
  buildManaged();
  buildWeb();
  pack(version, { built: true });
  const feed = resolve(root, "artifacts/packages/nuget");
  const cache = resolve(process.env.NUGET_PACKAGES);
  const removeLocalPackages = () => {
    for (const entry of workspace.nuget) rmSync(resolve(cache, entry.name.toLowerCase(), version), { recursive: true, force: true });
  };
  removeLocalPackages();
  try {
    const toolPath = resolve(root, "artifacts/editor-packed/tool");
    rmSync(toolPath, { recursive: true, force: true });
    run("dotnet", ["tool", "install", "dotnet-runic-translations", "--version", version, "--add-source", feed, "--tool-path", toolPath]);
    // Run the packaged tool through the muxer: its apphost cannot find a non-default .NET location.
    const tool = resolve(toolPath, ".store/dotnet-runic-translations", version, "dotnet-runic-translations", version,
      "tools/net10.0/any/dotnet-runic-translations.dll");
    assert.ok(existsSync(tool), `Missing installed tool ${tool}`);
    run("dotnet", ["build", workspace.editor.project, "--configuration", configuration, "-p:RunicTranslationsBuildMode=Verification", "--nologo",
      "-p:RunicEditorUsePackedTranslations=true", `-p:RunicEditorTranslationsPackageVersion=${version}`,
      `-p:RestoreAdditionalProjectSources=${feed}`, `-p:TranslationsToolCommand=dotnet "${tool}"`]);
    const editorDirectory = dirname(resolve(root, workspace.editor.project));
    const assets = JSON.parse(readFileSync(resolve(editorDirectory, "obj/project.assets.json"), "utf8"));
    const sourceProjects = Object.entries(assets.libraries)
      .filter(([identity, library]) => identity.startsWith("Runic.Translations") && library.type !== "package");
    assert.deepEqual(sourceProjects.map(([identity]) => identity), [], "The packed editor must not reference Translations source projects");
    for (const id of ["Runic.Translations", "Runic.Translations.Tooling", "Runic.Translations.Build"]) {
      const identity = `${id}/${version}`;
      assert.equal(assets.libraries[identity]?.type, "package", `${identity} must be a package dependency of the editor`);
      const metadata = JSON.parse(readFileSync(resolve(cache, id.toLowerCase(), version, ".nupkg.metadata"), "utf8"));
      assert.equal(resolve(metadata.source), feed, `${identity} must come from the local package feed`);
    }
    verifyEditorFrontend();
    console.log("EDITOR_PACKED_TRANSLATIONS_OK");
  } finally {
    // The local-only version must not linger in a shared package cache.
    removeLocalPackages();
  }
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

// Read-only vulnerability audit of every lockfile and NuGet restore graph; it never
// upgrades dependencies.
function auditDependencies() {
  const lockfiles = commandOutput("git", ["ls-files", "bun.lock", "**/bun.lock"]).split("\n").filter(Boolean);
  const findings = [];
  for (const lockfile of lockfiles) {
    try { run("bun", ["audit"], dirname(resolve(root, lockfile))); }
    catch { findings.push(`${lockfile}: bun audit reported vulnerabilities`); }
  }
  for (const project of ["Runic.Translations.slnx", workspace.editor.project]) {
    run("dotnet", ["restore", project]);
    const report = JSON.parse(commandOutput("dotnet", ["list", project, "package", "--vulnerable", "--include-transitive", "--format", "json"]));
    assert.deepEqual(report.problems ?? [], [], `dotnet list package reported problems for ${project}`);
    for (const entry of report.projects)
      for (const framework of entry.frameworks ?? [])
        for (const dependency of [...(framework.topLevelPackages ?? []), ...(framework.transitivePackages ?? [])])
          for (const vulnerability of dependency.vulnerabilities ?? [])
            findings.push(`${entry.path} (${framework.framework}): ${dependency.id} ${dependency.resolvedVersion} ${vulnerability.severity} ${vulnerability.advisoryurl}`);
  }
  assert.deepEqual(findings, [], `Vulnerable dependencies:\n${findings.join("\n")}`);
  console.log("No vulnerable npm or NuGet packages found.");
}

export function build() {
  buildManaged();
  buildWeb();
  buildEditor();
}

export function test() {
  build();
  verifyEditorFrontend();
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
    case "verify-editor-packed": verifyEditorPacked(version); break;
    case "native-aot": testNativeAot(); break;
    case "audit": auditDependencies(); break;
    default:
      throw new Error("Use bootstrap, build, test, pack [version], pack-built [version], verify-packages [version], verify-editor, verify-editor-packed [version], native-aot, or audit.");
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => {
    console.error(error.stack ?? error.message);
    process.exitCode = 1;
  });
}
