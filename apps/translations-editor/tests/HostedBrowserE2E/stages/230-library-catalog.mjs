import assert from "node:assert/strict";
import { readFile, lstat } from "node:fs/promises";
import { join } from "node:path";

// The interface language switch sets the window's locale through the ViewModel, the page follows it, and the
// choice survives a reload from the native editor state.
// Library catalogs (T5d): the library's text is layer 0, read-only; an override (layer 1) is written to
// overrides/<catalog>/<locale>.rmf2 seeded with the library text, edited, reverted to the library, and a locale the
// library lacks is added with its one fallback hop.
/** @param {import("../context.mjs").HostedContext} context */
export default async function libraryCatalog(context) {
  const { page, workspace, reportDirectory, waitFor, pageErrors } = context;
  const overridePath = join(workspace, "overrides", "acme.table", "de.rmf2");
  await page.locator('[data-testid="workspace-catalogs"] [data-library="acme.table"]').waitFor();
  await page.getByRole("button", { name: /^Libraries/ }).click();
  const libraryDialog = page.getByTestId("library-dialog");
  await libraryDialog.locator('[data-layer="library"]').first().waitFor();
  assert.deepEqual(await libraryDialog.locator("[data-locale]").evaluateAll(items => items.map(item => item.getAttribute("data-locale"))), ["en", "de"]);
  await libraryDialog.locator('[data-locale="de"]').click();
  const sortRow = libraryDialog.locator('[data-message="header_sort"]');
  await sortRow.getByRole("button", { name: "Override", exact: true }).click();
  const overrideText = sortRow.locator('[data-layer="override"]');
  await waitFor(async () => await overrideText.count() === 1 && (await overrideText.inputValue()) === "Sortieren nach {$column}", "override seeded with the library text");
  // The override is written on its first Save, not on the click.
  assert.equal(await lstat(overridePath).then(() => true, () => false), false, "Override wrote the file before Save.");
  await overrideText.fill("Sortiert nach {$column}");
  await sortRow.getByRole("button", { name: "Save", exact: true }).click();
  await waitFor(async () => await lstat(overridePath).then(() => true, () => false) && (await readFile(overridePath, "utf8")).includes("sort = Sortiert nach {$column}"), "override text saved to disk");
  assert.match(await readFile(overridePath, "utf8"), /header \{\n  # Acme\.Table 1\.0\.0, base locale en:\n[^]*  sort = Sortiert nach \{\$column\}\n\}/);
  await sortRow.locator('[data-badge="overridden"]').waitFor();
  await sortRow.getByRole("button", { name: "Revert to library", exact: true }).click();
  await waitFor(async () => !(await lstat(overridePath).then(() => true, () => false)), "override source removed by Revert to library");
  await sortRow.getByRole("button", { name: "Override", exact: true }).waitFor();
  // Typed in another spelling: the canonical de-CH is added and selected, not the base locale.
  await libraryDialog.getByTestId("library-new-locale").fill("de_ch");
  await libraryDialog.getByRole("button", { name: "Add locale", exact: true }).click();
  await waitFor(async () => (await libraryDialog.locator('[data-locale="de-CH"]').count()) === 1 &&
    (await libraryDialog.locator('[data-locale="de-CH"]').innerText()).includes("Added by you, falls back to de"), "app-added de-CH with its fallback");
  assert.match(await libraryDialog.locator('[data-coverage="de-CH"]').innerText(), /^0 of 2 in its own text/);
  assert.match(await libraryDialog.locator('[data-locale="de-CH"]').getAttribute("class") ?? "", /border-primary/, "The added locale is not selected.");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "library-catalog.png"), fullPage: true });
  await page.keyboard.press("Escape");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors in the library catalog.");
}
