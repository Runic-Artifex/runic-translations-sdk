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
const sentUiLocales = [];
// Every local-state record each `saveLocalState` request carried, newest last (key and value).
const sentLocalState = [];
let onUiLocaleSent = () => {};
// Resolves as soon as the page sends a `saveLocalState` request carrying `lang`, so the caller can act before its reply.
const uiLocaleSent = (lang, label) => {
  const sent = new Promise((resolveSent, rejectSent) => {
    const timer = setTimeout(() => rejectSent(new Error(`Timed out: ${label}; sent: ${JSON.stringify(sentUiLocales)}`)), 20_000);
    onUiLocaleSent = value => { if (value === lang) { clearTimeout(timer); onUiLocaleSent = () => {}; resolveSent(); } };
  });
  sent.catch(() => {});
  return sent;
};
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
  // The interface language each `saveLocalState` request carried when the page sent it on the Bridge's WebSocket
  // (CS-WebUI frames: a short binary header, then the Views request JSON).
  page.on("websocket", socket => socket.on("framesent", frame => {
    const text = String(frame.payload);
    if (!text.includes('"member":"saveLocalState"')) return;
    try {
      // The header's length bytes can include "{", so the JSON starts at the request's first member.
      const request = JSON.parse(text.slice(text.indexOf('{"kind":'), text.lastIndexOf("}") + 1));
      const entries = JSON.parse(request.input).argument;
      sentLocalState.push(entries);
      const value = entries.find(entry => entry.key === "runic-translations.ui-locale")?.value ?? null;
      sentUiLocales.push(value);
      onUiLocaleSent(value);
    } catch (error) {
      sentUiLocales.push(`unreadable: ${error}`);
    }
  }));
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
  // The toolbar's one save state: "Saved", or "Unsaved changes: …" naming what is unsaved.
  const saveState = async () => (await page.getByTestId("save-state").textContent())?.trim();
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

  // Unsaved means "differs from the saved text": an edit typed back to the saved text leaves nothing to save.
  stage = "edit-then-revert";
  await editor().fill("A temporary edit");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "unsaved text named in the toolbar");
  assert.equal(await page.getByRole("button", { name: "Save", exact: true }).isEnabled(), true);
  await editor().fill("Queued cancel from the real editor");
  await waitFor(async () => await saveState() === "Saved", "reverted edit is no unsaved change");
  assert.equal(await page.getByRole("button", { name: "Saved", exact: true }).isDisabled(), true, "Save stays disabled after a revert.");
  assert.ok((await readFile(diskPath, "utf8")).includes("cancel = Queued cancel from the real editor"));

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
  // One save: the toolbar names the unsaved review status, and Ctrl+S writes the review file.
  await waitFor(async () => await saveState() === "Unsaved changes: review status", "unsaved review status named in the toolbar");
  assert.equal(await page.getByRole("button", { name: "Save workflow" }).count(), 0, "There is no separate workflow save.");
  await page.keyboard.press("Control+s");
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
  await waitFor(async () => await saveState() === "Saved", "saved review data after the external change");

  // Unsaved review changes are kept in the native editor state like text drafts: after a reload (a closed window or
  // a crash) they are offered for recovery, restored on top of the review file, and saved with Ctrl+S.
  stage = "review-recovery";
  const reviewDraftSent = () => sentLocalState.some(entries => entries.some(entry =>
    entry.key.startsWith("runic-translations:review-drafts:1:") && entry.value.includes("Recovered after reload")));
  await page.locator("#review-note").fill("Recovered after reload");
  await waitFor(async () => reviewDraftSent(), "native save of the unsaved review change");
  await page.reload({ waitUntil: "domcontentloaded" });
  const recovered = page.getByTestId("recovered-work");
  await recovered.getByText("Unsaved review changes were found in local application storage.").waitFor();
  await recovered.getByRole("button", { name: "Restore", exact: true }).click();
  await recovered.waitFor({ state: "detached" });
  await waitFor(async () => await saveState() === "Unsaved changes: review status", "restored review change is unsaved");
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("common_save");
  await page.getByRole("button", { name: /^Workflow/ }).first().click();
  await waitFor(async () => (await page.locator("#review-note").inputValue()) === "Recovered after reload", "recovered review note shown in the workflow");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "review-unsaved-en.png"), fullPage: true });
  await page.keyboard.press("Control+s");
  await waitFor(async () => JSON.parse(await readFile(reviewPath, "utf8")).entries
    .find(item => item.key === "common_save" && item.locale === "en")?.note === "Recovered after reload", "recovered review note saved to the review file");
  await waitFor(async () => await saveState() === "Saved", "nothing unsaved after saving the recovered review change");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "review-recovered-en.png"), fullPage: true });

  // The translate view (#225 P1, P8): another locale's editor shows the base locale's text read-only above it, one
  // block per variant with the variable as a chip; the base locale (de in this workspace) has no source panel, and the
  // header names no `base` layer.
  stage = "translate-view-source";
  await select("files_selected");
  const sourcePanel = page.getByRole("region", { name: "Source text (German)", exact: true });
  await sourcePanel.waitFor();
  assert.deepEqual(await sourcePanel.locator("dd").evaluateAll(items => items.map(item => [item.lang, item.dir, item.textContent])),
    [["de", "ltr", "One edited file selected"], ["de", "ltr", "{$count} files selected"]], "The source panel shows the German base text per variant.");
  assert.deepEqual(await sourcePanel.locator("dt").allTextContents(), ["count: one", "count: Fallback"]);
  assert.equal(await sourcePanel.locator('[data-placeholder="variable"]').textContent(), "{$count}");
  assert.equal(await sourcePanel.locator("textarea, input, [contenteditable]").count(), 0, "The source text is read-only.");
  assert.equal(await page.locator('[data-slot="badge"]', { hasText: /^base$/ }).count(), 0, "The header names the base layer.");
  await page.getByRole("button", { name: /^de / }).first().click();
  await select("files_selected");
  assert.equal(await page.getByTestId("source-text").count(), 0, "The base locale shows a source panel of itself.");
  await page.getByRole("button", { name: /^en / }).first().click();

  // The "File" tab (formerly "Source") still opens the whole locale file.
  stage = "translate-view-file-tab";
  await select("common_cancel");
  await page.locator('[data-slot="toggle-group-item"]', { hasText: /^File$/ }).click();
  await waitFor(async () => (await page.locator("#translation-value").inputValue()).startsWith(preservedComment), "file view of en.rmf2");
  assert.match(await page.locator("#translation-value").inputValue(), /common \{\n  cancel = Cancel\n/);
  assert.equal(await page.getByTestId("source-text").count(), 0, "The file view shows the file only.");
  await page.locator('[data-slot="toggle-group-item"]', { hasText: /^Translation$/ }).click();
  await editor().waitFor();

  // The pattern box is one text field (P22): a click on its empty area puts the caret at the end of the text.
  stage = "translate-view-pattern-box";
  const patternBox = page.locator("[data-pattern-box]").first();
  assert.equal(await patternBox.evaluate(box => getComputedStyle(box).cursor), "text");
  const box = await patternBox.boundingBox();
  await page.mouse.click(box.x + box.width - 16, box.y + box.height - 16);
  await waitFor(async () => await editor().evaluate(input => document.activeElement === input && input.selectionStart === input.value.length),
    "caret at the end of the text segment");
  await page.keyboard.type(" now");
  await waitFor(async () => (await editor().inputValue()) === "Cancel now", "typing after a click on the empty pattern box");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "typed text is unsaved");
  await editor().fill("Cancel");
  await waitFor(async () => await saveState() === "Saved", "pattern box edit reverted");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "translate-view-en.png"), fullPage: true });

  // A missing translation (#225 P2) opens in the visual composer on an empty starter shaped for the target locale
  // (French: one, many and the fallback), under the source text, with nothing pre-filled and nothing written; its
  // length limit (P21) shows at the field.
  stage = "missing-translation-starter";
  const frPath = join(workspace, "fr.rmf2");
  const frSaved = await readFile(frPath, "utf8");
  const variantBox = title => page.getByRole("textbox", { name: new RegExp(`^Translation for count: ${title}`) }).first();
  const characterCount = async () => (await page.getByTestId("character-count").textContent())?.trim();
  await page.getByRole("button", { name: /^fr / }).first().click();
  await select("files_deleted");
  await variantBox("Fallback").waitFor();
  await page.getByRole("region", { name: "Source text (German)", exact: true }).getByText("Eine Datei gelöscht", { exact: true }).waitFor();
  assert.equal(await page.getByText("Visual editing is unavailable for this message.", { exact: false }).count(), 0,
    "A missing translation opens in the visual composer.");
  for (const title of ["one", "many", "Fallback"]) assert.equal(await variantBox(title).inputValue(), "", `The ${title} variant is not empty.`);
  assert.equal(await page.getByRole("textbox", { name: /^Translation for count: (few|other|two)/ }).count(), 0, "The starter has categories French does not use.");
  assert.equal((await page.getByTestId("workflow-state").textContent())?.trim(), "Not translated");
  assert.equal(await characterCount(), "0 / 24");
  assert.deepEqual((await page.getByTestId("variant-character-count").allTextContents()).map(item => item.trim()), ["0 / 24", "0 / 24", "0 / 24"]);
  // The composer's MF2 source of the starter is the starter, not the base text.
  const composerSource = page.getByRole("textbox", { name: "Message source", exact: true });
  await page.getByRole("button", { name: "Message source", exact: true }).first().click();
  assert.equal(await composerSource.inputValue(), ".input {$count :integer}\n.match $count\none {{}}\nmany {{}}\n* {{}}");
  await page.getByRole("button", { name: "Message source", exact: true }).first().click();
  await variantBox("one").waitFor();
  assert.equal(await saveState(), "Saved", "An empty starter is no unsaved change.");
  assert.equal(await readFile(frPath, "utf8"), frSaved);

  // Typing makes it a normal unsaved draft; the counter follows the longest variant and warns over the limit.
  await variantBox("one").fill("Un fichier supprimé");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "a starter with text is unsaved");
  await waitFor(async () => await characterCount() === "19 / 24", "counter of the typed variant");
  await variantBox("many").fill("Beaucoup de fichiers supprimés");
  await waitFor(async () => await characterCount() === "30 / 24", "counter over the limit");
  assert.equal(await page.getByTestId("character-count").getAttribute("data-over-limit"), "true");
  await page.getByTestId("length-limit").waitFor();
  assert.match(await page.getByTestId("length-limit").textContent(), /24/);
  await variantBox("many").fill("De nombreux fichiers");
  await variantBox("Fallback").fill("Des fichiers supprimés");
  await waitFor(async () => await characterCount() === "22 / 24", "counter back under the limit");
  assert.equal(await page.getByTestId("length-limit").count(), 0);
  assert.deepEqual((await page.getByTestId("variant-character-count").allTextContents()).map(item => item.trim()), ["19 / 24", "20 / 24", "22 / 24"]);
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "missing-translation-fr.png"), fullPage: true });
  // Ctrl+S writes the message into its existing group (P7).
  await page.keyboard.press("Control+s");
  await waitFor(async () => (await readFile(frPath, "utf8")).includes("deleted ="), "saved starter in fr.rmf2");
  await waitFor(async () => await saveState() === "Saved", "nothing unsaved after saving the starter");
  const frWritten = await readFile(frPath, "utf8");
  assert.equal((frWritten.match(/^files \{$/gm) ?? []).length, 1, "The new message opened a second files group.");
  const filesGroup = frWritten.slice(frWritten.indexOf("files {\n"));
  assert.ok(filesGroup.indexOf("selected =") < filesGroup.indexOf("deleted =") && filesGroup.indexOf("deleted =") < filesGroup.lastIndexOf("\n}\n"),
    "The message is written inside its group, after its sibling.");
  for (const line of [".match $count", "one {{Un fichier supprimé}}", "many {{De nombreux fichiers}}", "* {{Des fichiers supprimés}}"])
    assert.ok(filesGroup.includes(line), `fr.rmf2 lacks ${line}`);
  assert.ok(frWritten.startsWith(frSaved.slice(0, frSaved.indexOf("files {\n"))), "The rest of fr.rmf2 changed.");
  assert.notEqual((await page.getByTestId("workflow-state").textContent())?.trim(), "Not translated");

  // A starter cleared back to empty leaves the document clean (#231): nothing to save, no empty translation.
  stage = "missing-translation-clear";
  const enPath = join(workspace, "en.rmf2");
  const enSaved = await readFile(enPath, "utf8");
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("files_deleted");
  await variantBox("Fallback").waitFor();
  assert.equal(await page.getByRole("textbox", { name: /^Translation for count: many/ }).count(), 0, "English has no many variant.");
  await variantBox("one").fill("One file deleted");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "typed English starter is unsaved");
  await variantBox("one").fill("");
  await waitFor(async () => await saveState() === "Saved", "a cleared starter leaves the document clean");
  assert.equal(await readFile(enPath, "utf8"), enSaved);

  // "Copy source" fills every variant from the German base text; the placeholder stays a placeholder. Restoring the
  // untouched starter in its MF2 source leaves the document clean again.
  stage = "missing-translation-copy-source";
  await page.getByRole("button", { name: "Copy source", exact: true }).click();
  await waitFor(async () => (await variantBox("one").inputValue()) === "Eine Datei gelöscht", "Copy source fills the one variant");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "copied source is unsaved");
  await page.getByRole("button", { name: "Message source", exact: true }).first().click();
  await waitFor(async () => (await composerSource.inputValue()) === ".input {$count :integer}\n.match $count\none {{Eine Datei gelöscht}}\n* {{{$count} Dateien gelöscht}}",
    "copied starter source with the placeholder as an expression");
  await composerSource.fill(".input {$count :integer}\n.match $count\none {{}}\n* {{}}");
  await waitFor(async () => await saveState() === "Saved", "the restored empty starter leaves the document clean");
  await page.getByRole("button", { name: "Message source", exact: true }).first().click();
  assert.equal(await readFile(enPath, "utf8"), enSaved);
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors for a missing translation.");

  // Unsaved edits have their own undo history (#225 P4). The pilot's case: ` {$oops` typed into a plural message's MF2
  // source. Ctrl+Z in the field restores the valid text, the error is gone, and the document is clean again because
  // it equals the saved text; Ctrl+Shift+Z brings the edit back.
  stage = "draft-undo-invalid-source";
  const invalidField = () => page.locator('[data-slot="field"][data-invalid="true"]');
  await select("files_selected");
  await page.getByRole("button", { name: "Message source", exact: true }).first().click();
  const validSource = await composerSource.inputValue();
  assert.match(validSource, /\.match \$count/);
  await composerSource.focus();
  await composerSource.press("Control+End");
  await page.keyboard.type(" {$oops");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "the broken source is unsaved");
  await waitFor(async () => await invalidField().count() > 0, "the broken source is shown as invalid");
  await page.keyboard.press("Control+z");
  await waitFor(async () => (await composerSource.inputValue()) === validSource, "Ctrl+Z in the field restores the valid source");
  await waitFor(async () => await saveState() === "Saved", "the undone edit leaves the document clean");
  await waitFor(async () => await invalidField().count() === 0, "no error after the undo");
  assert.equal(await composerSource.evaluate(input => document.activeElement === input && input.selectionStart === input.value.length), true,
    "The field keeps the focus, with the caret where the undone text was.");
  assert.equal(await readFile(enPath, "utf8"), enSaved, "Undo wrote nothing.");
  await page.keyboard.press("Control+Shift+z");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "Ctrl+Shift+Z redoes the edit");
  await waitFor(async () => await invalidField().count() > 0, "the redone source is invalid again");
  await page.keyboard.press("Control+z");
  await waitFor(async () => (await composerSource.inputValue()) === validSource && await saveState() === "Saved", "undone again");
  await page.getByRole("button", { name: "Message source", exact: true }).first().click();
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "draft-undo-en.png"), fullPage: true });

  // The toolbar names the unsaved step by its dotted message path, as the command palette does. Undo works across a
  // message switch and selects the message it changed; Ctrl+Z right after typing takes back what was typed.
  stage = "draft-undo-toolbar";
  const undoButton = page.getByRole("button", { name: /^Undo(:| saved change)/ });
  const redoButton = page.getByRole("button", { name: /^Redo(:| saved change)/ });
  await branchEditor().fill("One file chosen");
  await waitFor(async () => (await undoButton.getAttribute("aria-label")) === "Undo: edit in files.selected", "the toolbar's Undo names the unsaved step");
  assert.equal(await undoButton.getAttribute("title"), "Undo: edit in files.selected (Ctrl+Z)");
  await page.keyboard.press("Control+k");
  await page.getByRole("option", { name: /Undo: edit in files\.selected/ }).waitFor();
  await page.keyboard.press("Escape");
  await select("common_cancel");
  await undoButton.click();
  await waitFor(async () => (await branchEditor().count()) === 1 && (await branchEditor().inputValue()) === "One file selected",
    "Undo selects files.selected and restores its text");
  await waitFor(async () => await saveState() === "Saved", "the undone edit leaves the document clean");
  await waitFor(async () => (await redoButton.getAttribute("aria-label")) === "Redo: edit in files.selected", "the toolbar's Redo names the undone step");
  await redoButton.click();
  await waitFor(async () => (await branchEditor().inputValue()) === "One file chosen", "Redo applies the edit again");
  await branchEditor().fill("One file chosen now");
  await branchEditor().press("Control+z");
  await waitFor(async () => (await branchEditor().inputValue()) === "One file chosen", "Ctrl+Z right after typing takes back the typing");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "the earlier edit is still unsaved");

  // After a save Undo continues with the saved-change history, named as a saved change.
  stage = "draft-undo-after-save";
  await page.keyboard.press("Control+s");
  await waitFor(async () => (await readFile(enPath, "utf8")).includes("one {{One file chosen}}"), "saved edit");
  await waitFor(async () => await saveState() === "Saved", "nothing unsaved after the save");
  await waitFor(async () => /^Undo saved change: .*en\.rmf2$/.test(await undoButton.getAttribute("aria-label") ?? ""),
    "after a save the toolbar's Undo is the saved change");
  await waitFor(async () => await undoButton.isEnabled(), "the saved change can be undone");
  await undoButton.click();
  await waitFor(async () => (await readFile(enPath, "utf8")) === enSaved, "Undo of the saved change restores en.rmf2");
  await waitFor(async () => (await branchEditor().inputValue()) === "One file selected", "the editor shows the restored text");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors while undoing.");

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
  // The override is written on its first Save, not on the click.
  assert.equal(await lstat(overridePath).then(() => true, () => false), false, "Override wrote the file before Save.");
  await overrideText.fill("Sortiert nach {$column}");
  await sortRow.getByRole("button", { name: "Save", exact: true }).click();
  await waitFor(async () => await lstat(overridePath).then(() => true, () => false) && (await readFile(overridePath, "utf8")).includes("sort = Sortiert nach {$column}"), "override text saved to disk");
  assert.match(await readFile(overridePath, "utf8"), /header \{\n  # Acme\.Table 1\.0\.0, base locale en:\n[^]*  sort = Sortiert nach \{\$column\}\n\}/);
  await sortRow.locator('[data-badge="overridden"]').waitFor();
  await sortRow.getByRole("button", { name: "Revert to library", exact: true }).click();
  await waitFor(async () => !(await lstat(overridePath).then(() => true, () => false)), "override source removed by Revert to library");
  await sortRow.getByRole("button", { name: "Override", exact: true }).waitFor();
  // Typed in another spelling: the canonical de-CH is added and selected, not the base locale.
  await libraryDialog.getByTestId("library-new-locale").fill("de_ch");
  await libraryDialog.getByRole("button", { name: "Add locale", exact: true }).click();
  await waitFor(async () => (await libraryDialog.locator('[data-locale="de-CH"]').count()) === 1 &&
    (await libraryDialog.locator('[data-locale="de-CH"]').innerText()).includes("Added by you, falls back to de"), "app-added de-CH with its fallback");
  assert.match(await libraryDialog.locator('[data-coverage="de-CH"]').innerText(), /^0 of 2 in its own text/);
  assert.match(await libraryDialog.locator('[data-locale="de-CH"]').getAttribute("class") ?? "", /border-primary/, "The added locale is not selected.");
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

  // In the German interface the source panel and the file tab are German too.
  stage = "translate-view-german-ui";
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("files_selected");
  await page.getByRole("region", { name: "Ausgangstext (Deutsch)", exact: true }).getByText("One edited file selected", { exact: true }).waitFor();
  assert.equal(await page.locator('[data-slot="toggle-group-item"]', { hasText: /^Datei$/ }).count(), 1, "The file tab is not named Datei.");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "translate-view-de.png"), fullPage: true });

  // A reload straight after a switch, as a user pressing F5 would. The reload ends the Bridge connection while the
  // native save is in flight: that must not surface as an uncaught `BridgeError: The Bridge is disconnected.`, and the
  // switch must survive the reload. The editor guarantees the latter only for a save whose request has left the page
  // (#147): one still waiting in the page's Bridge queue, or for the routed localState View to open, is lost with the
  // page. So each reload waits until the page has sent the save carrying the new language, but not for its reply.
  stage = "interface-language-immediate-reload";
  const settingsButton = /^(Editor settings|Editoreinstellungen), /;
  for (const [name, lang, summary] of [
    ["English", "en", /^Editor settings, .*interface language English$/],
    ["Deutsch", "de", /^Editoreinstellungen, .*Oberflächensprache Deutsch$/],
    ["English", "en", /^Editor settings, .*interface language English$/],
  ]) {
    const sent = uiLocaleSent(lang, `the native save of ${name} sent to the host`);
    await page.getByRole("button", { name: settingsButton }).click();
    await page.getByRole("menuitemradio", { name, exact: true }).click();
    await sent;
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
        writeFile(join(reportDirectory, "failure.json"), JSON.stringify({ stage, pageErrors, consoleErrors, sentUiLocales, calls }, null, 2) + "\n")),
    ]);
  }
  throw error;
} finally {
  await browser.close();
}
