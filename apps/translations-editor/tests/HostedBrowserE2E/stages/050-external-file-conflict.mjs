import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

/** @param {import("../context.mjs").HostedContext} context */
export default async function externalFileConflict(context) {
  const { page, reportDirectory, waitFor, select, editor, reloadFiles, handoff } = context;
  const { diskPath } = context.paths;
  const pluralSaved = handoff.get("pluralSaved");
  await editor().fill("Unsaved conflicting editor draft");
  const externalBytes = `${pluralSaved}# External edit must win the revision conflict.\n`;
  handoff.set("externalBytes", externalBytes);
  await writeFile(diskPath, externalBytes);
  await page.keyboard.press("Control+s");
  await waitFor(async () => (await page.getByText(/de\.rmf2.*changed on disk/i).count()) > 0, "external conflict notice");
  assert.equal(await readFile(diskPath, "utf8"), externalBytes, "A stale save must not overwrite the external edit.");
  assert.equal(await editor().inputValue(), "Unsaved conflicting editor draft");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "conflict-en.png"), fullPage: true });
  await reloadFiles();
  await select("common_cancel");
  await waitFor(async () => (await editor().inputValue()) === "Queued cancel from the real editor", "conflict reload");
}
