import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { join } from "node:path";

// #225 P19: closing the window with unsaved work asks Save, Discard or Cancel. Serve-only mode cannot guard the
// browser's own close, but the palette's Close window is the application's RequestCloseAsync, which asks the page as the
// native window's close button does. Cancel keeps the window open with the draft; the stage then reverts the draft, so
// the window stays open with nothing unsaved and no dialog, as 380 left it (English, common.cancel selected).
/** @param {import("../context.mjs").HostedContext} context */
export default async function closeQuestion(context) {
  const { page, reportDirectory, pageErrors, waitFor, editor, saveState } = context;
  const { enPath } = context.paths;
  const saved = await readFile(enPath, "utf8");
  assert.equal(await saveState(), "Saved");
  const original = await editor().inputValue();
  await editor().fill("A draft the close question keeps");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "the draft is unsaved");

  const closeWindow = async () => {
    await page.keyboard.press("Control+k");
    await page.getByRole("textbox", { name: "Search commands", exact: true }).fill("close window");
    await page.getByRole("dialog").getByRole("option", { name: /^Close window/ }).click();
  };
  const question = page.getByRole("dialog", { name: "Save your changes before closing?" });
  await closeWindow();
  await question.waitFor();
  const summary = (await question.locator("[data-close-summary]").textContent())?.replace(/\s+/g, " ").trim();
  assert.equal(summary, "Unsaved text in one document: en.rmf2", "The question did not name the unsaved document.");
  for (const name of ["Cancel", "Discard and close", "Save and close"])
    assert.equal(await question.getByRole("button", { name, exact: true }).isEnabled(), true, `${name} is not offered.`);
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "close-question.png") });

  // Cancel: the window stays open and connected, the draft as it was, nothing written.
  await question.getByRole("button", { name: "Cancel", exact: true }).click();
  await question.waitFor({ state: "detached" });
  assert.equal(await editor().inputValue(), "A draft the close question keeps");
  assert.equal(await saveState(), "Unsaved changes: text in one document");
  assert.equal(await readFile(enPath, "utf8"), saved, "Cancel wrote the draft.");

  // Escape answers Cancel too, and the question can be asked again.
  await closeWindow();
  await question.waitFor();
  await page.keyboard.press("Escape");
  await question.waitFor({ state: "detached" });
  assert.equal(await editor().inputValue(), "A draft the close question keeps");

  // The host still answers: reverting the draft leaves nothing unsaved.
  await editor().fill(original);
  await waitFor(async () => await saveState() === "Saved", "the reverted draft is no unsaved change");
  assert.equal(await readFile(enPath, "utf8"), saved);
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
