// Reuse the package artifact of the successful full CI run of a release's commit.
//   find-ci                                        green full ci.yml run on main for GITHUB_SHA and its artifact
//   prepare <packages> <version> <ci-run> <out>    check the packages; write an inventory
//   verify <packages> <inventory> <version> <run>  check them against the inventory, version and CI run
//   describe <packages> <version> <out>            write the CycloneDX SBOM of the release (local, deterministic)
//   release-check <version>                        tag and any release must be absent or at this commit (read-only)
//   release <version> <packages> <sbom>            create the release (githubPrerelease), or finish one that exists for this commit
//
// The editor extensions release from Runic-Artifex/runic-editors; a Translations release carries no VSIX files.
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { appendFileSync, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { root, workspace } from "../run.mjs";
import { TRAIN_FEED_ARTIFACT } from "../train-feed.mjs";
import { assertNpmSource } from "./npm-inputs.mjs";

export const REPOSITORY = "Runic-Artifex/runic-translations-sdk";
export const CI_WORKFLOW = ".github/workflows/ci.yml";
export const ARTIFACT = "runic-translations-packages";
const SCHEMA = "runic.translations.preview/1";
const sha256 = bytes => createHash("sha256").update(bytes).digest("hex");
const npmArchiveName = (name, version) => `${name.replace("@", "").replace("/", "-")}-${version}.tgz`;

// A full run ran every job, the macOS jobs included, and its `changes` job uploaded this marker: a nightly or dispatched
// run, the push run of a version or release commit, or a push that changed a macOS path (CONTRIBUTING.md, "CI
// triggers and macOS jobs"). An ordinary push run skips the macOS jobs, so its packages are never published.
export const FULL_RUN_ARTIFACT = "ci-full-run";
export const CI_EVENTS = ["push", "schedule", "workflow_dispatch"];
const dispatchFullRun = repository => `gh workflow run ci.yml -R ${repository} --ref main`;

// The ci.yml runs on main of exactly this commit, newest first: its push run and any nightly or dispatched run of it.
export function ciRunsOf(runs, { repository, sha }) {
  return runs.filter(run => run.head_sha === sha && CI_EVENTS.includes(run.event) && run.head_branch === "main"
    && run.path?.split("@")[0] === CI_WORKFLOW && run.repository?.full_name === repository
    && run.head_repository?.full_name === repository).sort((a, b) => b.id - a.id);
}

const succeeded = run => run.status === "completed" && run.conclusion === "success";

// The newest successful full run of the commit; `full` holds the ids of the runs with the ci-full-run marker.
export function selectCiRun(runs, { repository, sha, full = new Set() }) {
  const candidates = ciRunsOf(runs, { repository, sha });
  const green = candidates.find(run => succeeded(run) && full.has(run.id));
  if (green) return green;
  const [latest] = candidates;
  if (!latest) throw new Error(`No ${CI_WORKFLOW} run on main exists for ${sha}. Publish only a commit on main whose CI succeeded.`);
  if (latest.status !== "completed") throw new Error(`CI for ${sha} is still ${latest.status} (${latest.html_url}). Dispatch again after it succeeds.`);
  const partial = candidates.filter(succeeded);
  if (partial.length) throw new Error(`No full CI run exists for ${sha}: ${partial.map(run => run.html_url).join(", ")} succeeded without the macOS jobs ` +
    `(no ${FULL_RUN_ARTIFACT} artifact; an ordinary push skips them). Publish takes the packages only from a run of every job. ` +
    `Dispatch one on main (\`${dispatchFullRun(repository)}\`) and dispatch Publish preview again after it succeeds.`);
  throw new Error(`CI for ${sha} concluded ${latest.conclusion} (${latest.html_url}). Rerun its failed jobs; publish needs a successful run.`);
}

export function selectArtifact(artifacts, run, name = ARTIFACT) {
  const found = artifacts.filter(artifact => artifact.name === name);
  if (found.length !== 1) throw new Error(`CI run ${run.html_url} has no single ${name} artifact.`);
  const [artifact] = found;
  if (artifact.expired) throw new Error(`The ${name} artifact of ${run.html_url} has expired. Rerun all jobs of that CI run to regenerate it, or prepare a new version.`);
  assert.equal(artifact.workflow_run?.id, run.id, "Artifact belongs to a different run");
  assert.equal(artifact.workflow_run?.head_sha, run.head_sha, "Artifact belongs to a different commit");
  assert.ok(Number.isSafeInteger(artifact.id) && artifact.id > 0, "Artifact has no id");
  return artifact;
}

// A CI run that built against a train feed (eng/train-feed.mjs) used a Command Line candidate that is not the published
// package, and restored without its lock files; such a run is never published (COMPATIBILITY.md 3.4).
export function refuseTrainFeed(artifacts, run) {
  if (artifacts.some(artifact => artifact.name === TRAIN_FEED_ARTIFACT))
    throw new Error(`CI run ${run.html_url} built against the ${TRAIN_FEED_ARTIFACT} (an unpublished Runic Command Line candidate), so it cannot be published. ` +
      "Publish Runic Command Line first, commit the lock files refreshed against nuget.org, and publish the commit whose CI ran without the train feed.");
}

export async function findCiPackages({ repository, sha, token, fetchImpl = fetch }) {
  assert.match(sha, /^[a-f0-9]{40}$/, "Expected a full commit SHA");
  const api = async path => {
    const response = await fetchImpl(`https://api.github.com/repos/${repository}/${path}`, { headers: {
      accept: "application/vnd.github+json", authorization: `Bearer ${token}`, "x-github-api-version": "2022-11-28" } });
    if (!response.ok) throw new Error(`GitHub API ${path.split("?")[0]} failed: ${response.status}`);
    return response.json();
  };
  // Every event: a nightly or dispatched run of the commit is a full run too.
  const query = new URLSearchParams({ head_sha: sha, branch: "main", per_page: "100" });
  const runs = (await api(`actions/workflows/ci.yml/runs?${query}`)).workflow_runs;
  const artifactsOf = new Map();
  for (const candidate of ciRunsOf(runs, { repository, sha }).filter(succeeded))
    artifactsOf.set(candidate.id, (await api(`actions/runs/${candidate.id}/artifacts?per_page=100`)).artifacts);
  const full = new Set([...artifactsOf].filter(([, list]) => list.some(artifact => artifact.name === FULL_RUN_ARTIFACT)).map(([id]) => id));
  const run = selectCiRun(runs, { repository, sha, full });
  const artifacts = artifactsOf.get(run.id);
  refuseTrainFeed(artifacts, run);
  const artifact = selectArtifact(artifacts, run);
  // Download by id: re-uploading under the same name creates a new id, so the
  // bytes downloaded later are exactly the artifact selected here.
  return { runId: String(run.id), runUrl: run.html_url, artifact: artifact.name, artifactId: String(artifact.id) };
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

// Exactly the workspace packages at this version, packed from this commit for this repository. A NuGet package names
// `source` itself; an npm archive names the commit that last changed its inputs, which `npmSource` checks against
// `source` (npm-inputs.mjs assertNpmSource: an ancestor with the same inputs).
export function scan(directory, version, source, inventory = workspace, { npmSource = assertNpmSource } = {}) {
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
      if (registry === "npm") npmSource(name, metadata.source, source, { file });
      else assert.equal(metadata.source, source, `${file} was packed from ${metadata.source}, not ${source}`);
      packages.push({ registry, name, file: `${registry}/${file}`, sha256: sha256(readFileSync(path)) });
    }
  }
  return packages;
}

export function prepare(directory, version, source, ciRunId, inventory = workspace) {
  assert.match(ciRunId ?? "", /^[1-9][0-9]*$/, "Expected a CI run id");
  return { schema: SCHEMA, repository: REPOSITORY, version, source, ciRunId, packages: scan(directory, version, source, inventory) };
}

export function verify(directory, manifest, source, version, ciRunId, inventory = workspace) {
  assert.equal(manifest.schema, SCHEMA, "Not a Translations release inventory");
  assert.equal(manifest.repository, REPOSITORY, "Not a Translations release inventory");
  assert.equal(manifest.source, source, `Inventory is for ${manifest.source}, not ${source}`);
  assert.equal(manifest.version, version, `Inventory is for version ${manifest.version}, not ${version}`);
  assert.equal(manifest.ciRunId, ciRunId, `Inventory is for CI run ${manifest.ciRunId}, not ${ciRunId}`);
  assert.deepEqual(scan(directory, manifest.version, source, inventory), manifest.packages, "Packages differ from the candidate inventory");
}

const releaseFiles = packages =>
  ["nuget", "npm"].flatMap(registry => readdirSync(join(packages, registry)).sort().map(file => join(packages, registry, file)));
export const sbomName = version => `${REPOSITORY.split("/")[1]}-${version}.cdx.json`;

// Writes the SBOM of exactly the release files; the same files and commit always give the same bytes.
export function describe(directory, version, source, output, epoch, inventory = workspace) {
  const packages = scan(directory, version, source, inventory).map(p => resolve(directory, p.file));
  mkdirSync(output, { recursive: true });
  const path = join(output, sbomName(version));
  execFileSync("python3", [fileURLToPath(new URL("./sbom.py", import.meta.url)), "--repository", REPOSITORY, "--version", version,
    "--source", source, "--epoch", String(epoch), "--output", path,
    ...packages], { stdio: "inherit" });
  return path;
}

const ghFailure = (what, result) => new Error(`${what} failed: ${(result.stderr ?? "").trim() || `exit ${result.status ?? "unavailable"}`}`);

// The commit a tag points at, or undefined only when GitHub answers 404 for the tag
// ref. git/ref/tags/<tag> matches exactly that tag (commits/<ref> would also match a
// branch); annotated tags are followed to their commit. Any other error fails.
export function tagCommit(tag, spawn = spawnSync) {
  let path = `repos/${REPOSITORY}/git/ref/tags/${tag}`;
  for (let depth = 0; depth < 5; depth++) {
    const result = spawn("gh", ["api", path, "--jq", '.object.type + " " + .object.sha'], { encoding: "utf8" });
    if (result.status !== 0) {
      if (depth === 0 && /HTTP 404/.test(result.stderr ?? "")) return undefined;
      throw ghFailure(`Tag lookup for ${tag}`, result);
    }
    const [type, sha] = result.stdout.trim().split(" ");
    assert.match(sha ?? "", /^[a-f0-9]{40}$/, `Tag ${tag} lookup returned no object`);
    if (type === "commit") return sha;
    assert.equal(type, "tag", `Tag ${tag} points at a ${type}, not a commit`);
    path = `repos/${REPOSITORY}/git/tags/${sha}`;
  }
  throw new Error(`Tag ${tag} nests too many annotated tags`);
}

// The release for a tag (drafts included), or undefined only when gh reports it not found.
function findRelease(tag, spawn) {
  const result = spawn("gh", ["release", "view", tag, "--repo", REPOSITORY, "--json", "isDraft,url,assets,targetCommitish"], { encoding: "utf8" });
  if (result.status === 0) return JSON.parse(result.stdout);
  if (/release not found|HTTP 404/.test(result.stderr ?? "")) return undefined;
  throw ghFailure(`Release lookup for ${tag}`, result);
}

// The release of this exact tag and commit (a draft targeting this commit included),
// undefined if absent. Fails for a release or tag of any other commit and any lookup error.
function existingRelease(version, source, spawn) {
  const tag = `v${version}`;
  const found = findRelease(tag, spawn);
  const tagged = tagCommit(tag, spawn);
  if (tagged !== undefined && tagged !== source) throw new Error(`Tag ${tag} belongs to ${tagged}, not ${source}.`);
  if (!found) return undefined;
  if (found.isDraft) {
    if (found.targetCommitish !== source) throw new Error(`Draft release ${tag} (${found.url}) targets ${found.targetCommitish}, not ${source}.`);
    return found;
  }
  if (tagged === undefined) throw new Error(`GitHub release ${tag} (${found.url}) has no tag; delete it first.`);
  return found;
}

// Precedence of two SemVer versions without the "v": negative, zero or positive.
export function compareVersions(a, b) {
  const parse = version => {
    const [core, pre] = version.split("+")[0].split(/-(.*)/s);
    return { core: core.split(".").map(Number), pre: pre === undefined ? [] : pre.split(".") };
  };
  const x = parse(a), y = parse(b);
  for (let i = 0; i < 3; i++) if (x.core[i] !== y.core[i]) return x.core[i] - y.core[i];
  if (!x.pre.length || !y.pre.length) return y.pre.length - x.pre.length;
  for (let i = 0; i < Math.max(x.pre.length, y.pre.length); i++) {
    const [p, q] = [x.pre[i], y.pre[i]];
    if (p === undefined || q === undefined) return p === undefined ? -1 : 1;
    if (p === q) continue;
    const [m, n] = [/^\d+$/.test(p), /^\d+$/.test(q)];
    if (m && n) return Number(p) - Number(q);
    if (m !== n) return m ? -1 : 1;
    return p < q ? -1 : 1;
  }
  return 0;
}

// The SDK tag released before this version: the highest v<version> tag below it.
// GitHub would otherwise start the generated notes at the latest release before the
// new tag, which can be an editor-v* release (D078.2, #278). undefined when none exists.
export function previousTag(version, spawn = spawnSync) {
  const result = spawn("gh", ["api", "--paginate", `repos/${REPOSITORY}/git/matching-refs/tags/v`, "--jq", ".[].ref"], { encoding: "utf8" });
  if (result.status !== 0) throw ghFailure("Tag listing", result);
  return result.stdout.split("\n").map(ref => /^refs\/tags\/v(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)$/.exec(ref.trim())?.[1])
    .filter(found => found !== undefined && compareVersions(found, version) < 0)
    .sort(compareVersions).map(found => `v${found}`).at(-1);
}

// GitHub marks every release before 1.0 as a prerelease, and from 1.0 every version with a
// prerelease suffix (1.0.0-rc.1); a stable version from 1.0.0 is a full release.
export const githubPrerelease = version => /^0\./.test(version) || version.split("+")[0].includes("-");

export function releaseCheck(version, source, spawn = spawnSync) {
  releaseNotes(version);
  const found = existingRelease(version, source, spawn);
  if (found?.isDraft) return `Would resume the draft release v${version} for ${source} (${found.url}).`;
  if (found) return `GitHub release v${version} already exists for ${source} (${found.url}); a rerun keeps it.`;
  const start = previousTag(version, spawn);
  return `Would create the ${githubPrerelease(version) ? "prerelease" : "release"} v${version} at ${source}, `
    + (start ? `its generated notes starting at ${start}.` : "with generated notes from the start of the history.");
}

// A rerun after a partial publication keeps a release of this exact tag and commit,
// uploads only assets a draft is missing and publishes it, so tag-latest can still run.
// A published release is never changed: with immutable releases its assets cannot be
// added, so a published release missing an asset (for example one created before the
// SBOM existed) is kept as it is with a warning. The attestations still cover those files.
// Hand-written notes for a version, if eng/release/notes/<version>.md exists. The
// release pull request assembles them from the fragments in eng/release/notes/<version>/
// (D066.33); a release whose fragments were not assembled stops, also in a dry run.
export function releaseNotes(version, directory = resolve(root, "eng/release/notes")) {
  if (existsSync(join(directory, version)))
    throw new Error(`eng/release/notes/${version}/ holds unassembled release-note fragments; run bun eng/release/notes.mjs assemble ${version} in the release pull request.`);
  const path = join(directory, `${version}.md`);
  return existsSync(path) ? path : undefined;
}

// gh puts the --notes-file text before the notes that --generate-notes adds; those
// start at the previous SDK tag, never at an editor-v* tag.
export function createRelease(version, source, files, spawn = spawnSync, notes = releaseNotes(version)) {
  const tag = `v${version}`;
  const found = existingRelease(version, source, spawn);
  const gh = (args, what) => {
    if (spawn("gh", args, { stdio: "inherit" }).status !== 0) throw new Error(`Could not ${what} GitHub release ${tag}.`);
  };
  const prerelease = githubPrerelease(version);
  if (!found) {
    const start = previousTag(version, spawn);
    gh(["release", "create", tag, ...files, "--repo", REPOSITORY, "--target", source, ...(prerelease ? ["--prerelease"] : []), "--generate-notes",
      ...(start ? ["--notes-start-tag", start] : []), ...(notes ? ["--notes-file", notes] : [])], "create");
  }
  else {
    const present = new Set((found.assets ?? []).map(asset => asset.name));
    const missing = files.filter(file => !present.has(basename(file)));
    if (!missing.length && !found.isDraft) return `GitHub release ${tag} already exists for ${source} with every asset.`;
    if (!found.isDraft) {
      const names = missing.map(file => basename(file)).join(", ");
      console.log(`::warning title=Published release kept unchanged::GitHub release ${tag} is published without ${names}; published releases are not modified, so these assets are not added.`);
      return `GitHub release ${tag} already exists for ${source}; kept unchanged without ${names}.`;
    }
    if (missing.length) gh(["release", "upload", tag, ...missing, "--repo", REPOSITORY], "complete");
    if (found.isDraft) gh(["release", "edit", tag, "--repo", REPOSITORY, "--draft=false", `--prerelease=${prerelease}`], "publish");
  }
  return `${found ? "Completed" : "Created"} GitHub release ${tag} at ${source}.`;
}

const head = () => execFileSync("git", ["rev-parse", "HEAD"], { cwd: root, encoding: "utf8" }).trim();

async function main([command, ...args]) {
  if (command === "find-ci") {
    const { GITHUB_REPOSITORY: repository, GITHUB_SHA: sha, GH_TOKEN: token, GITHUB_OUTPUT: output } = process.env;
    assert.ok(repository && sha && token && output, "Run in GitHub Actions with GH_TOKEN");
    const found = await findCiPackages({ repository, sha, token });
    appendFileSync(output, `run-id=${found.runId}\nartifact-id=${found.artifactId}\n`);
    console.log(`Reusing ${found.artifact} (artifact ${found.artifactId}) from ${found.runUrl}`);
  } else if (command === "prepare" && args.length === 4) {
    const [directory, version, ciRunId, output] = args;
    const manifest = prepare(directory, version, head(), ciRunId, workspace);
    writeFileSync(output, `${JSON.stringify(manifest, null, 2)}\n`);
    console.log(`Verified ${manifest.packages.length} packages for ${version} at ${manifest.source}`);
  } else if (command === "verify" && args.length === 4) {
    const [directory, inventory, version, ciRunId] = args;
    verify(directory, JSON.parse(readFileSync(inventory, "utf8")), head(), version, ciRunId, workspace);
  } else if (command === "describe" && args.length === 3) {
    const [directory, version, output] = args;
    const source = head();
    const epoch = Number(execFileSync("git", ["show", "-s", "--format=%ct", source], { cwd: root, encoding: "utf8" }).trim());
    const path = describe(directory, version, source, output, epoch);
    if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `sbom=${basename(path)}\n`);
  } else if (command === "release-check" && args.length === 1) {
    console.log(releaseCheck(args[0], head()));
  } else if (command === "release" && args.length === 3) {
    const [version, directory, sbom] = args;
    const files = [...releaseFiles(directory), sbom];
    for (const file of files) assert.ok(existsSync(file), `Missing release asset ${file}`);
    console.log(createRelease(version, head(), files));
  } else throw new Error("Use find-ci, prepare <packages> <version> <ci-run-id> <inventory>, verify <packages> <inventory> <version> <ci-run-id>, describe <packages> <version> <output>, release-check <version>, or release <version> <packages> <sbom>");
}

if (import.meta.main) main(process.argv.slice(2)).catch(error => {
  console.log(`::error title=Release check failed::${error.message}`);
  process.exitCode = 1;
});
