import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

// D074.1, the side-by-side list: the toolbar's Workbench | List switch keeps the selection, the list shows the selected
// locale's messages in tree order under namespace headers with the source text beside them, a row is edited in place
// and saved through the one save, a missing row starts empty beside its source text and Tab goes on to the next row,
// and a plural message opens in the workbench. Adds a `listing` namespace to the base locale (de.rmf2) and restores
// both files at the end. Leaves the workbench, English, files.selected selected, nothing unsaved.
/** @param {import("../context.mjs").HostedContext} context */
export default async function listView(context) {
  const { page, reportDirectory, pageErrors, waitFor, select, saveState, reloadFiles, messageRow, sentLocalState } = context;
  const { diskPath, enPath } = context.paths;
  const deBefore = await readFile(diskPath, "utf8");
  const enBefore = await readFile(enPath, "utf8");
  await writeFile(diskPath, `${deBefore}listing {\n  greeting = Hallo {$name}!\n  title = Liste\n  zone = Zone\n}\n`);
  await reloadFiles();
  await messageRow("listing_title").waitFor();
  await select("common_cancel");
  assert.equal(await saveState(), "Saved");

  const viewSwitch = page.getByTestId("view-switch");
  const listButton = viewSwitch.getByRole("button", { name: "List", exact: true });
  const workbenchButton = viewSwitch.getByRole("button", { name: "Workbench", exact: true });
  const grid = page.getByRole("grid", { name: "Messages" });
  const row = key => grid.locator(`[data-list-row="${key}"]`);
  const textCell = key => row(key).locator("[data-list-text]");
  const field = key => row(key).locator("[data-pattern-box] textarea[data-draft-field]");
  const selectedRow = async () => grid.locator('[role="row"][aria-selected="true"]').getAttribute("data-list-row");
  const focusedRow = async () => page.evaluate(() => document.activeElement?.closest("[data-list-row]")?.getAttribute("data-list-row") ?? null);
  const focusedField = async () => page.evaluate(() => document.activeElement instanceof HTMLTextAreaElement && document.activeElement.hasAttribute("data-draft-field"));

  // The switch: the list replaces the workbench, keeps the selection, and is remembered in the editor's local state.
  assert.equal(await workbenchButton.getAttribute("aria-pressed"), "true");
  await listButton.click();
  await grid.waitFor();
  assert.equal(await listButton.getAttribute("aria-pressed"), "true");
  assert.equal(await page.getByTestId("workbench").count(), 0, "The workbench is still shown beside the list.");
  assert.equal(await selectedRow(), "common_cancel", "The selection did not carry over to the list.");
  await waitFor(async () => sentLocalState.some(entries => entries.some(entry => entry.key === "runic-translations.workspace-layout" && entry.value === "list")),
    "the list view is stored");
  const headers = (await grid.getByRole("columnheader").allTextContents()).map(text => text.trim());
  assert.deepEqual(headers, ["Path", "German", "English", "State"]);
  // Tree order under namespace headers.
  const order = await grid.locator("[data-list-row]").evaluateAll(rows => rows.map(row => row.getAttribute("data-list-row")));
  assert.deepEqual(order.filter(key => ["common_cancel", "common_save", "files_selected", "listing_greeting", "listing_title"].includes(key)),
    ["common_cancel", "common_save", "files_selected", "listing_greeting", "listing_title"]);
  await grid.locator('[data-list-group="listing"]').waitFor();
  // The source text shows its placeholder as a chip; a missing translation is named so, with the hollow square.
  assert.equal(await row("listing_greeting").locator(".list-source .placeholder-chip").textContent(), "{$name}");
  assert.equal(await row("listing_title").getAttribute("data-state"), "missing");
  assert.equal((await row("listing_title").locator(".list-state").textContent())?.trim(), "Missing");
  assert.equal(await row("listing_title").locator('.state-mark[data-state-mark="missing"]').count(), 1);
  assert.equal((await textCell("listing_title").textContent())?.trim(), "Not translated");

  // Keyboard: Up and Down move the selection, Enter edits in place, Escape leaves editing with the focus on the row.
  await textCell("common_cancel").focus();
  await page.keyboard.press("ArrowDown");
  await waitFor(async () => await selectedRow() === "common_save" && await focusedRow() === "common_save", "Down selects common.save");
  await page.keyboard.press("Enter");
  await waitFor(focusedField, "Enter focuses the row's field");
  await field("common_save").first().fill("Store");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "the list's edit is unsaved");
  await page.keyboard.press("Escape");
  await waitFor(async () => await focusedRow() === "common_save" && !(await focusedField()), "Escape leaves editing");
  assert.equal((await textCell("common_save").textContent())?.trim(), "Store");
  // The one save: Ctrl+S writes the list's edit.
  await page.keyboard.press("Control+s");
  await waitFor(async () => await saveState() === "Saved", "Ctrl+S saves the list's edit");
  assert.match(await readFile(enPath, "utf8"), /^ {2}save = Store$/m, "The list's edit was not saved.");

  // A missing row starts empty beside its source text; Tab goes on to the next row's text, which is edited in place.
  await textCell("listing_title").click();
  await waitFor(async () => await focusedRow() === "listing_title" && await focusedField(), "the missing row's empty field");
  assert.equal(await field("listing_title").first().inputValue(), "", "A missing translation does not start empty.");
  assert.equal((await row("listing_title").locator(".list-source").textContent())?.trim(), "Liste");
  await page.keyboard.type("List");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "the starter has text");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "list-view.png") });
  // listing.zone is missing too: Tab selects it and its starter's empty field takes the focus.
  await page.keyboard.press("Tab");
  await waitFor(async () => await selectedRow() === "listing_zone" && await focusedRow() === "listing_zone" && await focusedField(),
    "Tab edits the next row");
  assert.equal(await field("listing_zone").first().inputValue(), "");
  await page.keyboard.press("Escape");
  await page.keyboard.press("Control+s");
  await waitFor(async () => await saveState() === "Saved", "the missing row is saved");
  assert.match(await readFile(enPath, "utf8"), /title = List$/m, "The missing row was not saved.");
  await waitFor(async () => await row("listing_title").getAttribute("data-state") !== "missing", "the saved row is no longer missing");

  // A plural message shows a summary and opens in the workbench, selected.
  assert.equal(await row("files_selected").getAttribute("data-inline"), "false");
  await textCell("files_selected").click();
  await page.getByTestId("workbench").waitFor();
  assert.equal(await workbenchButton.getAttribute("aria-pressed"), "true");
  assert.equal(await page.locator('[role="treeitem"][data-message-key][aria-selected="true"]').getAttribute("data-message-key"), "files_selected");
  assert.equal(await grid.count(), 0);

  // The fixture's files as they were: the list's edits undone on disk, the listing namespace gone.
  await writeFile(enPath, enBefore);
  await writeFile(diskPath, deBefore);
  await reloadFiles();
  await waitFor(async () => await messageRow("listing_title").count() === 0, "the listing namespace is gone");
  assert.equal(await saveState(), "Saved");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
