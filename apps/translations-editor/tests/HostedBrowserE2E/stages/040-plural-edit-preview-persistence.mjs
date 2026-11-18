import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { join } from "node:path";

/** @param {import("../context.mjs").HostedContext} context */
export default async function pluralEditPreviewPersistence(context) {
  const { page, reportDirectory, waitFor, select, branchEditor, handoff } = context;
  const { diskPath } = context.paths;
  const { preservedComment } = context.fixture;
  await select("files_selected");
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
  handoff.set("pluralSaved", pluralSaved);
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
}
