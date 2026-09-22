// Real packaged editor + real WebSocket host. The only controlled response is
// one delayed transform; all parsing, authoring, validation, conflict handling
// and repair use the production RMF2 editor.
import assert from "node:assert/strict";
import { mkdir, readFile, writeFile, lstat } from "node:fs/promises";
import { resolve, join } from "node:path";
import { createRequire } from "node:module";

const fixtureMarker = "runic-hosted-rmf2-ui-v1";
const preservedComment = "# Runic hosted browser fixture: preserve this comment exactly.\n";
const [urlOrPrepare, workspaceArgument] = process.argv.slice(2);
if (!urlOrPrepare || !workspaceArgument) {
  throw new Error("Usage: hosted-rmf2-ui.mjs --prepare <new-workspace> | <host-url> <prepared-workspace>");
}

const workspace = resolve(workspaceArgument);
if (urlOrPrepare === "--prepare") {
  await mkdir(workspace, { recursive: false });
  await writeFile(join(workspace, ".hosted-rmf2-ui-fixture"), fixtureMarker);
  await writeFile(join(workspace, "runic.json"), JSON.stringify({
    schemaVersion: 1,
    catalog: "runic-hosted-e2e",
    code: { namespace: "Runic.HostedBrowserProof", className: "Messages" },
    baseLocale: "de",
    locales: ["de", "en", "fr"],
  }, null, 2) + "\n");
  for (const [locale, cancel, save] of [["de", "Abbrechen", "Speichern"], ["en", "Cancel", "Save"], ["fr", "Annuler", "Enregistrer"]]) {
    await writeFile(join(workspace, `${locale}.rmf2`), `${preservedComment}common {\n  cancel = ${cancel}\n  save = ${save}\n}\n`);
  }
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
if (!executablePath) throw new Error("WEBUI_BROWSER_PATH must name the project-pinned Chromium.");
const { chromium } = createRequire(import.meta.url)("playwright-core");
const browser = await chromium.launch({ executablePath, headless: true,
  args: ["--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage"] });
const reportDirectory = process.env.RUNIC_EDITOR_HOSTED_E2E_REPORT_DIR;
if (reportDirectory) await writeFile(join(reportDirectory, "browser-metadata.json"), JSON.stringify({
  browserVersion: browser.version(), browserExecutable: executablePath, platform: process.platform, architecture: process.arch,
}, null, 2) + "\n");
process.once("SIGTERM", () => {
  void browser.close().finally(() => process.exit(143));
});

const receipts = [];
const commands = [];
const pageErrors = [];
const wireFrames = [];
let heldCommandId;
let heldFrames = [];
let heldRoute;
let delayNextTransform = false;
let stage = "boot";
let page;

const decode = (message) => JSON.parse(typeof message === "string" ? message : message.toString("utf8"));
const waitFor = async (predicate, label) => {
  const deadline = Date.now() + 15_000;
  while (Date.now() < deadline) {
    if (await predicate()) return;
    await new Promise((resolveWait) => setTimeout(resolveWait, 25));
  }
  throw new Error(`Timed out: ${label}`);
};
const releaseTransform = () => {
  heldCommandId = undefined;
  const frames = heldFrames;
  heldFrames = [];
  for (const message of frames) heldRoute.send(message);
};

try {
  page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
  page.setDefaultTimeout(15_000);
  page.on("pageerror", (error) => pageErrors.push(error.message));
  page.on("dialog", (dialog) => void dialog.accept());
  page.on("websocket", (socket) => socket.on("close", () => wireFrames.push({ direction: "close", url: socket.url() })));
  await page.routeWebSocket("**/bridge", (route) => {
    const server = route.connectToServer();
    route.onMessage((message) => {
      const frame = decode(message);
      wireFrames.push({ direction: "client-to-server", frame });
      if (frame.kind === "dispatch") {
        commands.push(frame);
        if (delayNextTransform && frame.payload?._tag === "TransformDocument" && frame.payload.key === "common_cancel") {
          delayNextTransform = false;
          heldCommandId = frame.commandId;
          heldRoute = route;
        }
      }
      server.send(message);
    });
    server.onMessage((message) => {
      const frame = decode(message);
      wireFrames.push({ direction: "server-to-client", frame });
      if (frame.kind === "receipt") receipts.push(frame);
      if (heldFrames.length > 0 || (heldCommandId && frame.commandId === heldCommandId)) heldFrames.push(message);
      else route.send(message);
    });
  });

  const rootUrl = new URL("/", urlOrPrepare).href;
  await page.goto(rootUrl, { waitUntil: "domcontentloaded" });
  const select = async (key) => {
    await page.getByRole("button", { name: new RegExp(`^${key}:`) }).click();
    await page.locator('textarea[spellcheck="true"]').first().waitFor();
  };
  const editor = () => page.locator('textarea[spellcheck="true"]').first();
  const receiptAfter = async (tag, previous) => {
    await waitFor(() => receipts.slice(previous).some((frame) => commands.some((command) =>
      command.commandId === frame.commandId && command.payload?._tag === tag)), `${tag} receipt`);
    return receipts.slice(previous).find((frame) => commands.some((command) =>
      command.commandId === frame.commandId && command.payload?._tag === tag)).payload;
  };
  const resultOf = (payload) => payload.result ?? payload;
  const reloadFiles = async (expectDiscard = false) => {
    await page.getByRole("button", { name: "Project runic-hosted-e2e", exact: true }).click();
    await page.getByRole("menuitem", { name: "Reload", exact: true }).click();
    if (expectDiscard) await page.getByRole("button", { name: /^common_cancel:/ }).waitFor();
  };

  stage = "two-logical-keys-pending-transform";
  await select("common_cancel");
  delayNextTransform = true;
  await editor().fill("Queued cancel from the real editor");
  await waitFor(() => heldFrames.length > 0, "first real TransformDocument response held");
  const firstTransform = commands.find((command) => command.commandId === heldCommandId);
  assert.equal(firstTransform.payload.path, "de.rmf2");
  await select("common_save");
  await editor().fill("Queued save from the real editor");
  const beforeSave = receipts.length;
  await page.keyboard.press("Control+s");
  assert.ok(heldFrames.length > 0, "Save must wait for the first physical-document transform.");
  releaseTransform();
  const saved = resultOf(await receiptAfter("SaveDocument", beforeSave));
  assert.equal(saved.ok, true, JSON.stringify(saved));
  const secondTransform = commands.find((command) => command.payload?._tag === "TransformDocument" &&
    command.payload.key === "common_save" && command.payload.value === "Queued save from the real editor");
  assert.ok(secondTransform, "The second message must travel through the real transform queue.");
  assert.ok(secondTransform.payload.content.includes("Queued cancel from the real editor"),
    "The second transform must consume the first transform's complete physical RMF2 document.");
  const savedBytes = await readFile(diskPath, "utf8");
  assert.ok(savedBytes.includes("Queued cancel from the real editor") && savedBytes.includes("Queued save from the real editor"));
  assert.ok(savedBytes.startsWith(preservedComment), "Unchanged RMF2 comments must survive message authoring.");
  assert.match(savedBytes, /common\s*\{[\s\S]*cancel\s*=[\s\S]*save\s*=/, "Both logical keys must remain in one RMF2 group.");
  await page.reload({ waitUntil: "domcontentloaded" });
  await select("common_cancel");
  await waitFor(async () => (await editor().inputValue()) === "Queued cancel from the real editor", "first edit survives reload");
  await select("common_save");
  assert.equal(await editor().inputValue(), "Queued save from the real editor");

  stage = "external-file-conflict";
  await select("common_cancel");
  const beforeDraft = receipts.length;
  await editor().fill("Unsaved conflicting editor draft");
  await receiptAfter("TransformDocument", beforeDraft);
  const externalBytes = `${savedBytes}# External edit must win the revision conflict.\n`;
  await writeFile(diskPath, externalBytes);
  const beforeConflict = receipts.length;
  await page.keyboard.press("Control+s");
  const conflict = resultOf(await receiptAfter("SaveDocument", beforeConflict));
  assert.equal(conflict.ok, false);
  assert.equal(conflict.kind, "conflict");
  assert.equal(await readFile(diskPath, "utf8"), externalBytes, "A stale save must not overwrite the external RMF2 edit.");
  assert.equal(await editor().inputValue(), "Unsaved conflicting editor draft");
  await page.getByText(/de\.rmf2.*changed on disk/i).waitFor();
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "conflict-en.png"), fullPage: true });
  await reloadFiles(true);
  await select("common_cancel");
  await waitFor(async () => (await editor().inputValue()) === "Queued cancel from the real editor", "conflict reload discards the draft and reads disk");

  stage = "malformed-rmf2-repair";
  await writeFile(diskPath, "common {\n  cancel = \"unterminated\n");
  await reloadFiles();
  await page.getByRole("button", { name: "Repair de.rmf2", exact: true }).click();
  const repair = page.getByRole("dialog");
  const repairInput = repair.getByRole("textbox", { name: "Malformed source document", exact: true });
  await repairInput.fill(externalBytes);
  const repairedText = await repairInput.inputValue();
  const beforeRepair = receipts.length;
  await repair.getByRole("button", { name: "Validate and save", exact: true }).click();
  const repaired = resultOf(await receiptAfter("SaveDocument", beforeRepair));
  assert.equal(repaired.ok, true, JSON.stringify(repaired));
  await repair.waitFor({ state: "hidden" });
  assert.equal(await readFile(diskPath, "utf8"), repairedText);
  await page.reload({ waitUntil: "domcontentloaded" });
  await select("common_save");
  assert.equal(await editor().inputValue(), "Queued save from the real editor");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "repaired-en.png"), fullPage: true });
  assert.deepEqual(pageErrors, [], "The production editor must not report uncaught browser errors.");
  console.log("hosted-rmf2-ui-ok (real packaged frontend + real backend, no mock bridge)");
} catch (error) {
  console.error(`hosted-rmf2-ui failed at ${stage}: ${error.stack ?? error}`);
  if (reportDirectory && page && !page.isClosed()) {
    await Promise.allSettled([
      page.screenshot({ path: join(reportDirectory, "failure.png"), fullPage: true }),
      page.content().then((html) => writeFile(join(reportDirectory, "failure.html"), html)),
      writeFile(join(reportDirectory, "failure.json"), JSON.stringify({ stage, pageErrors, commands, receipts, wireFrames }, null, 2) + "\n"),
    ]);
  }
  throw error;
} finally {
  if (heldFrames.length > 0) releaseTransform();
  await browser.close();
}
