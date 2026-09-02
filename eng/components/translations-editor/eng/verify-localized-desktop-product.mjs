#!/usr/bin/env node
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { cp, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { basename, dirname, join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const root = resolve(import.meta.dirname, "..");
const schema = "runic.localized-desktop-product/1";
const repeatSchema = "runic.localized-desktop-product-repeat/1";
const canonicalPreviewVersion = "1.0.0-preview.1";
const feed = process.env.RUNIC_EDITOR_NUGET_FEED && resolve(process.env.RUNIC_EDITOR_NUGET_FEED);
const pluginArchive = process.env.RUNIC_EDITOR_NPM_ARCHIVE && resolve(process.env.RUNIC_EDITOR_NPM_ARCHIVE);
const compatibilitySet = process.env.RUNIC_EDITOR_COMPATIBILITY_SET && resolve(process.env.RUNIC_EDITOR_COMPATIBILITY_SET);
const pluginIdentity = "@runic-artifex/vite-plugin-runic-translations";
const candidateIdentities = ["Runic.Translations", "Runic.Translations.Tooling", "Runic.Translations.Build", "dotnet-runic-translations"];
const expectedGates = ["missing-manifest", "stale-manifest", "forged-manifest-schema", "unsupported-locale", "fingerprint-skew"];
const same = (left, right) => JSON.stringify(left) === JSON.stringify(right);
const sha256 = async value => createHash("sha256").update(typeof value === "string" ? await readFile(value) : value).digest("hex");
const sha512 = async value => "sha512-" + createHash("sha512").update(typeof value === "string" ? await readFile(value) : value).digest("base64");

function run(command, args, cwd = root, env = {}) {
  return new Promise(done => {
    const child = spawn(command, args, { cwd, env: { ...process.env, ...env }, stdio: ["ignore", "pipe", "pipe"] });
    const output = [];
    child.stdout.on("data", value => output.push(value)); child.stderr.on("data", value => output.push(value));
    child.on("error", error => done({ ok: false, exitCode: null, output: String(error) }));
    child.on("close", exitCode => done({ ok: exitCode === 0, exitCode, output: Buffer.concat(output).toString("utf8") }));
  });
}
function requireSuccess(name, result) { if (!result.ok) throw new Error(`${name} failed:\n${result.output.slice(-4096)}`); }
function phase(name, command, args, result) { return { name, argv: [command, ...args], status: result.ok ? "passed" : "failed", exitCode: result.exitCode }; }
function environment(directory) { return { DOTNET_CLI_HOME: join(directory, ".dotnet"), NUGET_PACKAGES: join(directory, ".nuget", "packages"), NUGET_HTTP_CACHE_PATH: join(directory, ".nuget", "http-cache"), BUN_INSTALL_CACHE_DIR: join(directory, ".bun-cache"), RUNIC_EDITOR_FRONTEND_CANDIDATES: "1" }; }
function parseJsonc(text) {
  let result = "", inString = false, escaped = false;
  for (let index = 0; index < text.length; index++) {
    const character = text[index];
    if (inString) {
      result += character;
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === '"') inString = false;
    } else if (character === '"') { inString = true; result += character; }
    else if (character === ",") {
      let next = index + 1; while (/\s/u.test(text[next] ?? "")) next++;
      if (text[next] !== "}" && text[next] !== "]") result += character;
    } else result += character;
  }
  return JSON.parse(result);
}
function packageVersion(packages, ecosystem, identity) {
  const value = packages.find(candidate => candidate.ecosystem === ecosystem && candidate.identity === identity)?.version;
  if (value !== canonicalPreviewVersion) throw new Error(`Compatibility set must pin ${ecosystem}:${identity} to ${canonicalPreviewVersion}.`);
  return value;
}

async function compatibilityFacts() {
  if (!compatibilitySet) throw new Error("RUNIC_EDITOR_COMPATIBILITY_SET must name the exact compatibility-set JSON.");
  const value = JSON.parse(await readFile(compatibilitySet, "utf8"));
  if (value.schemaVersion !== 1 || value.id !== "runic-1.0-preview.1" || value.releaseTrainVersion !== canonicalPreviewVersion || !Array.isArray(value.packages)) {
    throw new Error(`Compatibility set must be the canonical ${canonicalPreviewVersion} preview train.`);
  }
  for (const identity of candidateIdentities) packageVersion(value.packages, "nuget", identity);
  packageVersion(value.packages, "npm", pluginIdentity);
  const packageProperties = await readFile(join(root, "Directory.Packages.props"), "utf8");
  const defaults = new Map([...packageProperties.matchAll(/<(Runic\w+PackageVersion)\s+Condition="[^"]+">([^<]+)<\/\1>/g)].map(match => [match[1], match[2]]));
  const pins = [...packageProperties.matchAll(/<PackageVersion Include="([^"]+)" Version="\$\((Runic\w+PackageVersion)\)"\s*\/>/g)];
  for (const [, identity, property] of pins) {
    if (defaults.get(property) !== canonicalPreviewVersion || packageVersion(value.packages, "nuget", identity) !== canonicalPreviewVersion) {
      throw new Error(`Editor central package pin ${identity} must match the canonical ${canonicalPreviewVersion} compatibility pin.`);
    }
  }
  return { id: value.id, releaseTrainVersion: value.releaseTrainVersion, sha256: await sha256(compatibilitySet), packages: value.packages };
}

async function temporaryNuGetConfig(directory) {
  const config = join(directory, "NuGet.config");
  const source = feed.replaceAll("&", "&amp;").replaceAll("\"", "&quot;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
  await writeFile(config, `<?xml version="1.0" encoding="utf-8"?>\n<configuration>\n  <packageSources>\n    <clear />\n    <add key="exact-local" value="${source}" />\n    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />\n  </packageSources>\n  <packageSourceMapping>\n    <packageSource key="exact-local">\n      <package pattern="Runic.Application" />\n      <package pattern="Runic.Application.*" />\n      <package pattern="Runic.CommandLine" />\n      <package pattern="Runic.CommandLine.*" />\n      <package pattern="Runic.Translations" />\n      <package pattern="Runic.Translations.*" />\n      <package pattern="dotnet-runic-translations" />\n      <package pattern="Runic.Assets" />\n      <package pattern="Runic.Assets.*" />\n      <package pattern="Runic.Desktop" />\n    </packageSource>\n    <packageSource key="nuget.org">\n      <package pattern="*" />\n    </packageSource>\n  </packageSourceMapping>\n</configuration>\n`);
  return config;
}

async function manifestFacts(path) {
  const manifest = JSON.parse(await readFile(path, "utf8"));
  if (manifest.webModuleManifestVersion !== 1 || manifest.esmAbiVersion !== 2 || manifest.catalog !== "editor" || !/^sha256:[a-f0-9]{64}$/.test(manifest.contractFingerprint)) throw new Error("Editor generated manifest contract failed closed.");
  for (const asset of manifest.assets) {
    const content = await readFile(join(dirname(path), asset.path));
    if (asset.byteLength !== content.byteLength || asset.sha256 !== await sha256(content)) throw new Error(`Editor manifest asset mismatch: ${asset.path}`);
  }
  return { catalog: manifest.catalog, contractFingerprint: manifest.contractFingerprint, esmAbiVersion: manifest.esmAbiVersion, sha256: await sha256(path) };
}

async function gates(manifestPath, directory) {
  const copied = join(directory, "editor.esm");
  await cp(dirname(manifestPath), copied, { recursive: true });
  const copyManifest = join(copied, "web-module-manifest-v1.json");
  const manifest = JSON.parse(await readFile(copyManifest, "utf8"));
  const plugin = await import(pathToFileURL(join(root, "Frontend", "node_modules", ...pluginIdentity.split("/"), "index.js")).href);
  const rejects = async (name, mutate, path = copyManifest) => {
    await cp(dirname(manifestPath), copied, { recursive: true, force: true });
    await mutate();
    try { await plugin.runicTranslations({ manifest: path }).buildStart.call({ addWatchFile() {} }); }
    catch { return name; }
    throw new Error(`${name} did not fail closed.`);
  };
  const [runtime, messages, transport] = await Promise.all([
    import(pathToFileURL(join(copied, manifest.entrypoints.runtime)).href + "?runtime"),
    import(pathToFileURL(join(copied, manifest.entrypoints.messages)).href + "?messages"),
    import(pathToFileURL(join(copied, "transport.js")).href + "?transport"),
  ]);
  assert.equal(messages.m["App.Title"]({ locale: "en" }), "Translations");
  assert.equal(messages.m["App.Title"]({ locale: "de" }), "Übersetzungen");
  assert.throws(() => runtime.resolveLocale("fr"), RangeError);
  const reference = { version: 1, catalog: manifest.catalog, contractFingerprint: manifest.contractFingerprint, key: "App.Title", arguments: {} };
  assert.equal(transport.decodeTextReference(reference).ok, true);
  assert.equal(transport.decodeTextReference({ ...reference, contractFingerprint: "sha256:" + "0".repeat(64) }).ok, false);
  const entry = join(copied, manifest.entrypoints.messages);
  await cp(dirname(manifestPath), copied, { recursive: true, force: true });
  await writeFile(entry, Buffer.concat([await readFile(entry), Buffer.from("// stale\n")]));
  await assert.rejects(() => manifestFacts(copyManifest));
  return [
    await rejects("missing-manifest", async () => {}, join(copied, "missing.json")),
    "stale-manifest",
    await rejects("forged-manifest-schema", async () => writeFile(copyManifest, JSON.stringify({ ...manifest, webModuleManifestVersion: 99 }))),
    "unsupported-locale",
    "fingerprint-skew",
  ];
}

async function candidateFacts() {
  if (!feed || !pluginArchive) throw new Error("RUNIC_EDITOR_NUGET_FEED and RUNIC_EDITOR_NPM_ARCHIVE must name exact local candidates.");
  const compatibility = await compatibilityFacts();
  const nuget = await Promise.all(candidateIdentities.map(async identity => {
    const version = packageVersion(compatibility.packages, "nuget", identity);
    const path = join(feed, `${identity}.${version}.nupkg`);
    return { identity, version, source: "exact-local", archiveSha256: await sha256(path) };
  }));
  const manifest = JSON.parse((await run("tar", ["-xOf", pluginArchive, "package/package.json"])).output);
  if (manifest.name !== pluginIdentity || manifest.version !== packageVersion(compatibility.packages, "npm", pluginIdentity)) throw new Error("The supplied translations Vite archive is not the coordinated candidate.");
  const lock = parseJsonc(await readFile(join(root, "Frontend", "bun.lock"), "utf8"));
  const installed = Object.values(lock.packages ?? {}).find(entry => Array.isArray(entry) && entry[0] === `${pluginIdentity}@${manifest.version}`);
  if (!installed || installed[3] !== await sha512(pluginArchive)) throw new Error("The Editor Bun lockfile is not bound to the supplied exact-local translations Vite candidate.");
  return { compatibility, nuget, npm: { identity: manifest.name, version: manifest.version, source: "exact-local", integrity: installed[3], archiveSha256: await sha256(pluginArchive) } };
}

async function archiveFacts(path) {
  const listing = await run("7z", ["l", path]); requireSuccess("inspect embedded asset archive", listing);
  if (!listing.output.includes("runic-assets.json") || !listing.output.includes("assets/index.html")) throw new Error("The Editor package omitted its embedded frontend archive.");
  return { sha256: await sha256(path), bytes: (await readFile(path)).byteLength };
}

async function one() {
  const directory = await mkdtemp(join(tmpdir(), "runic-w40-localized-desktop-"));
  try {
    const candidates = await candidateFacts();
    const config = await temporaryNuGetConfig(directory);
    const env = environment(directory);
    const phases = [];
    for (const [name, command, args] of [
      ["tool-restore", "dotnet", ["tool", "restore", "--configfile", config, "--no-cache"]],
      ["editor-build", "dotnet", ["build", "Runic.Translations.Editor.csproj", "--configuration", "Release", "--nologo", `-p:RestoreConfigFile=${config}`]],
      ["frontend-check", "bun", ["run", "--cwd", "Frontend", "check"]],
    ]) {
      const result = await run(command, args, root, env);
      phases.push(phase(name, command, args.map(value => value.replaceAll(directory, "$WORKSPACE")), result));
      requireSuccess(name, result);
    }
    const manifestPath = join(root, "obj", "Release", "net10.0", "translations", "editor.esm", "web-module-manifest-v1.json");
    const generated = await manifestFacts(manifestPath);
    const negativeGates = await gates(manifestPath, directory);
    phases.push({ name: "manifest-contract", argv: ["node", "editor.esm"], status: "passed", exitCode: 0 });
    const smoke = await run("dotnet", ["run", "--project", "Runic.Translations.Editor.csproj", "--configuration", "Release", "--no-build", "--", "--smoke-test", "--workspace", "ExampleWorkspace"], root, env);
    phases.push(phase("editor-interchange-smoke", "dotnet", ["run", "--smoke-test"], smoke)); requireSuccess("editor interchange smoke", smoke);
    const publish = join(directory, "package");
    const packaged = await run("dotnet", ["publish", "Runic.Translations.Editor.csproj", "--configuration", "Release", "--no-restore", "--output", publish, "--nologo"], root, env);
    phases.push(phase("editor-package", "dotnet", ["publish", "Runic.Translations.Editor.csproj", "--configuration", "Release"], packaged)); requireSuccess("editor package", packaged);
    const packageSmoke = await run("dotnet", [join(publish, "Runic.Translations.Editor.dll"), "--smoke-test", "--workspace", join(publish, "ExampleWorkspace")], root, env);
    phases.push(phase("package-smoke", "dotnet", ["Runic.Translations.Editor.dll", "--smoke-test"], packageSmoke)); requireSuccess("packaged editor smoke", packageSmoke);
    const embedded = await archiveFacts(join(root, "obj", "Release", "net10.0", "runic-assets", "Runic.Translations.Editor.runic-assets"));
    return { schema, isolation: { nuget: ".nuget/packages", bun: ".bun-cache" }, compatibility: { id: candidates.compatibility.id, releaseTrainVersion: candidates.compatibility.releaseTrainVersion, sha256: candidates.compatibility.sha256 }, generated, embedded, negativeGates, localeEvidence: ["en", "de", "structured-interchange"], nugetCandidates: candidates.nuget, npmCandidate: candidates.npm, phases };
  } finally { await rm(directory, { recursive: true, force: true }); }
}

export function verifyReceipt(receipt) {
  const errors = [];
  if (receipt?.schema !== repeatSchema || !Array.isArray(receipt.journeys) || receipt.journeys.length !== 2) errors.push("two desktop journeys are required");
  for (const journey of receipt?.journeys ?? []) {
    if (journey?.schema !== schema || !same(journey.isolation, { nuget: ".nuget/packages", bun: ".bun-cache" })) errors.push("journey contract mismatch");
    if (journey?.compatibility?.id !== "runic-1.0-preview.1" || journey.compatibility?.releaseTrainVersion !== canonicalPreviewVersion || !/^[a-f0-9]{64}$/.test(journey.compatibility?.sha256 ?? "")) errors.push("canonical compatibility pin mismatch");
    if (journey?.generated?.catalog !== "editor" || journey.generated?.esmAbiVersion !== 2 || !/^sha256:[a-f0-9]{64}$/.test(journey.generated?.contractFingerprint ?? "") || !/^[a-f0-9]{64}$/.test(journey.embedded?.sha256 ?? "")) errors.push("generated or embedded artifact mismatch");
    if (!same(journey?.negativeGates, expectedGates) || !same(journey?.localeEvidence, ["en", "de", "structured-interchange"])) errors.push("closed-boundary evidence mismatch");
    if (!Array.isArray(journey?.nugetCandidates) || journey.nugetCandidates.length !== 4 || journey.nugetCandidates.some(item => item.source !== "exact-local" || !/^[a-f0-9]{64}$/.test(item.archiveSha256 ?? "")) || journey?.npmCandidate?.identity !== pluginIdentity || journey.npmCandidate?.source !== "exact-local" || !/^[a-f0-9]{64}$/.test(journey.npmCandidate?.archiveSha256 ?? "")) errors.push("candidate provenance mismatch");
    if (!same(journey?.phases?.map(item => item.name), ["tool-restore", "editor-build", "frontend-check", "manifest-contract", "editor-interchange-smoke", "editor-package", "package-smoke"]) || journey.phases.some(item => item.status !== "passed" || item.exitCode !== 0)) errors.push("desktop proof phases mismatch");
  }
  if (receipt?.journeys?.length === 2 && !same(receipt.journeys[0], receipt.journeys[1])) errors.push("desktop journeys are not deterministic");
  return { ok: errors.length === 0, errors };
}

async function main() {
  const [command, path] = process.argv.slice(2);
  if (command === "run-twice" && !path) {
    const receipt = { schema: repeatSchema, journeys: [await one(), await one()] };
    const report = verifyReceipt(receipt); if (!report.ok) throw new Error(report.errors.join("\n"));
    process.stdout.write(JSON.stringify(receipt, null, 2) + "\n"); return;
  }
  if (command === "verify-twice" && path) { const report = verifyReceipt(JSON.parse(await readFile(path, "utf8"))); if (!report.ok) throw new Error(report.errors.join("\n")); return; }
  throw new Error("Usage: node eng/verify-localized-desktop-product.mjs run-twice | verify-twice <receipt.json>");
}
if (import.meta.main) main().catch(error => { process.stderr.write(error.message + "\n"); process.exitCode = 1; });
