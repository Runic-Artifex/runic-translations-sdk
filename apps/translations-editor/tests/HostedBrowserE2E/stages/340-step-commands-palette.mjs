import assert from "node:assert/strict";
import { writeFile } from "node:fs/promises";
import { join } from "node:path";

// D074.1: Next missing, Previous missing and Save and next are commands of the palette, with their shortcuts, and run
// from it. Then restores de.rmf2 and en.rmf2 as 320 and 330 found them. Leaves English, common.cancel selected.
/** @param {import("../context.mjs").HostedContext} context */
export default async function stepCommandsPalette(context) {
  const { page, reportDirectory, pageErrors, waitFor, select, saveState, reloadFiles, messageRow, handoff } = context;
  const { diskPath, enPath } = context.paths;
  const selected = async () => page.locator('[role="treeitem"][data-message-key][aria-selected="true"]').getAttribute("data-message-key");
  // French misses alpha.first and zeta.
  await page.getByRole("button", { name: /^fr / }).first().click();
  await select("common_cancel");

  await page.keyboard.press("Control+k");
  const search = page.getByRole("textbox", { name: "Search commands", exact: true });
  await search.fill("missing");
  const option = name => page.getByRole("dialog").getByRole("option", { name });
  await option(/^Next missing/).waitFor();
  assert.equal((await option(/^Next missing/).textContent())?.replace(/\s+/g, " ").trim(), "Next missing Alt+↓");
  assert.equal((await option(/^Previous missing/).textContent())?.replace(/\s+/g, " ").trim(), "Previous missing Alt+↑");
  assert.equal((await option(/^Save and next missing/).textContent())?.replace(/\s+/g, " ").trim(), "Save and next missing Ctrl+Enter");
  assert.equal(await option(/^Next missing/).getAttribute("aria-disabled"), "false");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "palette-step-commands.png") });
  // The first match is Next missing; Enter runs it.
  assert.equal(await page.getByRole("dialog").getByRole("option").first().getAttribute("id"), "palette-option-document.next-message");
  await page.keyboard.press("Enter");
  await waitFor(async () => await selected() === "zeta", "the palette's Next missing selects zeta");
  // Previous missing from the palette goes back to alpha.first.
  await page.keyboard.press("Control+k");
  await search.fill("previous missing");
  await option(/^Previous missing/).click();
  await waitFor(async () => await selected() === "alpha_first", "the palette's Previous missing selects alpha.first");
  assert.equal(await saveState(), "Saved");

  // Restore the files of 320 and 330.
  await writeFile(diskPath, handoff.get("deBeforeNextMissing"));
  await writeFile(enPath, handoff.get("enBeforeSaveAndNext"));
  await reloadFiles();
  await waitFor(async () => await messageRow("zeta").count() === 0, "zeta is gone after the restore");
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("common_cancel");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
