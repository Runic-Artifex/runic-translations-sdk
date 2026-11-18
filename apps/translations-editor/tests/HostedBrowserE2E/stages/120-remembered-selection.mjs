import assert from "node:assert/strict";

// The last locale and message of the catalog are restored on the next launch (#225 P24).
/** @param {import("../context.mjs").HostedContext} context */
export default async function rememberedSelection(context) {
  const { page, waitFor, select, messageRow, groupRow, sentLocalState } = context;
  await page.getByRole("button", { name: /^fr / }).first().click();
  await select("common_save");
  await waitFor(async () => sentLocalState.some(entries => entries.some(entry =>
    entry.key === "runic-translations:catalog-selection:1" && entry.value.includes('"locale":"fr","key":"common_save"'))), "native save of the selection");
  // Its group is collapsed when the window closes: the restored message is revealed in the tree on the next launch.
  await groupRow("common").click();
  await waitFor(async () => await messageRow("common_save").count() === 0, "a click collapses the selected message's group");
  await waitFor(async () => sentLocalState.some(entries => entries.some(entry =>
    entry.key === "runic-translations:message-tree:1" && /"collapsed":\[[^\]]*"common"/.test(entry.value))), "native save of the collapsed group");
  await page.reload({ waitUntil: "domcontentloaded" });
  await page.getByRole("heading", { level: 2, name: "common.save", exact: true }).waitFor();
  assert.equal(await page.getByRole("button", { name: /^fr / }).first().getAttribute("aria-pressed"), "true", "The locale was not restored.");
  await waitFor(async () => await messageRow("common_save").getAttribute("aria-selected") === "true", "the restored message revealed and selected in the tree");
  assert.equal(await groupRow("common").getAttribute("aria-expanded"), "true", "The restored message's group is expanded.");
  await page.getByRole("button", { name: /^en / }).first().click();
}
