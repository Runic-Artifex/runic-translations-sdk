// Real packaged editor against a disposable workspace and the Views host.
import assert from "node:assert/strict";
import { mkdir, readFile, writeFile, lstat } from "node:fs/promises";
import { resolve, join } from "node:path";
import { createRequire } from "node:module";

const fixtureMarker = "runic-hosted-rmf2-ui-v1";
const preservedComment = "# Runic hosted browser fixture: preserve this comment exactly.\n";
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
  }, null, 2) + "\n");
  for (const [locale, cancel, save] of [["de", "Abbrechen", "Speichern"], ["en", "Cancel", "Save"], ["fr", "Annuler", "Enregistrer"]])
    await writeFile(join(workspace, `${locale}.rmf2`), `${preservedComment}common {\n  cancel = ${cancel}\n  save = ${save}\n}\n`);
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
const pageErrors = [];
const waitFor = async (predicate, label) => {
  const deadline = Date.now() + 20_000;
  while (Date.now() < deadline) {
    if (await predicate()) return;
    await new Promise(resolveWait => setTimeout(resolveWait, 30));
  }
  throw new Error(`Timed out: ${label}`);
};
try {
  page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
  page.setDefaultTimeout(20_000);
  page.on("pageerror", error => pageErrors.push(error.stack ?? error.message));
  page.on("dialog", dialog => void dialog.accept());
  await page.goto(new URL("/", urlOrPrepare).href, { waitUntil: "domcontentloaded" });
  const select = async key => {
    await page.getByRole("button", { name: new RegExp(`^${key}:`) }).click();
    await page.locator('textarea[spellcheck="true"]').first().waitFor();
  };
  const editor = () => page.locator('textarea[spellcheck="true"]').first();
  const reloadFiles = async () => {
    await page.getByRole("button", { name: "Project runic-hosted-e2e", exact: true }).click();
    await page.getByRole("menuitem", { name: "Reload", exact: true }).click();
  };

  stage = "two-logical-keys";
  await select("common_cancel");
  await page.evaluate(() => {
    const bridge = window.__runicBridge;
    const call = bridge.call.bind(bridge);
    window.__runicEditorDocumentCalls = [];
    bridge.call = async (name, ...args) => {
      try {
        const response = await call(name, ...args);
        window.__runicEditorDocumentCalls.push({ name, response: name.endsWith("Mount") ? response : undefined });
        return response;
      } catch (error) {
        window.__runicEditorDocumentCalls.push({ name, error: String(error) });
        throw error;
      }
    };
  });
  await editor().fill("Queued cancel from the real editor");
  await select("common_save");
  await editor().fill("Queued save from the real editor");
  await page.keyboard.press("Control+s");
  await waitFor(async () => (await readFile(diskPath, "utf8")).includes("Queued save from the real editor"), "save of both logical keys");
  const savedBytes = await readFile(diskPath, "utf8");
  assert.ok(savedBytes.includes("Queued cancel from the real editor"));
  assert.ok(savedBytes.startsWith(preservedComment), "Unchanged RMF2 comments must survive authoring.");
  await waitFor(async () => (await page.evaluate(() => window.__runicEditorDocumentCalls))
    .some(call => /^content.+Save$/.test(call.name)), "routed document save completion");
  const routedCalls = await page.evaluate(() => window.__runicEditorDocumentCalls);
  assert.ok(routedCalls.some(call => /^content.+Snapshot$/.test(call.name)), "Opening a document must read its routed ViewModel.");
  assert.ok(routedCalls.some(call => /^content.+Validate$/.test(call.name)), "Validation must use the routed document ViewModel.");
  assert.ok(routedCalls.some(call => /^content.+Save$/.test(call.name)), "Save must use the routed document ViewModel.");
  await page.reload({ waitUntil: "domcontentloaded" });
  await select("common_cancel");
  assert.equal(await editor().inputValue(), "Queued cancel from the real editor");

  stage = "external-file-conflict";
  await editor().fill("Unsaved conflicting editor draft");
  const externalBytes = `${savedBytes}# External edit must win the revision conflict.\n`;
  await writeFile(diskPath, externalBytes);
  await page.keyboard.press("Control+s");
  await waitFor(async () => (await page.getByText(/de\.rmf2.*changed on disk/i).count()) > 0, "external conflict notice");
  assert.equal(await readFile(diskPath, "utf8"), externalBytes, "A stale save must not overwrite the external edit.");
  assert.equal(await editor().inputValue(), "Unsaved conflicting editor draft");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "conflict-en.png"), fullPage: true });
  await reloadFiles();
  await select("common_cancel");
  await waitFor(async () => (await editor().inputValue()) === "Queued cancel from the real editor", "conflict reload");

  stage = "malformed-rmf2-repair";
  await writeFile(diskPath, "common {\n  cancel = \"unterminated\n");
  await reloadFiles();
  await page.getByRole("button", { name: "Repair de.rmf2", exact: true }).click();
  const repair = page.getByRole("dialog");
  const repairInput = repair.getByRole("textbox", { name: "Malformed source document", exact: true });
  await repairInput.fill(externalBytes);
  const repairedText = await repairInput.inputValue();
  await repair.getByRole("button", { name: "Validate and save", exact: true }).click();
  await waitFor(async () => (await readFile(diskPath, "utf8")) === repairedText, "repaired source save");
  await repair.waitFor({ state: "hidden" });
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "repaired-en.png"), fullPage: true });
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
  console.log("hosted-rmf2-ui-ok (real packaged frontend + Views host + editor session)");
} catch (error) {
  console.error(`hosted-rmf2-ui failed at ${stage}: ${error.stack ?? error}`);
  if (reportDirectory && page && !page.isClosed()) {
    await Promise.allSettled([
      page.screenshot({ path: join(reportDirectory, "failure.png"), fullPage: true }),
      page.content().then(html => writeFile(join(reportDirectory, "failure.html"), html)),
      page.evaluate(() => window.__runicEditorDocumentCalls ?? []).then(calls =>
        writeFile(join(reportDirectory, "failure.json"), JSON.stringify({ stage, pageErrors, calls }, null, 2) + "\n")),
    ]);
  }
  throw error;
} finally {
  await browser.close();
}
