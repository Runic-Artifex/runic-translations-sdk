import assert from "node:assert/strict";
import { join } from "node:path";

/** @param {import("../context.mjs").HostedContext} context */
export default async function interfaceLanguage(context) {
  const { page, reportDirectory, waitFor, pageErrors } = context;
  await page.evaluate(() => { window.__runicEditorDocumentCalls = []; });
  await page.getByRole("button", { name: /^Editor settings, / }).click();
  await page.getByRole("menuitemradio", { name: "Deutsch", exact: true }).click();
  await waitFor(async () => (await page.evaluate(() => document.documentElement.lang)) === "de", "German interface language");
  await waitFor(async () => (await page.evaluate(() => window.__runicEditorDocumentCalls))
    .some(call => call.member === "setUiLocale" && call.error === undefined), "setUiLocale request to the editor ViewModel");
  await page.getByRole("button", { name: /^Editoreinstellungen, .*Oberflächensprache Deutsch$/ }).waitFor();
  await waitFor(async () => (await page.evaluate(() => window.__runicEditorDocumentCalls))
    .some(call => call.member === "saveLocalState" && call.error === undefined), "native save of the interface language");
  await page.reload({ waitUntil: "domcontentloaded" });
  await page.getByRole("button", { name: /^Editoreinstellungen, .*Oberflächensprache Deutsch$/ }).waitFor();
  assert.equal(await page.evaluate(() => document.documentElement.lang), "de", "The interface language did not survive a reload.");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "interface-language-de.png"), fullPage: true });
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
