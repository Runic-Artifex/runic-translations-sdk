import assert from "node:assert/strict";
import { join } from "node:path";

// The pattern box is one text field (P22): a click on its empty area puts the caret at the end of the text.
/** @param {import("../context.mjs").HostedContext} context */
export default async function translateViewPatternBox(context) {
  const { page, reportDirectory, waitFor, editor, saveState } = context;
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
}
