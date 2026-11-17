// The source commit of an npm archive (D069.2; eng/release/README.md, "npm archive source commit"): the commit that
// last changed the package's inputs, not the build's HEAD. Bun packs with fixed mtimes, modes and entry order, so the
// archive bytes depend only on these inputs and the stamped version, and a commit outside them (a workflow, a .NET
// project, the editor, a document) leaves the archive, and so its integrity in runic-sdk's template locks, unchanged.
//
// A package's inputs are its directory, the directories of the workspace packages it depends on (transitively), and
// the shared build inputs. Keep the set conservative: a missing input lets two different archives share a gitHead.
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { join, resolve } from "node:path";

const defaultRoot = resolve(import.meta.dirname, "../..");

// Files and directories every npm archive's bytes may depend on: the workspace manifest and lockfile (tool and
// dependency versions), the toolchain (Bun, Node.js), the shared TypeScript base, the version authority, and the
// packing and stamping code.
export const sharedNpmInputs = [
  ".node-version",
  "bun.lock",
  "eng/release/candidate.mjs",
  "eng/release/npm-inputs.mjs",
  "eng/release/readme-links.mjs",
  "eng/run.mjs",
  "eng/typescript",
  "eng/workspace.json",
  "flake.lock",
  "flake.nix",
  "global.json",
  "nix/toolchain",
  "package.json",
];

const git = (root, args) => execFileSync("git", args, { cwd: root, encoding: "utf8" }).trim();
const workspaceOf = root => JSON.parse(readFileSync(join(root, "eng/workspace.json"), "utf8"));

// The sorted input paths of the npm package `name`, relative to the repository root.
export function npmInputs(name, { root = defaultRoot, workspace = workspaceOf(root) } = {}) {
  const byName = new Map(workspace.npm.map(entry => [entry.name, entry]));
  assert.ok(byName.has(name), `${name} is not an npm package of eng/workspace.json`);
  const inputs = new Set(sharedNpmInputs);
  const pending = [name], seen = new Set();
  while (pending.length) {
    const current = pending.pop();
    if (seen.has(current)) continue;
    seen.add(current);
    const { path } = byName.get(current);
    inputs.add(path);
    const manifest = JSON.parse(readFileSync(join(root, path, "package.json"), "utf8"));
    for (const field of ["dependencies", "devDependencies", "optionalDependencies", "peerDependencies"])
      for (const dependency of Object.keys(manifest[field] ?? {}))
        if (byName.has(dependency)) pending.push(dependency);
  }
  return [...inputs].sort();
}

function assertFullHistory(root) {
  assert.notEqual(git(root, ["rev-parse", "--is-shallow-repository"]), "true",
    "The npm source commit needs the full history: check out with fetch-depth: 0");
}

// The commit that last changed the inputs of `name` at `revision`: the gitHead its archive carries.
export function npmSourceCommit(name, { root = defaultRoot, workspace, revision = "HEAD" } = {}) {
  assertFullHistory(root);
  const commit = git(root, ["log", "-1", "--format=%H", revision, "--", ...npmInputs(name, { root, workspace })]);
  assert.match(commit, /^[a-f0-9]{40}$/, `No commit changes the inputs of ${name}`);
  return commit;
}

// Uncommitted changes to the inputs of `name`: the archive would not be the stamped commit's.
export function dirtyNpmInputs(name, { root = defaultRoot, workspace } = {}) {
  return execFileSync("git", ["status", "--porcelain", "--", ...npmInputs(name, { root, workspace })], { cwd: root, encoding: "utf8" })
    .split("\n").filter(Boolean);
}

// The release check: an npm archive built from `release` names as its source a commit (`stamped`) that is `release`
// or an ancestor of it, and the inputs of `name` are the same in both, so the archive is the release commit's.
export function assertNpmSource(name, stamped, release, { root = defaultRoot, workspace, file = name } = {}) {
  assert.match(stamped ?? "", /^[a-f0-9]{40}$/, `${file} was packed from ${stamped}, which is no commit`);
  if (stamped === release) return;
  assertFullHistory(root);
  const ancestor = spawnSync("git", ["merge-base", "--is-ancestor", stamped, release], { cwd: root });
  assert.equal(ancestor.status, 0, `${file} was packed from ${stamped}, which is not ${release} or an ancestor of it`);
  const changed = git(root, ["diff", "--name-only", stamped, release, "--", ...npmInputs(name, { root, workspace })]);
  assert.equal(changed, "", `${file} was packed from ${stamped}, but its inputs changed before ${release}:\n${changed}`);
}
