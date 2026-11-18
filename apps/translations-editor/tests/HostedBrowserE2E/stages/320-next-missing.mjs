import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";

// D074.1, Next missing: the toolbar button and Alt+↓ select the next message missing in the selected locale in the
// order of the tree, Alt+↑ the previous one. At the end of the list they wrap to the other end and say so. With the
// outdated filter on, the button steps through outdated messages and is named for them.
// Adds alpha.first and zeta to the base locale (de.rmf2), so English misses alpha.first, files.deleted and zeta, and
// hands the original de.rmf2 on as `deBeforeNextMissing`. Leaves English, zeta selected and nothing unsaved.
/** @param {import("../context.mjs").HostedContext} context */
export default async function nextMissing(context) {
  const { page, pageErrors, waitFor, select, saveState, reloadFiles, messageRow, handoff } = context;
  const { diskPath } = context.paths;
  const original = await readFile(diskPath, "utf8");
  handoff.set("deBeforeNextMissing", original);
  await writeFile(diskPath, `${original}alpha {\n  first = Erste Nachricht\n}\nzeta = Letzte Nachricht\n`);
  await reloadFiles();
  await messageRow("zeta").waitFor();
  await messageRow("alpha_first").waitFor();
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("common_cancel");

  const selected = async () => page.locator('[role="treeitem"][data-message-key][aria-selected="true"]').getAttribute("data-message-key");
  const heading = page.getByTestId("editor-column").getByRole("heading", { level: 2 });
  const notice = page.getByTestId("workbench-notice");
  const next = page.getByTestId("next-message");
  assert.equal(await next.getAttribute("aria-label"), "Next missing");
  assert.match(await next.getAttribute("title"), /^Next missing \(Alt\+↓\)$/);
  assert.equal(await next.getAttribute("aria-keyshortcuts"), "Alt+ArrowDown");

  // Tree order: alpha.first, common.cancel, common.save, files.deleted, files.selected, zeta. From common.cancel (not
  // missing) the next missing message is files.deleted.
  await next.click();
  await waitFor(async () => await selected() === "files_deleted", "Next missing selects files.deleted");
  await heading.filter({ hasText: "files.deleted" }).waitFor();
  assert.equal(await notice.count(), 0, "A step within the list reports nothing.");
  // Alt+↓ outside a text field, and in a draft field (the starter's variant).
  await page.getByRole("textbox", { name: /^Translation for count: one/ }).first().focus();
  await page.keyboard.press("Alt+ArrowDown");
  await waitFor(async () => await selected() === "zeta", "Alt+↓ in a draft field selects zeta");
  assert.equal(await saveState(), "Saved", "Stepping wrote nothing and left nothing unsaved.");
  // At the end of the list Next wraps to the top and says so.
  await messageRow("zeta").focus();
  await page.keyboard.press("Alt+ArrowDown");
  await waitFor(async () => await selected() === "alpha_first", "Alt+↓ wraps to alpha.first");
  await notice.filter({ hasText: "Continued from the top of the list." }).waitFor();
  assert.equal(await notice.getAttribute("role"), "status");
  // Alt+↑ goes back, wrapping to the bottom.
  await page.evaluate(() => (document.activeElement instanceof HTMLElement ? document.activeElement.blur() : undefined));
  await page.keyboard.press("Alt+ArrowUp");
  await waitFor(async () => await selected() === "zeta", "Alt+↑ wraps back to zeta");
  await notice.filter({ hasText: "Continued from the bottom of the list." }).waitFor();
  await page.keyboard.press("Alt+ArrowUp");
  await waitFor(async () => await selected() === "files_deleted", "Alt+↑ selects files.deleted");
  assert.equal(await notice.count(), 0);
  // In a text field that is not a draft (the search), Alt+↓ is left to the field.
  await page.locator("#message-search").focus();
  await page.keyboard.press("Alt+ArrowDown");
  assert.equal(await selected(), "files_deleted", "Alt+↓ in the search field stepped.");

  // The outdated filter: the button follows it. common.cancel is the outdated message of English after 280.
  await page.getByRole("button", { name: /^Message filters: / }).click();
  await page.getByRole("menuitemradio", { name: /^Stale/ }).click();
  await waitFor(async () => await next.getAttribute("aria-label") === "Next outdated", "the button is named Next outdated");
  assert.match(await next.getAttribute("title"), /^(Next outdated \(Alt\+↓\)|No other message is outdated in English\.)$/);
  await page.getByRole("button", { name: /^Message filters: / }).click();
  await page.getByRole("menuitemradio", { name: /^Review/ }).click();
  await waitFor(async () => await next.getAttribute("aria-label") === "Next to review", "the button is named Next to review");
  await page.getByRole("button", { name: /^Message filters: / }).click();
  await page.getByRole("menuitemradio", { name: /^All/ }).click();
  await waitFor(async () => await next.getAttribute("aria-label") === "Next missing", "the button is named Next missing again");

  await page.evaluate(() => (document.activeElement instanceof HTMLElement ? document.activeElement.blur() : undefined));
  await page.keyboard.press("Alt+ArrowDown");
  await waitFor(async () => await selected() === "zeta", "back on zeta");
  assert.equal(await saveState(), "Saved");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
