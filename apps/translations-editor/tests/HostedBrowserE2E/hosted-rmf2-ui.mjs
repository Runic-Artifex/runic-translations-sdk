// Real packaged editor against a disposable workspace and the Views host.
// The acceptance runs the stages in stages/ in the order of their numeric prefix (README.md, "Adding a stage").
import assert from "node:assert/strict";
import { mkdir, readFile, readdir, writeFile, lstat } from "node:fs/promises";
import { resolve, join } from "node:path";
import { createRequire } from "node:module";
import { createContext, createObservations, fixtureMarker, preservedComment } from "./context.mjs";

const [urlOrPrepare, workspaceArgument] = process.argv.slice(2);
if (!urlOrPrepare || !workspaceArgument)
  throw new Error("Usage: hosted-rmf2-ui.mjs --prepare <new-workspace> | <host-url> <prepared-workspace>");
const workspace = resolve(workspaceArgument);
if (urlOrPrepare === "--prepare") {
  await mkdir(workspace, { recursive: false });
  await writeFile(join(workspace, ".hosted-rmf2-ui-fixture"), fixtureMarker);
  await writeFile(join(workspace, "runic.json"), JSON.stringify({
    schemaVersion: 1,
    catalog: "runic-hosted-e2e",
    code: { namespace: "Runic.HostedBrowserProof", className: "Messages" },
    baseLocale: "de", locales: ["de", "en", "fr"],
    // files.deleted exists in the base locale only: the missing translations a translator starts from (#225 P2).
    validation: { translationCompleteness: "allow" },
    overrides: [{ catalog: "acme.table", from: "Acme.Table" }],
  }, null, 2) + "\n");
  // T5d: the acme.table library of the build-time override tests, installed as a restore leaves it: a NuGet package
  // folder beside the workspace and the project's obj/project.assets.json.
  const library = join(workspace, "..", "packages", "acme.table", "1.0.0", "runic", "acme.table");
  await mkdir(library, { recursive: true });
  await writeFile(join(library, "runic.json"), JSON.stringify({ schemaVersion: 1, catalog: "acme.table",
    code: { namespace: "Acme.Table", className: "TableText", visibility: "public" }, baseLocale: "en", locales: ["en", "de"] }));
  await writeFile(join(library, "en.rmf2"), "empty = No rows\nheader {\n  sort = Sort by {$column}\n}\n");
  await writeFile(join(library, "de.rmf2"), "empty = Keine Zeilen\nheader {\n  sort = Sortieren nach {$column}\n}\n");
  await writeFile(join(workspace, "Workspace.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
  await mkdir(join(workspace, "obj"));
  await writeFile(join(workspace, "obj", "project.assets.json"), JSON.stringify({ version: 3,
    libraries: { "Acme.Table/1.0.0": { type: "package", path: "acme.table/1.0.0", files: ["runic/acme.table/de.rmf2", "runic/acme.table/en.rmf2", "runic/acme.table/runic.json"] } },
    packageFolders: { [resolve(workspace, "..", "packages") + "/"]: {} } }));
  // A plural with a length limit that only the base locale (de) has.
  const deleted = "  @maxLength 24\n  deleted =\n    .input {$count :integer}\n    .match $count\n    one {{Eine Datei gelöscht}}\n    * {{{$count} Dateien gelöscht}}\n";
  for (const [locale, cancel, save] of [["de", "Abbrechen", "Speichern"], ["en", "Cancel", "Save"], ["fr", "Annuler", "Enregistrer"]])
    await writeFile(join(workspace, `${locale}.rmf2`), `${preservedComment}common {\n  cancel = ${cancel}\n  save = ${save}\n}\nfiles {\n  # Context for a selected-file count.\n  @tags files selection\n  @example {"count":1}\n  @example {"count":2}\n  selected =\n    .input {$count :integer}\n    .match $count\n    one {{One file selected}}\n    * {{{$count} files selected}}\n${locale === "de" ? deleted : ""}}\n`);
  console.log(`prepared real-host RMF2 fixture: ${workspace}`);
  process.exit(0);
}

assert.equal(await readFile(join(workspace, ".hosted-rmf2-ui-fixture"), "utf8"), fixtureMarker,
  "External-write checks require the test-owned prepared workspace.");
const config = JSON.parse(await readFile(join(workspace, "runic.json"), "utf8"));
assert.equal(config.catalog, "runic-hosted-e2e");
assert.ok(!Object.hasOwn(config, "sourceLayout"));
assert.ok(!Object.hasOwn(config, "executionProfile"));
const diskPath = join(workspace, "de.rmf2");
assert.ok((await lstat(diskPath)).isFile() && !(await lstat(diskPath)).isSymbolicLink());

// Each stages/NNN-<name>.mjs exports one stage as its default function; <name> is the stage named in failure reports.
const stageDirectory = new URL("./stages/", import.meta.url);
const stages = [];
for (const file of (await readdir(stageDirectory)).sort()) {
  const match = /^(\d{3})-([a-z0-9]+(?:-[a-z0-9]+)*)\.mjs$/.exec(file);
  if (!match) throw new Error(`stages/${file} is not named NNN-<stage-name>.mjs.`);
  if (stages.some(other => other.order === match[1])) throw new Error(`stages/${file} reuses the order ${match[1]}.`);
  const { default: run } = await import(new URL(file, stageDirectory).href);
  if (typeof run !== "function") throw new Error(`stages/${file} has no default stage function.`);
  stages.push({ order: match[1], name: match[2], run });
}
assert.ok(stages.length > 0, "No hosted acceptance stages found.");
const executablePath = process.env.WEBUI_BROWSER_PATH;
if (!executablePath) throw new Error("WEBUI_BROWSER_PATH must name the pinned Chromium.");
const { chromium } = createRequire(import.meta.url)("playwright-core");
const browser = await chromium.launch({ executablePath, headless: true,
  args: ["--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage"] });
const reportDirectory = process.env.RUNIC_EDITOR_HOSTED_E2E_REPORT_DIR;
if (reportDirectory) await writeFile(join(reportDirectory, "browser-metadata.json"), JSON.stringify({
  browserVersion: browser.version(), browserExecutable: executablePath, platform: process.platform, architecture: process.arch,
}, null, 2) + "\n");
process.once("SIGTERM", () => { void browser.close().finally(() => process.exit(143)); });

let stage = "boot";
let page;
const observations = createObservations();
const { pageErrors, consoleErrors, sentUiLocales } = observations;
try {
  page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
  page.setDefaultTimeout(20_000);
  observations.observe(page);
  await page.goto(new URL("/", urlOrPrepare).href, { waitUntil: "domcontentloaded" });
  const context = createContext({ page, workspace, reportDirectory, observations });
  for (const { name, run } of stages) {
    stage = name;
    await run(context);
  }
  console.log("hosted-rmf2-ui-ok (real packaged frontend + Views host + editor session)");
} catch (error) {
  console.error(`hosted-rmf2-ui failed at ${stage}: ${error.stack ?? error}`);
  if (reportDirectory && page && !page.isClosed()) {
    await Promise.allSettled([
      page.screenshot({ path: join(reportDirectory, "failure.png"), fullPage: true }),
      page.content().then(html => writeFile(join(reportDirectory, "failure.html"), html)),
      page.evaluate(() => window.__runicEditorDocumentCalls ?? []).then(calls =>
        writeFile(join(reportDirectory, "failure.json"), JSON.stringify({ stage, pageErrors, consoleErrors, sentUiLocales, calls }, null, 2) + "\n")),
    ]);
  }
  throw error;
} finally {
  await browser.close();
}
