// Reuse the package artifact of the successful CI push run for a release.
//   find-ci                                        green ci.yml push run on main for GITHUB_SHA and its artifact
//   prepare <packages> <version> <ci-run> <out>    check packages against version and commit; write an inventory
//   verify <packages> <inventory>                  check packages against an inventory written by prepare
//   release-check <version>                        the GitHub release must not exist; the tag must be absent or here
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { appendFileSync, lstatSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { root, workspace } from "../run.mjs";

export const REPOSITORY = "Runic-Artifex/runic-translations-sdk";
export const CI_WORKFLOW = ".github/workflows/ci.yml";
export const ARTIFACT = "runic-translations-packages";
const SCHEMA = "runic.translations.preview/1";
const sha256 = bytes => createHash("sha256").update(bytes).digest("hex");
const npmArchiveName = (name, version) => `${name.replace("@", "").replace("/", "-")}-${version}.tgz`;

export function selectCiRun(runs, { repository, sha }) {
  const candidates = runs.filter(run => run.head_sha === sha && run.event === "push" && run.head_branch === "main"
    && run.path?.split("@")[0] === CI_WORKFLOW && run.repository?.full_name === repository
    && run.head_repository?.full_name === repository).sort((a, b) => b.id - a.id);
  const green = candidates.find(run => run.status === "completed" && run.conclusion === "success");
  if (green) return green;
  const [latest] = candidates;
  if (!latest) throw new Error(`No push run of ${CI_WORKFLOW} on main exists for ${sha}. Publish only a commit on main whose CI succeeded.`);
  if (latest.status !== "completed") throw new Error(`CI for ${sha} is still ${latest.status} (${latest.html_url}). Dispatch again after it succeeds.`);
  throw new Error(`CI for ${sha} concluded ${latest.conclusion} (${latest.html_url}). Rerun its failed jobs; publish needs a successful run.`);
}

export function selectArtifact(artifacts, run) {
  const found = artifacts.filter(artifact => artifact.name === ARTIFACT);
  if (found.length !== 1) throw new Error(`CI run ${run.html_url} has no single ${ARTIFACT} artifact.`);
  const [artifact] = found;
  if (artifact.expired) throw new Error(`The ${ARTIFACT} artifact of ${run.html_url} has expired. Rerun all jobs of that CI run to regenerate it, or prepare a new version.`);
  assert.equal(artifact.workflow_run?.id, run.id, "Artifact belongs to a different run");
  assert.equal(artifact.workflow_run?.head_sha, run.head_sha, "Artifact belongs to a different commit");
  return artifact;
}

export async function findCiPackages({ repository, sha, token, fetchImpl = fetch }) {
  assert.match(sha, /^[a-f0-9]{40}$/, "Expected a full commit SHA");
  const api = async path => {
    const response = await fetchImpl(`https://api.github.com/repos/${repository}/${path}`, { headers: {
      accept: "application/vnd.github+json", authorization: `Bearer ${token}`, "x-github-api-version": "2022-11-28" } });
    if (!response.ok) throw new Error(`GitHub API ${path.split("?")[0]} failed: ${response.status}`);
    return response.json();
  };
  const query = new URLSearchParams({ head_sha: sha, event: "push", branch: "main", per_page: "100" });
  const run = selectCiRun((await api(`actions/workflows/ci.yml/runs?${query}`)).workflow_runs, { repository, sha });
  const artifact = selectArtifact((await api(`actions/runs/${run.id}/artifacts?name=${ARTIFACT}`)).artifacts, run);
  return { runId: String(run.id), runUrl: run.html_url, artifact: artifact.name };
}

const nuspec = path => {
  const read = "import sys,zipfile\nz=zipfile.ZipFile(sys.argv[1])\nn=[x for x in z.namelist() if x.endswith('.nuspec') and '/' not in x]\nassert len(n)==1,'one root nuspec'\nsys.stdout.write(z.read(n[0]).decode())";
  const xml = execFileSync("python3", ["-c", read, path], { encoding: "utf8" });
  const field = name => xml.match(new RegExp(`<${name}>([^<]*)</${name}>`))?.[1];
  const repository = xml.match(/<repository\b[^>]*>/)?.[0] ?? "";
  return { name: field("id"), version: field("version"), repository: repository.match(/\burl="([^"]*)"/)?.[1],
    source: repository.match(/\bcommit="([^"]*)"/)?.[1] };
};
const npmManifest = path => {
  const manifest = JSON.parse(execFileSync("tar", ["-xzOf", path, "package/package.json"], { encoding: "utf8" }));
  const url = typeof manifest.repository === "string" ? manifest.repository : manifest.repository?.url;
  return { name: manifest.name, version: manifest.version, repository: url, source: manifest.gitHead };
};
const normalizeRepository = url => (url ?? "").replace(/^git\+/, "").replace(/\.git$/, "").replace(/\/$/, "");

// Exactly the workspace packages at this version, packed from this commit for this repository.
export function scan(directory, version, source, inventory = workspace) {
  assert.deepEqual(readdirSync(directory).sort(), ["npm", "nuget"], "Artifact directory must contain only npm/ and nuget/");
  const expected = {
    nuget: inventory.nuget.map(entry => ({ name: entry.name, file: `${entry.name}.${version}.nupkg`, read: nuspec })),
    npm: inventory.npm.map(entry => ({ name: entry.name, file: npmArchiveName(entry.name, version), read: npmManifest })),
  };
  const packages = [];
  for (const registry of ["nuget", "npm"]) {
    assert.deepEqual(readdirSync(join(directory, registry)).sort(), expected[registry].map(p => p.file).sort(),
      `${registry}/ must contain exactly the ${version} workspace packages`);
    for (const { name, file, read } of expected[registry]) {
      const path = resolve(directory, registry, file);
      assert.ok(lstatSync(path).isFile(), `${file} must be a regular file`);
      const metadata = read(path);
      assert.equal(metadata.name, name, `${file} declares ${metadata.name}`);
      assert.equal(metadata.version, version, `${file} declares version ${metadata.version}`);
      assert.equal(normalizeRepository(metadata.repository), `https://github.com/${REPOSITORY}`, `${file} names repository ${metadata.repository}`);
      assert.equal(metadata.source, source, `${file} was packed from ${metadata.source}, not ${source}`);
      packages.push({ registry, name, file: `${registry}/${file}`, sha256: sha256(readFileSync(path)) });
    }
  }
  return packages;
}

export function prepare(directory, version, source, ciRunId, inventory = workspace) {
  assert.match(ciRunId ?? "", /^[1-9][0-9]*$/, "Expected a CI run id");
  return { schema: SCHEMA, repository: REPOSITORY, version, source, ciRunId, packages: scan(directory, version, source, inventory) };
}

export function verify(directory, manifest, source, inventory = workspace) {
  assert.equal(manifest.schema, SCHEMA, "Not a Translations release inventory");
  assert.equal(manifest.repository, REPOSITORY, "Not a Translations release inventory");
  assert.equal(manifest.source, source, `Inventory is for ${manifest.source}, not ${source}`);
  assert.deepEqual(scan(directory, manifest.version, source, inventory), manifest.packages, "Packages differ from the candidate inventory");
}

export function releaseCheck(version, source, spawn = spawnSync) {
  const tag = `v${version}`;
  const release = spawn("gh", ["release", "view", tag, "--repo", REPOSITORY, "--json", "url"], { encoding: "utf8" });
  if (release.status === 0) throw new Error(`GitHub release ${tag} already exists (${JSON.parse(release.stdout).url}); publish a new version.`);
  const tagged = spawn("gh", ["api", `repos/${REPOSITORY}/commits/${tag}`, "--jq", ".sha"], { encoding: "utf8" });
  if (tagged.status === 0 && tagged.stdout.trim() !== source) throw new Error(`Tag ${tag} belongs to ${tagged.stdout.trim()}, not ${source}.`);
  return `Would create the prerelease ${tag} at ${source}.`;
}

const head = () => execFileSync("git", ["rev-parse", "HEAD"], { cwd: root, encoding: "utf8" }).trim();

async function main([command, ...args]) {
  if (command === "find-ci") {
    const { GITHUB_REPOSITORY: repository, GITHUB_SHA: sha, GH_TOKEN: token, GITHUB_OUTPUT: output } = process.env;
    assert.ok(repository && sha && token && output, "Run in GitHub Actions with GH_TOKEN");
    const found = await findCiPackages({ repository, sha, token });
    appendFileSync(output, `run-id=${found.runId}\nartifact=${found.artifact}\n`);
    console.log(`Reusing ${found.artifact} from ${found.runUrl}`);
  } else if (command === "prepare" && args.length === 4) {
    const [directory, version, ciRunId, output] = args;
    const manifest = prepare(directory, version, head(), ciRunId);
    writeFileSync(output, `${JSON.stringify(manifest, null, 2)}\n`);
    console.log(`Verified ${manifest.packages.length} packages for ${version} at ${manifest.source}`);
  } else if (command === "verify" && args.length === 2) {
    verify(args[0], JSON.parse(readFileSync(args[1], "utf8")), head());
  } else if (command === "release-check" && args.length === 1) {
    console.log(releaseCheck(args[0], head()));
  } else throw new Error("Use find-ci, prepare <packages> <version> <ci-run-id> <inventory>, verify <packages> <inventory>, or release-check <version>");
}

if (import.meta.main) main(process.argv.slice(2)).catch(error => {
  console.log(`::error title=Release check failed::${error.message}`);
  process.exitCode = 1;
});
