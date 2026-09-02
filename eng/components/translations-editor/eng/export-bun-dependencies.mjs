#!/usr/bin/env node

import { createHash } from "node:crypto";
import { readdir, readFile, realpath } from "node:fs/promises";
import { join, resolve } from "node:path";

const [lockArgument, frontendArgument] = process.argv.slice(2);
if (!lockArgument || !frontendArgument) {
  throw new Error("usage: node eng/export-bun-dependencies.mjs <bun.lock> <frontend-root>");
}

function parseJsonc(text) {
  let result = "";
  let inString = false;
  let escaped = false;
  for (let index = 0; index < text.length; index++) {
    const character = text[index];
    if (inString) {
      result += character;
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === '"') inString = false;
      continue;
    }
    if (character === '"') {
      inString = true;
      result += character;
      continue;
    }
    if (character === ",") {
      let next = index + 1;
      while (/\s/u.test(text[next] ?? "")) next++;
      if (text[next] === "}" || text[next] === "]") continue;
    }
    result += character;
  }
  return JSON.parse(result);
}

const digest = (value) => createHash("sha256").update(value).digest("hex");
const lockPath = resolve(lockArgument);
const frontendRoot = resolve(frontendArgument);
const lock = parseJsonc(await readFile(lockPath, "utf8"));
if (lock.lockfileVersion !== 2 || typeof lock.packages !== "object") {
  throw new Error(`Unsupported Bun lockfile '${lockPath}'.`);
}

const locked = Object.entries(lock.packages).filter(([, entry]) => Array.isArray(entry));
const packageRoots = [];
const visitedNodeModules = new Set();
async function collectNodeModules(nodeModules) {
  let canonical;
  try {
    canonical = await realpath(nodeModules);
  } catch {
    return;
  }
  if (visitedNodeModules.has(canonical)) return;
  visitedNodeModules.add(canonical);

  for (const entry of await readdir(nodeModules, { withFileTypes: true })) {
    if (entry.name === ".bin") continue;
    const entryPath = join(nodeModules, entry.name);
    if (entry.name.startsWith("@")) {
      for (const scoped of await readdir(entryPath, { withFileTypes: true })) {
        if (scoped.isDirectory() || scoped.isSymbolicLink()) packageRoots.push(join(entryPath, scoped.name));
      }
    } else if (entry.isDirectory() || entry.isSymbolicLink()) {
      packageRoots.push(entryPath);
    }
  }
}

await collectNodeModules(join(frontendRoot, "node_modules"));
for (let index = 0; index < packageRoots.length; index++) {
  await collectNodeModules(join(packageRoots[index], "node_modules"));
}

const dependencies = new Map();
for (const packageRoot of packageRoots) {
  let bytes;
  try {
    bytes = await readFile(join(packageRoot, "package.json"));
  } catch {
    continue;
  }
  const metadata = JSON.parse(bytes.toString("utf8"));
  const name = metadata.name;
  const version = metadata.version;
  if (!name || !version || dependencies.has(`${name}@${version}`)) continue;

  const registryEntry = locked.find(([, [resolution]]) => resolution === `${name}@${version}`)?.[1];
  const archiveEntry = locked.find(([identity, [resolution]]) =>
    identity === name
    && name.startsWith("@runic-artifex/")
    && typeof resolution === "string"
    && resolution.includes(version)
    && resolution.endsWith(".tgz"))?.[1];
  const lockEntry = registryEntry ?? archiveEntry;
  if (!lockEntry) throw new Error(`Installed Bun dependency '${name}@${version}' is absent from the lockfile.`);
  const archive = lockEntry === archiveEntry;
  const integrity = archive ? lockEntry[2] : lockEntry[3];
  if (typeof integrity !== "string" || !integrity.startsWith("sha512-")) {
    throw new Error(`Bun dependency '${name}@${version}' lacks locked integrity metadata.`);
  }
  let source = archive ? `https://npm.pkg.github.com/${name}` : lockEntry[1];
  if (typeof source !== "string") throw new Error(`Bun dependency '${name}@${version}' has an invalid source.`);
  if (source.includes("127.0.0.1") || source.includes("localhost")) {
    throw new Error(`Bun dependency '${name}@${version}' retains an ephemeral registry source.`);
  }
  if (!source) {
    source = name.startsWith("@runic-artifex/")
      ? `https://npm.pkg.github.com/${name}`
      : `https://registry.npmjs.org/${name}`;
  }
  const license = typeof metadata.license === "string" && metadata.license.trim() ? metadata.license : "NOASSERTION";
  const metadataSha256 = digest(`${name}\n${version}\n${source}\n${integrity}\n${license}`);
  dependencies.set(`${name}@${version}`, {
    name,
    version,
    integrity,
    source,
    license,
    metadataSha256,
    installedMetadataSha256: digest(bytes),
    ecosystem: "npm",
  });
}

process.stdout.write(`${JSON.stringify([...dependencies.values()].sort((left, right) =>
  left.name.localeCompare(right.name) || left.version.localeCompare(right.version)))}\n`);
