import assert from "node:assert/strict";
import { join } from "node:path";

// In the German interface the source panel and the file tab are German too.
/** @param {import("../context.mjs").HostedContext} context */
export default async function translateViewGermanUi(context) {
  const { page, reportDirectory, select } = context;
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("files_selected");
  await page.getByRole("region", { name: "Ausgangstext (Deutsch)", exact: true }).getByText("One edited file selected", { exact: true }).waitFor();
  assert.equal(await page.locator('[data-slot="toggle-group-item"]', { hasText: /^Datei$/ }).count(), 1, "The file tab is not named Datei.");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "translate-view-de.png"), fullPage: true });
}
