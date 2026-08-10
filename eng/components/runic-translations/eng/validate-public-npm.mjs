#!/usr/bin/env node

import { readdirSync } from "node:fs";
import { resolve } from "node:path";
import { spawnSync } from "node:child_process";

const [, , version, suppliedDirectory, repositoryCommit] = process.argv;
const expectedName = "@runic-artifex/vite-plugin-runic-translations";
const expectedRepository = "git+https://github.com/Runic-Artifex/runic-translations.git";

if (!/^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$/u.test(version ?? "")) {
  throw new Error(`Invalid package version '${version}'.`);
}
if (!/^[0-9a-f]{40}$/iu.test(repositoryCommit ?? "")) {
  throw new Error("Repository commit must be a full Git commit.");
}

const directory = resolve(suppliedDirectory);
const archives = readdirSync(directory).filter((file) => file.endsWith(".tgz")).sort();
if (archives.length !== 1) {
  throw new Error(`Expected one npm package, found ${archives.length}.`);
}

const archivePath = resolve(directory, archives[0]);
const manifestResult = spawnSync("tar", ["-xOf", archivePath, "package/package.json"], { encoding: "utf8" });
const entriesResult = spawnSync("tar", ["-tzf", archivePath], { encoding: "utf8" });
if (manifestResult.status !== 0 || entriesResult.status !== 0) {
  throw new Error(`Could not inspect ${archives[0]}.`);
}

const manifest = JSON.parse(manifestResult.stdout);
const entries = new Set(entriesResult.stdout.split("\n").filter(Boolean));
if (manifest.name !== expectedName) throw new Error(`Unexpected package name '${manifest.name}'.`);
if (manifest.version !== version) throw new Error(`${expectedName} has version '${manifest.version}'.`);
if (manifest.private === true) throw new Error(`${expectedName} is marked private.`);
if (manifest.license !== "MIT") throw new Error(`${expectedName} must use MIT.`);
if (typeof manifest.description !== "string" || manifest.description.length < 20) {
  throw new Error(`${expectedName} must provide a meaningful description.`);
}
if (manifest.repository?.url !== expectedRepository || manifest.repository?.directory !== "web") {
  throw new Error(`${expectedName} has invalid repository provenance.`);
}
if (manifest.gitHead !== repositoryCommit) {
  throw new Error(`${expectedName} has gitHead '${manifest.gitHead}'; expected '${repositoryCommit}'.`);
}
if (manifest.publishConfig?.registry !== "https://registry.npmjs.org" || manifest.publishConfig?.access !== "public") {
  throw new Error(`${expectedName} is not staged for public npm publication.`);
}
for (const entry of ["package/README.md", "package/index.js", "package/index.d.ts"]) {
  if (!entries.has(entry)) throw new Error(`${expectedName} does not include ${entry.replace("package/", "")}.`);
}

console.log(`Validated the public npm artifact for ${expectedName}@${version}.`);
