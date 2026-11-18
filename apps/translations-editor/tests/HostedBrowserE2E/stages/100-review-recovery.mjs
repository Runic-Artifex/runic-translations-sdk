import { readFile } from "node:fs/promises";
import { join } from "node:path";

// Unsaved review changes are kept in the native editor state like text drafts: after a reload (a closed window or
// a crash) they are offered for recovery, restored on top of the review file, and saved with Ctrl+S.
/** @param {import("../context.mjs").HostedContext} context */
export default async function reviewRecovery(context) {
  const { page, reportDirectory, waitFor, select, saveState, sentLocalState } = context;
  const { reviewPath } = context.paths;
  const reviewDraftSent = () => sentLocalState.some(entries => entries.some(entry =>
    entry.key.startsWith("runic-translations:review-drafts:1:") && entry.value.includes("Recovered after reload")));
  await page.locator("#review-note").fill("Recovered after reload");
  await waitFor(async () => reviewDraftSent(), "native save of the unsaved review change");
  await page.reload({ waitUntil: "domcontentloaded" });
  const recovered = page.getByTestId("recovered-work");
  await recovered.getByText("Unsaved review changes were found in local application storage.").waitFor();
  await recovered.getByRole("button", { name: "Restore", exact: true }).click();
  await recovered.waitFor({ state: "detached" });
  await waitFor(async () => await saveState() === "Unsaved changes: review status", "restored review change is unsaved");
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("common_save");
  await page.getByRole("button", { name: /^Workflow/ }).first().click();
  await waitFor(async () => (await page.locator("#review-note").inputValue()) === "Recovered after reload", "recovered review note shown in the workflow");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "review-unsaved-en.png"), fullPage: true });
  await page.keyboard.press("Control+s");
  await waitFor(async () => JSON.parse(await readFile(reviewPath, "utf8")).entries
    .find(item => item.key === "common_save" && item.locale === "en")?.note === "Recovered after reload", "recovered review note saved to the review file");
  await waitFor(async () => await saveState() === "Saved", "nothing unsaved after saving the recovered review change");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "review-recovered-en.png"), fullPage: true });
}
