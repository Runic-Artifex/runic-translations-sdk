import assert from "node:assert/strict";
import { join } from "node:path";

// D074.1, the workbench: in a wide window the message tree is on the left, the editor column (heading, workflow,
// source panel, composer) in the middle and the rail with the preview and the diagnostics on the right, side by side.
// Leaves the English locale and files.selected selected, at the runner's 1440 × 1100 window.
/** @param {import("../context.mjs").HostedContext} context */
export default async function workbenchRailWide(context) {
  const { page, reportDirectory, pageErrors, select, messageRow } = context;
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("files_selected");
  const column = page.getByTestId("editor-column");
  const rail = page.getByRole("complementary", { name: "Preview and diagnostics", exact: true });
  await rail.waitFor();
  assert.equal(await rail.getAttribute("data-testid"), "editor-rail");

  // The editor column holds the heading, the workflow, the source panel and the composer; the rail the preview and
  // the diagnostics.
  await column.getByRole("heading", { level: 2, name: "files.selected", exact: true }).waitFor();
  await column.getByTestId("workflow-state").waitFor();
  await column.getByTestId("source-text").waitFor();
  await column.getByRole("textbox", { name: /Translation for count: one/ }).first().waitFor();
  await rail.locator(".message-preview").waitFor();
  await rail.getByText("Ready to save", { exact: true }).waitFor();
  assert.equal(await column.locator(".message-preview").count(), 0, "The preview is still in the editor column.");
  assert.equal(await column.getByText("Ready to save", { exact: true }).count(), 0, "The diagnostics are still in the editor column.");

  // Side by side: the rail starts right of the editor column, at its top, and the tree is visible on the left.
  const columnBox = await column.boundingBox();
  const railBox = await rail.boundingBox();
  const rowBox = await messageRow("files_selected").boundingBox();
  assert.ok(columnBox && railBox && rowBox, "The workbench panes have no layout.");
  assert.ok(railBox.x >= columnBox.x + columnBox.width - 1, `The rail (x ${railBox.x}) is not right of the editor column (ends at ${columnBox.x + columnBox.width}).`);
  assert.ok(Math.abs(railBox.y - columnBox.y) <= 1, "The rail and the editor column do not start at the same height.");
  assert.ok(rowBox.x + rowBox.width <= columnBox.x, "The message tree is not left of the editor column.");
  assert.ok(railBox.width >= 280 && railBox.width <= 420, `The rail is ${railBox.width}px wide.`);
  // Each pane scrolls on its own: the rail keeps its place when the editor column scrolls.
  await column.evaluate(element => { element.scrollTop = element.scrollHeight; });
  assert.equal((await rail.boundingBox())?.y, railBox.y, "The rail scrolled with the editor column.");
  await column.evaluate(element => { element.scrollTop = 0; });
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "workbench-wide-en.png") });
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
