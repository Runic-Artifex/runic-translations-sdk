import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { join } from "node:path";

// Unsaved edits have their own undo history (#225 P4). The pilot's case: ` {$oops` typed into a plural message's MF2
// source. Ctrl+Z in the field restores the valid text, the error is gone, and the document is clean again because
// it equals the saved text; Ctrl+Shift+Z brings the edit back.
/** @param {import("../context.mjs").HostedContext} context */
export default async function draftUndoInvalidSource(context) {
  const { page, reportDirectory, waitFor, select, saveState, composerSource, handoff } = context;
  const { enPath } = context.paths;
  const enSaved = handoff.get("enSaved");
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
}
