import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

// #225 P20: a stale approval shows the source it was approved for next to the current one, with the changed words
// marked, and "Confirm translation" records the new source text too, beside the review file
// (<catalog>.review-sources.json). Leaves the English locale selected, nothing unsaved, and de.rmf2 as it found it.
/** @param {import("../context.mjs").HostedContext} context */
export default async function staleSourceDiff(context) {
  const { page, workspace, reportDirectory, pageErrors, waitFor, select, saveState, reloadFiles } = context;
  const { diskPath, reviewPath } = context.paths;
  const sourcesPath = join(workspace, "runic-hosted-e2e.review-sources.json");
  const original = await readFile(diskPath, "utf8");
  assert.match(original, /^ {2}cancel = .*$/m, "The base file has no common.cancel line.");
  const withCancel = text => original.replace(/^ {2}cancel = .*$/m, `  cancel = ${text}`);
  const entry = async () => JSON.parse(await readFile(reviewPath, "utf8")).entries.find(item => item.key === "common_cancel" && item.locale === "en");
  const recorded = async fingerprint => {
    try {
      return JSON.parse(await readFile(sourcesPath, "utf8")).sources.find(item => item.key === "common_cancel" && item.sourceFingerprint === fingerprint)?.source;
    } catch {
      return undefined;
    }
  };
  const confirm = async () => {
    const staleBox = page.getByTestId("workflow-stale");
    await staleBox.getByRole("button", { name: "Confirm translation", exact: true }).click();
    await staleBox.waitFor({ state: "detached" });
    await page.keyboard.press("Control+s");
  };
  const showBase = async text => {
    await writeFile(diskPath, withCancel(text));
    await reloadFiles();
    await page.getByRole("button", { name: /^en / }).first().click();
    await select("common_cancel");
    await page.getByTestId("workflow-stale").waitFor();
  };

  // The English approval is confirmed for "Vorgang abbrechen": the one save records that text beside the review file.
  await waitFor(async () => await saveState() === "Saved", "nothing unsaved before the stale-source stage");
  await showBase("Vorgang abbrechen");
  await confirm();
  let approved;
  await waitFor(async () => {
    approved = await entry();
    return approved?.state === "approved" && await recorded(approved.sourceFingerprint) === "Vorgang abbrechen";
  }, "the confirmed approval and its source text written beside the review file");
  assert.equal(JSON.parse(await readFile(reviewPath, "utf8")).$schema, "runic.translations.interchange-review/2", "The review file changed its format.");

  // The source changes: the stale approval shows the text it was approved for above the current one.
  await showBase("Bearbeitung abbrechen");
  const diff = page.getByTestId("workflow-source-diff");
  await diff.waitFor();
  await page.getByTestId("workflow-stale").getByText("See what changed below", { exact: false }).waitFor();
  const before = diff.getByTestId("workflow-source-before");
  const now = diff.getByTestId("workflow-source-now");
  assert.equal((await before.locator("dt").textContent())?.trim(), "Source when marked");
  assert.equal((await now.locator("dt").textContent())?.trim(), "Current source");
  assert.deepEqual(await before.locator('del[data-change="removed"]').allTextContents(), ["Removed: Vorgang"], "The removed word is not marked in the recorded source.");
  assert.deepEqual(await now.locator('ins[data-change="added"]').allTextContents(), ["Added: Bearbeitung"], "The added word is not marked in the current source.");
  assert.equal((await before.locator("dd").textContent())?.replace("Removed: ", ""), "Vorgang abbrechen");
  assert.equal((await now.locator("dd").textContent())?.replace("Added: ", ""), "Bearbeitung abbrechen");
  assert.equal(await before.locator("dd").getAttribute("lang"), "de", "The compared texts are not marked as the base language.");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "stale-source-diff-en.png"), fullPage: true });

  // Confirming records the approval and the text for the current source; the comparison goes away.
  await confirm();
  await diff.waitFor({ state: "detached" });
  await waitFor(async () => {
    const current = await entry();
    return current?.state === "approved" && current.sourceFingerprint !== approved.sourceFingerprint && await recorded(current.sourceFingerprint) === "Bearbeitung abbrechen";
  }, "the re-confirmed approval and the new source text written beside the review file");
  await waitFor(async () => await saveState() === "Saved", "nothing unsaved after the confirmation");

  await writeFile(diskPath, original);
  await reloadFiles();
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors while comparing the source.");
}
