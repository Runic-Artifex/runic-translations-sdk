import assert from "node:assert/strict";

// #225 M1: the message list is a namespace tree; B6 heading and P15 hidden-selection note.
/** @param {import("../context.mjs").HostedContext} context */
export default async function messageTree(context) {
  const { page, waitFor, select, messageRow, groupRow, focusedRow } = context;
  const tree = page.getByRole("tree", { name: "Messages", exact: true });
  assert.equal(await groupRow("common").getAttribute("aria-expanded"), "true", "The common group starts expanded.");
  assert.match(await groupRow("common").getAttribute("aria-label"), /^common, 2 messages/);
  assert.deepEqual(await tree.locator('[role="treeitem"][aria-level="2"]').evaluateAll(rows => rows.map(row => row.querySelector(".font-mono")?.textContent.trim())).then(texts => texts.slice(0, 2)),
    ["cancel", "save"], "The group lists its messages by their last segment.");
  await select("common_cancel");
  assert.equal(await page.getByRole("heading", { level: 2, name: "common.cancel", exact: true }).count(), 1, "The heading shows the dotted path.");
  assert.match(await page.getByTestId("message-code-identifier").textContent(), /^\s*Code identifier\s*common_cancel\s*$/,
    "The code identifier is secondary text.");
  // Keyboard: Left moves to the group, Left collapses, Right expands, Right enters, Down moves, Enter selects.
  await messageRow("common_cancel").focus();
  await page.keyboard.press("ArrowLeft");
  assert.equal(await focusedRow(), "g:common", "Left moves from a message to its group.");
  await page.keyboard.press("ArrowLeft");
  await waitFor(async () => await groupRow("common").getAttribute("aria-expanded") === "false", "Left collapses the group");
  assert.equal(await messageRow("common_cancel").count(), 0, "A collapsed group hides its messages.");
  await page.keyboard.press("ArrowRight");
  await messageRow("common_cancel").waitFor();
  await page.keyboard.press("ArrowRight");
  assert.equal(await focusedRow(), "m:common_cancel", "Right moves into an expanded group.");
  await page.keyboard.press("ArrowDown");
  assert.equal(await focusedRow(), "m:common_save");
  await page.keyboard.press("Enter");
  await page.getByRole("heading", { level: 2, name: "common.save", exact: true }).waitFor();
  assert.equal(await messageRow("common_save").getAttribute("aria-selected"), "true");
  await page.keyboard.press("Home");
  assert.equal(await focusedRow(), "g:common", "Home moves to the first row.");
  // A click collapses a group; a search expands the groups with matches and leaves the own state afterwards.
  await groupRow("common").click();
  await waitFor(async () => await messageRow("common_save").count() === 0, "a click collapses the group");
  await select("files_selected");
  await page.locator("#message-search").fill("Queued cancel");
  await messageRow("common_cancel").waitFor();
  assert.equal(await messageRow("files_selected").count(), 0, "The search hides the other messages.");
  const hiddenNote = page.getByTestId("selected-message-hidden");
  await hiddenNote.waitFor();
  assert.match(await hiddenNote.textContent(), /Selected message is hidden by the filter\./);
  assert.equal(await page.getByRole("heading", { level: 2, name: "files.selected", exact: true }).count(), 1, "The selection stays.");
  await hiddenNote.getByRole("button", { name: "Show all messages", exact: true }).click();
  await messageRow("files_selected").waitFor();
  assert.equal(await page.locator("#message-search").inputValue(), "", "Show all clears the search.");
  assert.equal(await hiddenNote.count(), 0);
  assert.equal(await groupRow("common").getAttribute("aria-expanded"), "false", "The own collapse state is back after the search.");
  await groupRow("common").click();
  await messageRow("common_cancel").waitFor();
}
