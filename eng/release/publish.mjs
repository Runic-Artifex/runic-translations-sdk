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

// Reads entries by exact name: unzip treats names as wildcard patterns, so it
// cannot extract a NuGet package's `[Content_Types].xml`.
const zipEntries = `import hashlib, json, sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as archive:
    print(json.dumps([[entry.filename, hashlib.sha256(archive.read(entry)).hexdigest()] for entry in archive.infolist()]))`;

export function zipContent(path) {
  const entries = JSON.parse(execFileSync("python3", ["-I", "-c", zipEntries, path], { encoding: "utf8" }))
    .filter(([name]) => name !== ".signature.p7s");
  assert.equal(new Set(entries.map(([name]) => name)).size, entries.length, `Archive contains duplicate entries: ${path}`);
  return Object.fromEntries(entries.sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0));
}

async function response(url) {
  const value = await fetch(url, { signal: AbortSignal.timeout(30_000) });
  assert.ok(value.ok || value.status === 404, `Registry lookup failed: ${value.status} ${url}`);
  return value;
}

// Waits for a just-published npm name: npm answers 401 for a scoped name it does
// not know yet, and 404, 429 or 503 while it catches up. Bounded to about three minutes.
export async function availableResponse(url, { fetchImpl = fetch, sleep = ms => new Promise(done => setTimeout(done, ms)), attempts = 12 } = {}) {
  for (let attempt = 1; ; attempt++) {
    const value = await fetchImpl(url, { signal: AbortSignal.timeout(30_000) });
    if (value.ok || ![401, 404, 429, 503].includes(value.status) || attempt === attempts) {
      assert.ok(value.ok, `Registry lookup failed: ${value.status} ${url}`);
      return value;
    }
    await value.body?.cancel();
    await sleep(Math.min(15_000, 1000 * 2 ** attempt));
  }
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

// The nuget.org trusted-publishing policy, mirrored by hand: nuget.org does not
// publish it, and no request can test a key's push permission for a version that
// does not exist yet (README "New NuGet package IDs").
export const nugetPolicy = JSON.parse(readFileSync(resolve(root, "eng/release/nuget-policy.json"), "utf8"));

// nuget.org package globs: `*` matches any run of characters, case-insensitively.
export function globMatches(pattern, id) {
  const expression = pattern.split("*").map(part => part.replace(/[.+?^${}()|[\]\\]/g, "\\$&")).join(".*");
  return new RegExp(`^${expression}$`, "i").test(id);
}

async function nugetJson(url, fetchImpl) {
  const value = await fetchImpl(url, { signal: AbortSignal.timeout(30_000) });
  assert.ok(value.ok || value.status === 404, `Registry lookup failed: ${value.status} ${url}`);
  if (value.status === 404) {
    await value.body?.cancel();
    return undefined;
  }
  return value.json();
}

// Read-only and keyless: the owners of an ID nuget.org knows (undefined when the
// search index lists none of its versions), or `exists: false` for a new ID.
export async function nugetOwnership(id, { fetchImpl = fetch } = {}) {
  const index = await nugetJson("https://api.nuget.org/v3/index.json", fetchImpl);
  const search = index?.resources?.find(resource => String(resource["@type"]).startsWith("SearchQueryService"))?.["@id"];
  assert.equal(typeof search, "string", "nuget.org service index has no SearchQueryService");
  const query = new URLSearchParams({ q: `packageid:${id}`, prerelease: "true", semVerLevel: "2.0.0", take: "1" });
  const result = await nugetJson(`${search}?${query}`, fetchImpl);
  const entry = result?.data?.find(value => value.id.toLowerCase() === id.toLowerCase());
  if (entry) return { exists: true, owners: [entry.owners ?? []].flat() };
  // Search omits an ID whose versions are all unlisted; the flat container keeps them.
  const versions = await nugetJson(`https://api.nuget.org/v3-flatcontainer/${id.toLowerCase()}/index.json`, fetchImpl);
  return versions ? { exists: true, owners: undefined } : { exists: false };
}

// Fails, naming every ID and the remedy, when the trusted-publishing key could not
// push one of these IDs. Runs before the first push and in the dry run.
export async function checkNugetPush(ids, { policy = nugetPolicy, ownership = nugetOwnership, log = console.log } = {}) {
  const problems = [];
  for (const id of ids) {
    const glob = policy.packageGlobs.find(pattern => globMatches(pattern, id));
    const state = await ownership(id);
    if (!glob)
      problems.push(`${id} matches none of the policy's package globs (${policy.packageGlobs.join(", ")}).`);
    if (state.exists && state.owners && !state.owners.some(owner => owner.toLowerCase() === policy.owner.toLowerCase()))
      problems.push(`${id} is owned on nuget.org by ${state.owners.join(", ") || "nobody listed"}, not by ${policy.owner}, so the policy cannot push it; add ${policy.owner} as an owner or choose another ID.`);
    else if (!state.exists && !policy.pushNewPackages)
      problems.push(`${id} is a new nuget.org package ID, but the policy may only push new versions of existing packages.`);
    else if (glob && !state.exists)
      log(`::notice::${id} is a new nuget.org package ID; the mirrored policy of ${policy.owner} covers it (glob ${glob}).`);
    else if (glob && !state.owners)
      log(`::warning::${id} has no listed version on nuget.org, so its owners could not be checked.`);
  }
  if (problems.length) throw new Error([
    "The NuGet trusted-publishing key cannot push every package of this release; nothing was published.",
    ...problems.map(problem => `- ${problem}`),
    `Before releasing, edit the trusted-publishing policy of ${policy.owner} on nuget.org so that it may push new packages and its package glob matches each ID, then update eng/release/nuget-policy.json to match (eng/release/README.md, "New NuGet package IDs").`,
  ].join("\n"));
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

// Until 1.0 every release is a preview, so npm latest follows the newest one.
// Never move latest back to an older version, for example on a late rerun.
export function needsLatest(current, version) {
  return current === undefined || Bun.semver.order(current, version) < 0;
}

// npm answers the dist-tags of an unpublished scoped name with 401, so a dry run,
// which precedes publication, first checks whether the package exists at all.
async function npmLatest(name, dryRun) {
  if (dryRun) {
    const document = await response(`https://registry.npmjs.org/${encodeURIComponent(name)}`);
    await document.body?.cancel();
    if (document.status === 404) return undefined;
  }
  const url = `https://registry.npmjs.org/-/package/${encodeURIComponent(name)}/dist-tags`;
  const tags = dryRun ? await response(url) : await availableResponse(url);
  assert.ok(tags.ok, `npm dist-tag lookup failed for ${name}: ${tags.status}`);
  return (await tags.json()).latest;
}

async function tagLatest(version, dryRun) {
  if (!dryRun) assert.ok(process.env.ACTIONS_ID_TOKEN_REQUEST_URL, "OIDC unavailable");
  for (const entry of workspace.npm) {
    const current = await npmLatest(entry.name, dryRun);
    if (!needsLatest(current, version)) console.log(`Keeping ${entry.name} latest at ${current}`);
    else if (dryRun) console.log(`Would move ${entry.name} latest from ${current ?? "(unpublished)"} to ${version}`);
    else run("npm", ["dist-tag", "add", `${entry.name}@${version}`, "latest", "--registry", "https://registry.npmjs.org"]);
  }
}

async function main() {
  // --dry-run performs every registry read and reports the writes it would make.
  const dryRun = process.argv.includes("--dry-run");
  const [command, version, ...extra] = process.argv.slice(2).filter(argument => argument !== "--dry-run");
  assert.ok(["publish", "tag-latest"].includes(command) && !extra.length, "Use: bun eng/release/publish.mjs publish|tag-latest <version> [--dry-run]");
  assert.match(version, /^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$/);
  if (command === "tag-latest") return tagLatest(version, dryRun);
  const candidates = packageFiles(version);
  for (const candidate of candidates) assert.ok(readdirSync(resolve(candidate.file, "..")).includes(candidate.file.split("/").at(-1)), `Missing ${candidate.file}`);
  await publishCandidates(candidates, version, { dryRun });
}

// Every registry read and the NuGet push check finish before the first push, so a
// refused package ID stops the release before anything is published.
export async function publishCandidates(candidates, version, { dryRun, published = publishedMatches, checkPush = checkNugetPush, push = publish, log = console.log }) {
  const pending = [];
  for (const candidate of candidates) {
    if (await published(candidate, version)) log(`Already published with matching contents: ${candidate.name}@${version}`);
    else pending.push(candidate);
  }
  await checkPush(pending.filter(candidate => candidate.registry === "nuget").map(candidate => candidate.name));
  for (const candidate of pending) {
    if (dryRun) log(`Would publish ${candidate.name}@${version}`);
    else await push(candidate, version);
  }
}

if (import.meta.main) main().catch(error => {
  const secret = process.env.NUGET_API_KEY;
  const message = String(error.stack ?? error.message);
  console.error(secret ? message.replaceAll(secret, "[redacted]") : message);
  process.exitCode = 1;
});
