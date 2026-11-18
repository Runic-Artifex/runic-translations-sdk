import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

// After a save Undo continues with the saved-change history, named as a saved change.
/** @param {import("../context.mjs").HostedContext} context */
export default async function draftUndoAfterSave(context) {
  const { page, waitFor, saveState, branchEditor, undoButton, pageErrors, handoff } = context;
  const { enPath } = context.paths;
  const enSaved = handoff.get("enSaved");
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
}
