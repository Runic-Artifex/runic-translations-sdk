import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import { dirname, join } from "node:path";

// D074: the colour palettes are gone and Density (comfortable or compact) takes their place in the editor settings.
// The runner seeded the native record with a palette an earlier version stored: the host dropped it on load, without
// quarantining the record, and no save carries it. Leaves the comfortable density selected.
/** @param {import("../context.mjs").HostedContext} context */
export default async function densitySetting(context) {
  const { page, workspace, reportDirectory, pageErrors, waitFor, sentLocalState } = context;
  const stateDirectory = join(dirname(workspace), "state", "RunicArtifex", "Runic.Translations.Editor");
  const statePath = join(stateDirectory, "editor-state-v1.json");
  const storedEntries = async () => JSON.parse(await readFile(statePath, "utf8")).entries;
  const root = () => page.evaluate(() => ({ ...document.documentElement.dataset }));

  assert.deepEqual((await readdir(stateDirectory)).filter(name => name.includes(".corrupt-")), [],
    "The record with an earlier version's palette was quarantined instead of migrated.");
  assert.ok(sentLocalState.length > 0, "The page never saved its local state.");
  assert.ok(sentLocalState.every(entries => entries.every(entry => entry.key !== "runic-translations.theme-palette")),
    "A save carried the retired palette.");
  assert.equal((await storedEntries()).some(entry => entry.key === "runic-translations.theme-palette"), false,
    "The native record still holds the retired palette after a save.");

  const initial = await root();
  assert.equal(initial.density, "comfortable", "Comfortable is not the default density.");
  assert.ok(initial.scheme === "light" || initial.scheme === "dark", "The brand tokens' scheme is not set on the root.");
  assert.equal(initial.theme, undefined, "A palette is still set on the root.");
  const row = page.locator('[role="treeitem"][data-message-key]').first();
  assert.equal((await row.boundingBox())?.height, 32, "A comfortable tree row is not 32px high.");

  const openSettings = async () => {
    await page.getByRole("button", { name: /^(Editor settings|Editoreinstellungen), / }).click();
    await page.getByRole("menu").waitFor();
  };
  await openSettings();
  const menu = page.getByRole("menu");
  assert.equal(await menu.getByRole("menuitemradio", { name: /^(Compact|Kompakt)$/ }).count(), 1, "The settings menu offers no compact density.");
  assert.equal(await menu.getByRole("menuitemradio", { name: /^(Comfortable|Komfortabel)$/ }).getAttribute("aria-checked"), "true");
  assert.equal(await menu.getByRole("menuitemradio", { name: /Runic Gold|Runisches Gold|Moss|Moos|Fjord|Ember|Glut/ }).count(), 0,
    "The settings menu still offers a colour palette.");
  if (reportDirectory) {
    await page.waitForTimeout(400);
    await page.screenshot({ path: join(reportDirectory, "settings-density.png") });
  }

  await menu.getByRole("menuitemradio", { name: /^(Compact|Kompakt)$/ }).click();
  await waitFor(async () => (await root()).density === "compact", "compact density on the root");
  await waitFor(async () => (await row.boundingBox())?.height === 28, "compact tree rows");
  await waitFor(async () => (await storedEntries().catch(() => [])).some(entry => entry.key === "runic-translations.density" && entry.value === "compact"),
    "native save of the compact density");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "density-compact.png") });

  await page.reload({ waitUntil: "domcontentloaded" });
  await page.getByRole("button", { name: /^(Editor settings|Editoreinstellungen), / }).waitFor();
  await waitFor(async () => (await root()).density === "compact", "compact density after a reload");

  await openSettings();
  await page.getByRole("menu").getByRole("menuitemradio", { name: /^(Comfortable|Komfortabel)$/ }).click();
  await waitFor(async () => (await root()).density === "comfortable", "comfortable density on the root");
  await waitFor(async () => (await storedEntries().catch(() => [])).some(entry => entry.key === "runic-translations.density" && entry.value === "comfortable"),
    "native save of the comfortable density");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
