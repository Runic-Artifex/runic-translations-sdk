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
  for (const [locale, cancel, save] of [["de", "Abbrechen", "Speichern"], ["en", "Cancel", "Save"], ["fr", "Annuler", "Enregistrer"]])
    await writeFile(join(workspace, `${locale}.rmf2`), `${preservedComment}common {\n  cancel = ${cancel}\n  save = ${save}\n}\nfiles {\n  # Context for a selected-file count.\n  @tags files selection\n  @example {"count":1}\n  @example {"count":2}\n  selected =\n    .input {$count :integer}\n    .match $count\n    one {{One file selected}}\n    * {{{$count} files selected}}\n}\n`);
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
const consoleErrors = [];
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
  page.on("console", message => { if (message.type() === "error") { consoleErrors.push(message.text()); console.error(message.text()); } });
  page.on("dialog", dialog => void dialog.accept());
  await page.goto(new URL("/", urlOrPrepare).href, { waitUntil: "domcontentloaded" });
  const select = async key => {
    await page.evaluate(() => {
      const bridge = window.__runicBridge;
      if (bridge.__authoringObserved) return;
      bridge.__authoringObserved = true;
      // Views protocol 4 (Runic SDK 0.16) sends each request as one JSON message through `request`, such as
      // {"kind":"start","route":…,"member":"save"} for an operation and {"kind":"open","route":…} for a routed View.
      const request = bridge.request.bind(bridge);
      window.__runicEditorDocumentCalls = [];
      bridge.request = async (text, ...rest) => {
        let message;
        try { message = JSON.parse(text); } catch { message = {}; }
        const call = { kind: message.kind, route: message.route, member: message.member };
        try {
          const response = await request(text, ...rest);
          window.__runicEditorDocumentCalls.push(call);
          return response;
        } catch (error) {
          window.__runicEditorDocumentCalls.push({ ...call, error: String(error) });
          throw error;
        }
      };
    });
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
    // Records again from here, so the checks below see only the queued edits and their save.
    window.__runicEditorDocumentCalls = [];
  });
  await editor().fill("Queued cancel from the real editor");
  await select("common_save");
  await editor().fill("Queued save from the real editor");
  await page.keyboard.press("Control+s");
  await waitFor(async () => (await readFile(diskPath, "utf8")).includes("Queued save from the real editor"), "save of both logical keys");
  const savedBytes = await readFile(diskPath, "utf8");
  assert.ok(savedBytes.includes("Queued cancel from the real editor"));
  assert.ok(savedBytes.startsWith(preservedComment), "Unchanged RMF2 comments must survive authoring.");
  // The document's operations run on its own routed View, not on the root View that opened it.
  const documentOperation = member => call => call.kind === "start" && call.member === member && typeof call.route === "string" && call.route.length > 0;
  await waitFor(async () => (await page.evaluate(() => window.__runicEditorDocumentCalls))
    .some(documentOperation("save")), "routed document save completion");
  const routedCalls = await page.evaluate(() => window.__runicEditorDocumentCalls);
  const saveRoute = routedCalls.find(documentOperation("save")).route;
  // `open` presents the routed View and replies with its state; `snapshot` reads it again.
  assert.ok(routedCalls.some(call => (call.kind === "open" || call.kind === "snapshot") && call.route === saveRoute), "Opening a document must read its routed ViewModel.");
  assert.ok(routedCalls.some(call => documentOperation("validate")(call) && call.route === saveRoute), "Validation must use the routed document ViewModel.");
  await page.reload({ waitUntil: "domcontentloaded" });
  await select("common_cancel");
  assert.equal(await editor().inputValue(), "Queued cancel from the real editor");

  stage = "plural-edit-preview-persistence";
  await select("files_selected");
  const branchEditor = () => page.getByRole("textbox", { name: /Translation for count: one/ }).first();
  await branchEditor().fill("One edited file selected");
  const previewCanvas = page.locator(".preview-canvas");
  await waitFor(async () => (await previewCanvas.textContent()).includes("One edited file selected"), "edited singular compiler preview");
  const countSample = page.locator(".sample-inputs label").filter({ hasText: "count" }).locator("input");
  await countSample.fill("2");
  await waitFor(async () => (await previewCanvas.textContent()).includes("2 files selected"), "plural compiler preview for 2");
  await page.getByRole("button", { name: "Example 1", exact: true }).click();
  await waitFor(async () => (await previewCanvas.textContent()).includes("One edited file selected"), "context singular example");
  await page.getByRole("button", { name: "Example 2", exact: true }).click();
  await waitFor(async () => (await previewCanvas.textContent()).includes("2 files selected"), "context plural example");
  await page.keyboard.press("Control+s");
  await waitFor(async () => (await readFile(diskPath, "utf8")).includes("one {{One edited file selected}}"), "saved plural MF2 source");
  const pluralSaved = await readFile(diskPath, "utf8");
  assert.ok(pluralSaved.startsWith(preservedComment));
  assert.ok(pluralSaved.includes('@example {"count":2}'));
  assert.ok(pluralSaved.includes("* {{{$count} files selected}}"));
  assert.ok(!pluralSaved.includes('"variants"'), "Structural editing must never serialize legacy JSON.");
  await page.reload({ waitUntil: "domcontentloaded" });
  await select("files_selected");
  assert.equal(await branchEditor().inputValue(), "One edited file selected");
  await waitFor(async () => (await previewCanvas.textContent()).includes("One edited file selected"), "reloaded singular compiler preview");
  await countSample.fill("2");
  await waitFor(async () => (await previewCanvas.textContent()).includes("2 files selected"), "reloaded plural compiler preview");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "plural-saved-en.png"), fullPage: true });
  await select("common_cancel");

  stage = "external-file-conflict";
  await editor().fill("Unsaved conflicting editor draft");
  const externalBytes = `${pluralSaved}# External edit must win the revision conflict.\n`;
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
  stage = "diagnostic-span-and-shared-fix";
  const invalidBytes = "# 🦊 Unicode before the diagnostic\n" + repairedText.replace("cancel = Queued cancel from the real editor", "cancel = ");
  await writeFile(diskPath, invalidBytes);
  await reloadFiles();
  await page.getByRole("button", { name: "Repair de.rmf2", exact: true }).click();
  const diagnosticDialog = page.getByRole("dialog");
  const diagnosticInput = diagnosticDialog.getByRole("textbox", { name: "Malformed source document", exact: true });
  await diagnosticDialog.getByRole("button", { name: /RTR0050.*assignment without a body/i }).click();
  const nameOffset = invalidBytes.indexOf("cancel = ");
  // The compiler points at the complete incomplete assignment, including its
  // equals sign and trailing space. UTF-16 selection must retain that span.
  await waitFor(async () => (await diagnosticInput.evaluate(input => [input.selectionStart, input.selectionEnd])).join(":") === `${nameOffset}:${nameOffset + "cancel = ".length}`, "physical Unicode diagnostic span selection");
  await diagnosticDialog.getByRole("button", { name: "Make message explicitly empty", exact: true }).click();
  await waitFor(async () => (await diagnosticInput.inputValue()).includes("cancel = {{}}"), "compiler-owned explicit-empty quick fix");
  await diagnosticDialog.getByRole("button", { name: "Validate and save", exact: true }).click();
  await diagnosticDialog.waitFor({ state: "hidden" });
  assert.ok((await readFile(diskPath, "utf8")).includes("cancel = {{}}"));
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "diagnostic-fixed-en.png"), fullPage: true });

  // Review states live in the SDK review file beside runic.json, the same file runic-translations review mark and
  // xliff import write. An entry recorded for another source is stale (RTR0090) in the row's quality checks;
  // approving it in the workflow records the source the editor loaded, and the note goes onto the review entry.
  stage = "sdk-review-file";
  const reviewPath = join(workspace, "runic-hosted-e2e.review.json");
  const staleFingerprint = "sha256:" + "0".repeat(64);
  await writeFile(reviewPath, JSON.stringify({
    $schema: "runic.translations.interchange-review/2", catalog: "runic-hosted-e2e",
    entries: [{ key: "common_save", locale: "en", state: "needs-review", sourceFingerprint: staleFingerprint }],
  }, null, 2) + "\n");
  await reloadFiles();
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("common_save");
  const workflow = page.getByRole("button", { name: /^Workflow/ }).first();
  await workflow.getByText("Source changed", { exact: true }).waitFor();
  await workflow.click();
  await page.getByText(/RTR0090.*predates a change to its source/).first().waitFor();
  await page.locator("#workflow-status").click();
  await page.getByRole("option", { name: "Approved", exact: true }).click();
  await page.locator("#review-note").fill("Checked against the new source");
  await page.getByRole("button", { name: "Save workflow", exact: true }).first().click();
  await waitFor(async () => {
    const review = JSON.parse(await readFile(reviewPath, "utf8"));
    const entry = review.entries.find(item => item.key === "common_save" && item.locale === "en");
    return entry?.state === "approved" && entry.sourceFingerprint?.startsWith("sha256:") && entry.sourceFingerprint !== staleFingerprint;
  }, "approval recorded in the SDK review file for the current source");
  await workflow.getByText("Source changed", { exact: true }).waitFor({ state: "detached" });
  const saved = JSON.parse(await readFile(reviewPath, "utf8")).entries.find(item => item.key === "common_save" && item.locale === "en");
  assert.equal(saved?.note, "Checked against the new source", "The note of a row with a review entry is not on the review entry.");
  const editorStatePath = join(workspace, ".runic-translations", "runic-hosted-e2e.editor-state.json");
  const editorState = await readFile(editorStatePath, "utf8").catch(() => "");
  assert.ok(!editorState.includes('"state"') && !editorState.includes("Checked against"),
    "Review states or the review entry's note were written to the editor state.");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "review-file-en.png"), fullPage: true });

  // Another writer (runic-translations review mark, the language server) changes the review file: the editor's
  // external-change check reloads the review data without a workspace reload.
  stage = "sdk-review-file-external";
  const external = JSON.parse(await readFile(reviewPath, "utf8"));
  external.entries = external.entries.map(item => item.key === "common_save" && item.locale === "en" ? { ...item, note: "Changed by the CLI" } : item);
  await writeFile(reviewPath, JSON.stringify(external, null, 2) + "\n");
  await page.getByText("The review file changed on disk; the review data was reloaded.").first().waitFor({ timeout: 15_000 });
  await waitFor(async () => (await page.locator("#review-note").inputValue()) === "Changed by the CLI", "external review note shown in the workflow");

  // The interface language switch sets the window's locale through the ViewModel, the page follows it, and the
  // choice survives a reload from the native editor state.
  // Library catalogs (T5d): the library's text is layer 0, read-only; an override (layer 1) is written to
  // overrides/<catalog>/<locale>.rmf2 seeded with the library text, edited, reverted to the library, and a locale the
  // library lacks is added with its one fallback hop.
  stage = "library-catalog";
  const overridePath = join(workspace, "overrides", "acme.table", "de.rmf2");
  await page.locator('[data-testid="workspace-catalogs"] [data-library="acme.table"]').waitFor();
  await page.getByRole("button", { name: /^Libraries/ }).click();
  const libraryDialog = page.getByTestId("library-dialog");
  await libraryDialog.locator('[data-layer="library"]').first().waitFor();
  assert.deepEqual(await libraryDialog.locator("[data-locale]").evaluateAll(items => items.map(item => item.getAttribute("data-locale"))), ["en", "de"]);
  await libraryDialog.locator('[data-locale="de"]').click();
  const sortRow = libraryDialog.locator('[data-message="header_sort"]');
  await sortRow.getByRole("button", { name: "Override", exact: true }).click();
  const overrideText = sortRow.locator('[data-layer="override"]');
  await waitFor(async () => await overrideText.count() === 1 && (await overrideText.inputValue()) === "Sortieren nach {$column}", "override seeded with the library text");
  assert.match(await readFile(overridePath, "utf8"), /header \{\n  # Acme\.Table 1\.0\.0, base locale en:\n[^]*  sort = Sortieren nach \{\$column\}\n\}/);
  await overrideText.fill("Sortiert nach {$column}");
  await sortRow.getByRole("button", { name: "Save", exact: true }).click();
  await waitFor(async () => (await readFile(overridePath, "utf8")).includes("sort = Sortiert nach {$column}"), "override text saved to disk");
  await sortRow.locator('[data-badge="overridden"]').waitFor();
  await sortRow.getByRole("button", { name: "Revert to library", exact: true }).click();
  await waitFor(async () => !(await lstat(overridePath).then(() => true, () => false)), "override source removed by Revert to library");
  await sortRow.getByRole("button", { name: "Override", exact: true }).waitFor();
  await libraryDialog.getByTestId("library-new-locale").fill("de-CH");
  await libraryDialog.getByRole("button", { name: "Add locale", exact: true }).click();
  await waitFor(async () => (await libraryDialog.locator('[data-locale="de-CH"]').count()) === 1 &&
    (await libraryDialog.locator('[data-locale="de-CH"]').innerText()).includes("Added by you, falls back to de"), "app-added de-CH with its fallback");
  assert.match(await libraryDialog.locator('[data-coverage="de-CH"]').innerText(), /^0 of 2 in its own text/);
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "library-catalog.png"), fullPage: true });
  await page.keyboard.press("Escape");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors in the library catalog.");

  stage = "interface-language";
  await page.evaluate(() => { window.__runicEditorDocumentCalls = []; });
  await page.getByRole("button", { name: /^Editor settings, / }).click();
  await page.getByRole("menuitemradio", { name: "Deutsch", exact: true }).click();
  await waitFor(async () => (await page.evaluate(() => document.documentElement.lang)) === "de", "German interface language");
  await waitFor(async () => (await page.evaluate(() => window.__runicEditorDocumentCalls))
    .some(call => call.member === "setUiLocale" && call.error === undefined), "setUiLocale request to the editor ViewModel");
  await page.getByRole("button", { name: /^Editoreinstellungen, .*Oberflächensprache Deutsch$/ }).waitFor();
  await waitFor(async () => (await page.evaluate(() => window.__runicEditorDocumentCalls))
    .some(call => call.member === "saveLocalState" && call.error === undefined), "native save of the interface language");
  await page.reload({ waitUntil: "domcontentloaded" });
  await page.getByRole("button", { name: /^Editoreinstellungen, .*Oberflächensprache Deutsch$/ }).waitFor();
  assert.equal(await page.evaluate(() => document.documentElement.lang), "de", "The interface language did not survive a reload.");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "interface-language-de.png"), fullPage: true });
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");

  // A reload straight after a switch, without waiting for its native save, as a user pressing F5 would. The reload
  // ends the Bridge connection while the save may still be in flight: that must not surface as an uncaught
  // `BridgeError: The Bridge is disconnected.`, and the switch must still survive the reload.
  stage = "interface-language-immediate-reload";
  const settingsButton = /^(Editor settings|Editoreinstellungen), /;
  for (const [name, lang, summary] of [
    ["English", "en", /^Editor settings, .*interface language English$/],
    ["Deutsch", "de", /^Editoreinstellungen, .*Oberflächensprache Deutsch$/],
    ["English", "en", /^Editor settings, .*interface language English$/],
  ]) {
    await page.getByRole("button", { name: settingsButton }).click();
    await page.getByRole("menuitemradio", { name, exact: true }).click();
    await page.reload({ waitUntil: "domcontentloaded" });
    await page.getByRole("button", { name: summary }).waitFor();
    assert.equal(await page.evaluate(() => document.documentElement.lang), lang, `The switch to ${name} did not survive an immediate reload.`);
  }
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors after an immediate reload.");
  console.log("hosted-rmf2-ui-ok (real packaged frontend + Views host + editor session)");
} catch (error) {
  console.error(`hosted-rmf2-ui failed at ${stage}: ${error.stack ?? error}`);
  if (reportDirectory && page && !page.isClosed()) {
    await Promise.allSettled([
      page.screenshot({ path: join(reportDirectory, "failure.png"), fullPage: true }),
      page.content().then(html => writeFile(join(reportDirectory, "failure.html"), html)),
      page.evaluate(() => window.__runicEditorDocumentCalls ?? []).then(calls =>
        writeFile(join(reportDirectory, "failure.json"), JSON.stringify({ stage, pageErrors, consoleErrors, calls }, null, 2) + "\n")),
    ]);
  }
  throw error;
} finally {
  await browser.close();
}
