import assert from "node:assert/strict";

// The toolbar names the unsaved step by its dotted message path, as the command palette does. Undo works across a
// message switch and selects the message it changed; Ctrl+Z right after typing takes back what was typed.
/** @param {import("../context.mjs").HostedContext} context */
export default async function draftUndoToolbar(context) {
  const { page, waitFor, select, saveState, branchEditor, undoButton } = context;
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
}
