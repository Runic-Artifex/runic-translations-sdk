import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

// Review states live in the SDK review file beside runic.json, the same file runic-translations review mark and
// xliff import write. An entry recorded for another source is stale (RTR0090) in the row's quality checks;
// approving it in the workflow records the source the editor loaded, and the note goes onto the review entry.
/** @param {import("../context.mjs").HostedContext} context */
export default async function sdkReviewFile(context) {
  const { page, workspace, reportDirectory, waitFor, select, saveState, reloadFiles } = context;
  const { reviewPath } = context.paths;
  const { staleFingerprint } = context.fixture;
  await writeFile(reviewPath, JSON.stringify({
    $schema: "runic.translations.interchange-review/2", catalog: "runic-hosted-e2e",
    entries: [{ key: "common_save", locale: "en", state: "needs-review", sourceFingerprint: staleFingerprint }],
  }, null, 2) + "\n");
  await reloadFiles();
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("common_save");
  const workflow = page.getByRole("button", { name: /^Workflow/ }).first();
  await workflow.getByText("Source changed", { exact: true }).waitFor();
  await workflow.click();
  // The quality check names the editor's actions instead of the diagnostic's `runic-translations review mark` (#225 P20).
  const staleCheck = page.getByRole("region", { name: "Quality checks", exact: true }).getByRole("button", { name: /^RTR0090/ });
  await staleCheck.getByText(/confirm it or mark it for review in the workflow panel/).waitFor();
  assert.doesNotMatch(await staleCheck.innerText(), /review mark/, "The workflow's quality check repeats the command-line instruction.");
  await page.locator("#workflow-status").click();
  await page.getByRole("option", { name: "Approved", exact: true }).click();
  await page.locator("#review-note").fill("Checked against the new source");
  // One save: the toolbar names the unsaved review status, and Ctrl+S writes the review file.
  await waitFor(async () => await saveState() === "Unsaved changes: review status", "unsaved review status named in the toolbar");
  assert.equal(await page.getByRole("button", { name: "Save workflow" }).count(), 0, "There is no separate workflow save.");
  await page.keyboard.press("Control+s");
  await waitFor(async () => {
    const review = JSON.parse(await readFile(reviewPath, "utf8"));
    const entry = review.entries.find(item => item.key === "common_save" && item.locale === "en");
    return entry?.state === "approved" && entry.sourceFingerprint?.startsWith("sha256:") && entry.sourceFingerprint !== staleFingerprint;
  }, "approval recorded in the SDK review file for the current source");
  await workflow.getByText("Source changed", { exact: true }).waitFor({ state: "detached" });
  const saved = JSON.parse(await readFile(reviewPath, "utf8")).entries.find(item => item.key === "common_save" && item.locale === "en");
  assert.equal(saved?.note, "Checked against the new source", "The note of a row with a review entry is not on the review entry.");
  const editorStatePath = join(workspace, ".runic-translations", "runic-hosted-e2e.editor-state.json");
  const editorState = await readFile(editorStatePath, "utf8").catch(() => "");
  assert.ok(!editorState.includes('"state"') && !editorState.includes("Checked against"),
    "Review states or the review entry's note were written to the editor state.");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "review-file-en.png"), fullPage: true });
}
