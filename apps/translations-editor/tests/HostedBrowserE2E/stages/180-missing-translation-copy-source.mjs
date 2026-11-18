import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

// "Copy source" fills every variant from the German base text; the placeholder stays a placeholder. Restoring the
// untouched starter in its MF2 source leaves the document clean again.
/** @param {import("../context.mjs").HostedContext} context */
export default async function missingTranslationCopySource(context) {
  const { page, waitFor, saveState, variantBox, composerSource, pageErrors, handoff } = context;
  const { enPath } = context.paths;
  const enSaved = handoff.get("enSaved");
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
}
