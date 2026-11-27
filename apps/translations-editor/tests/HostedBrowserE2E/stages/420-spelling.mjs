import assert from "node:assert/strict";

// The editor's own spell checking (D074.10, D086.2), in the real host with the bundled English dictionaries. The runner
// makes the system language English, gives the host empty dictionary and download folders, and points HTTPS at a
// closed port, so nothing is downloaded. The pattern box turns the webview's checker off and marks a misspelled English
// word, not the placeholder beside it; the word at the caret offers corrections, and one replaces it. French, which is
// not the system language, is offered for download first, and a failed download offers to try again. The settings
// turn checking off and on. Then common.cancel is "Cancel" again and nothing is unsaved.
/** @param {import("../context.mjs").HostedContext} context */
export default async function spelling(context) {
  const { page, reportDirectory, pageErrors, waitFor, select, saveState } = context;

  await page.getByRole("button", { name: /^en / }).first().click();
  await select("common_cancel");
  const box = page.locator("[data-pattern-box]").first();
  const field = box.locator("textarea[data-draft-field]").first();
  await field.waitFor();
  assert.equal(await field.getAttribute("spellcheck"), "false", "The pattern box leaves the webview's spell checker on.");

  await field.fill("Cancel teh order");
  const mark = box.locator('.spelling-mark[data-spelling-word="teh"]');
  await waitFor(async () => await mark.count() === 1, "the misspelled English word is marked");
  assert.equal(await box.locator(".spelling-mark").count(), 1, "A correctly spelled English word is marked.");

  // The caret in the word offers its corrections; one replaces the word.
  await field.focus();
  await field.evaluate((element) => element.setSelectionRange(8, 8));
  await page.keyboard.press("ArrowRight");
  const suggestions = page.locator('[data-spelling-suggestions="teh"]');
  await suggestions.waitFor();
  await suggestions.getByRole("button", { name: "the", exact: true }).click();
  await waitFor(async () => (await field.inputValue()) === "Cancel the order", "the correction replaced the word");
  await waitFor(async () => await box.locator(".spelling-mark").count() === 0, "the corrected word is no longer marked");
  if (reportDirectory) await page.screenshot({ path: `${reportDirectory}/spelling-pattern.png` });

  // French is not the system language: its dictionary is offered, not downloaded. The download fails (HTTPS goes to a
  // closed port) and installs nothing; the composer offers to try again.
  await field.fill("Cancel");
  await page.getByRole("button", { name: /^fr / }).first().click();
  await select("common_cancel");
  const offer = page.locator('[data-spelling-notice="offer"]');
  const french = await offer.waitFor({ timeout: 5_000 }).then(() => true, () => false);
  if (french) {
    assert.match(await offer.textContent() ?? "", /French|français/, "The offer does not name the language.");
    await offer.getByRole("button", { name: "Download", exact: true }).click();
    await page.locator('[data-spelling-notice="failed"]').getByRole("button", { name: "Try again", exact: true }).waitFor({ timeout: 30_000 });
    if (reportDirectory) await page.screenshot({ path: `${reportDirectory}/spelling-download-failed.png` });
  } else {
    // A runner with a French Hunspell dictionary of its own checks French with it and offers nothing.
    console.log("note: French has a system dictionary here; the download offer is not shown.");
  }

  // The settings turn checking off (no marks) and on again; the system language's download is on by default.
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("common_cancel");
  await field.fill("Cancel teh");
  await waitFor(async () => await box.locator(".spelling-mark").count() === 1, "the word is marked again");
  const openSettings = async () => {
    await page.getByRole("button", { name: /^(Editor settings|Editoreinstellungen), / }).click();
    await page.getByRole("menu").waitFor();
  };
  await openSettings();
  const menu = page.getByRole("menu");
  assert.equal(await menu.getByRole("menuitemcheckbox", { name: "Download my language’s dictionary automatically" }).getAttribute("aria-checked"), "true",
    "The system language's dictionary does not download on its own by default.");
  await menu.getByRole("menuitemcheckbox", { name: "Check spelling" }).click();
  await page.keyboard.press("Escape");
  await waitFor(async () => await box.locator(".spelling-mark").count() === 0, "checking off removes the marks");
  await openSettings();
  await page.getByRole("menu").getByRole("menuitemcheckbox", { name: "Check spelling" }).click();
  await page.keyboard.press("Escape");
  await waitFor(async () => await box.locator(".spelling-mark").count() === 1, "checking on marks the word again");

  await field.fill("Cancel");
  await waitFor(async () => await saveState() === "Saved", "common.cancel is unchanged again");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
