import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";

import { prepareWebPackage } from "./prepare-web-package.mjs";

test("GitHub candidates carry exact source and dependency coordinates", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "runic-translations-package-"));
  fs.mkdirSync(path.join(root, "web"));
  fs.writeFileSync(
    path.join(root, "web", "package.json"),
    `${JSON.stringify({ name: "@runic-artifex/vite-plugin-runic-translations" })}\n`,
  );
  const revision = "a".repeat(40);
  const dependencyVersion = "1.0.0-ci.shabbbbbbbbbbbbbbbb";

  prepareWebPackage(root, "1.0.0-ci.shaaaaaaaaaaaaaaaaa", revision, "github", dependencyVersion);
  const manifest = JSON.parse(fs.readFileSync(path.join(root, "web", "package.json"), "utf8"));

  assert.equal(manifest.version, "1.0.0-ci.shaaaaaaaaaaaaaaaaa");
  assert.equal(manifest.gitHead, revision);
  assert.deepEqual(manifest.publishConfig, {
    registry: "https://npm.pkg.github.com",
    access: "restricted",
  });
  assert.deepEqual(manifest.runicCandidate.dependencies, [
    { ecosystem: "nuget", package: "Runic.CommandLine", version: dependencyVersion },
  ]);
});

test("public packages retain provenance and use npmjs.org", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "runic-translations-package-"));
  fs.mkdirSync(path.join(root, "web"));
  fs.writeFileSync(path.join(root, "web", "package.json"), "{}\n");
  prepareWebPackage(root, "1.0.0", "b".repeat(40), "public", "1.0.0");
  const manifest = JSON.parse(fs.readFileSync(path.join(root, "web", "package.json"), "utf8"));
  assert.deepEqual(manifest.publishConfig, {
    registry: "https://registry.npmjs.org",
    access: "public",
  });
});
