import assert from "node:assert/strict";

// The "File" tab (formerly "Source") still opens the whole locale file.
/** @param {import("../context.mjs").HostedContext} context */
export default async function translateViewFileTab(context) {
  const { page, waitFor, select, editor } = context;
  const { preservedComment } = context.fixture;
  await select("common_cancel");
  await page.locator('[data-slot="toggle-group-item"]', { hasText: /^File$/ }).click();
  await waitFor(async () => (await page.locator("#translation-value").inputValue()).startsWith(preservedComment), "file view of en.rmf2");
  assert.match(await page.locator("#translation-value").inputValue(), /common \{\n  cancel = Cancel\n/);
  assert.equal(await page.getByTestId("source-text").count(), 0, "The file view shows the file only.");
  await page.locator('[data-slot="toggle-group-item"]', { hasText: /^Translation$/ }).click();
  await editor().waitFor();
}
