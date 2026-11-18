import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

// A stale approval (approved for source text that changed since) is re-confirmed in the editor (#225 P20): with
// "Confirm translation", or by choosing the same state again; the one save records it for the current source.
/** @param {import("../context.mjs").HostedContext} context */
export default async function staleApprovalConfirm(context) {
  const { page, reportDirectory, waitFor, select, saveState, reloadFiles } = context;
  const { reviewPath } = context.paths;
  const { staleFingerprint } = context.fixture;
  const staleReview = JSON.parse(await readFile(reviewPath, "utf8"));
  staleReview.entries.push(
    { key: "common_cancel", locale: "en", state: "approved", sourceFingerprint: staleFingerprint },
    { key: "files_selected", locale: "en", state: "approved", sourceFingerprint: staleFingerprint },
  );
  await writeFile(reviewPath, JSON.stringify(staleReview, null, 2) + "\n");
  await reloadFiles();
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("common_cancel");
  assert.equal(await page.title(), "runic-hosted-e2e – Runic Translations Editor", "The window title names the catalog.");
  const staleNotice = page.getByTestId("workflow-stale");
  await staleNotice.getByText("The source text changed after this message was marked “Approved”.", { exact: false }).waitFor();
  assert.equal(await staleNotice.getByRole("button", { name: "Mark needs review", exact: true }).count(), 1);
  await staleNotice.getByRole("button", { name: "Confirm translation", exact: true }).click();
  await staleNotice.waitFor({ state: "detached" });
  await waitFor(async () => await saveState() === "Unsaved changes: review status", "the confirmation is an unsaved review change");
  assert.equal(await page.title(), "• runic-hosted-e2e – Runic Translations Editor", "The window title marks unsaved work.");
  await select("files_selected");
  await page.getByTestId("workflow-stale").waitFor();
  await page.getByRole("button", { name: /^Workflow/ }).first().click();
  await page.locator("#workflow-status").click();
  await page.getByRole("option", { name: "Approved", exact: true }).click();
  await page.getByTestId("workflow-stale").waitFor({ state: "detached" });
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "stale-confirmed-en.png"), fullPage: true });
  await page.keyboard.press("Control+s");
  await waitFor(async () => {
    const entries = JSON.parse(await readFile(reviewPath, "utf8")).entries;
    return ["common_cancel", "files_selected"].every(key => {
      const entry = entries.find(item => item.key === key && item.locale === "en");
      return entry?.state === "approved" && entry.sourceFingerprint?.startsWith("sha256:") && entry.sourceFingerprint !== staleFingerprint;
    });
  }, "both stale approvals recorded again for the current source");
  await waitFor(async () => await saveState() === "Saved", "nothing unsaved after saving the confirmations");
  assert.equal(await page.title(), "runic-hosted-e2e – Runic Translations Editor");
}
