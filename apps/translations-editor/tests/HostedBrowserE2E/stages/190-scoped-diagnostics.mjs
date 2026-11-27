import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

// The diagnostics panel (#225 P6, P12): with an error in the selected message and a warning in another message, the
// selected message's error is listed first under its heading and the other warnings are under a collapsed disclosure.
// An error in another message blocks saving, so it is listed without expanding anything.
/** @param {import("../context.mjs").HostedContext} context */
export default async function scopedDiagnostics(context) {
  const { page, reportDirectory, waitFor, select, editor, saveState, reloadFiles, messageRow } = context;
  const { reviewPath } = context.paths;
  const { preservedComment, staleFingerprint } = context.fixture;
  const reviewBefore = await readFile(reviewPath, "utf8");
  const scopedReview = JSON.parse(reviewBefore);
  scopedReview.entries.push({ key: "common_save", locale: "fr", state: "needs-review", sourceFingerprint: staleFingerprint });
  await writeFile(reviewPath, JSON.stringify(scopedReview, null, 2) + "\n");
  await reloadFiles();
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("common_cancel");
  await page.locator('[data-slot="toggle-group-item"]', { hasText: /^File$/ }).click();
  const fileText = page.locator("#translation-value");
  await waitFor(async () => (await fileText.inputValue()).startsWith(preservedComment), "file view of en.rmf2");
  const enText = await fileText.inputValue();
  assert.ok(enText.includes("  cancel = Cancel\n") && enText.includes("  save = Save\n"));
  const brokenCancel = enText.replace("  cancel = Cancel\n", "  cancel = Cancel {$what :nosuch}\n");
  await fileText.fill(brokenCancel);
  const messageIssues = page.getByRole("region", { name: "Issues in common.cancel (en)", exact: true });
  await waitFor(async () => await messageIssues.locator("[data-severity]").count() > 0, "the selected message's diagnostics");
  assert.equal(await messageIssues.locator("[data-severity]").first().getAttribute("data-severity"), "error",
    "The selected message's error is not listed first.");
  assert.match(await messageIssues.locator("[data-severity]").first().textContent(), /Line 1/, "The error does not name its line in the message.");
  // The project is in the current file format, so the panel names no schema version (D086.1).
  assert.equal(await page.getByTestId("validation-older-format").count(), 0, "A current project is named an older file format.");
  assert.doesNotMatch(await page.getByRole("alert").filter({ has: messageIssues }).textContent(), /schema/i,
    "The validation panel shows the schema version.");
  const blockingIssues = page.getByTestId("diagnostics-blocking");
  assert.equal(await blockingIssues.count(), 0, "Errors elsewhere are shown although the project has none.");
  const otherIssues = page.getByTestId("diagnostics-other");
  const disclosure = otherIssues.getByRole("button", { name: /^Other warnings in the project \(\d+\)$/ });
  assert.equal(await disclosure.getAttribute("aria-expanded"), "false", "Other warnings are not collapsed.");
  assert.equal(await otherIssues.locator("[data-severity]").count(), 0, "Collapsed warnings are listed.");
  assert.match(await page.getByTestId("diagnostics-message-counts").textContent(), /^This message: \d+ errors?, \d+ warnings?$/);
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "scoped-diagnostics-en.png"), fullPage: true });
  await disclosure.focus();
  await page.keyboard.press("Enter");
  await waitFor(async () => await disclosure.getAttribute("aria-expanded") === "true", "expanded other warnings");
  await otherIssues.getByRole("button", { name: /RTR0090.*in common\.save \(fr\)/ }).first().waitFor();
  assert.deepEqual(await otherIssues.locator("[data-severity]").evaluateAll(items => [...new Set(items.map(item => item.getAttribute("data-severity")))]),
    ["warning"], "The collapsed section holds warnings only.");
  await disclosure.click();
  await waitFor(async () => await disclosure.getAttribute("aria-expanded") === "false", "collapsed other warnings");
  // An error in another message of the file: pinned under its own heading, visible while the warnings stay collapsed.
  await fileText.fill(brokenCancel.replace("  save = Save\n", "  save = Save {$what :nosuch}\n"));
  const elsewhere = page.getByRole("region", { name: "Errors that block saving", exact: true });
  const saveError = elsewhere.getByRole("button", { name: /RTR0065.*in common\.save \(en\)/ });
  await saveError.waitFor();
  assert.equal(await saveError.isVisible(), true, "The other message's error is not visible without expanding.");
  assert.equal(await disclosure.getAttribute("aria-expanded"), "false");
  assert.equal(await otherIssues.locator("[data-severity]").count(), 0);
  assert.equal(await messageIssues.locator("[data-severity]").first().getAttribute("data-severity"), "error");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "scoped-diagnostics-errors-en.png"), fullPage: true });
  // A pinned error navigates to its message: the message is selected and revealed in the tree, and the error is now
  // listed under that message's heading.
  await saveError.click();
  await waitFor(async () => await messageRow("common_save").getAttribute("aria-selected") === "true", "pinned error selects its message");
  assert.equal(await messageRow("common_save").isVisible(), true, "The pinned error's message is not revealed in the tree.");
  await page.getByRole("region", { name: "Issues in common.save (en)", exact: true })
    .getByRole("button", { name: /RTR0065/ }).first().waitFor();
  // A collapsed warning navigates to its message in its file's locale (French) too.
  await disclosure.click();
  await waitFor(async () => await disclosure.getAttribute("aria-expanded") === "true", "expanded other warnings again");
  await otherIssues.getByRole("button", { name: /RTR0096.*in files\.deleted \(fr\)/ }).first().click();
  await page.getByRole("region", { name: "Issues in files.deleted (fr)", exact: true }).getByRole("button", { name: /RTR0096/ }).first().waitFor();
  await waitFor(async () => await messageRow("files_deleted").getAttribute("aria-selected") === "true", "warning selects its message");
  assert.equal(await messageRow("files_deleted").isVisible(), true, "The warning's message is not revealed in the tree.");
  // Back to the English file: revert the draft and the review file.
  await page.getByRole("button", { name: /^en / }).first().click();
  await messageRow("common_cancel").click();
  await page.locator('[data-slot="toggle-group-item"]', { hasText: /^File$/ }).click();
  await waitFor(async () => (await fileText.inputValue()).startsWith(preservedComment), "file view of the en.rmf2 draft");
  await fileText.fill(enText);
  await waitFor(async () => await saveState() === "Saved", "scoped-diagnostics edit reverted");
  await writeFile(reviewPath, reviewBefore);
  await reloadFiles();
  await page.getByRole("button", { name: /^en / }).first().click();
  await messageRow("common_cancel").click();
  await page.locator('[data-slot="toggle-group-item"]', { hasText: /^Translation$/ }).click();
  await editor().waitFor();
}
