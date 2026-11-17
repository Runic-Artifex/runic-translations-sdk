import { expect, test } from "bun:test";
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve } from "node:path";
import { stampNpmManifest } from "./candidate.mjs";
import { assertNpmSource, dirtyNpmInputs, npmInputs, npmSourceCommit, sharedNpmInputs } from "./npm-inputs.mjs";

const root = resolve(import.meta.dirname, "../..");
const workspace = JSON.parse(readFileSync(join(root, "eng/workspace.json"), "utf8"));
const sha256 = bytes => createHash("sha256").update(bytes).digest("hex");

// A repository with a shared build input and two packages: kit depends on svelte.
function repository(callback) {
  const directory = mkdtempSync(join(tmpdir(), "runic-translations-npm-inputs-"));
  const git = (...args) => execFileSync("git", ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false", ...args],
    { cwd: directory, encoding: "utf8" }).trim();
  const write = (path, text) => { mkdirSync(dirname(join(directory, path)), { recursive: true }); writeFileSync(join(directory, path), text); };
  const commit = (path, text) => { write(path, text); git("add", "--", path); git("commit", "-q", "-m", `change ${path}`); return git("rev-parse", "HEAD"); };
  const manifest = (name, dependencies = {}) => `${JSON.stringify({ name, version: "1.0.0",
    repository: "https://github.com/Runic-Artifex/runic-translations-sdk", files: ["dist"], devDependencies: dependencies }, null, 2)}\n`;
  const local = { npm: [{ name: "@runic-artifex/kit", path: "packages/web/kit" }, { name: "@runic-artifex/svelte", path: "packages/web/svelte" }] };
  try {
    git("init", "-q", "-b", "main");
    write("eng/workspace.json", JSON.stringify(local));
    write("packages/web/svelte/package.json", manifest("@runic-artifex/svelte"));
    write("packages/web/svelte/dist/index.js", "export const svelte = 1;\n");
    write("packages/web/kit/package.json", manifest("@runic-artifex/kit", { "@runic-artifex/svelte": "1.0.0" }));
    write("packages/web/kit/dist/index.js", "export const kit = 1;\n");
    write("bun.lock", "{}\n");
    write("docs/notes.md", "notes\n");
    git("add", ".");
    git("commit", "-q", "-m", "initial");
    return callback({ directory, git, commit, workspace: local });
  } finally { rmSync(directory, { recursive: true, force: true }); }
}

// Packs as eng/run.mjs packNpm does: bun pm pack, then the version and gitHead stamped into the archive.
function pack(directory, workspace, output) {
  mkdirSync(output);
  const packageDirectory = join(directory, "packages/web/kit");
  execFileSync("bun", ["pm", "pack", "--quiet", "--destination", output], { cwd: packageDirectory, stdio: "ignore" });
  const archive = join(output, readdirSync(output)[0]);
  stampNpmManifest(archive, { version: "1.0.0", gitHead: npmSourceCommit("@runic-artifex/kit", { root: directory, workspace }) });
  return archive;
}

test("a package's inputs are its directory, its workspace dependencies and the shared build inputs", () => repository(({ directory, workspace }) => {
  const kit = npmInputs("@runic-artifex/kit", { root: directory, workspace });
  expect(kit).toContain("packages/web/kit");
  expect(kit).toContain("packages/web/svelte");
  for (const input of sharedNpmInputs) expect(kit).toContain(input);
  expect(npmInputs("@runic-artifex/svelte", { root: directory, workspace })).not.toContain("packages/web/kit");
  expect(() => npmInputs("@runic-artifex/missing", { root: directory, workspace })).toThrow("not an npm package");
}));

test("the real input sets name existing paths and cover each package's tsconfig bases", () => {
  for (const input of sharedNpmInputs) expect({ input, exists: existsSync(join(root, input)) }).toEqual({ input, exists: true });
  expect(npmInputs("@runic-artifex/translations-sveltekit", { root, workspace })).toContain("packages/web/translations-svelte");
  for (const { name, path } of workspace.npm) {
    const inputs = npmInputs(name, { root, workspace });
    const directory = join(root, path);
    for (const file of readdirSync(directory).filter(file => /^tsconfig.*\.json$/.test(file))) {
      const base = JSON.parse(readFileSync(join(directory, file), "utf8")).extends;
      if (!base?.startsWith(".")) continue;
      const reference = relative(root, resolve(directory, base));
      expect({ name, reference, covered: inputs.some(input => reference === input || reference.startsWith(`${input}/`)) })
        .toEqual({ name, reference, covered: true });
    }
  }
});

test("packing at two commits that differ only outside the inputs gives byte-identical archives", () => repository(({ directory, commit, workspace }) => {
  const first = readFileSync(pack(directory, workspace, join(directory, "out-1")));
  commit("docs/notes.md", "other notes\n");
  commit(".github/workflows/ci.yml", "on: push\n");
  const second = readFileSync(pack(directory, workspace, join(directory, "out-2")));
  expect(sha256(second)).toBe(sha256(first));
  // The gzip header names no host operating system.
  expect(second[9]).toBe(0xff);
}));

test("a change to an input changes the gitHead, a change elsewhere does not", () => repository(({ directory, git, commit, workspace }) => {
  const source = name => npmSourceCommit(name, { root: directory, workspace });
  const initial = git("rev-parse", "HEAD");
  commit("docs/notes.md", "other notes\n");
  expect(source("@runic-artifex/kit")).toBe(initial);
  const svelte = commit("packages/web/svelte/dist/index.js", "export const svelte = 2;\n");
  expect(source("@runic-artifex/svelte")).toBe(svelte);
  expect(source("@runic-artifex/kit")).toBe(svelte);
  const shared = commit("bun.lock", "{\"lockfileVersion\":1}\n");
  expect(source("@runic-artifex/svelte")).toBe(shared);
  const kit = commit("packages/web/kit/dist/index.js", "export const kit = 2;\n");
  expect(source("@runic-artifex/kit")).toBe(kit);
  expect(source("@runic-artifex/svelte")).toBe(shared);
  expect(dirtyNpmInputs("@runic-artifex/kit", { root: directory, workspace })).toEqual([]);
  writeFileSync(join(directory, "packages/web/svelte/dist/index.js"), "export const svelte = 3;\n");
  expect(dirtyNpmInputs("@runic-artifex/kit", { root: directory, workspace })).toEqual([" M packages/web/svelte/dist/index.js"]);
}));

test("the release check accepts an ancestor with the same inputs and refuses every other source", () => repository(({ directory, git, commit, workspace }) => {
  const check = (stamped, release) => assertNpmSource("@runic-artifex/kit", stamped, release, { root: directory, workspace });
  const stamped = git("rev-parse", "HEAD");
  const release = commit("docs/notes.md", "release notes\n");
  check(stamped, release);
  check(release, release);
  expect(() => check(undefined, release)).toThrow("which is no commit");
  expect(() => check("b".repeat(40), release)).toThrow("or an ancestor of it");
  const later = commit("docs/notes.md", "later notes\n");
  expect(() => check(later, release)).toThrow("or an ancestor of it");
  git("checkout", "-q", "-b", "side", stamped);
  const side = commit("docs/side.md", "side\n");
  git("checkout", "-q", "main");
  expect(() => check(side, later)).toThrow("or an ancestor of it");
  const changed = commit("packages/web/svelte/dist/index.js", "export const svelte = 2;\n");
  expect(() => check(stamped, changed)).toThrow("its inputs changed before");
  check(changed, commit("docs/notes.md", "after\n"));
}));

test("the source commit needs the full history", () => repository(({ directory, commit, workspace }) => {
  commit("docs/notes.md", "second\n");
  const clone = join(directory, "shallow");
  execFileSync("git", ["clone", "-q", "--depth", "1", `file://${directory}`, clone]);
  expect(() => npmSourceCommit("@runic-artifex/kit", { root: clone, workspace })).toThrow("fetch-depth: 0");
}));
