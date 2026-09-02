#!/usr/bin/env node

import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";

const registry = "https://npm.pkg.github.com";
const outputDirectory = process.argv[2];
const token = process.env.NODE_AUTH_TOKEN;
const authorityPath = process.env.RUNIC_COMPATIBILITY_SET
  ?? path.resolve(import.meta.dirname, "../../.github/runic.compatibility-set.json");

if (!outputDirectory || !token) {
  throw new Error("usage: NODE_AUTH_TOKEN=... node eng/download-github-npm.mjs <directory>");
}

const frontend = JSON.parse(fs.readFileSync(path.resolve("Frontend/package.json"), "utf8"));
const required = new Set(
  Object.keys({ ...frontend.dependencies, ...frontend.devDependencies })
    .filter((identity) => identity.startsWith("@runic-artifex/")),
);
const candidateSet = JSON.parse(fs.readFileSync(path.resolve(authorityPath), "utf8"));
const revisions = new Map(candidateSet.sources.map((source) => [source.repository, source.revision]));
const packages = candidateSet.packages
  .filter(({ ecosystem, identity }) => ecosystem === "npm" && required.has(identity))
  .map(({ identity, source }) => {
    const revision = revisions.get(source);
    if (!/^[0-9a-f]{40}$/u.test(revision ?? "")) {
      throw new Error(`Compatibility authority has no exact source for ${identity}.`);
    }
    required.delete(identity);
    return { identity, version: `1.0.0-ci.sha${revision.slice(0, 16)}` };
  });
if (required.size !== 0) {
  throw new Error(`Compatibility authority omits required npm packages: ${[...required].join(", ")}.`);
}

fs.mkdirSync(outputDirectory, { recursive: true });
for (const { identity, version } of packages) {
  const metadataResponse = await fetch(`${registry}/${encodeURIComponent(identity)}`, {
    headers: { Accept: "application/json", Authorization: `Bearer ${token}` },
  });
  if (!metadataResponse.ok) {
    throw new Error(`Registry metadata request failed for ${identity}: ${metadataResponse.status}`);
  }

  const distribution = (await metadataResponse.json()).versions?.[version]?.dist;
  if (!distribution?.tarball || !distribution.integrity) {
    throw new Error(`GitHub Packages does not contain ${identity}@${version}.`);
  }

  const response = await fetch(distribution.tarball, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!response.ok) throw new Error(`Registry tarball request failed for ${identity}: ${response.status}`);

  const tarball = Buffer.from(await response.arrayBuffer());
  const integrity = `sha512-${crypto.createHash("sha512").update(tarball).digest("base64")}`;
  if (integrity !== distribution.integrity) {
    throw new Error(`Registry integrity mismatch for ${identity}@${version}.`);
  }

  const filename = `${identity.slice(1).replaceAll("/", "-")}-${version}.tgz`;
  fs.writeFileSync(path.join(outputDirectory, filename), tarball);
  console.log(`downloaded: ${identity}@${version}`);
}
