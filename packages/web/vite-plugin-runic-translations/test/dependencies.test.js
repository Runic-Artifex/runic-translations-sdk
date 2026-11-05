import assert from "node:assert/strict";
import { mkdtemp, mkdir, realpath, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { discoverDependencyCatalogs, findApplicationRoot } from "../dist/dependencies.js";

const fingerprint = `sha256:${"a".repeat(64)}`;

async function json(path, value) {
  await mkdir(join(path, ".."), { recursive: true });
  await writeFile(path, JSON.stringify(value));
}

// A library package with a catalog: package.json#runicTranslations, its packed runic.json and its prebuilt manifest.
async function library(directory, name, catalog, { version = "1.0.0", dependencies, contract = fingerprint } = {}) {
  await json(join(directory, "package.json"), {
    name, version, type: "module", ...(dependencies ? { dependencies } : {}),
    exports: { ".": "./index.js", "./runic-translations": `./translations/${catalog}.esm-v5/web-module-manifest-v3.json` },
    runicTranslations: { catalog, project: `./runic/${catalog}/runic.json` },
  });
  await json(join(directory, "runic", catalog, "runic.json"), { schemaVersion: 1, catalog });
  await json(join(directory, "translations", `${catalog}.esm-v5`, "web-module-manifest-v3.json"), { catalog, contractFingerprint: contract });
}

async function withRoot(run) {
  const root = await realpath(await mkdtemp(join(tmpdir(), "runic-dependencies-")));
  try { await run(root); } finally { await rm(root, { recursive: true, force: true }); }
}

test("discovers catalogs transitively through catalog libraries and deduplicates copies by catalog ID", () => withRoot(async root => {
  await json(join(root, "package.json"), { name: "shop", dependencies: { "@acme/widgets": "1.0.0", "@acme/table": "1.0.0", plain: "1.0.0", missing: "1.0.0" } });
  // widgets ships a catalog and depends on table; a nested copy of the same table version sits below widgets.
  await library(join(root, "node_modules", "@acme", "widgets"), "@acme/widgets", "acme.widgets", { dependencies: { "@acme/table": "1.0.0", "@acme/chart": "1.0.0" } });
  await library(join(root, "node_modules", "@acme", "table"), "@acme/table", "acme.table");
  await library(join(root, "node_modules", "@acme", "widgets", "node_modules", "@acme", "table"), "@acme/table", "acme.table");
  // chart is reached only through widgets.
  await library(join(root, "node_modules", "@acme", "chart"), "@acme/chart", "acme.chart");
  // A package without runicTranslations is not followed.
  await json(join(root, "node_modules", "plain", "package.json"), { name: "plain", version: "1.0.0", dependencies: { "@acme/hidden": "1.0.0" } });
  await library(join(root, "node_modules", "@acme", "hidden"), "@acme/hidden", "acme.hidden");

  // Vite's root may be a subdirectory: the nearest package.json above it is the application's.
  await mkdir(join(root, "src"));
  assert.equal(findApplicationRoot(join(root, "src")), root);
  const { catalogs, warnings } = discoverDependencyCatalogs(join(root, "src"));
  assert.deepEqual(catalogs.map(item => item.catalog), ["acme.table", "acme.widgets", "acme.chart"]);
  const table = catalogs.find(item => item.catalog === "acme.table");
  assert.deepEqual(table.copies.map(copy => copy.packageRoot), [join(root, "node_modules", "@acme", "table"), join(root, "node_modules", "@acme", "widgets", "node_modules", "@acme", "table")],
    "the copy nearest the application first, then the nested copy");
  assert.equal(table.project, join(root, "node_modules", "@acme", "table", "runic", "acme.table", "runic.json"));
  assert.equal(table.copies[0].contractFingerprint, fingerprint);
  assert.equal(warnings.length, 1);
  assert.match(warnings[0], /Dependency 'missing' of the application is not installed/);
}));

test("two versions or two packages with one catalog ID are errors", () => withRoot(async root => {
  await json(join(root, "package.json"), { name: "shop", dependencies: { "@acme/widgets": "1.0.0", "@acme/table": "2.0.0" } });
  await library(join(root, "node_modules", "@acme", "widgets"), "@acme/widgets", "acme.widgets", { dependencies: { "@acme/table": "1.0.0" } });
  await library(join(root, "node_modules", "@acme", "table"), "@acme/table", "acme.table", { version: "2.0.0" });
  await library(join(root, "node_modules", "@acme", "widgets", "node_modules", "@acme", "table"), "@acme/table", "acme.table");
  assert.throws(() => discoverDependencyCatalogs(root), /Catalog 'acme\.table' is installed in two versions: '@acme\/table@2\.0\.0'.*'@acme\/table@1\.0\.0'.*install one version/s);

  await json(join(root, "package.json"), { name: "shop", dependencies: { "@acme/table": "2.0.0", "other-table": "1.0.0" } });
  await library(join(root, "node_modules", "other-table"), "other-table", "acme.table");
  assert.throws(() => discoverDependencyCatalogs(root), /both ship catalog 'acme\.table'/);
}));

test("Yarn Plug'n'Play is reported instead of finding nothing", () => withRoot(async root => {
  await json(join(root, "package.json"), { name: "shop", dependencies: { "@acme/table": "1.0.0" } });
  await writeFile(join(root, ".pnp.cjs"), "");
  assert.throws(() => discoverDependencyCatalogs(root), /Yarn Plug'n'Play.*nodeLinker: node-modules/s);
  // A .pnp.cjs beside an installed node_modules is not Plug'n'Play.
  await library(join(root, "node_modules", "@acme", "table"), "@acme/table", "acme.table");
  assert.deepEqual(discoverDependencyCatalogs(root).catalogs.map(item => item.catalog), ["acme.table"]);
}));

test("a package.json is needed at or above the root", () => withRoot(async root => {
  assert.throws(() => findApplicationRoot(join(root, "src")), /needs the application's package\.json/);
}));
