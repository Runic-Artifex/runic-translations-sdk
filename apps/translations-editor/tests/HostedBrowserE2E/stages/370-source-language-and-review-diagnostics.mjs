import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

// The real-window check (#225, R1 and R5). The base locale (German) of the translation project holds the source text:
// the composer edits "the text" ("Edit the text", "Default text", the Text tab), without the review workflow or
// translation state marks, and the palette offers no review states for it; English is still a translation. Then two
// stale review states of English are settled with "Confirm translation" and "Mark needs review": after the one save the
// issues rail no longer lists RTR0090 for them, without a reload. Leaves English, common.cancel selected, nothing
// unsaved, and the review file as it found it.
/** @param {import("../context.mjs").HostedContext} context */
export default async function sourceLanguageAndReviewDiagnostics(context) {
  const { page, reportDirectory, pageErrors, waitFor, select, saveState, reloadFiles, messageRow } = context;
  const { reviewPath } = context.paths;
  const { staleFingerprint } = context.fixture;
  const column = page.getByTestId("editor-column");

  // R1: the source text. German (de) is the fixture's base locale.
  await page.getByRole("button", { name: /^de / }).first().click();
  await select("common_save");
  await column.getByRole("heading", { name: "Edit the text", exact: true }).waitFor();
  assert.equal(await column.getByText("Translate the message", { exact: true }).count(), 0, "The source text is translated.");
  assert.equal(await column.getByText("Default translation", { exact: true }).count(), 0);
  await column.getByText("Default text", { exact: true }).first().waitFor();
  await select("files_selected");
  await column.getByRole("heading", { name: "Edit the text", exact: true }).waitFor();
  await page.getByRole("textbox", { name: /^Text for count: one/ }).first().waitFor();
  assert.equal(await page.getByRole("textbox", { name: /^Translation for count:/ }).count(), 0);
  assert.deepEqual((await page.locator('[data-slot="toggle-group-item"]').allTextContents()).map((text) => text.trim()), ["Text", "File"]);
  assert.equal(await page.getByRole("button", { name: /^Workflow/ }).count(), 0, "The source text has a review workflow.");
  assert.equal(await page.getByTestId("workflow-state").count(), 0);
  assert.equal(await page.getByTestId("source-text").count(), 0, "The source text shows a source panel.");
  assert.equal(await page.getByRole("tree").locator("[data-state-mark]").count(), 0, "The source text's messages show translation states.");
  assert.equal(await page.getByRole("button", { name: "Message bulk actions" }).count(), 0, "The source text offers bulk review actions.");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "source-language.png") });
  await page.keyboard.press("Control+k");
  const search = page.getByRole("textbox", { name: "Search commands", exact: true });
  await search.fill("message");
  await page.getByRole("dialog").getByRole("option").first().waitFor();
  const titles = (await page.getByRole("dialog").getByRole("option").allTextContents()).map((title) => title.replace(/\s+/g, " ").trim());
  assert.ok(!titles.some((title) => /^(Approve message|Mark message as|Mark visible messages|Approve visible messages)/.test(title)),
    `The palette offers review states for the source text: ${titles.join(" | ")}`);
  await page.keyboard.press("Escape");
  // English is a translation of it.
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("files_selected");
  await column.getByRole("heading", { name: "Translate the message", exact: true }).waitFor();
  await page.getByRole("button", { name: /^Workflow/ }).first().waitFor();
  await page.getByTestId("source-text").waitFor();
  assert.ok(await page.getByRole("tree").locator("[data-state-mark]").count() > 0, "A translation's messages show no states.");

  // R5: two stale review states of English, settled in the editor and saved: RTR0090 leaves the issues rail.
  const reviewBefore = await readFile(reviewPath, "utf8");
  const review = JSON.parse(reviewBefore);
  review.entries = review.entries.filter((entry) => !(entry.locale === "en" && ["common_save", "common_cancel"].includes(entry.key)));
  review.entries.push(
    { key: "common_save", locale: "en", state: "approved", sourceFingerprint: staleFingerprint },
    { key: "common_cancel", locale: "en", state: "translated", sourceFingerprint: staleFingerprint },
  );
  await writeFile(reviewPath, JSON.stringify(review, null, 2) + "\n");
  await reloadFiles();
  await page.getByRole("button", { name: /^en / }).first().click();
  // The rail lists the selected message's diagnostics in their own section.
  const messageSection = page.getByTestId("editor-rail").getByTestId("diagnostics-message");
  const staleShown = async () => await messageSection.getByText("RTR0090", { exact: true }).count() > 0;
  await select("common_save");
  await page.getByTestId("workflow-stale").waitFor();
  await waitFor(staleShown, "RTR0090 of common.save in the issues rail");
  await page.getByTestId("workflow-confirm").click();
  await page.getByTestId("workflow-stale").waitFor({ state: "detached" });
  await select("common_cancel");
  await page.getByTestId("workflow-stale").waitFor();
  await waitFor(staleShown, "RTR0090 of common.cancel in the issues rail");
  await page.getByTestId("workflow-stale").getByRole("button", { name: "Mark needs review", exact: true }).click();
  await page.getByTestId("workflow-stale").waitFor({ state: "detached" });
  await waitFor(async () => await saveState() === "Unsaved changes: review status", "the review changes are unsaved");
  // Before the save the issues rail still reports it: the review file says the state is stale.
  assert.ok(await staleShown());
  await page.keyboard.press("Control+s");
  await waitFor(async () => await saveState() === "Saved", "the review changes are saved");
  await waitFor(async () => !(await staleShown()), "RTR0090 of common.cancel leaves the issues rail after the review save");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "review-saved-diagnostics.png") });
  // The other settled message, and back: the refreshed diagnostics stay after another selection.
  await select("common_save");
  await messageSection.waitFor();
  await page.waitForTimeout(300);
  assert.equal(await staleShown(), false, "RTR0090 of common.save stayed after the review save.");
  await select("common_cancel");
  await page.waitForTimeout(300);
  assert.equal(await staleShown(), false, "RTR0090 of common.cancel came back after another selection.");
  const saved = JSON.parse(await readFile(reviewPath, "utf8")).entries;
  assert.equal(saved.find((entry) => entry.key === "common_save" && entry.locale === "en")?.state, "approved");
  assert.equal(saved.find((entry) => entry.key === "common_cancel" && entry.locale === "en")?.state, "needs-review");
  assert.ok(saved.filter((entry) => entry.locale === "en" && entry.key.startsWith("common_")).every((entry) => entry.sourceFingerprint !== staleFingerprint));

  // Back as found: the review file, English, common.cancel.
  await writeFile(reviewPath, reviewBefore);
  await reloadFiles();
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("common_cancel");
  await waitFor(async () => await saveState() === "Saved", "nothing unsaved");
  assert.ok(await messageRow("common_cancel").count() === 1);
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
