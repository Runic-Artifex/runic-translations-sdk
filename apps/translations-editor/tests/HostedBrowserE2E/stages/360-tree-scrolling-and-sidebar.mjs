import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

// The real-window check (#225, R2 and R3) at its window size, 1448×941, in comfortable density: the message tree
// scrolls with the mouse wheel, Next missing reveals the message it selects below the visible rows, and the Languages
// section takes the height its three languages need, leaving the rest to the tree. Adds the group `gallery` (in German
// and English) and `zulu.deleteConfirm` (German only, so missing in English) for a tree taller than the window, and
// restores both files at the end. Leaves English, common.cancel selected, nothing unsaved.
/** @param {import("../context.mjs").HostedContext} context */
export default async function treeScrollingAndSidebar(context) {
  const { page, reportDirectory, pageErrors, waitFor, select, saveState, reloadFiles, messageRow } = context;
  const { diskPath, enPath } = context.paths;
  const deBefore = await readFile(diskPath, "utf8");
  const enBefore = await readFile(enPath, "utf8");
  const gallery = (prefix) => `gallery {\n${Array.from({ length: 40 }, (_, index) => `  item${String(index).padStart(2, "0")} = ${prefix} ${index}\n`).join("")}}\n`;
  await writeFile(diskPath, `${deBefore}${gallery("Bild")}zulu {\n  deleteConfirm = Wirklich löschen?\n}\n`);
  await writeFile(enPath, `${enBefore}${gallery("Picture")}`);
  const viewport = page.viewportSize();
  await page.setViewportSize({ width: 1448, height: 941 });
  try {
    await reloadFiles();
    await context.groupRow("gallery").waitFor();
    await page.getByRole("button", { name: /^en / }).first().click();
    await select("common_cancel");
    assert.equal(await page.evaluate(() => document.documentElement.dataset.density), "comfortable");
    const tree = page.getByRole("navigation", { name: "Messages", exact: true });
    const selected = async () => page.locator('[role="treeitem"][data-message-key][aria-selected="true"]').getAttribute("data-message-key");
    const scrollTop = () => tree.evaluate((element) => element.scrollTop);
    // Whether the row of `key` is rendered inside the tree's visible area.
    const rowVisible = (key) => tree.evaluate((element, wanted) => {
      const row = element.querySelector(`[data-message-key="${wanted}"]`);
      if (row === null) return false;
      const bounds = row.getBoundingClientRect();
      const area = element.getBoundingClientRect();
      return bounds.top >= area.top - 1 && bounds.bottom <= area.bottom + 1;
    }, key);

    // R3: the Languages section takes the height its three languages need; the tree gets the rest of the sidebar.
    const layout = await page.evaluate(() => {
      const panels = document.querySelector(".sidebar-section-panels")?.getBoundingClientRect();
      const languages = document.querySelector(".languages-panel");
      const items = [...(languages?.querySelectorAll("[aria-pressed]") ?? [])];
      const last = items.at(-1)?.getBoundingClientRect();
      const nav = document.querySelector('nav[aria-label="Messages"]')?.getBoundingClientRect();
      return {
        panels: panels?.height ?? 0,
        languages: languages?.getBoundingClientRect().height ?? 0,
        emptyBelowLast: (languages?.getBoundingClientRect().bottom ?? 0) - (last?.bottom ?? 0),
        tree: nav?.height ?? 0,
      };
    });
    assert.ok(layout.emptyBelowLast < 24, `The Languages section leaves empty space below its last language: ${JSON.stringify(layout)}`);
    assert.ok(layout.languages < layout.panels * 0.4, `The Languages section takes more than its share: ${JSON.stringify(layout)}`);
    assert.ok(layout.tree > layout.panels * 0.5, `The tree does not get most of the sidebar: ${JSON.stringify(layout)}`);
    // The resize handle is still there, as a slider.
    await page.getByRole("slider", { name: "Resize Languages and Messages" }).waitFor();
    if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "sidebar-three-languages.png"), clip: { x: 0, y: 0, width: 420, height: 941 } });

    // R2: the mouse wheel over the tree scrolls it, down to the last group.
    assert.equal(await scrollTop(), 0);
    assert.equal(await rowVisible("zulu_deleteConfirm"), false, "The last message is visible without scrolling.");
    const box = await tree.boundingBox();
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.wheel(0, 600);
    await waitFor(async () => await scrollTop() > 0, "the wheel scrolls the message tree");
    for (let turn = 0; turn < 20 && !(await rowVisible("zulu_deleteConfirm")); turn += 1) {
      await page.mouse.wheel(0, 600);
      await page.waitForTimeout(50);
    }
    assert.ok(await rowVisible("zulu_deleteConfirm"), "The wheel does not reach the last message of the tree.");
    if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "tree-scrolled.png"), clip: { x: 0, y: 0, width: 420, height: 941 } });
    assert.equal(await selected(), "common_cancel", "Scrolling changed the selection.");
    await page.mouse.wheel(0, -100_000);
    await waitFor(async () => await scrollTop() === 0, "the wheel scrolls the tree back to the top");

    // R2: Next missing selects zulu.deleteConfirm, below the visible rows, and the tree reveals it. The tree is scrolled
    // back to the top before each step, so the last step has to reveal its message.
    const next = page.getByTestId("next-message");
    for (let step = 0; step < 10 && await selected() !== "zulu_deleteConfirm"; step += 1) {
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
      await page.mouse.wheel(0, -100_000);
      await waitFor(async () => await scrollTop() === 0, "the tree at the top");
      const before = await selected();
      await next.click();
      await waitFor(async () => await selected() !== before, "Next missing selects another message");
    }
    assert.equal(await selected(), "zulu_deleteConfirm", "Next missing does not reach zulu.deleteConfirm.");
    await waitFor(async () => await rowVisible("zulu_deleteConfirm"), "the tree reveals the message Next missing selected");
    assert.ok(await scrollTop() > 0);
    await page.getByTestId("editor-column").getByRole("heading", { level: 2, name: "zulu.deleteConfirm", exact: true }).waitFor();
    if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "tree-next-missing-revealed.png") });
    assert.equal(await saveState(), "Saved");
  } finally {
    await writeFile(diskPath, deBefore);
    await writeFile(enPath, enBefore);
    await page.setViewportSize(viewport ?? { width: 1440, height: 1100 });
  }
  await reloadFiles();
  await waitFor(async () => await messageRow("gallery_item00").count() === 0, "the added messages are gone");
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("common_cancel");
  assert.equal(await saveState(), "Saved");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
