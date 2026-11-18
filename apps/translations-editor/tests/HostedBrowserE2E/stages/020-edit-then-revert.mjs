import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

// Unsaved means "differs from the saved text": an edit typed back to the saved text leaves nothing to save.
/** @param {import("../context.mjs").HostedContext} context */
export default async function editThenRevert(context) {
  const { page, waitFor, editor, saveState } = context;
  const { diskPath } = context.paths;
  await editor().fill("A temporary edit");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "unsaved text named in the toolbar");
  assert.equal(await page.getByRole("button", { name: "Save", exact: true }).isEnabled(), true);
  await editor().fill("Queued cancel from the real editor");
  await waitFor(async () => await saveState() === "Saved", "reverted edit is no unsaved change");
  assert.equal(await page.getByRole("button", { name: "Saved", exact: true }).isDisabled(), true, "Save stays disabled after a revert.");
  assert.ok((await readFile(diskPath, "utf8")).includes("cancel = Queued cancel from the real editor"));
}
