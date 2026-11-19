import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

// D074.1, #225: the list renders only the rows in view (plus overscan, and the row with the tab stop), measures rows of
// different heights, and keeps the grid's row count and positions for screen readers. Keyboard navigation goes
// across the rendered window (Down, End, Home), Tab edits row after row past it, the edited row keeps the focus while
// it scrolls out of view, and a selection from outside (Next missing) is revealed. Adds four namespaces of 150
// messages to de.rmf2 and en.rmf2 (en lacks bulk2.m120) and restores both files at the end. Leaves the workbench,
// English, files.selected selected, nothing unsaved.
/** @param {import("../context.mjs").HostedContext} context */
export default async function listViewVirtualization(context) {
  const { page, reportDirectory, pageErrors, waitFor, select, saveState, reloadFiles, messageRow } = context;
  const { diskPath, enPath } = context.paths;
  const deBefore = await readFile(diskPath, "utf8");
  const enBefore = await readFile(enPath, "utf8");
  const bulk = (locale) => Array.from({ length: 4 }, (_, group) => {
    const lines = Array.from({ length: 150 }, (_, item) => {
      if (locale === "en" && group === 2 && item === 120) return "";
      const name = `m${String(item).padStart(3, "0")}`;
      // Every fifth text wraps over several lines in the list's text column: the rows differ in height.
      const long = item % 5 === 0 ? " that goes on for a long while so that the list's text column wraps it over a few lines of text" : "";
      return `  ${name} = ${locale === "en" ? "Text" : "Text de"} ${group}.${item}${long}\n`;
    });
    return `bulk${group} {\n${lines.join("")}}\n`;
  }).join("");
  await writeFile(diskPath, `${deBefore}${bulk("de")}`);
  await writeFile(enPath, `${enBefore}${bulk("en")}`);
  await reloadFiles();
  await messageRow("common_cancel").waitFor();
  await select("common_cancel");
  assert.equal(await saveState(), "Saved");

  const grid = page.getByRole("grid", { name: "Messages" });
  const scroller = page.getByTestId("list-view");
  const row = key => grid.locator(`[data-list-row="${key}"]`);
  const textCell = key => row(key).locator("[data-list-text]");
  const field = key => row(key).locator("[data-pattern-box] textarea[data-draft-field]");
  const selectedRow = async () => grid.locator('[role="row"][aria-selected="true"]').getAttribute("data-list-row");
  const focusedRow = async () => page.evaluate(() => document.activeElement?.closest("[data-list-row]")?.getAttribute("data-list-row") ?? null);
  const focusedField = async () => page.evaluate(() => document.activeElement instanceof HTMLTextAreaElement && document.activeElement.hasAttribute("data-draft-field"));
  // Whether the row is inside the scroller's viewport, below the sticky column headers.
  const inView = key => page.evaluate((rowKey) => {
    const element = document.querySelector(`[data-list-row="${CSS.escape(rowKey)}"]`);
    const scrollerElement = document.querySelector('[data-testid="list-view"]');
    const head = scrollerElement?.querySelector(".list-head");
    if (element === null || scrollerElement === null || head === null) return false;
    const box = element.getBoundingClientRect();
    const view = scrollerElement.getBoundingClientRect();
    return box.top >= head.getBoundingClientRect().bottom - 1 && box.bottom <= view.bottom + 1;
  }, key);
  const rendered = async () => grid.locator('[role="row"][aria-rowindex]').evaluateAll(rows => rows.map(row => ({
    key: row.getAttribute("data-list-row") ?? `# ${row.getAttribute("data-list-group")}`,
    index: Number(row.getAttribute("aria-rowindex")),
  })));

  await page.getByTestId("view-switch").getByRole("button", { name: "List", exact: true }).click();
  await grid.waitFor();
  await row("common_cancel").waitFor();

  // Only a window of the rows is in the DOM; the grid counts them all and numbers the shown rows in order. The selected
  // row (common.cancel, below the bulk namespaces) was revealed, so the top of the list is not rendered.
  const rowCount = Number(await grid.getAttribute("aria-rowcount"));
  assert.ok(rowCount > 600, `The grid counts ${rowCount} rows.`);
  await waitFor(async () => await inView("common_cancel"), "the selected row is revealed when the list opens");
  const first = await rendered();
  assert.ok(first.length < 120, `${first.length} rows are rendered for a list of ${rowCount}.`);
  assert.equal(first[0].index, 1, "The column headers are not row 1.");
  assert.ok(first.every((entry, position) => position === 0 || entry.index > first[position - 1].index), "The shown rows are out of order.");
  assert.equal(await row("bulk0_m010").count(), 0, "A row far above the window is rendered.");

  // Home and End: the first and the last message, far outside the window, selected, focused and shown.
  await textCell("common_cancel").focus();
  await page.keyboard.press("Home");
  await waitFor(async () => await selectedRow() === "bulk0_m000" && await focusedRow() === "bulk0_m000" && await inView("bulk0_m000"),
    "Home selects the first message");
  assert.equal(await row("common_cancel").count(), 0, "A row far below the window stays rendered.");
  // Rows differ in height and are measured: a long text's row is taller than a short one's.
  const heightOf = key => row(key).evaluate(element => element.getBoundingClientRect().height);
  assert.ok(await heightOf("bulk0_m000") > await heightOf("bulk0_m001") + 8, "A long text's row is not taller.");
  await page.keyboard.press("End");
  await waitFor(async () => {
    const shown = await rendered();
    const key = await selectedRow();
    return shown.at(-1)?.index === rowCount && shown.at(-1)?.key === key && await focusedRow() === key && await inView(key);
  }, "End selects, focuses and shows the last row");
  await page.keyboard.press("Home");
  await waitFor(async () => await focusedRow() === "bulk0_m000" && await inView("bulk0_m000"), "Home goes back to the first message");

  // Down goes past the rendered window: each step selects, focuses and shows the next row (into the next namespace).
  for (let step = 0; step < 150; step += 1) await page.keyboard.press("ArrowDown");
  await waitFor(async () => await selectedRow() === "bulk1_m000" && await focusedRow() === "bulk1_m000", "Down reaches bulk1.m000");
  await waitFor(async () => await inView("bulk1_m000"), "the row Down reached is in view");
  assert.equal(await row("bulk0_m000").count(), 0, "Rows far above the window stay rendered.");
  await page.keyboard.press("ArrowUp");
  await waitFor(async () => await selectedRow() === "bulk0_m149" && await focusedRow() === "bulk0_m149", "Up goes back over the namespace header");
  await page.keyboard.press("ArrowDown");
  await waitFor(async () => await focusedRow() === "bulk1_m000", "Down again");

  // Tab edits row after row, past the rendered window, each row's field focused.
  await page.keyboard.press("Enter");
  await waitFor(async () => await focusedRow() === "bulk1_m000" && await focusedField(), "Enter edits the row");
  for (let step = 1; step <= 61; step += 1) {
    await page.keyboard.press("Tab");
    const key = `bulk1_m${String(step).padStart(3, "0")}`;
    await waitFor(async () => await selectedRow() === key && await focusedRow() === key && await focusedField(), `Tab edits ${key}`);
  }
  await waitFor(async () => await inView("bulk1_m061"), "the row Tab reached is in view");
  assert.equal(await row("bulk1_m000").count(), 0, "The rows Tab left far behind stay rendered.");
  // The edited row keeps the focus while the list scrolls far away from it, and typing still edits it.
  await scroller.evaluate(element => { element.scrollTop = element.scrollHeight; });
  await page.waitForTimeout(200);
  await scroller.evaluate(element => { element.scrollTop = 0; });
  await page.waitForTimeout(200);
  assert.equal(await focusedRow(), "bulk1_m061", "The edited row lost the focus while it was scrolled out of view.");
  assert.ok(await focusedField(), "The edited row's field lost the focus.");
  await page.keyboard.type("!");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "typing after scrolling edits the row");
  assert.equal(await field("bulk1_m061").first().inputValue(), "Text 1.61!");
  assert.equal(await row("bulk1_m061").getAttribute("data-unsaved"), "true");
  await page.keyboard.press("Shift+Tab");
  await waitFor(async () => await selectedRow() === "bulk1_m060" && await focusedField() && await inView("bulk1_m060"), "Shift+Tab edits the row before");
  await page.keyboard.press("Escape");
  await page.keyboard.press("Control+s");
  await waitFor(async () => await saveState() === "Saved", "the edit is saved");
  assert.match(await readFile(enPath, "utf8"), /^ {2}m061 = Text 1\.61!$/m, "The edit after scrolling was not saved.");

  // A selection from outside the list is revealed: Next missing goes to bulk2.m120, far below.
  await page.keyboard.press("Home");
  await waitFor(async () => await focusedRow() === "bulk0_m000", "Home");
  await page.keyboard.press("Alt+ArrowDown");
  await waitFor(async () => await selectedRow() === "bulk2_m120", "Next missing selects bulk2.m120");
  await waitFor(async () => await inView("bulk2_m120"), "the row Next missing selected is revealed");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "list-view-virtualized.png") });

  // The fixture's files as they were, and the workbench.
  await page.getByTestId("view-switch").getByRole("button", { name: "Workbench", exact: true }).click();
  await writeFile(enPath, enBefore);
  await writeFile(diskPath, deBefore);
  await reloadFiles();
  await waitFor(async () => await messageRow("bulk0_m000").count() === 0, "the bulk namespaces are gone");
  await select("files_selected");
  assert.equal(await saveState(), "Saved");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
