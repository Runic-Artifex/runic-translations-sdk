import assert from "node:assert/strict";
import { join } from "node:path";

// D074.1, the workbench in narrow windows: below 1100px the rail folds under the editor column, which scrolls with it
// as one column; below 768px the sidebar collapses behind the toolbar's navigation toggle, which opens the tree.
// Both densities fold the same way. Leaves the runner's 1440 × 1100 window and the comfortable density.
/** @param {import("../context.mjs").HostedContext} context */
export default async function workbenchRailNarrow(context) {
  const { page, reportDirectory, pageErrors, waitFor, messageRow } = context;
  const column = page.getByTestId("editor-column");
  const rail = page.getByRole("complementary", { name: "Preview and diagnostics", exact: true });
  const setDensity = async name => {
    await page.getByRole("button", { name: /^Editor settings, / }).click();
    await page.getByRole("menu").getByRole("menuitemradio", { name, exact: true }).click();
    await waitFor(async () => await page.evaluate(() => document.documentElement.dataset.density) === name.toLowerCase(), `${name} density`);
    await page.keyboard.press("Escape");
    await page.getByRole("menu").waitFor({ state: "detached" }).catch(() => undefined);
  };
  // The rail under the editor column at the column's width; measured once the sidebar's width transition has settled.
  const folded = async label => {
    const layout = async () => {
      const columnBox = await column.boundingBox();
      const railBox = await rail.boundingBox();
      return columnBox !== null && railBox !== null && railBox.y >= columnBox.y + columnBox.height - 1 &&
        Math.abs(railBox.x - columnBox.x) <= 1 && Math.abs(railBox.width - columnBox.width) <= 1;
    };
    await waitFor(layout, `${label}: the rail folds under the editor column at its width`);
    await page.waitForTimeout(300);
    assert.equal(await layout(), true, `${label}: the folded layout did not stay.`);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true, `${label}: the page scrolls sideways.`);
  };

  for (const density of ["Comfortable", "Compact"]) {
    await page.setViewportSize({ width: 1000, height: 900 });
    if (density === "Compact") await setDensity(density);
    await folded(`1000px, ${density}`);
    // The sidebar still shows the tree next to the folded workbench.
    assert.equal(await messageRow("files_selected").isVisible(), true, "The tree is hidden at 1000px.");
    // The rail is reached by scrolling the one column.
    await rail.locator(".message-preview").scrollIntoViewIfNeeded();
    assert.equal(await rail.locator(".message-preview").isVisible(), true);
    if (reportDirectory && density === "Comfortable") await page.screenshot({ path: join(reportDirectory, "workbench-narrow-1000.png") });

    // Below 768px the sidebar is behind the navigation toggle.
    await page.setViewportSize({ width: 700, height: 900 });
    await waitFor(async () => !(await messageRow("files_selected").isVisible()), `the sidebar collapses at 700px (${density})`);
    await folded(`700px, ${density}`);
    const toggle = page.getByRole("button", { name: "Open editor navigation", exact: true });
    assert.equal(await toggle.isVisible(), true, "The navigation toggle is hidden at 700px.");
    await toggle.click();
    await messageRow("files_selected").waitFor({ state: "visible" });
    if (reportDirectory && density === "Comfortable") await page.screenshot({ path: join(reportDirectory, "workbench-narrow-700-navigation.png") });
    await page.getByRole("dialog").getByRole("button", { name: "Close", exact: true }).click();
    await waitFor(async () => !(await messageRow("files_selected").isVisible()), "the navigation closes");
    if (reportDirectory && density === "Comfortable") await page.screenshot({ path: join(reportDirectory, "workbench-narrow-700.png") });
  }

  await page.setViewportSize({ width: 1440, height: 1100 });
  await setDensity("Comfortable");
  await waitFor(async () => {
    const railBox = await rail.boundingBox();
    const columnBox = await column.boundingBox();
    return railBox !== null && columnBox !== null && railBox.x >= columnBox.x + columnBox.width - 1;
  }, "the rail is beside the editor column again");
  assert.equal(await messageRow("files_selected").isVisible(), true, "The sidebar is not back at 1440px.");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
