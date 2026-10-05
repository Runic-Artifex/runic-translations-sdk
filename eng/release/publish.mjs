import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { root, workspace } from "../run.mjs";

const sha256 = bytes => createHash("sha256").update(bytes).digest("hex");

function packageFiles(version) {
  const nuget = resolve(root, "artifacts/packages/nuget");
  const npm = resolve(root, "artifacts/packages/npm");
  return [
    ...workspace.nuget.map(entry => ({ registry: "nuget", name: entry.name, file: join(nuget, `${entry.name}.${version}.nupkg`) })),
    ...workspace.npm.map(entry => ({ registry: "npm", name: entry.name, file: join(npm, `${entry.name.replace("@", "").replace("/", "-")}-${version}.tgz`) })),
  ];
}

function zipContent(path) {
  const names = execFileSync("unzip", ["-Z1", path], { encoding: "utf8" }).split("\n").filter(Boolean)
    .filter(name => name !== ".signature.p7s");
  assert.equal(new Set(names).size, names.length, `Archive contains duplicate entries: ${path}`);
  return Object.fromEntries(names.sort().map(name => [name, sha256(execFileSync("unzip", ["-p", path, name]))]));
}

async function response(url) {
  const value = await fetch(url, { signal: AbortSignal.timeout(30_000) });
  assert.ok(value.ok || value.status === 404, `Registry lookup failed: ${value.status} ${url}`);
  return value;
}

async function publishedMatches(candidate, version) {
  if (candidate.registry === "npm") {
    const metadata = await response(`https://registry.npmjs.org/${encodeURIComponent(candidate.name)}/${version}`);
    if (metadata.status === 404) return false;
    const tarball = (await metadata.json()).dist?.tarball;
    assert.equal(typeof tarball, "string", `npm registry response omitted tarball for ${candidate.name}`);
    const archive = await response(tarball);
    assert.equal(sha256(Buffer.from(await archive.arrayBuffer())), sha256(readFileSync(candidate.file)),
      `Published npm package differs: ${candidate.name}@${version}; choose a new version`);
    return true;
  }
  const identity = candidate.name.toLowerCase();
  const url = `https://api.nuget.org/v3-flatcontainer/${identity}/${version}/${identity}.${version}.nupkg`;
  const archive = await response(url);
  if (archive.status === 404) return false;
  const temporary = mkdtempSync(join(tmpdir(), "runic-translations-registry-"));
  try {
    const remote = join(temporary, "remote.nupkg");
    writeFileSync(remote, Buffer.from(await archive.arrayBuffer()));
    assert.deepEqual(zipContent(remote), zipContent(candidate.file),
      `Published NuGet package differs: ${candidate.name}@${version}; choose a new version`);
    return true;
  } finally {
    rmSync(temporary, { recursive: true, force: true });
  }
}

function run(command, args, { redact = [] } = {}) {
  console.log(`> ${command} ${args.map(value => redact.includes(value) ? "[redacted]" : value).join(" ")}`);
  execFileSync(command, args, { cwd: root, stdio: "inherit" });
}

async function publish(candidate, version) {
  try {
    if (candidate.registry === "npm")
      run("npm", ["publish", candidate.file, "--tag", "preview", "--access", "public", "--provenance", "--registry", "https://registry.npmjs.org"]);
    else {
      const apiKey = process.env.NUGET_API_KEY ?? "";
      run("dotnet", ["nuget", "push", candidate.file, "--source", "https://api.nuget.org/v3/index.json", "--api-key", apiKey], { redact: [apiKey] });
    }
  } catch (error) {
    // A concurrent publisher can make a missing version appear between the
    // preflight lookup and upload. Re-read once and accept only identical bits.
    if (await publishedMatches(candidate, version)) {
      console.log(`Published concurrently with matching contents: ${candidate.name}@${version}`);
      return;
    }
    throw error;
  }
}

async function main() {
  const [command, version] = process.argv.slice(2);
  assert.equal(command, "publish", "Use: bun eng/release/publish.mjs publish <version>");
  assert.match(version, /^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$/);
  const candidates = packageFiles(version);
  for (const candidate of candidates) assert.ok(readdirSync(resolve(candidate.file, "..")).includes(candidate.file.split("/").at(-1)), `Missing ${candidate.file}`);
  for (const candidate of candidates) {
    if (await publishedMatches(candidate, version)) {
      console.log(`Already published with matching contents: ${candidate.name}@${version}`);
      continue;
    }
    await publish(candidate, version);
  }
}

main().catch(error => {
  const secret = process.env.NUGET_API_KEY;
  const message = String(error.stack ?? error.message);
  console.error(secret ? message.replaceAll(secret, "[redacted]") : message);
  process.exitCode = 1;
});
