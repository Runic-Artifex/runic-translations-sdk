import assert from "node:assert/strict";

/** @param {import("../context.mjs").HostedContext} context */
export default async function reviewStateGermanUi(context) {
  const { page, select } = context;
  await select("common_cancel");
  assert.equal((await page.getByTestId("workflow-state").textContent())?.trim(), "Freigegeben", "The approved badge is not German.");
  await page.getByRole("button", { name: /^fr / }).first().click();
  await select("common_cancel");
  assert.equal((await page.getByTestId("workflow-state").textContent())?.trim(), "Übersetzt", "The translated badge is not German.");
  for (const coverage of await page.getByTestId("locale-coverage").allTextContents())
    assert.match(coverage.trim(), /^\d{1,3}\u00a0%$/, `Coverage ${coverage} is not whole percent in German format.`);
  assert.equal(await page.title(), "runic-hosted-e2e – Runic Translations Editor");
  await page.getByRole("button", { name: /^en / }).first().click();
}
