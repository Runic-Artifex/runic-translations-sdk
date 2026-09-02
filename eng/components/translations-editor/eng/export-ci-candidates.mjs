#!/usr/bin/env node

import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const authorityPath = process.env.RUNIC_COMPATIBILITY_SET
  ?? resolve(import.meta.dirname, "../../.github/runic.compatibility-set.json");
const candidateSet = JSON.parse(readFileSync(resolve(authorityPath), "utf8"));
const versionProperties = new Map([
  ["runic-command-line", "RunicCommandLinePackageVersion"],
  ["runic-assets", "RunicAssetsPackageVersion"],
  ["runic-translations", "RunicTranslationsPackageVersion"],
  ["runic-toolkit", "RunicApplicationPackageVersion"],
]);

if (candidateSet.schemaVersion !== 1 || candidateSet.publication !== "forbidden" || !Array.isArray(candidateSet.sources)) {
  throw new Error("The compatibility authority must be a publication-forbidden schema version 1 set.");
}

const repositories = new Set();
for (const source of candidateSet.sources) {
  if (!/^[0-9a-f]{40}$/u.test(source.revision) || repositories.has(source.repository)) {
    throw new Error(`Invalid or duplicate CI source '${source.repository}'.`);
  }
  repositories.add(source.repository);

  const property = versionProperties.get(source.repository);
  if (property) process.stdout.write(`${property}=1.0.0-ci.sha${source.revision.slice(0, 16)}\n`);
}

for (const [repository, property] of versionProperties) {
  if (!repositories.has(repository)) throw new Error(`The CI candidate set must include ${repository} for ${property}.`);
}

const translations = candidateSet.sources.find(({ repository }) => repository === "runic-translations");
process.stdout.write(`RUNIC_TRANSLATIONS_TOOL_VERSION=1.0.0-ci.sha${translations.revision.slice(0, 16)}\n`);
