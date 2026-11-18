import assert from "node:assert/strict";

// The translate view (#225 P1, P8): another locale's editor shows the base locale's text read-only above it, one
// block per variant with the variable as a chip; the base locale (de in this workspace) has no source panel, and the
// header names no `base` layer.
/** @param {import("../context.mjs").HostedContext} context */
export default async function translateViewSource(context) {
  const { page, select } = context;
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
}
