import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { join } from "node:path";

// D074.1, Save and next: Ctrl+Enter (and the button next to the composer of a missing or outdated message) saves all
// unsaved work through the one save, then selects the next message of the same kind. An invalid text blocks the save:
// the message stays selected and the editor says why. Runs after 320 (alpha.first and zeta missing in English); hands
// the English file before its saves on as `enBeforeSaveAndNext`. Leaves English, alpha.first selected, nothing unsaved.
/** @param {import("../context.mjs").HostedContext} context */
export default async function saveAndNext(context) {
  const { page, reportDirectory, pageErrors, waitFor, select, editor, saveState, variantBox, composerSource, handoff } = context;
  const { enPath } = context.paths;
  const enBefore = await readFile(enPath, "utf8");
  handoff.set("enBeforeSaveAndNext", enBefore);
  const selected = async () => page.locator('[role="treeitem"][data-message-key][aria-selected="true"]').getAttribute("data-message-key");
  const notice = page.getByTestId("workbench-notice");
  const button = page.getByTestId("save-and-next");

  // Blocked: an invalid text in files.selected. Ctrl+Enter in the field stays on the message and says why; nothing is
  // written. A translated message that is not outdated offers no button, but the shortcut works.
  await select("files_selected");
  assert.equal(await button.count(), 0, "A translated message offers Save and next.");
  await page.getByRole("button", { name: "Message source", exact: true }).first().click();
  const validSource = await composerSource.inputValue();
  await composerSource.focus();
  await composerSource.press("Control+End");
  await page.keyboard.type(" {$oops");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "the broken source is unsaved");
  await page.keyboard.press("Control+Enter");
  await notice.filter({ hasText: "Not saved: the text has errors. Fix them, then save again." }).waitFor();
  assert.equal(await selected(), "files_selected", "A blocked save moved on.");
  assert.equal(await readFile(enPath, "utf8"), enBefore, "A blocked save wrote the file.");
  assert.equal(await saveState(), "Unsaved changes: text in one document");
  await page.keyboard.press("Control+z");
  await waitFor(async () => (await composerSource.inputValue()) === validSource && await saveState() === "Saved", "the broken source undone");
  await page.getByRole("button", { name: "Message source", exact: true }).first().click();

  // files.deleted is missing in English: the button names the kind and its shortcut; Ctrl+Enter in the composer saves
  // it and selects the next missing message, zeta.
  await select("files_deleted");
  await button.waitFor();
  assert.equal((await button.textContent())?.replace(/\s+/g, " ").trim(), "Save and next missing Ctrl+Enter");
  assert.equal(await button.getAttribute("title"), "Save and next missing (Ctrl+Enter)");
  await variantBox("one").fill("One file deleted");
  await variantBox("Fallback").fill("Several files deleted");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "the starter has text");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "save-and-next-en.png") });
  await page.keyboard.press("Control+Enter");
  await waitFor(async () => await selected() === "zeta", "Ctrl+Enter selects the next missing message");
  await waitFor(async () => await saveState() === "Saved", "nothing unsaved after Save and next");
  const written = await readFile(enPath, "utf8");
  for (const line of ["deleted =", "one {{One file deleted}}", "* {{Several files deleted}}"]) assert.ok(written.includes(line), `en.rmf2 lacks ${line}`);

  // The button: zeta is saved and the next missing message is alpha.first, at the top of the list.
  await editor().fill("Last message");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "zeta has text");
  await button.click();
  await waitFor(async () => await selected() === "alpha_first", "the button selects alpha.first");
  await notice.filter({ hasText: "Continued from the top of the list." }).waitFor();
  assert.match(await readFile(enPath, "utf8"), /^zeta = Last message$/m, "zeta was not saved.");
  assert.equal(await saveState(), "Saved");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
