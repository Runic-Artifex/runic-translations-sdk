import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

// A starter cleared back to empty leaves the document clean (#231): nothing to save, no empty translation.
/** @param {import("../context.mjs").HostedContext} context */
export default async function missingTranslationClear(context) {
  const { page, waitFor, select, saveState, variantBox, handoff } = context;
  const { enPath } = context.paths;
  const enSaved = await readFile(enPath, "utf8");
  handoff.set("enSaved", enSaved);
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("files_deleted");
  await variantBox("Fallback").waitFor();
  assert.equal(await page.getByRole("textbox", { name: /^Translation for count: many/ }).count(), 0, "English has no many variant.");
  await variantBox("one").fill("One file deleted");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "typed English starter is unsaved");
  await variantBox("one").fill("");
  await waitFor(async () => await saveState() === "Saved", "a cleared starter leaves the document clean");
  assert.equal(await readFile(enPath, "utf8"), enSaved);
}
