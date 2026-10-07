// Reuse the package and VSIX artifacts of the successful CI push run for a release.
//   find-ci                                              green ci.yml push run on main for GITHUB_SHA and its artifacts
//   prepare <packages> <version> <ci-run> <out> <vsix>   check packages and VSIX files; write an inventory
//   verify <packages> <inventory> <version> <run> <vsix> check them against the inventory, version and CI run
//   describe <packages> <vsix> <version> <out>           write the CycloneDX SBOM of the release (local, deterministic)
//   release-check <version>                              tag and any release must be absent or at this commit (read-only)
//   release <version> <packages> <vsix> <sbom>           create the prerelease, or finish one that exists for this commit
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { appendFileSync, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { root, workspace } from "../run.mjs";
import { ideVersions, vsixMetadata } from "./ide-versions.mjs";

export const REPOSITORY = "Runic-Artifex/runic-translations-sdk";
export const CI_WORKFLOW = ".github/workflows/ci.yml";
export const ARTIFACT = "runic-translations-packages";
// The IDE extensions CI builds and stamps for this commit; released as GitHub release assets.
export const VSIX = [
  { artifact: "rmf2-vscode-vsix", file: "runic-translations.vsix", id: "runic-translations", publisher: "runic-artifex", format: "vscode" },
  { artifact: "rmf2-visualstudio-vsix", file: "runic-translations-visualstudio.vsix", id: "Runic.Artifex.Translations.Rmf2",
    publisher: "Runic Artifex", format: "visualStudio" },
];
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
  const artifacts = (await api(`actions/runs/${run.id}/artifacts?per_page=100`)).artifacts;
  const artifact = selectArtifact(artifacts, run);
  const vsix = VSIX.map(entry => selectArtifact(artifacts, run, entry.artifact));
  // Download by id: re-uploading under the same name creates a new id, so the
  // bytes downloaded later are exactly the artifacts selected here.
  return { runId: String(run.id), runUrl: run.html_url, artifact: artifact.name, artifactId: String(artifact.id),
    vsixArtifactIds: vsix.map(entry => entry.id).join(",") };
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

// Exactly the two IDE extensions, one per CI artifact directory, with their identities and the
// version CI stamped for this release (see ide-versions.mjs); VS Code is a pre-release, with the
// Marketplace "Preview" label, exactly for a preview.
export function scanVsix(directory, version) {
  const versions = ideVersions(version);
  assert.deepEqual(readdirSync(directory).sort(), VSIX.map(entry => entry.artifact).sort(), "VSIX directory must contain exactly the IDE extension artifacts");
  return VSIX.map(({ artifact, file, id, publisher, format }) => {
    assert.deepEqual(readdirSync(join(directory, artifact)), [file], `${artifact} must contain only ${file}`);
    const path = resolve(directory, artifact, file);
    assert.ok(lstatSync(path).isFile(), `${file} must be a regular file`);
    const { packageVersion, packagePreview, preRelease, preview, ...identity } = vsixMetadata(path);
    assert.equal(identity.id, id, `${file} declares extension ${identity.id}`);
    assert.equal(identity.publisher, publisher, `${file} declares publisher ${identity.publisher}`);
    assert.equal(identity.version, versions[format], `${file} declares version ${identity.version}, not ${versions[format]} for ${version}`);
    if (format === "vscode") {
      assert.equal(packageVersion, versions.vscode, `${file} bundles package.json version ${packageVersion}, not ${versions.vscode}`);
      assert.equal(preRelease, versions.vscodePreRelease, `${file} ${preRelease ? "is" : "is not"} marked pre-release`);
      assert.equal(preview, versions.vscodePreRelease, `${file} ${preview ? "has" : "lacks"} the Marketplace Preview flag`);
      assert.equal(packagePreview, versions.vscodePreRelease, `${file} bundles package.json preview ${packagePreview}`);
    }
    return { name: artifact, file: `${artifact}/${file}`, sha256: sha256(readFileSync(path)), identity };
  });
}

export function prepare(directory, version, source, ciRunId, inventory = workspace, vsixDirectory) {
  assert.match(ciRunId ?? "", /^[1-9][0-9]*$/, "Expected a CI run id");
  assert.ok(vsixDirectory, "Expected the VSIX directory");
  return { schema: SCHEMA, repository: REPOSITORY, version, source, ciRunId, packages: scan(directory, version, source, inventory),
    vsix: scanVsix(vsixDirectory, version) };
}

export function verify(directory, manifest, source, version, ciRunId, inventory = workspace, vsixDirectory) {
  assert.equal(manifest.schema, SCHEMA, "Not a Translations release inventory");
  assert.equal(manifest.repository, REPOSITORY, "Not a Translations release inventory");
  assert.equal(manifest.source, source, `Inventory is for ${manifest.source}, not ${source}`);
  assert.equal(manifest.version, version, `Inventory is for version ${manifest.version}, not ${version}`);
  assert.equal(manifest.ciRunId, ciRunId, `Inventory is for CI run ${manifest.ciRunId}, not ${ciRunId}`);
  assert.deepEqual(scan(directory, manifest.version, source, inventory), manifest.packages, "Packages differ from the candidate inventory");
  assert.ok(vsixDirectory, "Expected the VSIX directory");
  assert.deepEqual(scanVsix(vsixDirectory, manifest.version), manifest.vsix, "VSIX files differ from the candidate inventory");
}

const releaseFiles = (packages, vsixDirectory) => [
  ...["nuget", "npm"].flatMap(registry => readdirSync(join(packages, registry)).sort().map(file => join(packages, registry, file))),
  ...VSIX.map(entry => join(vsixDirectory, entry.artifact, entry.file))];
export const sbomName = version => `${REPOSITORY.split("/")[1]}-${version}.cdx.json`;

// Writes the SBOM of exactly the release files; the same files and commit always give the same bytes.
// Each VSIX is described with its mapped IDE version instead of the release version.
export function describe(directory, vsixDirectory, version, source, output, epoch, inventory = workspace) {
  const packages = scan(directory, version, source, inventory).map(p => resolve(directory, p.file));
  const vsix = scanVsix(vsixDirectory, version);
  const versions = ideVersions(version);
  mkdirSync(output, { recursive: true });
  const path = join(output, sbomName(version));
  execFileSync("python3", [fileURLToPath(new URL("./sbom.py", import.meta.url)), "--repository", REPOSITORY, "--version", version,
    "--source", source, "--epoch", String(epoch), "--output", path,
    ...VSIX.flatMap(entry => ["--artifact-version", `${entry.file}=${versions[entry.format]}`]),
    ...packages, ...vsix.map(entry => resolve(vsixDirectory, entry.file))], { stdio: "inherit" });
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

export function releaseCheck(version, source, spawn = spawnSync) {
  const found = existingRelease(version, source, spawn);
  if (found?.isDraft) return `Would resume the draft release v${version} for ${source} (${found.url}).`;
  return found ? `GitHub release v${version} already exists for ${source} (${found.url}); a rerun keeps it.`
    : `Would create the prerelease v${version} at ${source}.`;
}

// A rerun after a partial publication keeps a release of this exact tag and commit,
// uploads only assets a draft is missing and publishes it, so tag-latest can still run.
// A published release is never changed: with immutable releases its assets cannot be
// added, so a published release missing an asset (for example one created before the
// SBOM existed) is kept as it is with a warning. The attestations still cover those files.
export function createRelease(version, source, files, spawn = spawnSync) {
  const tag = `v${version}`;
  const found = existingRelease(version, source, spawn);
  const gh = (args, what) => {
    if (spawn("gh", args, { stdio: "inherit" }).status !== 0) throw new Error(`Could not ${what} GitHub release ${tag}.`);
  };
  if (!found) gh(["release", "create", tag, ...files, "--repo", REPOSITORY, "--target", source, "--prerelease", "--generate-notes"], "create");
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
    if (found.isDraft) gh(["release", "edit", tag, "--repo", REPOSITORY, "--draft=false", "--prerelease"], "publish");
  }
  return `${found ? "Completed" : "Created"} GitHub release ${tag} at ${source}.`;
}

const head = () => execFileSync("git", ["rev-parse", "HEAD"], { cwd: root, encoding: "utf8" }).trim();

async function main([command, ...args]) {
  if (command === "find-ci") {
    const { GITHUB_REPOSITORY: repository, GITHUB_SHA: sha, GH_TOKEN: token, GITHUB_OUTPUT: output } = process.env;
    assert.ok(repository && sha && token && output, "Run in GitHub Actions with GH_TOKEN");
    const found = await findCiPackages({ repository, sha, token });
    appendFileSync(output, `run-id=${found.runId}\nartifact-id=${found.artifactId}\nvsix-artifact-ids=${found.vsixArtifactIds}\n`);
    console.log(`Reusing ${found.artifact} (artifact ${found.artifactId}) and VSIX artifacts ${found.vsixArtifactIds} from ${found.runUrl}`);
  } else if (command === "prepare" && args.length === 5) {
    const [directory, version, ciRunId, output, vsixDirectory] = args;
    const manifest = prepare(directory, version, head(), ciRunId, workspace, vsixDirectory);
    writeFileSync(output, `${JSON.stringify(manifest, null, 2)}\n`);
    console.log(`Verified ${manifest.packages.length} packages and ${manifest.vsix.length} VSIX files for ${version} at ${manifest.source}`);
  } else if (command === "verify" && args.length === 5) {
    const [directory, inventory, version, ciRunId, vsixDirectory] = args;
    verify(directory, JSON.parse(readFileSync(inventory, "utf8")), head(), version, ciRunId, workspace, vsixDirectory);
  } else if (command === "describe" && args.length === 4) {
    const [directory, vsixDirectory, version, output] = args;
    const source = head();
    const epoch = Number(execFileSync("git", ["show", "-s", "--format=%ct", source], { cwd: root, encoding: "utf8" }).trim());
    const path = describe(directory, vsixDirectory, version, source, output, epoch);
    if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `sbom=${basename(path)}\n`);
  } else if (command === "release-check" && args.length === 1) {
    console.log(releaseCheck(args[0], head()));
  } else if (command === "release" && args.length === 4) {
    const [version, directory, vsixDirectory, sbom] = args;
    const files = [...releaseFiles(directory, vsixDirectory), sbom];
    for (const file of files) assert.ok(existsSync(file), `Missing release asset ${file}`);
    console.log(createRelease(version, head(), files));
  } else throw new Error("Use find-ci, prepare <packages> <version> <ci-run-id> <inventory> <vsix>, verify <packages> <inventory> <version> <ci-run-id> <vsix>, describe <packages> <vsix> <version> <output>, release-check <version>, or release <version> <packages> <vsix> <sbom>");
}

if (import.meta.main) main(process.argv.slice(2)).catch(error => {
  console.log(`::error title=Release check failed::${error.message}`);
  process.exitCode = 1;
});
