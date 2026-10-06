import { expect, test } from "bun:test";
import { execFileSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createStaging, stageAndPromote, stampNpmManifest } from "./candidate.mjs";

function scratch(callback) {
  const directory = mkdtempSync(join(tmpdir(), "runic-candidate-test-"));
  try { return callback(directory); } finally { rmSync(directory, { recursive: true, force: true }); }
}
const tree = path => readdirSync(path, { recursive: true }).sort();

test("a failed pack leaves the previous candidate and no staging directory", () => scratch(directory => {
  const target = join(directory, "packages");
  mkdirSync(join(target, "npm"), { recursive: true });
  writeFileSync(join(target, "npm", "old.tgz"), "old");
  expect(() => stageAndPromote(target, staging => {
    writeFileSync(join(staging, "partial.tgz"), "partial");
    throw new Error("interrupted");
  })).toThrow("interrupted");
  expect(tree(target)).toEqual(["npm", "npm/old.tgz"]);
  expect(readdirSync(directory)).toEqual(["packages"]);
}));

test("a complete pack replaces the candidate as a whole", () => scratch(directory => {
  const target = join(directory, "packages");
  mkdirSync(target);
  writeFileSync(join(target, "stale.tgz"), "stale");
  stageAndPromote(target, staging => writeFileSync(join(staging, "new.tgz"), "new"));
  expect(tree(target)).toEqual(["new.tgz"]);
  expect(readdirSync(directory)).toEqual(["packages"]);
}));

test("leftovers from a killed pack are removed before the next pack", () => scratch(directory => {
  const target = join(directory, "packages");
  mkdirSync(join(directory, ".packages-staging-killed"));
  mkdirSync(join(directory, ".packages-previous-1"));
  writeFileSync(join(directory, ".packages-previous-1", "old.tgz"), "old");
  const staging = createStaging(target);
  expect(readdirSync(directory).sort()).toEqual([staging.split(/[\\/]/).pop(), "packages"].sort());
  expect(tree(target)).toEqual(["old.tgz"]);
}));

test("stamping rewrites only the archived manifest", () => scratch(directory => {
  const source = join(directory, "source");
  mkdirSync(join(source, "dist"), { recursive: true });
  const manifest = '{ "name": "@runic-artifex/candidate-test", "version": "1.0.0", "files": ["dist"] }\n';
  writeFileSync(join(source, "package.json"), manifest);
  writeFileSync(join(source, "dist", "index.js"), "export const value = 1;\n".repeat(100));
  execFileSync("bun", ["pm", "pack", "--quiet", "--destination", directory], { cwd: source, stdio: "ignore" });
  const archive = join(directory, "runic-artifex-candidate-test-1.0.0.tgz");
  stampNpmManifest(archive, { version: "1.0.0-local", gitHead: "a".repeat(40) });
  expect(readFileSync(join(source, "package.json"), "utf8")).toBe(manifest);
  const packed = JSON.parse(execFileSync("tar", ["-xzOf", archive, "package/package.json"], { encoding: "utf8" }));
  expect([packed.version, packed.gitHead]).toEqual(["1.0.0-local", "a".repeat(40)]);
  expect(execFileSync("tar", ["-xzOf", archive, "package/dist/index.js"], { encoding: "utf8" }))
    .toBe("export const value = 1;\n".repeat(100));
}));
